using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Xml.Linq;
using StandardIo.ArchitectureDiagram.Core.Models;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal static class DrawioDiagnosticReportBuilder
{
    private sealed record DiagnosticContactFact(
        string FirstRouteId,
        string SecondRouteId,
        CanonicalContact Contact);

    private sealed record FoundationCostObservation(
        long ConstraintMergeAndMaterializationMicroseconds,
        long InvalidationMicroseconds,
        long TerminalComponentConstructionMicroseconds,
        int InvalidationCount,
        int UnresolvedTerminalComponents,
        int ResolvedTerminalComponents);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static string BuildJson(
        RenderLayout layout,
        CoordinateOwnershipCompilation ownership,
        IReadOnlyList<TraceabilityViolation> enforced,
        LayoutSettings layoutSettings,
        IReadOnlyList<PipelineStageMetric> stageTimings,
        bool diagnosticReuse,
        InterLayerReport? bandReport = null)
    {
        var requiredSpacing = layoutSettings.ParallelLaneSpacing;
        var projectNames = layout.Graph.Projects.ToDictionary(project => project.Id, project => project.Name, StringComparer.Ordinal);
        var routes = enforced
            .GroupBy(violation => violation.EdgeId, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group =>
            {
                var link = layout.Links[group.Key];
                var source = layout.Nodes[link.Link.SourceId];
                var target = layout.Nodes[link.Link.TargetId];
                var selected = SelectedCandidate(layout, group.Key);
                var evaluations = CandidateEvaluations(layout, group.Key);
                var decision = CandidateDecision(layout, group.Key);
                var physical = ownership.Segments
                    .Where(segment => segment.LogicalEdgeId == group.Key)
                    .OrderBy(segment => segment.SegmentIndex)
                    .Select(segment => segment.Id)
                    .ToArray();
                var failedCorridors = layout.Corridors.SegmentMappings
                    .Where(mapping => mapping.EdgeId == group.Key && layout.Lanes.FailedCorridorIds.Contains(mapping.CorridorId))
                    .Select(mapping => mapping.CorridorId)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(id => id, StringComparer.Ordinal)
                    .ToArray();

                return new
                {
                    logicalRouteId = group.Key,
                    sourceNode = new
                    {
                        id = source.Node.Id,
                        name = source.Node.Name,
                        fullName = source.Node.FullName,
                        projectId = source.Node.ProjectId,
                        project = ProjectName(projectNames, source.Node.ProjectId),
                        visualLayer = source.Depth
                    },
                    targetNode = new
                    {
                        id = target.Node.Id,
                        name = target.Node.Name,
                        fullName = target.Node.FullName,
                        projectId = target.Node.ProjectId,
                        project = ProjectName(projectNames, target.Node.ProjectId),
                        visualLayer = target.Depth
                    },
                    selectedCandidateId = selected?.Signature.Value ?? decision?.FinalSignature,
                    selectedPoints = CompletePoints(link).Select(ToPoint).ToArray(),
                    physicalDrawioSegmentIds = physical,
                    selectionScore = evaluations.FirstOrDefault(item => item.IsSelected)?.Score.ToString(),
                    selectionReason = decision?.Reason,
                    fallbackStatus = new
                    {
                        used = failedCorridors.Length > 0 || selected is null,
                        failedCorridorIds = failedCorridors,
                        traversalDiagnostics = layout.Traversals.Traversals.TryGetValue(group.Key, out var traversal)
                            ? traversal.Diagnostics.Select(item => (object)new
                            {
                                item.Code,
                                item.Message,
                                item.SegmentIndex,
                                item.JunctionId
                            }).ToArray()
                            : Array.Empty<object>()
                    },
                    candidateAlternativesAvailable = evaluations.Count(item => !item.IsSelected),
                    candidates = evaluations.Select(item => new
                    {
                        id = item.Signature,
                        selected = item.IsSelected,
                        score = item.Score.ToString(),
                        scoreComponents = Score(item.Score),
                        reason = item.Reason
                    }).ToArray(),
                    violations = group.Select((violation, index) => Finding(violation, index + 1, requiredSpacing)).ToArray()
                };
            })
            .ToArray();

        var findings = enforced
            .OrderBy(violation => violation.EdgeId, StringComparer.Ordinal)
            .ThenBy(violation => violation.Code)
            .ThenBy(violation => violation.OtherEdgeId, StringComparer.Ordinal)
            .Select((violation, index) => Finding(violation, index + 1, requiredSpacing))
            .ToArray();
        var categories = enforced
            .GroupBy(CategoryName)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new
            {
                category = group.Key,
                uniqueLogicalRoutes = group.Select(item => item.EdgeId).Distinct(StringComparer.Ordinal).Count(),
                rawValidatorFindings = group.Count(),
                distinctPhysicalLocations = group
                    .SelectMany(item => item.Locations ?? Array.Empty<Point>())
                    .Distinct()
                    .Count()
            })
            .ToArray();
        var routeGeometry = layout.Links.Values
            .OrderBy(link => link.Link.Id, StringComparer.Ordinal)
            .Select(link => new
            {
                logicalRouteId = link.Link.Id,
                sourceId = link.Link.SourceId,
                targetId = link.Link.TargetId,
                selectedCandidateId = SelectedCandidate(layout, link.Link.Id)?.Signature.Value,
                selectedCandidatePoints = SelectedCandidate(layout, link.Link.Id)?.Points.Select(ToPoint).ToArray(),
                points = CompletePoints(link).Select(ToPoint).ToArray()
            })
            .ToArray();
        var nonOrthogonalSegments = NonOrthogonalSegments(layout, ownership, bandReport);
        var nodeTimer = Stopwatch.StartNew();
        var nodeWidthObservation = ObserveNodeWidths(layout, layoutSettings);
        nodeTimer.Stop();
        var contactTimer = Stopwatch.StartNew();
        var contactObservation = ObserveCanonicalContacts(layout, requiredSpacing);
        contactTimer.Stop();
        var foundationCosts = ObserveFoundationCosts(layout, layoutSettings);
        var adjacentDownward = ObserveAdjacentDownward(layout, bandReport, requiredSpacing, layoutSettings.LinkPadding);

        return JsonSerializer.Serialize(new
        {
            diagnostic = true,
            generatedAtUtc = DateTime.UtcNow,
            summary = new
            {
                uniqueRejectedRoutes = routes.Length,
                enforcedFindings = enforced.Count,
                allValidatorFindings = layout.Traceability.Violations.Count,
                preRepairFindings = layout.PreRepairTraceability.Violations.Count,
                repairAttempts = layout.RepairAttempts.Count,
                repairsApplied = layout.RepairAttempts.Count(attempt => attempt.Applied),
                repairWorkUsed = layout.RepairWorkUsed,
                repairBudgetExhausted = layout.RepairBudgetExhausted,
                repairRunReason = layout.RepairRunReason,
                layoutRevision = layout.LayoutRevision.Value,
                layoutRevisionsCreated = layout.LayoutRevision.Value + 1,
                routeRevisionsCreated = layout.Links.Values.Sum(link => link.RouteState.Revision),
                staleStateRejections = layout.Links.Values.Sum(link =>
                    link.RouteState.History.Count(snapshot => snapshot.CompilationStatus == LogicalRouteCompilationStatus.Rejected)),
                routesInvalidated = layout.RoutesInvalidated,
                routePairsRevalidated = layout.RoutePairsRevalidated,
                corridorRebuildCount = layout.CorridorRebuildCount,
                capacityFailures = layout.CapacityFailureCount,
                capacityExpansions = layout.CapacityExpansionCount,
                diagnosticReuse
            },
            categories,
            findings,
            routes,
            routeGeometry,
            consolidatedFoundation = new
            {
                nodeWidths = nodeWidthObservation,
                contacts = contactObservation,
                directInvalidationCount = foundationCosts.InvalidationCount,
                terminalUnresolvedComponents = foundationCosts.UnresolvedTerminalComponents,
                terminalResolvedComponents = foundationCosts.ResolvedTerminalComponents,
                adjacentDownward,
                timings = new
                {
                    nodeAndTerminalDemandMicroseconds = ToMicroseconds(nodeTimer),
                    canonicalContactClassificationMicroseconds = ToMicroseconds(contactTimer),
                    foundationCosts.ConstraintMergeAndMaterializationMicroseconds,
                    foundationCosts.InvalidationMicroseconds,
                    foundationCosts.TerminalComponentConstructionMicroseconds
                }
            },
            stageTimings,
            interLayerBands = bandReport is null ? null : new
            {
                telemetry = bandReport.Telemetry,
                bands = bandReport.Bands.Select(band => new
                {
                    id = band.Id.ToString(),
                    band.Id.UpperLayer,
                    band.Id.LowerLayer,
                    layoutRevision = band.Id.LayoutRevision.Value,
                    band.CurrentExtent,
                    band.RequiredExtent,
                    band.MissingExtent,
                    band.OverlapGroupCount,
                    band.MaximumSimultaneousOverlap,
                    band.HypotheticalLaneCount,
                    band.ReturnLaneCount,
                    band.ReturnRegions,
                    band.Memberships,
                    band.Demands,
                    band.UnsupportedShapes
                }).ToArray(),
                findingCorrelations = bandReport.FindingCorrelations
            },
            nonOrthogonalSegments,
            groupedSpacing = layout.GroupedSpacingPlan is null ? null : new
            {
                supportedSubset = "adjacent-layer-downward",
                convergenceIterations = layout.GroupedSpacingIterations,
                telemetry = layout.GroupedSpacingPlan.Telemetry,
                constraints = layout.GroupedSpacingPlan.Constraints,
                invalidatedRoutes = layout.GroupedSpacingPlan.InvalidatedRoutes,
                groups = layout.GroupedSpacingPlan.Groups.Select(group => new
                {
                    group.Id,
                    orientation = "horizontal-band",
                    routes = group.Demands.Select(item => item.LogicalEdgeIdentity)
                        .Distinct(StringComparer.Ordinal).OrderBy(item => item, StringComparer.Ordinal).ToArray(),
                    segments = group.Demands.Select(item => item.Id).ToArray(),
                    group.CurrentLaneCount,
                    group.RequiredLaneCount,
                    group.CurrentExtent,
                    group.RequiredExtent,
                    group.MissingExtent,
                    group.MovementScope
                }).ToArray()
            },
            repair = new
            {
                preRepairFindings = layout.PreRepairTraceability.Violations.Select((finding, index) =>
                    Finding(finding, index + 1, requiredSpacing)).ToArray(),
                attempts = layout.RepairAttempts,
                postRepairFindings = layout.Traceability.Violations.Select((finding, index) =>
                    Finding(finding, index + 1, requiredSpacing)).ToArray(),
                layout.RepairWorkUsed,
                layout.RepairBudgetExhausted,
                layout.RepairRunReason
            }
        }, JsonOptions);
    }

    private static FoundationCostObservation ObserveFoundationCosts(RenderLayout layout, LayoutSettings settings)
    {
        var incoming = layout.Graph.Links.GroupBy(link => link.TargetId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var outgoing = layout.Graph.Links.GroupBy(link => link.SourceId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var separation = LinkConnectionDemandCalculator.AttachmentSeparation(
            settings.EdgePortSpacing, settings.ParallelLaneSpacing);
        var changedNodeIds = new HashSet<string>(StringComparer.Ordinal);
        var constraintTimer = Stopwatch.StartNew();
        var constraints = new GenerationConstraintStore();
        var basis = new Dictionary<MovementScopeIdentity, Rect>();
        foreach (var node in layout.Graph.Nodes.OrderBy(node => node.Id, StringComparer.Ordinal))
        {
            var incomingCount = incoming.TryGetValue(node.Id, out var inc) ? inc : 0;
            var outgoingCount = outgoing.TryGetValue(node.Id, out var outgoingValue) ? outgoingValue : 0;
            var textWidth = LinkConnectionDemandCalculator.EstimatedTextWidth(node.Name, node.FullName);
            var demand = LinkConnectionDemandCalculator.Measure(
                node.Id, settings.NodeWidth, textWidth, incomingCount, outgoingCount,
                separation, settings.LinkNodeWidthPadding);
            var legacyWidth = Math.Max(settings.NodeWidth, Math.Max(
                textWidth + settings.LinkNodeWidthPadding,
                Math.Max(0, incomingCount + outgoingCount - 1) * settings.EdgePortSpacing +
                settings.LinkNodeWidthPadding));
            if (legacyWidth != demand.RequiredWidth) changedNodeIds.Add(node.Id);
            var scope = new MovementScopeIdentity(MovementScopeKind.Node, node.Id);
            basis[scope] = layout.Nodes[node.Id].Rect with { Width = settings.NodeWidth };
            constraints.Merge(new GenerationConstraint(
                new GenerationConstraintKey(scope, GenerationConstraintKind.MinimumWidth),
                demand.RequiredWidth,
                "terminal demand"));
        }
        _ = constraints.Materialize(basis);
        constraintTimer.Stop();

        var invalidationTimer = Stopwatch.StartNew();
        var routeReferences = layout.Links.Values.Select(link => new SemanticLinkReference(
            link.Link.Id, link.Link.SourceId, link.Link.TargetId, new RouteRevision(link.RouteState.Revision)));
        var invalidations = changedNodeIds.Count == 0
            ? Array.Empty<LinkInvalidation>()
            : LinkInvalidationCalculator.ForChangedNodes(
                routeReferences, changedNodeIds, LinkInvalidationCause.EndpointResized,
                layout.LayoutRevision, layout.LayoutRevision.Next()).ToArray();
        invalidationTimer.Stop();

        var componentTimer = Stopwatch.StartNew();
        var claims = layout.Links.Values.SelectMany(link => new[]
        {
            new LinkConnectionClaim(link.Link.Id, link.Link.SourceId,
                LinkConnectionSide.OutgoingBottom, "bottom", link.SourcePoint.X),
            new LinkConnectionClaim(link.Link.Id, link.Link.TargetId,
                LinkConnectionSide.IncomingTop, "top", link.TargetPoint.X)
        }).ToArray();
        var ids = layout.Links.Keys.OrderBy(id => id, StringComparer.Ordinal).ToArray();
        var unresolved = ConflictComponentBuilder.Build(ids, id => id, LinkConnectionInteractions.BeforeAllocation(claims));
        var resolved = ConflictComponentBuilder.Build(ids, id => id, LinkConnectionInteractions.AfterAllocation(claims));
        componentTimer.Stop();

        return new FoundationCostObservation(
            ToMicroseconds(constraintTimer),
            ToMicroseconds(invalidationTimer),
            ToMicroseconds(componentTimer),
            invalidations.Length,
            unresolved.Count,
            resolved.Count);
    }

    private static object? ObserveAdjacentDownward(
        RenderLayout layout,
        InterLayerReport? bandReport,
        int requiredSpacing,
        int padding)
    {
        if (bandReport is null) return null;
        var memberships = bandReport.Bands.SelectMany(item => item.Memberships)
            .GroupBy(item => item.LogicalEdgeIdentity, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<InterLayerLinkMembership>)group.ToArray(), StringComparer.Ordinal);
        var demands = bandReport.Bands.SelectMany(item => item.Demands)
            .GroupBy(item => item.LogicalEdgeIdentity, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<InterLayerLinkDemand>)group.ToArray(), StringComparer.Ordinal);
        var ranges = bandReport.Bands.ToDictionary(
            item => item.Id, item => new AxisInterval(item.UpperBoundary, item.LowerBoundary));
        var routeRevision = bandReport.Telemetry.RouteRevision;
        var duplicatedExposureTree = layout.Graph.PlacementParentByNode.Count == 0 &&
            layout.Graph.Nodes.Any(item => item.Id.StartsWith("tree_", StringComparison.Ordinal));
        var contexts = layout.Links.Values.OrderBy(item => item.Link.Id, StringComparer.Ordinal).Select(route =>
            new AdjacentDownwardLinkContext(
                route,
                layout.Nodes[route.Link.SourceId],
                layout.Nodes[route.Link.TargetId],
                layout.LayoutRevision,
                routeRevision,
                memberships.TryGetValue(route.Link.Id, out var routeMemberships)
                    ? routeMemberships : Array.Empty<InterLayerLinkMembership>(),
                demands.TryGetValue(route.Link.Id, out var routeDemands)
                    ? routeDemands : Array.Empty<InterLayerLinkDemand>(),
                ranges,
                layout.Corridors,
                layout.Lanes,
                layout.GroupedSpacingPlan,
                duplicatedExposureTree)).ToArray();
        var observation = AdjacentDownwardLinkDemandDiscovery.Observe(contexts);
        var projection = AdjacentDownwardComponentProjector.Project(observation, requiredSpacing);
        var common = AdjacentDownwardCommonAuthorityObserver.Observe(observation, requiredSpacing, padding);
        var eligible = observation.Routes.Where(item => item.Eligible).ToArray();
        var assignedPairs = new HashSet<string>(projection.AssignedEdges.Select(EdgePair), StringComparer.Ordinal);
        var removedEdges = projection.UnassignedEdges
            .Where(item => !assignedPairs.Contains(EdgePair(item)))
            .ToArray();
        var routesInRemovedEdges = removedEdges.SelectMany(item => new[] { item.FirstId, item.SecondId })
            .Distinct(StringComparer.Ordinal).OrderBy(item => item, StringComparer.Ordinal).ToArray();
        var assignedSources = eligible.SelectMany(item => item.ExistingSegmentMappings).GroupBy(item => item.Source)
            .OrderBy(item => item.Key).ToDictionary(item => item.Key.ToString(), item => item.Count());
        return new
        {
            eligibleRoutes = eligible.Length,
            rejectedRoutes = observation.Routes.Count - eligible.Length,
            rejections = observation.Routes.Where(item => !item.Eligible).GroupBy(item => item.RejectionReason)
                .OrderBy(item => item.Key).ToDictionary(item => item.Key?.ToString() ?? "Unknown", item => item.Count()),
            demandsByRole = eligible.SelectMany(item => item.Demands).GroupBy(item => item.Role)
                .OrderBy(item => item.Key).ToDictionary(item => item.Key.ToString(), item => item.Count()),
            transitions = eligible.Sum(item => item.Transitions.Count),
            assignedSources,
            parity = eligible.GroupBy(item => item.Parity).OrderBy(item => item.Key)
                .ToDictionary(item => item.Key.ToString(), item => item.Count()),
            unassignedComponents = projection.UnassignedComponents.Count,
            assignedComponents = projection.AssignedComponents.Count,
            largestAssignedComponent = projection.AssignedComponents.Select(item => item.Members.Count).DefaultIfEmpty(0).Max(),
            edgesRemovedAfterAssignment = removedEdges.Length,
            routesInRemovedEdges,
            unassignedEdges = projection.UnassignedEdges.GroupBy(item => item.Cause).OrderBy(item => item.Key)
                .ToDictionary(item => item.Key, item => item.Count()),
            assignedEdges = projection.AssignedEdges.GroupBy(item => item.Cause).OrderBy(item => item.Key)
                .ToDictionary(item => item.Key, item => item.Count()),
            retainedAssignedConflictDetails = projection.AssignedEdges.Select(edge => new
            {
                edge.FirstId,
                edge.SecondId,
                edge.Cause,
                first = RetainedRouteEvidence(edge.FirstId),
                second = RetainedRouteEvidence(edge.SecondId)
            }),
            timings = new
            {
                observation.DemandProductionMicroseconds,
                observation.ExistingLaneAdaptationMicroseconds,
                observation.ReconstructionMicroseconds,
                observation.ParityComparisonMicroseconds,
                componentProjectionMicroseconds = projection.ElapsedMicroseconds
            },
            commonAssignment = new
            {
                regions = common.Regions.Count,
                components = common.Regions.Sum(item => item.Assignment.Components.Count),
                demandsAssigned = common.Regions.Sum(item => item.Assignment.SegmentsByDemandId.Count),
                largestComponent = common.Regions.SelectMany(item => item.Assignment.Components)
                    .Select(item => item.Demands.Count).DefaultIfEmpty(0).Max(),
                existingParity = common.Routes.SelectMany(item => item.ExistingParity)
                    .GroupBy(item => $"{item.Key}:{item.Value}").OrderBy(item => item.Key)
                    .ToDictionary(item => item.Key, item => item.Count()),
                reconstructionParity = common.Routes.GroupBy(item => item.ReconstructionParity)
                    .OrderBy(item => item.Key).ToDictionary(item => item.Key.ToString(), item => item.Count()),
                constraintProposals = common.Regions.Count(item => item.ConstraintProposal is not null),
                requiredExtents = common.Regions.Select(item => new
                {
                    item.Region.EnvelopeIdentity,
                    currentExtent = item.Region.AllowedAxisRange.Length,
                    item.Assignment.RequiredExtent,
                    missingExtent = Math.Max(0, item.Assignment.RequiredExtent - item.Region.AllowedAxisRange.Length),
                    movementScope = item.Region.MovementScope?.ToString(),
                    proposal = item.ConstraintProposal
                }),
                timings = new
                {
                    common.AssignmentMicroseconds,
                    conflictDiscoveryMicroseconds = common.Regions.Sum(item => item.Assignment.ConflictDiscoveryMicroseconds),
                    componentConstructionMicroseconds = common.Regions.Sum(item => item.Assignment.ComponentConstructionMicroseconds),
                    laneAssignmentMicroseconds = common.Regions.Sum(item => item.Assignment.SlotAssignmentMicroseconds),
                    extentCalculationMicroseconds = common.Regions.Sum(item => item.Assignment.ExtentCalculationMicroseconds),
                    common.ConstraintProjectionMicroseconds,
                    common.ReconstructionMicroseconds,
                    common.ParityComparisonMicroseconds
                },
                routes = common.Routes
            },
            routes = observation.Routes.Select(item => new
            {
                item.LogicalRouteId,
                item.Eligible,
                rejectionReason = item.RejectionReason?.ToString(),
                demands = item.Demands.Select(demand => new
                {
                    demand.Id,
                    role = demand.Role.ToString(),
                    orientation = demand.Orientation.ToString(),
                    occupiedMinimum = demand.OccupiedInterval.Minimum,
                    occupiedMaximum = demand.OccupiedInterval.Maximum,
                    allowedAxisMinimum = demand.AllowedAxisRange.Minimum,
                    allowedAxisMaximum = demand.AllowedAxisRange.Maximum,
                    demand.PreferredAxis,
                    demand.ConnectionOrder,
                    demand.TurnOrder,
                    movementScope = demand.MovementScope?.ToString(),
                    placementRevision = demand.PlacementRevision.Value,
                    routeRevision = demand.RouteRevision.Value
                }),
                laneMappings = item.ExistingSegmentMappings.Select(mapping => new
                {
                    source = mapping.Source.ToString(),
                    mapping.Rail.SlotIndex,
                    mapping.Rail.AxisCoordinate,
                    mapping.SpecializedMetadata
                }),
                transitions = item.Transitions.Select(transition => new
                {
                    transition.Id,
                    transition.Order,
                    transition.Turn
                }),
                parity = item.Parity.ToString(),
                item.CanonicalAuthoritativePoints,
                item.ReconstructedPoints,
                item.Diagnostics
            })
        };

        object? RetainedRouteEvidence(string routeId)
        {
            var route = observation.Routes.FirstOrDefault(item => item.LogicalRouteId == routeId);
            if (route is null) return null;
            var logical = layout.Links[routeId];
            var sourceNode = layout.Nodes[logical.Link.SourceId];
            var targetNode = layout.Nodes[logical.Link.TargetId];
            return new
            {
                route.LogicalRouteId,
                source = new { sourceNode.Node.Id, sourceNode.Node.Name, sourceNode.Node.FullName, sourceNode.Rect },
                target = new { targetNode.Node.Id, targetNode.Node.Name, targetNode.Node.FullName, targetNode.Rect },
                assignedRails = route.SelectedAssignedLinkSegments.Select(item => new
                {
                    role = item.Role.ToString(),
                    item.AxisCoordinate,
                    item.SlotIndex,
                    item.OccupiedInterval
                }),
                turns = route.Transitions.Select(item => item.Turn),
                route.CanonicalAuthoritativePoints
            };
        }

        string EdgePair(ConflictEdge edge) => string.CompareOrdinal(edge.FirstId, edge.SecondId) <= 0
            ? $"{edge.FirstId}\u001f{edge.SecondId}"
            : $"{edge.SecondId}\u001f{edge.FirstId}";
    }

    private static long ToMicroseconds(Stopwatch timer) =>
        timer.ElapsedTicks * 1000000 / Stopwatch.Frequency;

    private static object ObserveNodeWidths(RenderLayout layout, LayoutSettings settings)
    {
        var separation = LinkConnectionDemandCalculator.AttachmentSeparation(
            settings.EdgePortSpacing, settings.ParallelLaneSpacing);
        var incoming = layout.Graph.Links.GroupBy(link => link.TargetId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var outgoing = layout.Graph.Links.GroupBy(link => link.SourceId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var nodes = layout.Graph.Nodes.OrderBy(node => node.Id, StringComparer.Ordinal).Select(node =>
        {
            var incomingCount = incoming.TryGetValue(node.Id, out var inc) ? inc : 0;
            var outgoingCount = outgoing.TryGetValue(node.Id, out var outgoingValue) ? outgoingValue : 0;
            var measuredText = LinkConnectionDemandCalculator.EstimatedTextWidth(node.Name, node.FullName);
            var demand = LinkConnectionDemandCalculator.Measure(
                node.Id, settings.NodeWidth, measuredText, incomingCount, outgoingCount,
                separation, settings.LinkNodeWidthPadding);
            var legacyWidth = Math.Max(settings.NodeWidth, Math.Max(
                measuredText + settings.LinkNodeWidthPadding,
                Math.Max(0, incomingCount + outgoingCount - 1) * settings.EdgePortSpacing +
                settings.LinkNodeWidthPadding));
            var winningRequirements = new[]
            {
                new { Name = "Current", Value = settings.NodeWidth },
                new { Name = "Text", Value = demand.TextSpaceRequirement },
                new { Name = "Incoming", Value = demand.IncomingAttachmentSpaceRequirement },
                new { Name = "Outgoing", Value = demand.OutgoingAttachmentSpaceRequirement }
            }.Where(item => item.Value == demand.RequiredWidth).Select(item => item.Name).ToArray();
            var actuallyResized = legacyWidth != demand.RequiredWidth;
            return new
            {
                nodeId = node.Id,
                node.Name,
                node.FullName,
                previousWidth = legacyWidth,
                requiredWidth = demand.RequiredWidth,
                actualWidth = layout.Nodes[node.Id].Rect.Width,
                demand.TextSpaceRequirement,
                demand.IncomingAttachmentSpaceRequirement,
                demand.OutgoingAttachmentSpaceRequirement,
                incomingCount,
                outgoingCount,
                winningRequirements,
                actualResize = actuallyResized,
                resizeCause = !actuallyResized ? "None" : string.Join("+", winningRequirements.Where(item => item != "Current")),
                changedFromPreviousFormula = actuallyResized,
                expandedFromConfiguredWidth = demand.RequiredWidth > settings.NodeWidth,
                reason = demand.RequiredWidth == settings.NodeWidth ? "CurrentWidth" :
                    demand.RequiredWidth == demand.TextSpaceRequirement ? "Text" :
                    demand.IncomingAttachmentSpaceRequirement >= demand.OutgoingAttachmentSpaceRequirement
                        ? "IncomingAttachments" : "OutgoingAttachments"
            };
        }).ToArray();
        var resizedIds = new HashSet<string>(nodes.Where(node => node.changedFromPreviousFormula)
            .Select(node => node.nodeId), StringComparer.Ordinal);
        return new
        {
            configuredWidth = settings.NodeWidth,
            totalHorizontalPadding = settings.LinkNodeWidthPadding,
            attachmentSeparation = separation,
            alreadySufficient = nodes.Count(node => !node.expandedFromConfiguredWidth),
            expandedByText = nodes.Count(node => node.expandedFromConfiguredWidth && node.reason == "Text"),
            expandedByIncomingAttachments = nodes.Count(node => node.expandedFromConfiguredWidth && node.reason == "IncomingAttachments"),
            expandedByOutgoingAttachments = nodes.Count(node => node.expandedFromConfiguredWidth && node.reason == "OutgoingAttachments"),
            changedFromPreviousFormula = resizedIds.Count,
            maximumAbsoluteChangeFromPreviousFormula = nodes.Select(node => Math.Abs(node.requiredWidth - node.previousWidth)).DefaultIfEmpty(0).Max(),
            observationalIncidentLinkInvalidations = layout.Graph.Links.Count(link =>
                resizedIds.Contains(link.SourceId) || resizedIds.Contains(link.TargetId)),
            nodes
        };
    }

    private static object ObserveCanonicalContacts(RenderLayout layout, int requiredSpacing)
    {
        var segments = layout.Links.Values.OrderBy(link => link.Link.Id, StringComparer.Ordinal)
            .SelectMany(link => ContactSegments(link).Select(segment => new
            {
                link.Link.Id,
                Segment = segment,
                MinimumX = Math.Min(segment.Segment.Start.X, segment.Segment.End.X),
                MaximumX = Math.Max(segment.Segment.Start.X, segment.Segment.End.X)
            }))
            .OrderBy(item => item.MinimumX).ThenBy(item => item.MaximumX).ThenBy(item => item.Id, StringComparer.Ordinal)
            .ToArray();
        var counts = Enum.GetValues(typeof(CanonicalContactKind)).Cast<CanonicalContactKind>()
            .ToDictionary(kind => kind, _ => 0);
        var facts = new List<DiagnosticContactFact>();
        long comparisons = 0;
        for (var left = 0; left < segments.Length; left++)
        {
            for (var right = left + 1; right < segments.Length; right++)
            {
                if (segments[right].MinimumX > segments[left].MaximumX + requiredSpacing) break;
                if (string.Equals(segments[left].Id, segments[right].Id, StringComparison.Ordinal)) continue;
                comparisons++;
                var contact = CanonicalContactClassifier.Classify(
                    segments[left].Segment, segments[right].Segment, requiredSpacing);
                counts[contact.Kind]++;
                if (contact.Kind == CanonicalContactKind.Disjoint) continue;
                var firstId = string.CompareOrdinal(segments[left].Id, segments[right].Id) <= 0
                    ? segments[left].Id : segments[right].Id;
                var secondId = string.CompareOrdinal(segments[left].Id, segments[right].Id) <= 0
                    ? segments[right].Id : segments[left].Id;
                facts.Add(new DiagnosticContactFact(firstId, secondId, contact));
            }
        }
        var policyTimer = Stopwatch.StartNew();
        var projectedFacts = facts.Select(fact => new
        {
            fact.FirstRouteId,
            fact.SecondRouteId,
            kind = fact.Contact.Kind.ToString(),
            fact.Contact.Point,
            fact.Contact.PositiveOverlap,
            fact.Contact.Separation,
            createsFinalGeometryEdge = ContactInteractionPolicy.CreatesFinalGeometryEdge(fact.Contact.Kind)
        }).ToArray();
        policyTimer.Stop();
        return new
        {
            comparisons,
            counts = counts.OrderBy(item => item.Key).ToDictionary(item => item.Key.ToString(), item => item.Value),
            policyProjectionMicroseconds = ToMicroseconds(policyTimer),
            facts = projectedFacts
        };
    }

    private static IReadOnlyList<ContactSegment> ContactSegments(LinkLayout link)
    {
        var points = CompletePoints(link).ToArray();
        var segments = points.Zip(points.Skip(1), (start, end) => new Segment(start, end)).ToArray();
        return segments.Select((segment, index) => new ContactSegment(
            segment,
            index > 0 ? segments[index - 1] : (Segment?)null,
            index + 1 < segments.Length ? segments[index + 1] : (Segment?)null,
            StartIsTerminal: index == 0,
            EndIsTerminal: index == segments.Length - 1)).ToArray();
    }

    private static IReadOnlyList<NonOrthogonalSegmentDiagnostic> NonOrthogonalSegments(
        RenderLayout layout,
        CoordinateOwnershipCompilation ownership,
        InterLayerReport? bandReport)
    {
        var anchors = new HashSet<Point>(ownership.Anchors.Select(anchor => anchor.AbsolutePoint));
        return layout.Links.Values.OrderBy(link => link.Link.Id, StringComparer.Ordinal).SelectMany(link =>
        {
            var points = CompletePoints(link).ToArray();
            var physical = ownership.Segments.Where(segment => segment.LogicalEdgeId == link.Link.Id)
                .OrderBy(segment => segment.SegmentIndex).ToArray();
            var physicalDiagnostics = physical.Select(segment => new PhysicalSegmentDiagnostic(
                segment.Id, segment.SegmentIndex, segment.Role, segment.OwnerProjectId,
                new[] { segment.AbsoluteStart }.Concat(segment.AbsoluteWaypoints).Concat(new[] { segment.AbsoluteEnd }).ToArray()))
                .ToArray();
            var reconstructed = new List<Point>();
            foreach (var segment in physicalDiagnostics)
            {
                foreach (var point in segment.AbsolutePoints)
                {
                    if (reconstructed.Count == 0 || reconstructed[reconstructed.Count - 1] != point) reconstructed.Add(point);
                }
            }
            var fallback = layout.Traversals.Geometry.TryGetValue(link.Link.Id, out var geometry) && geometry.UsedFallback;
            return Enumerable.Range(0, points.Length - 1).Where(index =>
                    points[index].X != points[index + 1].X && points[index].Y != points[index + 1].Y)
                .Select(index =>
                {
                    var start = points[index];
                    var end = points[index + 1];
                    var terminal = index == 0 || index == points.Length - 2;
                    var boundary = anchors.Contains(start) || anchors.Contains(end);
                    var classification = terminal ? "terminal attachment artefact" :
                        boundary ? "ownership-anchor artefact" :
                        fallback ? "fallback geometry" : "actual final diagonal route";
                    var memberships = bandReport?.Bands.SelectMany(band => band.Memberships)
                        .Where(membership => membership.LogicalEdgeIdentity == link.Link.Id &&
                            index >= membership.FirstSegmentIndex && index <= membership.LastSegmentIndex)
                        .Select(membership => membership.BandId.ToString()).Distinct(StringComparer.Ordinal)
                        .OrderBy(id => id, StringComparer.Ordinal).ToArray() ?? Array.Empty<string>();
                    var findings = layout.Traceability.Violations.Where(finding =>
                            finding.EdgeId == link.Link.Id || finding.OtherEdgeId == link.Link.Id)
                        .Select(finding => finding.Code).Distinct().OrderBy(code => code).ToArray();
                    var history = link.RouteState.History.Select(snapshot => new LogicalRouteHistoryDiagnostic(
                        snapshot.Revision, snapshot.Stage, snapshot.Producer, snapshot.CompilationStatus,
                        snapshot.Points,
                        snapshot.Points.Zip(snapshot.Points.Skip(1), (left, right) => (left, right))
                            .Any(pair => pair.left.X != pair.right.X && pair.left.Y != pair.right.Y))).ToArray();
                    var xmlContains = reconstructed.Zip(reconstructed.Skip(1), (left, right) => (left, right))
                        .Any(pair => pair.left == start && pair.right == end || pair.left == end && pair.right == start);
                    return new NonOrthogonalSegmentDiagnostic(
                        link.Link.Id, link.Link.SourceId, layout.Nodes[link.Link.SourceId].Node.Name,
                        link.Link.TargetId, layout.Nodes[link.Link.TargetId].Node.Name,
                        link.RouteState.Revision, index, start, end, end.X - start.X, end.Y - start.Y,
                        memberships, link.RouteState.Producer, link.RouteState.Stage, fallback,
                        layout.Traversals.Traversals.TryGetValue(link.Link.Id, out var traversal)
                            ? traversal.Diagnostics.Select(item => item.Code).ToArray()
                            : Array.Empty<string>(),
                        terminal, boundary,
                        classification, findings, history, points, physicalDiagnostics, reconstructed, xmlContains);
                });
        }).ToArray();
    }

    public static IReadOnlyDictionary<string, string> FocusedOutputs(
        string content,
        IReadOnlyList<TraceabilityViolation> violations,
        RenderLayout? layout = null) =>
        violations.GroupBy(FocusGroup)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => Annotate(content, group.ToArray(), group.Key, layout),
                StringComparer.Ordinal);

    public static string Annotate(
        string content,
        IReadOnlyList<TraceabilityViolation> violations,
        string title,
        RenderLayout? layout = null)
    {
        var document = XDocument.Parse(content, LoadOptions.PreserveWhitespace);
        var file = document.Root ?? throw new InvalidOperationException("Draw.io document has no root.");
        file.SetAttributeValue("diagnostic", "true");
        file.SetAttributeValue("validationFindings", violations.Count);
        var architecture = file.Elements("diagram").First(element => (string?)element.Attribute("name") == "Architecture");
        var architectureRoot = architecture.Descendants("root").First();
        architectureRoot.Add(Banner(title, violations.Count));

        var markerCells = MarkerCells(violations, layout).ToArray();
        foreach (var marker in markerCells)
        {
            architectureRoot.Add(new XElement(marker));
        }

        var diagnosticRoot = new XElement("root",
            new XElement("mxCell", new XAttribute("id", "0")),
            new XElement("mxCell", new XAttribute("id", "1"), new XAttribute("parent", "0")),
            Banner(title, violations.Count));
        foreach (var marker in markerCells)
        {
            diagnosticRoot.Add(marker);
        }

        file.Add(new XElement("diagram",
            new XAttribute("name", $"Diagnostics - {title}"),
            new XElement("mxGraphModel",
                new XAttribute("grid", "0"),
                new XElement("root", diagnosticRoot.Elements()))));
        return document.ToString(SaveOptions.DisableFormatting);
    }

    private static object Finding(TraceabilityViolation violation, int number, int requiredSpacing) => new
    {
        number,
        category = CategoryName(violation),
        validatorCode = violation.Code.ToString(),
        logicalRouteId = violation.EdgeId,
        otherRouteId = violation.OtherEdgeId,
        otherNodeId = violation.OtherNodeId,
        violation.Magnitude,
        violation.Description,
        locations = (violation.Locations ?? Array.Empty<Point>()).Select(ToPoint).ToArray(),
        offendingSegments = (violation.OffendingSegments ?? Array.Empty<Segment>()).Select(ToSegment).ToArray(),
        requiredSpacing = violation.RequiredSpacing ?? (violation.Code == TraceabilityViolationCode.ParallelSpacing ? requiredSpacing : (int?)null),
        actualSpacing = violation.ActualSpacing,
        spacingDeficit = violation.Code == TraceabilityViolationCode.ParallelSpacing ? violation.Magnitude : (int?)null,
        parallelOverlapLength = violation.ParallelOverlapLength
    };

    internal static string CategoryName(TraceabilityViolation violation) => violation.Code switch
    {
        TraceabilityViolationCode.NodeCollision => "NodeInteriorIntersection",
        TraceabilityViolationCode.SharedSegment => "SharedNonZeroLengthSegment",
        TraceabilityViolationCode.ParallelSpacing => "SpacingDeficit",
        TraceabilityViolationCode.ReusedBend => "ReusedBend",
        TraceabilityViolationCode.ImmediateReversal => "ImmediateReversal",
        TraceabilityViolationCode.PerpendicularCrossing => "PerpendicularCrossing",
        _ => "Other"
    };

    private static string FocusGroup(TraceabilityViolation violation) => violation.Code switch
    {
        TraceabilityViolationCode.NodeCollision => "node-intersections",
        TraceabilityViolationCode.SharedSegment or TraceabilityViolationCode.ReusedBend => "shared-or-ambiguous-geometry",
        TraceabilityViolationCode.ParallelSpacing => "spacing-problems",
        TraceabilityViolationCode.ImmediateReversal => "malformed-traversal-or-reversals",
        TraceabilityViolationCode.PerpendicularCrossing => "perpendicular-crossings",
        _ => "other"
    };

    private static IEnumerable<XElement> MarkerCells(
        IReadOnlyList<TraceabilityViolation> violations,
        RenderLayout? layout)
    {
        var number = 0;
        foreach (var violation in violations
            .OrderBy(item => item.EdgeId, StringComparer.Ordinal)
            .ThenBy(item => item.Code)
            .ThenBy(item => item.OtherEdgeId, StringComparer.Ordinal))
        {
            number++;
            var location = (violation.Locations ?? Array.Empty<Point>()).FirstOrDefault();
            var x = location == default ? 20 + number * 6 : location.X;
            var y = location == default ? 60 + number * 6 : location.Y;
            var colour = violation.Code switch
            {
                TraceabilityViolationCode.NodeCollision => "#ff0000",
                TraceabilityViolationCode.SharedSegment => "#ff6600",
                TraceabilityViolationCode.ParallelSpacing => "#ffd966",
                TraceabilityViolationCode.ReusedBend => "#a64dff",
                _ => "#cc0000"
            };
            var routeLabel = layout is not null && layout.Links.TryGetValue(violation.EdgeId, out var link)
                ? $"{layout.Nodes[link.Link.SourceId].Node.Name} → {layout.Nodes[link.Link.TargetId].Node.Name}"
                : violation.EdgeId;
            var value = $"{number}: {routeLabel} | {CategoryName(violation)} | {violation.EdgeId}" +
                (violation.OtherEdgeId is null ? string.Empty : $" / {violation.OtherEdgeId}") +
                (violation.OtherNodeId is null ? string.Empty : $" / node {violation.OtherNodeId}") +
                $" | ({x},{y})";
            yield return new XElement("mxCell",
                new XAttribute("id", $"diagnostic_marker_{number:D4}"),
                new XAttribute("value", value),
                new XAttribute("style", $"shape=ellipse;whiteSpace=wrap;html=1;fillColor={colour};strokeColor=#ffffff;fontColor=#ffffff;fontSize=10;"),
                new XAttribute("vertex", "1"),
                new XAttribute("parent", "1"),
                new XElement("mxGeometry",
                    new XAttribute("x", x - 9),
                    new XAttribute("y", y - 9),
                    new XAttribute("width", 18),
                    new XAttribute("height", 18),
                    new XAttribute("as", "geometry")));
        }
    }

    private static XElement Banner(string title, int count) =>
        new("mxCell",
            new XAttribute("id", $"diagnostic_banner_{SafeId(title)}"),
            new XAttribute("value", $"DIAGNOSTIC OUTPUT - generation failed strict validation - {title} ({count})"),
            new XAttribute("style", "rounded=0;whiteSpace=wrap;html=1;fillColor=#990000;strokeColor=#ffffff;fontColor=#ffffff;fontStyle=1;"),
            new XAttribute("vertex", "1"),
            new XAttribute("parent", "1"),
            new XElement("mxGeometry",
                new XAttribute("x", 20),
                new XAttribute("y", 20),
                new XAttribute("width", 620),
                new XAttribute("height", 32),
                new XAttribute("as", "geometry")));

    private static CorridorPathCandidate? SelectedCandidate(RenderLayout layout, string edgeId)
    {
        if (layout.RegionalPathSelection?.Selected.TryGetValue(edgeId, out var regional) == true)
        {
            return regional;
        }

        return layout.PathSelection?.Selected.TryGetValue(edgeId, out var global) == true ? global : null;
    }

    private static IReadOnlyList<CorridorPathEvaluation> CandidateEvaluations(RenderLayout layout, string edgeId) =>
        layout.PathSelection?.Evaluations.Where(item => item.EdgeId == edgeId).ToArray()
        ?? Array.Empty<CorridorPathEvaluation>();

    private static CorridorPathDecision? CandidateDecision(RenderLayout layout, string edgeId) =>
        layout.PathSelection?.Decisions.FirstOrDefault(item => item.EdgeId == edgeId);

    private static IEnumerable<Point> CompletePoints(LinkLayout link) =>
        new[] { link.SourcePoint }.Concat(link.Points).Concat(new[] { link.TargetPoint });

    private static string? ProjectName(IReadOnlyDictionary<string, string> projects, string? projectId) =>
        projectId is not null && projects.TryGetValue(projectId, out var name) ? name : null;

    private static object ToPoint(Point point) => new { point.X, point.Y };

    private static object ToSegment(Segment segment) => new { start = ToPoint(segment.Start), end = ToPoint(segment.End), segment.Length };

    private static object Score(GlobalRouteScore score) => new
    {
        nodeCollisionOrInvalidGeometryCount = score.InvalidGeometry,
        sharedSegmentLength = score.SharedSegmentLength,
        spacingDeficit = score.SpacingDeficit,
        terminalFanoutViolations = score.LinkConnectionFanoutViolations,
        ambiguousTransitions = score.AmbiguousTransitions,
        capacityFailure = score.CapacityFailure,
        perpendicularCrossingsAndCongestion = score.CrossingsAndCongestion,
        routeEnvelopeExpansion = RouteEnvelopeExpansion(score),
        pathEconomy = score.PathEconomy
    };

    private static int RouteEnvelopeExpansion(GlobalRouteScore score)
    {
        var property = score.GetType().GetProperty("RouteEnvelopeExpansion") ??
            score.GetType().GetProperty("CanvasEscape");
        return property?.GetValue(score) is int value ? value : 0;
    }

    private static string SafeId(string value) =>
        new(value.Select(character => char.IsLetterOrDigit(character) ? character : '_').ToArray());
}
