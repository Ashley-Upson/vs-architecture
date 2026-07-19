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
        var bands = InterLayerDemandDiscovery.Observe(placement, generated, settings, production.Traceability);
        var contexts = AdjacentDownwardContextFactory.Create(production, bands);
        var adjacentObservation = AdjacentDownwardLinkDemandDiscovery.Observe(contexts);
        var generalObservation = GeneralDownwardLinkSegmentDemandProducer.Observe(
            contexts, settings.Layout.LinkPadding);
        var positionalDemands = PreAssignmentConstraintDemandProducer.Detect(
            placement, generalObservation, contexts, settings.Layout.ParallelLaneSpacing, settings.Layout.LinkPadding);
        var potentiallySupportedRoutes = new HashSet<string>(
            generalObservation.Routes.Where(item => item.Observation.Eligible)
                .Select(item => item.Observation.LogicalRouteId), StringComparer.Ordinal);
        foreach (var context in contexts.Where(item => item.Target.Depth <= item.Source.Depth &&
                     !item.ExposureTreeSpecific && item.Source.Node.ProjectId is not null &&
                     item.Route.ExitY == 1 && item.Route.EntryY == 0))
            potentiallySupportedRoutes.Add(context.Route.Link.Id);
        phase["provisional topology and positional constraint detection"] = Elapsed(timer);

        timer.Restart();
        var persistentDemands = positionalDemands.Demands.ToDictionary(item => item.Id, StringComparer.Ordinal);
        PreAssignmentMovementResult movement;
        PlacedGraph activePlacement;
        IReadOnlyDictionary<string, LinkLayout> projectedLinks;
        var persistentIterations = 0;
        var differenceConstraintStore = new GenerationConstraintStore();
        var lockedColumnDirections = new Dictionary<string, HorizontalMovementDirection>(StringComparer.Ordinal);
        while (true)
        {
            var differenceConstraints = ColumnDifferenceConstraintMaterializer.Propose(
                placement, positionalDemands.ColumnToEnvelopeConstraints,
                positionalDemands.ColumnToColumnConstraints, production.Links, potentiallySupportedRoutes,
                lockedColumnDirections);
            var differenceIncreased = false;
            foreach (var constraint in differenceConstraints)
                differenceIncreased |= differenceConstraintStore.Merge(constraint);
            movement = PreAssignmentMovementPlanner.Solve(
                placement, persistentDemands.Values, settings, production.Links, potentiallySupportedRoutes,
                differenceConstraintStore.Snapshot());
            activePlacement = movement.Placement.Revision != placement.Revision ? movement.Placement : placement;
            projectedLinks = activePlacement.Revision != placement.Revision
                ? PreAssignmentRouteProjection.Project(activePlacement, production.Links, settings)
                : production.Links;
            var provisionalLayout = production.WithTrialGeometry(
                production.Graph, activePlacement.Nodes, activePlacement.Projects, projectedLinks,
                production.Traceability, activePlacement.Revision);
            generated = new GeneratedLogicalRoutes(activePlacement, projectedLinks, routeRevision.Next());
            bands = InterLayerDemandDiscovery.Observe(activePlacement, generated, settings, production.Traceability);
            contexts = AdjacentDownwardContextFactory.Create(provisionalLayout, bands);
            adjacentObservation = AdjacentDownwardLinkDemandDiscovery.Observe(contexts);
            generalObservation = GeneralDownwardLinkSegmentDemandProducer.Observe(contexts, settings.Layout.LinkPadding);
            var next = PreAssignmentConstraintDemandProducer.Detect(
                activePlacement, generalObservation, contexts, settings.Layout.ParallelLaneSpacing, settings.Layout.LinkPadding);
            var increased = false;
            foreach (var demand in next.Demands)
                if (!persistentDemands.TryGetValue(demand.Id, out var existing) ||
                    demand.MinimumSeparation > existing.MinimumSeparation)
                {
                    persistentDemands[demand.Id] = demand;
                    increased = true;
                }
            persistentIterations++;
            positionalDemands = new PreAssignmentConstraintDemandReport(
                persistentDemands.Values.OrderBy(item => item.Id, StringComparer.Ordinal).ToArray(),
                next.ColumnToEnvelopeConstraints,
                next.ColumnToColumnConstraints,
                next.ReturnColumnConstraints,
                next.DestinationColumnConflicts, next.VerticalColumnObstacles, next.ReturnStubObstacles);
            if (!increased && !differenceIncreased) break;
        }
        var movementPlanned = activePlacement.Revision != placement.Revision;
        phase["positional constraint solving and position materialisation"] = Elapsed(timer);

        timer.Restart();
        var common = GeneralDownwardCommonAllocator.Assign(
            generalObservation, activePlacement.Nodes, settings.Layout.ParallelLaneSpacing, settings.Layout.LinkPadding);
        var returns = ReturnLinkCommonAllocator.Assign(
            contexts, activePlacement, settings.Layout.ParallelLaneSpacing, settings.Layout.LinkPadding);
        var returnIds = new HashSet<string>(returns.Plans.Select(item => item.LogicalRouteId), StringComparer.Ordinal);
        var capabilities = generalObservation.Routes.Where(item => !returnIds.Contains(item.Observation.LogicalRouteId))
            .Select(item => new CommonAuthorityRouteCapability(
                item.Observation.LogicalRouteId, item.Observation.Eligible, CapabilityReason(item)))
            .Concat(returns.Assignments.Select(item => new CommonAuthorityRouteCapability(
                item.LogicalRouteId, item.IsValid, ReturnCapabilityReason(item))))
            .OrderBy(item => item.LogicalRouteId, StringComparer.Ordinal).ToArray();
        var supportedByRoute = capabilities.ToDictionary(
            item => item.LogicalRouteId, item => item.Eligible, StringComparer.Ordinal);
        var interactions = DiscoverInteractions(production.Links, settings.Layout.ParallelLaneSpacing).ToList();
        var movementClosures = new List<object>();
        var blockedByUnclosedMovement = new HashSet<string>(StringComparer.Ordinal);
        foreach (var region in common.Regions.Where(item => item.ConstraintProposal is not null))
        {
            var verticalMovement = LayerSuffixConstraintMaterializer.Materialize(
                activePlacement, new[] { region.ConstraintProposal! }, settings, projectedLinks);
            var invalidated = verticalMovement.InvalidatedRouteIds.OrderBy(item => item, StringComparer.Ordinal).ToArray();
            var fullySupported = invalidated.All(routeId =>
                supportedByRoute.TryGetValue(routeId, out var supported) && supported);
            if (!fullySupported)
                foreach (var routeId in region.Assignment.Components.SelectMany(item => item.Demands)
                             .Select(item => item.LogicalRouteId))
                    blockedByUnclosedMovement.Add(routeId);
            movementClosures.Add(new
            {
                region.Region.EnvelopeIdentity,
                movementScope = region.Region.MovementScope?.ToString(),
                proposedMinimum = region.ConstraintProposal!.Minimum,
                verticalMovement.MaximumDelta,
                verticalMovement.LayersMoved,
                verticalMovement.NodesMoved,
                invalidatedRouteIds = invalidated,
                fullySupported,
                disposition = fullySupported
                    ? "EligibleForConstraintExecution"
                    : "MixedMovementBoundaryRejected"
            });
        }
        var stableInteractions = interactions.Distinct().OrderBy(item => item.FirstRouteId, StringComparer.Ordinal)
            .ThenBy(item => item.SecondRouteId, StringComparer.Ordinal).ThenBy(item => item.Kind, StringComparer.Ordinal).ToArray();
        var attribution = MixedBoundaryAttributor.Attribute(contexts, adjacentObservation, stableInteractions, bands);
        var closure = CommonAuthorityComponentClassifier.Classify(capabilities, stableInteractions);
        phase["conflict grouping and link-segment assignment"] = Elapsed(timer);

        timer.Restart();
        var assignmentByRoute = common.Routes.Concat(returns.Assignments)
            .ToDictionary(item => item.LogicalRouteId, StringComparer.Ordinal);
        var current = production.Links.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        var attempts = new List<DevelopmentTrialComponentAttempt>();
        var acceptedRouteIds = new HashSet<string>(StringComparer.Ordinal);
        var movementComponentAccepted = activePlacement.Revision == placement.Revision;
        if (!movementComponentAccepted)
        {
            var invalidated = movement.InvalidatedLinkIds.OrderBy(item => item, StringComparer.Ordinal).ToArray();
            var unavailable = invalidated.Where(routeId =>
                !assignmentByRoute.TryGetValue(routeId, out var assignment) || !assignment.IsValid ||
                assignment.ReconstructedPoints.Count == 0).ToArray();
            if (unavailable.Length == 0)
            {
                var movementCandidate = current.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
                foreach (var routeId in invalidated)
                    movementCandidate[routeId] = movementCandidate[routeId].AcceptGeometry(
                        assignmentByRoute[routeId].ReconstructedPoints, LogicalRouteStage.Validated,
                        "PreAssignmentMovementClosure");
                var validation = TraceabilityValidator.Validate(
                    activePlacement.Nodes, movementCandidate, settings.Layout.ParallelLaneSpacing);
                var hard = validation.Violations.Where(item => invalidated.Contains(item.EdgeId, StringComparer.Ordinal) ||
                    item.OtherEdgeId is not null && invalidated.Contains(item.OtherEdgeId, StringComparer.Ordinal)).ToArray();
                if (hard.Length == 0)
                {
                    current = movementCandidate;
                    foreach (var routeId in invalidated) acceptedRouteIds.Add(routeId);
                    movementComponentAccepted = true;
                    attempts.Add(new DevelopmentTrialComponentAttempt("pre-assignment-movement", "accepted", "Validated",
                        invalidated, invalidated));
                }
                else
                    attempts.Add(new DevelopmentTrialComponentAttempt("pre-assignment-movement", "rejected after validation",
                        "HardGeometryFinding", invalidated,
                        hard.Select(item => $"{item.Code}:{item.EdgeId}:{item.OtherEdgeId}").ToArray()));
            }
            else
                attempts.Add(new DevelopmentTrialComponentAttempt("pre-assignment-movement", "rejected before execution",
                    "IncompleteMovementClosure", invalidated, unavailable.SelectMany(routeId =>
                        assignmentByRoute.TryGetValue(routeId, out var assignment)
                            ? assignment.Diagnostics.Select(diagnostic => $"{routeId}:{diagnostic}")
                            : new[] { $"{routeId}:MissingAssignment" }).ToArray()));
        }
        foreach (var component in closure.Components.OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            if (!movementComponentAccepted && activePlacement.Revision != placement.Revision) break;
            if (component.Disposition != CommonAuthorityComponentDisposition.Eligible)
            {
                attempts.Add(Attempt(component, "rejected before execution", component.Reason, Array.Empty<string>()));
                continue;
            }

            var routeIds = component.Routes.Select(item => item.LogicalRouteId).OrderBy(item => item, StringComparer.Ordinal).ToArray();
            if (routeIds.Any(acceptedRouteIds.Contains)) continue;
            var movementBlocked = routeIds.Where(blockedByUnclosedMovement.Contains).ToArray();
            if (movementBlocked.Length > 0)
            {
                attempts.Add(Attempt(component, "rejected before execution", "MixedMovementBoundaryRejected", movementBlocked));
                continue;
            }
            var rejectedAssignments = routeIds.Where(routeId =>
                !assignmentByRoute.TryGetValue(routeId, out var assignment) || !assignment.IsValid).ToArray();
            if (rejectedAssignments.Length > 0)
            {
                attempts.Add(Attempt(component, "rejected before execution", "GeneralDownwardAssignmentFailed",
                    rejectedAssignments.SelectMany(routeId => assignmentByRoute.TryGetValue(routeId, out var assignment)
                        ? assignment.Diagnostics : new[] { $"MissingAssignment:{routeId}" }).ToArray()));
                continue;
            }

            var candidate = current.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
            var unable = new List<string>();
            foreach (var routeId in routeIds)
            {
                var points = assignmentByRoute[routeId].ReconstructedPoints;
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

            var validation = TraceabilityValidator.Validate(activePlacement.Nodes, candidate, settings.Layout.ParallelLaneSpacing);
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
        var committedPlacement = movementComponentAccepted ? activePlacement : placement;
        if (!movementComponentAccepted)
        {
            current = production.Links.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
            acceptedRouteIds.Clear();
        }
        var trialNodes = committedPlacement.Nodes.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        var trialProjects = committedPlacement.Projects.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        var trialGraph = production.Graph;
        var disconnected = DisconnectedNodeProjectLayouter.Create(production.Graph, production.Nodes, settings);
        if (disconnected is not null)
        {
            foreach (var node in disconnected.Nodes) trialNodes[node.Key] = node.Value;
            trialProjects[disconnected.Project.Id] = disconnected.ProjectLayout;
            trialGraph = production.Graph.WithDisconnectedProject(disconnected);
        }
        var finalValidation = TraceabilityValidator.Validate(trialNodes, current, settings.Layout.ParallelLaneSpacing);
        var layout = production.WithTrialGeometry(trialGraph, trialNodes, trialProjects, current, finalValidation);
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
        var boundaryInteractions = stableInteractions.Where(item =>
            acceptedRouteIds.Contains(item.FirstRouteId) != acceptedRouteIds.Contains(item.SecondRouteId)).ToArray();
        var report = new
        {
            mode = "DevelopmentOnlyCommonAuthorityTrial",
            normalProductionExposure = false,
            routeCapabilities = capabilities,
            preAssignmentMovement = new
            {
                movementPlanned,
                movementAccepted = movementComponentAccepted,
                positionalDemands.DestinationColumnConflicts,
                positionalDemands.VerticalColumnObstacles,
                positionalDemands.ReturnStubObstacles,
                constraintsProposed = positionalDemands.Demands.Count,
                constraintsAccepted = movement.Solutions.Where(item => item.IsValid).Sum(item => item.AcceptedCandidates.Count),
                columnToEnvelopeDemands = positionalDemands.ColumnToEnvelopeConstraints.Count,
                columnToColumnDemands = positionalDemands.ColumnToColumnConstraints.Count,
                returnColumnEnvelopeDemands = positionalDemands.ReturnColumnConstraints.Count,
                orderingInvariantReturnBlockers = positionalDemands.ReturnColumnConstraints.Count(item =>
                    ReturnColumnSolvabilityAnalyzer.Analyze(item) ==
                    ReturnColumnHorizontalSolvability.OrderingInvariantInteriorBlocker),
                differenceConstraints = differenceConstraintStore.Snapshot().Select(item => new
                {
                    scope = item.Key.Scope.ToString(), kind = item.Key.Kind.ToString(), item.Minimum, item.Reason
                }),
                components = movement.Components.Count,
                solvedComponents = movement.Solutions.Count(item => item.IsValid),
                nodesMoved = movement.Placement.Nodes.Count(item => item.Value.Rect != placement.Nodes[item.Key].Rect),
                movement.InvalidatedLinkIds,
                iterations = persistentIterations,
                solutions = movement.Solutions.Select(solution => new
                {
                    solution.Component.Id,
                    solution.IsValid,
                    solution.Reason,
                    demands = solution.Component.Demands.Select(item => new
                    {
                        item.Id, reason = item.Reason.ToString(), direction = item.Direction.ToString(),
                        item.MinimumSeparation, affectedLayers = item.AffectedLayerInterval,
                        item.LeftStructureId, item.RightStructureId, item.SourceLinkIds
                    }),
                    accepted = solution.AcceptedCandidates.Select(item => new
                    {
                        scope = item.Scope.ToString(), direction = item.Direction.ToString(), item.Delta,
                        nodeCount = item.NodeIds.Count, item.InvalidatedLinkCount
                    }),
                    rejected = solution.EvaluatedCandidates.Where(item => !item.IsValid).Select(item => new
                    {
                        scope = item.Scope.ToString(), item.RejectionReason
                    })
                })
            },
            routeCapabilityDetails = contexts.OrderBy(item => item.Route.Link.Id, StringComparer.Ordinal).Select(context =>
            {
                var generalPlan = generalObservation.Routes.Single(item =>
                    item.Observation.LogicalRouteId == context.Route.Link.Id);
                var returnAssignment = returns.Assignments.FirstOrDefault(item =>
                    item.LogicalRouteId == context.Route.Link.Id);
                var capability = capabilities.Single(item => item.LogicalRouteId == context.Route.Link.Id);
                return new
                {
                    capability.LogicalRouteId,
                    capability.Eligible,
                    capability.Reason,
                    sourceDepth = context.Source.Depth,
                    targetDepth = context.Target.Depth,
                    semanticSourceId = context.Route.Link.SemanticSourceId ?? context.Route.Link.SourceId,
                    semanticTargetId = context.Route.Link.SemanticTargetId ?? context.Route.Link.TargetId,
                    renderSourceId = context.Route.Link.SourceId,
                    renderTargetId = context.Route.Link.TargetId,
                    sourceProjectId = context.Source.Node.ProjectId,
                    targetProjectId = context.Target.Node.ProjectId,
                    sourcePositionalRoot = PositionalRoot(context.Source.Node.Id, placement.PositionalHierarchy),
                    targetPositionalRoot = PositionalRoot(context.Target.Node.Id, placement.PositionalHierarchy),
                    projectRelationship = string.Equals(context.Source.Node.ProjectId, context.Target.Node.ProjectId,
                        StringComparison.Ordinal) ? "SameProject" : "CrossProject",
                    connectionTopology = $"exitY:{context.Route.ExitY};entryY:{context.Route.EntryY}",
                    generalRejection = generalPlan.Observation.RejectionReason?.ToString(),
                    generalDiagnostics = generalPlan.Observation.Diagnostics,
                    assignmentDiagnostics = returnAssignment?.Diagnostics ?? Array.Empty<string>(),
                    legacyHardFindings = production.Traceability.Violations
                        .Where(item => item.EdgeId == context.Route.Link.Id || item.OtherEdgeId == context.Route.Link.Id)
                        .Select(item => item.Code.ToString()).Distinct().OrderBy(item => item, StringComparer.Ordinal).ToArray()
                };
            }).ToArray(),
            eligibleRoutes = generalObservation.Routes.Count(item => item.Observation.Eligible) +
                returns.Assignments.Count(item => item.IsValid),
            returnTopologies = new
            {
                sameLayer = returns.Plans.Count(item => item.Kind == ReturnLinkTopologyKind.SameLayer),
                upward = returns.Plans.Count(item => item.Kind == ReturnLinkTopologyKind.Upward),
                assignedColumns = returns.VerticalColumns.ColumnsByDemandId.Count,
                acceptedAssignments = returns.Assignments.Count(item => item.IsValid)
            },
            disconnectedNodes = disconnected is null ? null : new
            {
                projectId = disconnected.Project.Id,
                nodeCount = disconnected.NodeIds.Count,
                nodesPerLayer = disconnected.NodesPerLayer,
                bounds = disconnected.ProjectLayout.Rect
            },
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
                demandCount = item.Assignment.SegmentsByDemandId.Count,
                movementScope = item.Region.MovementScope?.ToString()
            }).ToArray(),
            mixedBoundaryAttribution = attribution,
            movementClosures,
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

    private static string CapabilityReason(GeneralDownwardLinkPlan plan)
    {
        if (plan.Observation.Eligible) return "CommonDownwardTopology";
        switch (plan.Observation.RejectionReason)
        {
            case AdjacentDownwardRejectionReason.CrossProject:
                return "UnsupportedCrossProjectOwnership";
            case AdjacentDownwardRejectionReason.ExposureTreeSpecific:
                return "UnsupportedExposureTreeInteraction";
            case AdjacentDownwardRejectionReason.UnsupportedConnectionTopology:
                return "UnsupportedConnectionAllocation";
            case AdjacentDownwardRejectionReason.RevisionMismatch:
                return "StaleRevisionDependency";
            case AdjacentDownwardRejectionReason.MultipleInterLayer:
            case AdjacentDownwardRejectionReason.SkippedLayer:
                return "UnsupportedLinkPathCompilation";
            case AdjacentDownwardRejectionReason.SameLayer:
            case AdjacentDownwardRejectionReason.UpwardOrReturn:
                return "UnsupportedTopology";
            default:
                return "LegacyOnlyGeometry";
        }
    }

    private static string ReturnCapabilityReason(GeneralDownwardLinkAssignment assignment)
    {
        if (assignment.IsValid) return "CommonReturnTopology";
        if (assignment.Diagnostics.Any(item => item.StartsWith("ReturnTopologyBlocked:", StringComparison.Ordinal)))
            return "UnsupportedObstacleMovement";
        if (assignment.Diagnostics.Any(item => item.Contains("ConnectionInvariant", StringComparison.Ordinal)))
            return "UnsupportedConnectionAllocation";
        return "UnsupportedLinkPathCompilation";
    }

    private static string PositionalRoot(string nodeId, PositionalHierarchy hierarchy)
    {
        var current = nodeId;
        while (hierarchy.ParentByNode.TryGetValue(current, out var parent)) current = parent;
        return current;
    }

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
