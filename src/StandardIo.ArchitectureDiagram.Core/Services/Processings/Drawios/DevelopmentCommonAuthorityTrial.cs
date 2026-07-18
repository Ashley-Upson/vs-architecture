using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using StandardIo.ArchitectureDiagram.Core.Models;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal sealed record DevelopmentCommonAuthorityApplication(
    RenderLayout Layout,
    string ReportJson);

internal sealed record DevelopmentTrialComponentAttempt(
    string ComponentId,
    string Status,
    string Reason,
    IReadOnlyList<string> RouteIds,
    IReadOnlyList<string> Details);

internal static class DevelopmentCommonAuthorityTrial
{
    public static DevelopmentCommonAuthorityApplication Apply(RenderLayout production, DiagramSettings settings)
    {
        var phase = new Dictionary<string, long>(StringComparer.Ordinal);
        var timer = Stopwatch.StartNew();
        var placement = new PlacedGraph(production.Graph, production.Nodes, production.Projects, production.LayoutRevision);
        var routeRevision = new RouteRevision(production.Links.Values.Select(item => item.RouteState.Revision).DefaultIfEmpty(0).Max());
        var generated = new GeneratedLogicalRoutes(placement, production.Links, routeRevision);
        var bands = InterLayerBandObserver.Observe(placement, generated, settings, production.Traceability);
        var observation = AdjacentDownwardRailDemandObserver.Observe(AdjacentDownwardContextFactory.Create(production, bands));
        phase["eligibility and rail-demand production"] = Elapsed(timer);

        timer.Restart();
        var common = AdjacentDownwardCommonRailObserver.Observe(
            observation, settings.Layout.ParallelLaneSpacing, settings.Layout.LinkPadding);
        var interactions = DiscoverInteractions(production.Links, settings.Layout.ParallelLaneSpacing);
        var capabilities = observation.Routes.Select(item => new CommonAuthorityRouteCapability(
            item.LogicalRouteId,
            item.Eligible,
            item.Eligible ? "Eligible" : item.RejectionReason?.ToString() ?? "Unsupported"));
        var closure = CommonAuthorityComponentClassifier.Classify(capabilities, interactions);
        phase["conflict grouping and rail assignment"] = Elapsed(timer);

        timer.Restart();
        var comparisonByRoute = common.Routes.ToDictionary(item => item.LogicalRouteId, StringComparer.Ordinal);
        var current = production.Links.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        var attempts = new List<DevelopmentTrialComponentAttempt>();
        var acceptedRouteIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var component in closure.Components.OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            if (component.Disposition != CommonAuthorityComponentDisposition.Eligible)
            {
                attempts.Add(Attempt(component, "rejected before execution", component.Reason, Array.Empty<string>()));
                continue;
            }

            var routeIds = component.Routes.Select(item => item.LogicalRouteId).OrderBy(item => item, StringComparer.Ordinal).ToArray();
            var rails = routeIds.SelectMany(routeId => Rails(observation, comparisonByRoute, routeId)).ToArray();
            var turnAllocation = DeterministicSharedTurnAllocator.Assign(rails);
            if (turnAllocation.RejectedRouteIds.Count > 0)
            {
                attempts.Add(Attempt(component, "rejected before execution", "SharedTurnAllocationFailed",
                    turnAllocation.RejectedRouteIds));
                continue;
            }

            var candidate = current.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
            var unable = new List<string>();
            foreach (var routeId in routeIds)
            {
                var observed = observation.Routes.Single(item => item.LogicalRouteId == routeId);
                var assigned = Rails(observation, comparisonByRoute, routeId).ToArray();
                var points = AdjacentDownwardRailDemandObserver.Reconstruct(
                    observed.CanonicalAuthoritativePoints[0],
                    observed.CanonicalAuthoritativePoints[observed.CanonicalAuthoritativePoints.Count - 1],
                    assigned,
                    turnAllocation.TransitionsByRouteId[routeId]);
                if (points.Count == 0)
                {
                    unable.Add(routeId);
                    continue;
                }
                candidate[routeId] = candidate[routeId].AcceptGeometry(
                    points, LogicalRouteStage.Validated, "DevelopmentCommonAuthorityTrial");
            }
            if (unable.Count > 0)
            {
                attempts.Add(Attempt(component, "rejected before execution", "ReconstructionFailed", unable));
                continue;
            }

            var validation = TraceabilityValidator.Validate(production.Nodes, candidate, settings.Layout.ParallelLaneSpacing);
            var hard = validation.Violations.Where(item =>
                routeIds.Contains(item.EdgeId, StringComparer.Ordinal) ||
                item.OtherEdgeId is not null && routeIds.Contains(item.OtherEdgeId, StringComparer.Ordinal)).ToArray();
            if (hard.Length > 0)
            {
                attempts.Add(Attempt(component, "rejected after validation", "HardGeometryFinding",
                    hard.Select(item => $"{item.Code}:{item.EdgeId}:{item.OtherEdgeId}").ToArray()));
                continue;
            }

            current = candidate;
            foreach (var routeId in routeIds) acceptedRouteIds.Add(routeId);
            attempts.Add(Attempt(component, "accepted", "Validated", routeIds));
        }
        phase["turn assignment, regeneration and component validation"] = Elapsed(timer);

        timer.Restart();
        var finalValidation = TraceabilityValidator.Validate(production.Nodes, current, settings.Layout.ParallelLaneSpacing);
        var layout = production.WithTrialLinks(current, finalValidation);
        phase["combined validation"] = Elapsed(timer);
        var acceptedBefore = production.Links.Values.Where(item => acceptedRouteIds.Contains(item.Link.Id)).ToArray();
        var acceptedAfter = current.Values.Where(item => acceptedRouteIds.Contains(item.Link.Id)).ToArray();
        var beforeQuality = Quality(acceptedBefore);
        var afterQuality = Quality(acceptedAfter);
        var routeChanges = acceptedRouteIds.OrderBy(item => item, StringComparer.Ordinal).Select(routeId =>
        {
            var beforeRoute = RouteQuality(production.Links[routeId]);
            var afterRoute = RouteQuality(current[routeId]);
            return new
            {
                routeId,
                beforeLength = beforeRoute.Length,
                afterLength = afterRoute.Length,
                lengthIncrease = afterRoute.Length - beforeRoute.Length,
                beforeBends = beforeRoute.Bends,
                afterBends = afterRoute.Bends,
                envelopeExpansion = afterRoute.Envelope - beforeRoute.Envelope
            };
        }).OrderByDescending(item => item.lengthIncrease).ThenBy(item => item.routeId, StringComparer.Ordinal).Take(10).ToArray();
        var boundaryInteractions = interactions.Where(item =>
            acceptedRouteIds.Contains(item.FirstRouteId) != acceptedRouteIds.Contains(item.SecondRouteId)).ToArray();
        var report = new
        {
            mode = "DevelopmentOnlyCommonAuthorityTrial",
            normalProductionExposure = false,
            eligibleRoutes = observation.Routes.Count(item => item.Eligible),
            eligibleClosedComponents = closure.Components.Count(item => item.Disposition == CommonAuthorityComponentDisposition.Eligible),
            rejectedPreExecutionComponents = attempts.Count(item => item.Status == "rejected before execution"),
            acceptedComponents = attempts.Count(item => item.Status == "accepted"),
            rejectedPostValidationComponents = attempts.Count(item => item.Status == "rejected after validation"),
            routesRegenerated = acceptedRouteIds.Count,
            legacyRoutesRetained = current.Count - acceptedRouteIds.Count,
            componentAttempts = attempts,
            commonRegions = common.Regions.Select(item => new
            {
                item.Region.EnvelopeIdentity,
                allowedMinimum = item.Region.AllowedAxisRange.Minimum,
                allowedMaximum = item.Region.AllowedAxisRange.Maximum,
                availableExtent = item.Region.AllowedAxisRange.Length,
                item.Assignment.RequiredExtent,
                missingExtent = Math.Max(0, item.Assignment.RequiredExtent - item.Region.AllowedAxisRange.Length),
                demandCount = item.Assignment.RailsByDemandId.Count,
                movementScope = item.Region.MovementScope?.ToString()
            }).ToArray(),
            advisoryCleanCrossovers = closure.AdvisoryCrossovers.Count,
            beforeFindings = FullCounts(production.Nodes, production.Links, production.Traceability, settings.Layout.ParallelLaneSpacing),
            afterFindings = FullCounts(production.Nodes, current, finalValidation, settings.Layout.ParallelLaneSpacing),
            commonRouteQualityBefore = beforeQuality,
            commonRouteQualityAfter = afterQuality,
            largestRouteLengthIncreases = routeChanges,
            routesFlaggedUnreasonable = routeChanges.Where(item =>
                item.afterLength > Math.Max(item.beforeLength * 2, item.beforeLength + settings.Layout.NodeWidth * 4) ||
                item.afterBends > item.beforeBends + 4).Select(item => item.routeId).ToArray(),
            commonLegacyBoundaryInteractions = boundaryInteractions.GroupBy(item => item.Kind)
                .OrderBy(item => item.Key, StringComparer.Ordinal)
                .ToDictionary(item => item.Key, item => item.Count(), StringComparer.Ordinal),
            phaseMicroseconds = phase,
            repairOwnership = new
            {
                routeRepairCoordinator = false,
                separateOverlappingCorners = false,
                traversalFallback = false,
                legacyCapacityExpansion = false
            }
        };
        return new DevelopmentCommonAuthorityApplication(layout,
            JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static IReadOnlyList<AssignedRail> Rails(
        AdjacentDownwardObservationReport observation,
        IReadOnlyDictionary<string, CommonRailRouteComparison> comparisons,
        string routeId)
    {
        var route = observation.Routes.Single(item => item.LogicalRouteId == routeId);
        var common = comparisons[routeId].CommonThroughRail;
        if (common is null) return Array.Empty<AssignedRail>();
        var departure = route.SelectedAssignedRails.Single(item => item.Role == RailSemanticRole.TerminalDeparture) with
        {
            Id = $"{routeId}:common-departure:assigned",
            OccupiedInterval = new AxisInterval(route.CanonicalAuthoritativePoints[0].Y, common.AxisCoordinate)
        };
        var arrival = route.SelectedAssignedRails.Single(item => item.Role == RailSemanticRole.TerminalArrival) with
        {
            Id = $"{routeId}:common-arrival:assigned",
            OccupiedInterval = new AxisInterval(common.AxisCoordinate,
                route.CanonicalAuthoritativePoints[route.CanonicalAuthoritativePoints.Count - 1].Y)
        };
        return new[] { departure, common, arrival };
    }

    private static IReadOnlyList<CommonAuthorityInteraction> DiscoverInteractions(
        IReadOnlyDictionary<string, LinkLayout> links,
        int spacing)
    {
        var ordered = links.Values.OrderBy(item => item.Link.Id, StringComparer.Ordinal).ToArray();
        var result = new List<CommonAuthorityInteraction>();
        for (var left = 0; left < ordered.Length; left++)
        for (var right = left + 1; right < ordered.Length; right++)
        {
            foreach (var contact in CanonicalRouteContactDiscovery.Discover(ordered[left], ordered[right], spacing))
            {
                var clean = contact.Contact.Kind == CanonicalContactKind.CleanPerpendicularCrossover;
                if (!clean && !ContactInteractionPolicy.CreatesFinalGeometryEdge(contact.Contact.Kind)) continue;
                result.Add(new CommonAuthorityInteraction(
                    contact.FirstRouteId, contact.SecondRouteId, contact.Contact.Kind.ToString(), !clean));
            }
            if (ordered[left].Link.SourceId == ordered[right].Link.SourceId ||
                ordered[left].Link.TargetId == ordered[right].Link.TargetId)
                result.Add(new CommonAuthorityInteraction(
                    ordered[left].Link.Id, ordered[right].Link.Id, "TerminalSideCompetition", true));
        }
        return result;
    }

    private static DevelopmentTrialComponentAttempt Attempt(
        CommonAuthorityComponent component,
        string status,
        string reason,
        IReadOnlyList<string> details) =>
        new(component.Id, status, reason,
            component.Routes.Select(item => item.LogicalRouteId).ToArray(), details);

    private static object Quality(IEnumerable<LinkLayout> routes)
    {
        var values = routes.Select(RouteQuality).ToArray();
        return new
        {
            routeCount = values.Length,
            totalLength = values.Sum(item => item.Length),
            bendCount = values.Sum(item => item.Bends),
            maximumEnvelope = values.Select(item => item.Envelope).DefaultIfEmpty(0).Max()
        };
    }

    private static (int Length, int Bends, int Envelope) RouteQuality(LinkLayout route)
    {
        var points = new[] { route.SourcePoint }.Concat(route.Points).Concat(new[] { route.TargetPoint }).ToArray();
        return (points.Zip(points.Skip(1), Distance).Sum(), Math.Max(0, points.Length - 2),
            points.Length == 0 ? 0 : points.Max(item => item.X) - points.Min(item => item.X) +
                points.Max(item => item.Y) - points.Min(item => item.Y));
    }

    private static int Distance(Point first, Point second) => Math.Abs(first.X - second.X) + Math.Abs(first.Y - second.Y);
    private static IReadOnlyDictionary<string, int> Counts(IEnumerable<TraceabilityViolation> findings) =>
        findings.GroupBy(item => item.Code).OrderBy(item => item.Key)
            .ToDictionary(item => item.Key.ToString(), item => item.Count(), StringComparer.Ordinal);
    private static IReadOnlyDictionary<string, int> FullCounts(
        IReadOnlyDictionary<string, NodeLayout> nodes,
        IReadOnlyDictionary<string, LinkLayout> links,
        TraceabilityValidationResult validation,
        int spacing)
    {
        var counts = Counts(validation.Violations).ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        var bendContact = 0;
        var endpointInterior = 0;
        var clean = 0;
        var ordered = links.Values.OrderBy(item => item.Link.Id, StringComparer.Ordinal).ToArray();
        for (var left = 0; left < ordered.Length; left++)
        for (var right = left + 1; right < ordered.Length; right++)
        foreach (var contact in CanonicalRouteContactDiscovery.Discover(ordered[left], ordered[right], spacing))
        {
            if (contact.Contact.Kind == CanonicalContactKind.BendInvolvedPerpendicularContact) bendContact++;
            if (contact.Contact.Kind == CanonicalContactKind.EndpointToInterior) endpointInterior++;
            if (contact.Contact.Kind == CanonicalContactKind.CleanPerpendicularCrossover) clean++;
        }
        counts["BendInvolvedPerpendicularContact"] = bendContact;
        counts["EndpointToInterior"] = endpointInterior;
        counts["NonOrthogonalSegment"] = links.Values.Sum(link =>
        {
            var points = new[] { link.SourcePoint }.Concat(link.Points).Concat(new[] { link.TargetPoint }).ToArray();
            return points.Zip(points.Skip(1), (a, b) => new Segment(a, b)).Count(item => !item.IsOrthogonal);
        });
        counts["CleanPerpendicularCrossover"] = clean;
        counts["TraceabilityViolation"] = validation.Violations.Count(item => item.Code != TraceabilityViolationCode.PerpendicularCrossing);
        return counts;
    }
    private static long Elapsed(Stopwatch timer)
    {
        timer.Stop();
        return timer.ElapsedTicks * 1_000_000 / Stopwatch.Frequency;
    }
}
