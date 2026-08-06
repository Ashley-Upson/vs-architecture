using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using StandardIo.ArchitectureDiagram.Core.Models.Architectures;
using StandardIo.ArchitectureDiagram.Core.Models.ArchitectureV6;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.ArchitectureV6;

public sealed class ArchitectureDiagramV6Planner : IArchitectureDiagramPlanner
{
    public PlannedArchitectureDiagram Plan(ArchitecturePlanningRequest request)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));

        var stageTimings = new Dictionary<string, long>(StringComparer.Ordinal);
        var stageInvocations = new Dictionary<string, int>(StringComparer.Ordinal);
        void TimeStage(string name, Action action)
        {
            var timer = Stopwatch.StartNew();
            action();
            timer.Stop();
            stageTimings[name] = stageTimings.TryGetValue(name, out var elapsed) ? elapsed + timer.ElapsedMilliseconds : timer.ElapsedMilliseconds;
            stageInvocations[name] = stageInvocations.TryGetValue(name, out var count) ? count + 1 : 1;
        }
        ArchitectureProjectionResult projection = null!;
        TimeStage("projection", () => projection = new ProjectionBuilder(request).Build());
        var requiredSpans = new Dictionary<string, int>(StringComparer.Ordinal);
        var placementRebuildCount = 0;
        var expansionRequirementCount = 0;
        var expandedNodeIds = new HashSet<string>(StringComparer.Ordinal);
        var expandedNodeSpans = new Dictionary<string, string>(StringComparer.Ordinal);
        var invalidatedRouteCount = 0;
        var rebuiltReservationCount = 0;
        LogicalPlacementResult placement = null!;
        AbstractRoutePlanningResult routing = null!;
        ArchitectureLaneAllocationResult allocation = null!;
        ArchitectureRouteBoundaryValidationResult boundaryValidation = new ArchitectureRouteBoundaryValidationResult(
            Array.Empty<PlannedRouteBoundaryContract>(), Array.Empty<RouteBoundaryContractFinding>());
        for (var pass = 0; pass < 2; pass++)
        {
            TimeStage("authoritativePlacementAndGrid", () => placement = new ArchitectureV6LogicalPlacementBuilder(request, projection, requiredSpans).Build());
            TimeStage("abstractRouting", () => routing = new ArchitectureV6AbstractRoutePlanner(request, projection.PhysicalNodes, projection.PhysicalLinks,
                placement.NodePlacements, placement.NodeMetadata, placement.ProjectGrids, placement.DiagramGrid).Build());
            TimeStage("laneAllocation", () => allocation = new ArchitectureV6LaneAllocator(request, projection.PhysicalLinks, placement.NodePlacements, placement.NodeMetadata,
                routing.Routes, routing.StraightRuns, routing.EndpointDemands, routing.DestinationApproaches).Build());
            TimeStage("routeBoundaryContract", () => boundaryValidation = new ArchitectureV6RouteBoundaryContractBuilder(
                allocation, projection.PhysicalNodes, placement.NodePlacements).Build());
            allocation = allocation with { BoundaryValidation = boundaryValidation };
            var expanded = allocation.FootprintExpansionRequirements
                .Where(item => item.RequiredOddSpan > item.CurrentSpan)
                .GroupBy(item => item.PhysicalNodeId, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Max(item => item.RequiredOddSpan), StringComparer.Ordinal);
            if (expanded.Count == 0) break;
            placementRebuildCount++;
            stageInvocations["footprintExpansionRebuild"] = stageInvocations.TryGetValue("footprintExpansionRebuild", out var rebuilds) ? rebuilds + 1 : 1;
            expansionRequirementCount += allocation.FootprintExpansionRequirements.Count;
            invalidatedRouteCount += routing.Routes.Count;
            rebuiltReservationCount += placement.SubtreeReservations.Count;
            foreach (var nodeId in expanded.Keys) expandedNodeIds.Add(nodeId);
            foreach (var item in allocation.FootprintExpansionRequirements
                .Where(item => item.RequiredOddSpan > item.CurrentSpan)
                .GroupBy(item => item.PhysicalNodeId, StringComparer.Ordinal))
            {
                var current = item.Min(requirement => requirement.CurrentSpan);
                var required = item.Max(requirement => requirement.RequiredOddSpan);
                expandedNodeSpans[item.Key] = $"{current}->{required}";
            }
            foreach (var item in expanded) requiredSpans[item.Key] = Math.Max(requiredSpans.TryGetValue(item.Key, out var existing) ? existing : 0, item.Value);
        }
        var projectGrids = routing.ProjectGrids;
        var diagramGrid = routing.DiagramGrid;
        var structuralRowsBeforeRouting = placement.ProjectGrids.Sum(grid => grid.Grid.Rows.Count);
        var structuralColumnsBeforeRouting = placement.ProjectGrids.Sum(grid => grid.Grid.Columns.Count);
        var structuralRowsAfterRouting = projectGrids.Sum(grid => grid.Grid.Rows.Count);
        var structuralColumnsAfterRouting = projectGrids.Sum(grid => grid.Grid.Columns.Count);
        PhysicalSizingResult sizing = null!;
        TimeStage("relativeSizing", () => sizing = new ArchitectureV6TrackSizingPlanner(request, projection.PhysicalNodes,
            placement.NodePlacements, placement.NodeMetadata, projectGrids, placement.SubtreeReservations,
            allocation.Sizing.Constraints, diagramGrid).Build());
        var sizedPlan = sizing.Sizing;
        PlannedArchitecturePhysicalScene physicalScene = null!;
        TimeStage("absoluteGeometry", () => physicalScene = new ArchitectureV6PhysicalSceneCompiler(request, projection.PhysicalNodes,
            projection.PhysicalLinks, diagramGrid, projectGrids, placement.NodePlacements, allocation.Routes, sizedPlan,
            sizing.RelativeGeometry, allocation, placement.SubtreeReservations, boundaryValidation.Routes).Compile());
        var cardinalityFindings = structuralRowsBeforeRouting != structuralRowsAfterRouting || structuralColumnsBeforeRouting != structuralColumnsAfterRouting
            ? new[] { Deferred("StructuralGridCardinalityChanged", PlanningDiagnosticSubject.Grid, "Abstract routing changed structural row or column cardinality.") }
            : Array.Empty<ArchitecturePlanningDiagnostic>();
        var boundaryDiagnostics = boundaryValidation.Findings.Select(finding => new ArchitecturePlanningDiagnostic(
            finding.Code, finding.Message, PlanningDiagnosticSubject.PhysicalLink, finding.PhysicalLinkId)).ToArray();
        var boundaryComponentTypeCounts = boundaryValidation.Routes.SelectMany(route => route.Components)
            .GroupBy(component => component.Kind.ToString())
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var boundaryAdjacencyPairCounts = boundaryValidation.Routes.SelectMany(route =>
                route.Components.Zip(route.Components.Skip(1), (before, after) => before.Kind + "->" + after.Kind))
            .GroupBy(pair => pair, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var boundaryPairs = boundaryValidation.Routes.SelectMany(route =>
                route.Components.Zip(route.Components.Skip(1), (before, after) => (before, after)))
            .ToArray();
        var boundaryPairKeys = boundaryPairs.Select(pair => (Key: pair.before.Kind + "->" + pair.after.Kind, Pair: pair));
        var boundaryMatchingPairCounts = boundaryPairKeys.Where(item => item.Pair.before.ExitBoundary == item.Pair.after.EntryBoundary)
            .GroupBy(item => item.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var boundaryMismatchedPairCounts = boundaryPairKeys.Where(item => item.Pair.before.ExitBoundary != item.Pair.after.EntryBoundary)
            .GroupBy(item => item.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var matchingBoundaryPairCount = boundaryPairs.Count(pair => pair.before.ExitBoundary == pair.after.EntryBoundary);
        var mismatchedBoundaryPairCount = boundaryPairs.Length - matchingBoundaryPairCount;
        var completeTurnContractCount = boundaryValidation.Routes.SelectMany(route => route.Components)
            .Count(component => component.Kind == PlannedRouteComponentKind.Turn && component.EntryBoundary?.Lane is not null && component.ExitBoundary?.Lane is not null);
        var incompleteTurnContractCount = boundaryValidation.Routes.SelectMany(route => route.Components)
            .Count(component => component.Kind == PlannedRouteComponentKind.Turn) - completeTurnContractCount;
        var findings = projection.Diagnostics.Concat(cardinalityFindings)
            .Concat(routing.Diagnostics).Concat(allocation.Diagnostics).Concat(boundaryDiagnostics)
            .Concat(sizing.Diagnostics).Concat(physicalScene.Diagnostics).ToArray();

        var metrics = new ArchitecturePlanningMetrics(
            request.SemanticModel.Projects.Sum(project => project.Nodes.Count) + request.SemanticModel.ExternalNodes.Count,
            request.SemanticModel.Links.Count,
            projection.PhysicalNodes.Count,
            projection.PhysicalLinks.Count,
            projectGrids.Count,
            projectGrids.Sum(grid => grid.Grid.Rows.Count),
            projectGrids.Sum(grid => grid.Grid.Columns.Count),
            projectGrids.Sum(grid => grid.Grid.Cells.Count(item => item.Value.Occupancy == CellOccupancy.NodeAnchor)),
            allocation.Routes.SelectMany(route => route.Steps).Select(step => step.CellId).Distinct().Count(),
            allocation.Routes.GroupBy(route => route.TopologyFamily).ToDictionary(group => group.Key.ToString(), group => group.Count(), StringComparer.Ordinal),
            allocation.Routes.Sum(route => route.Steps.Count),
            allocation.StraightRuns.Count,
            allocation.HorizontalLanes.Concat(allocation.VerticalLanes).Select(run => run.Lane).Distinct().Count(),
            null,
            findings.Length,
            projection.SemanticNodeToPhysicalNodeIds.ToDictionary(item => item.Key, item => item.Value.Count, StringComparer.Ordinal),
            projection.RootPhysicalNodeIds.Count,
            projection.ExternalPhysicalNodeIds.Count,
            projection.StandalonePhysicalNodeIds.Count,
            projection.CycleSemanticNodeIds.Count,
            LogicalLayerCount: placement.NodeMetadata.Select(item => item.LogicalLayer).Distinct().Count(),
            AnchorCellCount: placement.NodePlacements.Count,
            FootprintCellCount: placement.NodePlacements.Sum(item => item.Footprint.Count),
            SubtreeReservationCount: placement.SubtreeReservations.Count,
            PositionalOwnerCount: placement.NodeMetadata.Count(item => item.PositionalOwnerId is not null),
             SizedNodeCount: sizing.RelativeGeometry.Nodes.Count,
             GeometryProjectCount: sizing.RelativeGeometry.Projects.Count,
             GeometryGridCount: sizing.RelativeGeometry.Grids.Count,
             GeometryWidth: sizing.DiagramWidth,
             GeometryHeight: sizing.DiagramHeight,
             GeometryCollisionCount: physicalScene.Metrics.NodeOverlapCount,
             InvalidDimensionCount: sizing.Diagnostics.Count(item => item.Code == "SizingInvalidTrack" || item.Code == "RelativeInvalidDimension"),
             UnplacedNodeCount: projection.PhysicalNodes.Count - placement.NodePlacements.Count,
             OverlappingFootprintCount: placement.Diagnostics.Count(item => item.Code == "LogicalPlacementFootprintOverlap"),
             IncompatibleReservationCount: placement.Diagnostics.Count(item => item.Code == "LogicalPlacementReservationConflict"),
             FootprintExpansionRequirementCount: expansionRequirementCount,
             PlacementRebuildCount: placementRebuildCount,
             ExpandedNodeCount: expandedNodeIds.Count,
             UnsupportedRouteCount: allocation.Routes.Count(route => !route.IsStructurallySupported),
             LaneAllocationConflictCounts: allocation.Conflicts.GroupBy(conflict => conflict.Kind.ToString())
                 .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal),
             ExpandedNodeSpans: expandedNodeSpans,
             InvalidatedRouteCount: invalidatedRouteCount,
             RebuiltReservationCount: rebuiltReservationCount,
             ShiftedRegionCount: 0,
              SizingConstraintCounts: sizedPlan.Constraints.GroupBy(item => item.Kind.ToString())
                  .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal),
             SizingContributionExtents: new Dictionary<string, int>(StringComparer.Ordinal),
              SizingSolverIterations: sizing.ReconciliationIterations,
              SizingIdempotent: sizing.SizingIdempotent,
              LargestRowExtent: sizedPlan.Rows.Select(item => item.FinalExtent).DefaultIfEmpty(0).Max(),
              LargestColumnExtent: sizedPlan.Columns.Select(item => item.FinalExtent).DefaultIfEmpty(0).Max(),
              LargestSpanMinimum: sizedPlan.Constraints.Where(item => item.Rows.Count > 1 || item.Columns.Count > 1)
                  .Select(item => item.MinimumExtent).DefaultIfEmpty(0).Max(),
              UnsatisfiedSizingConstraintCount: sizing.Diagnostics.Count(item => item.Code.StartsWith("Sizing", StringComparison.Ordinal) && item.Code != "SizingInvalidTrack"),
             StructuralRowCountBeforeRouting: structuralRowsBeforeRouting,
             StructuralColumnCountBeforeRouting: structuralColumnsBeforeRouting,
             StructuralRowCountAfterRouting: structuralRowsAfterRouting,
             StructuralColumnCountAfterRouting: structuralColumnsAfterRouting,
             StructuralRowRoleCounts: projectGrids.SelectMany(grid => grid.Grid.Rows).GroupBy(row => row.Role.ToString())
                 .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal),
             StructuralColumnRoleCounts: projectGrids.SelectMany(grid => grid.Grid.Columns).GroupBy(column => column.Role.ToString())
                 .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal),
             RouteOnlyRowCount: 0,
             RouteOnlyColumnCount: 0,
             StageTimingMilliseconds: stageTimings,
             StageInvocationCounts: stageInvocations,
              CellsBeforeRouting: placement.ProjectGrids.Sum(grid => grid.Grid.Cells.Count),
              CellsAfterRouting: projectGrids.Sum(grid => grid.Grid.Cells.Count),
             HorizontalLaneCount: allocation.HorizontalLanes.Count,
             VerticalLaneCount: allocation.VerticalLanes.Count,
             MaximumHorizontalLanesInDomain: allocation.HorizontalLanes.GroupBy(lane => lane.DomainId).Select(group => group.Count()).DefaultIfEmpty(0).Max(),
             MaximumVerticalLanesInDomain: allocation.VerticalLanes.GroupBy(lane => lane.DomainId).Select(group => group.Count()).DefaultIfEmpty(0).Max(),
             ProfileComputationMilliseconds: placement.Performance.ProfileComputationMilliseconds,
             ProfileCompositionMilliseconds: placement.Performance.ProfileCompositionMilliseconds,
             ColumnMaterializationMilliseconds: placement.Performance.ColumnMaterializationMilliseconds,
             ReservationConstructionMilliseconds: placement.Performance.ReservationConstructionMilliseconds,
             SubtreeProfileCacheHits: placement.Performance.ProfileCacheHits,
             SubtreeProfileCacheMisses: placement.Performance.ProfileCacheMisses,
             IntervalCompatibilityChecks: placement.Performance.IntervalCompatibilityChecks,
             LaneAllocationMilliseconds: allocation.Performance?.ElapsedMilliseconds ?? 0,
             LaneDomainCount: allocation.Performance?.DomainCount ?? 0,
             LaneOrderingVertexCount: allocation.Performance?.OrderingVertexCount ?? 0,
             LaneOrderingEdgeCount: allocation.Performance?.OrderingEdgeCount ?? 0,
             LaneOrderingCycleCount: allocation.Performance?.OrderingCycleCount ?? 0,
             TurnCellCount: allocation.Performance?.TurnCellCount ?? 0,
              MaximumTurnsInCell: allocation.Performance?.MaximumTurnsInCell ?? 0,
              LaneIntervalComparisons: allocation.Performance?.IntervalComparisons ?? 0,
              LaneCapacityRequirementCount: allocation.Performance?.CapacityRequirementCount ?? 0,
              SizingConstraintConstructionMilliseconds: sizing.Performance.ConstraintConstructionMilliseconds,
              SizingSolverMilliseconds: sizing.Performance.SolverMilliseconds,
              SizingOffsetCompilationMilliseconds: sizing.Performance.OffsetCompilationMilliseconds,
              SizingNodeEnvelopeMilliseconds: sizing.Performance.NodeEnvelopeMilliseconds,
              SizingReservationEnvelopeMilliseconds: sizing.Performance.ReservationEnvelopeMilliseconds,
              SizingValidationMilliseconds: sizing.Performance.ValidationMilliseconds,
              NodeGridEvidence: placement.NodePlacements.OrderBy(item => item.PhysicalNodeId, StringComparer.Ordinal).Select(item =>
             {
                 var metadata = placement.NodeMetadata.Single(value => value.PhysicalNodeId == item.PhysicalNodeId);
                 var physical = projection.PhysicalNodes.Single(value => value.PhysicalNodeId == item.PhysicalNodeId);
                 return new ArchitectureNodeGridEvidence(item.PhysicalNodeId, physical.SemanticNodeId,
                     item.AnchorCellId.RowId.Value, item.AnchorCellId.ColumnId.Value,
                     item.Footprint.Select(cell => cell.ColumnId.Value).ToArray(), metadata.PositionalOwnerId,
                     metadata.PositionalChildIds, metadata.SubtreeId);
              }).ToArray(),
              AbsoluteNodeCount: physicalScene.Metrics.AbsoluteNodeCount,
              AbsoluteTerminalCount: physicalScene.Metrics.TerminalCount,
              PhysicalRouteCount: physicalScene.Metrics.PhysicalRouteCount,
              PhysicalSegmentCount: physicalScene.Metrics.SegmentCount,
              PhysicalBendCount: physicalScene.Metrics.BendCount,
              PhysicalCleanCrossingCount: physicalScene.Metrics.CleanCrossingCount,
              PhysicalTransitionCount: physicalScene.Metrics.TransitionCount,
              TotalRouteLength: physicalScene.Metrics.TotalRouteLength,
              MaximumRouteLength: physicalScene.Metrics.MaximumRouteLength,
              PhysicalNodeOverlapCount: physicalScene.Metrics.NodeOverlapCount,
              RouteNodeIntersectionCount: physicalScene.Metrics.RouteNodeIntersectionCount,
              SharedCollinearSegmentCount: physicalScene.Metrics.SharedCollinearSegmentCount,
              SharedBendCount: physicalScene.Metrics.SharedBendCount,
              InvalidCrossingCount: physicalScene.Metrics.InvalidCrossingCount,
              TerminalFindingCount: physicalScene.Metrics.TerminalFindingCount,
              OwnershipTransformFindingCount: physicalScene.Metrics.OwnershipFindingCount,
              LabelGeometryUnavailableCount: physicalScene.Metrics.LabelGeometryUnavailableCount,
              PhysicalTopologyCounts: physicalScene.Metrics.TopologyCounts,
              PhysicalStageTimingMilliseconds: physicalScene.Metrics.StageTimingsMilliseconds,
              BoundaryValidRouteCount: boundaryValidation.ValidRouteCount,
              BoundaryInvalidRouteCount: boundaryValidation.InvalidRouteCount,
              BoundaryFindingCount: boundaryValidation.Findings.Count,
              BoundaryComponentTypeCounts: boundaryComponentTypeCounts,
              BoundaryAdjacencyPairCounts: boundaryAdjacencyPairCounts,
              BoundaryMatchingPairCounts: boundaryMatchingPairCounts,
              BoundaryMismatchedPairCounts: boundaryMismatchedPairCounts,
              BoundaryMatchingPairCount: matchingBoundaryPairCount,
              BoundaryMismatchedPairCount: mismatchedBoundaryPairCount,
              CompleteTurnContractCount: completeTurnContractCount,
              IncompleteTurnContractCount: incompleteTurnContractCount,
              InvalidPhysicalRouteCount: physicalScene.Metrics.InvalidRouteCount,
              AttemptedPhysicalSegmentCount: physicalScene.Metrics.AttemptedSegmentCount,
              DiagonalPhysicalSegmentCount: physicalScene.Metrics.DiagonalSegmentCount,
              CorridorEscapeCount: physicalScene.Metrics.CorridorEscapeCount,
              ComponentContinuityFailureCount: physicalScene.Metrics.ComponentContinuityFailureCount,
              SourceStubDirectionFailureCount: physicalScene.Metrics.SourceStubDirectionFailureCount,
              DestinationStubDirectionFailureCount: physicalScene.Metrics.DestinationStubDirectionFailureCount);

        return new PlannedArchitectureDiagram(
            request,
            projection.PhysicalNodes,
            projection.PhysicalLinks,
            diagramGrid,
            projectGrids,
            placement.NodePlacements,
            allocation.Routes,
             sizedPlan,
            new ArchitecturePlanningDiagnostics(findings, metrics),
            projection,
            placement.NodeMetadata,
            placement.LinkMetadata,
            placement.SubtreeReservations,
            new ArchitecturePlanningStageStatus(true, true, true, false, false, false, true, true, true, false),
            routing.DestinationApproaches,
            allocation.StraightRuns,
            routing.TurnDemands,
            routing.EndpointDemands,
             allocation,
             sizing.RelativeGeometry)
        {
            Geometry = physicalScene.Geometry,
            PhysicalScene = physicalScene
        };
    }

    private static ArchitecturePlanningDiagnostic Deferred(string code, PlanningDiagnosticSubject subject, string message) =>
        new(code, message, subject, null);

    private static Regex ToRegex(string? value)
    {
        var pattern = string.IsNullOrWhiteSpace(value) ? ".*" : value!;
        if (pattern.IndexOf(".*", StringComparison.Ordinal) >= 0 || pattern.IndexOf("$", StringComparison.Ordinal) >= 0 || pattern.IndexOf("(", StringComparison.Ordinal) >= 0)
            return new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return new Regex("^" + Regex.Escape(pattern).Replace("\\*", ".*") + "$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static DiagramRoutingGrid EmptyDiagramGrid()
    {
        var gridId = new PlanningGridId("diagram");
        var grid = new PlanningGrid(gridId, Array.Empty<PlanningGridRow>(), Array.Empty<PlanningGridColumn>(),
            new Dictionary<PlanningGridCellId, PlanningGridCell>(), new GridTransform(gridId, new RelativePoint(0, 0)));
        return new DiagramRoutingGrid(grid, Array.Empty<RelativeRectangle>(), Array.Empty<GridTransition>());
    }

    private sealed class ProjectionBuilder
    {
        private readonly ArchitecturePlanningRequest request;
        private readonly Dictionary<string, SemanticInfo> nodes = new(StringComparer.Ordinal);
        private readonly List<string> order = new();
        private readonly List<ArchitecturePlanningDiagnostic> diagnostics = new();

        public ProjectionBuilder(ArchitecturePlanningRequest request) => this.request = request;

        public ArchitectureProjectionResult Build()
        {
            ReadSemanticNodes();
            var discoveredOrder = order.Distinct(StringComparer.Ordinal).ToArray();
            var links = request.SemanticModel.Links.Where(link => nodes.ContainsKey(link.SourceId) && nodes.ContainsKey(link.TargetId)).ToArray();
            var parents = links.GroupBy(link => link.TargetId).ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
            var children = links.GroupBy(link => link.SourceId).ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
            var roots = FindRoots(parents);
            var physicalNodes = discoveredOrder.Select(id =>
            {
                var info = nodes[id];
                return new PlannedPhysicalNode($"physical:{id}", id, PhysicalNodeProjectionMode.Canonical, null,
                    info.ProjectId ?? FindExternalOwner(id, links), null, info.IsExternal,
                    !parents.ContainsKey(id) && !children.ContainsKey(id))
                {
                    SemanticName = info.Name,
                    SemanticFullName = info.FullName
                };
            }).ToList();
            var bySemantic = physicalNodes.ToDictionary(node => node.SemanticNodeId, StringComparer.Ordinal);
            var nodeMapBuilder = bySemantic.Values.ToDictionary(node => node.SemanticNodeId,
                node => new List<string> { node.PhysicalNodeId }, StringComparer.Ordinal);
            var duplicatePatterns = request.NodeProjection.DuplicationExceptionPatterns
                .Where(pattern => !string.IsNullOrWhiteSpace(pattern)).Select(ToRegex).ToArray();
            var duplicateCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            var targetUses = new Dictionary<string, int>(StringComparer.Ordinal);
            var physicalLinksBuilder = new List<PlannedPhysicalLink>();
            foreach (var link in links)
            {
                var source = bySemantic[link.SourceId];
                var target = bySemantic[link.TargetId];
                if (request.NodeProjection.Mode == NodeProjectionMode.DuplicateBranches && targetUses.TryGetValue(link.TargetId, out var useCount) && useCount > 0 &&
                    duplicatePatterns.Any(pattern => pattern.IsMatch(nodes[link.TargetId].Name) || pattern.IsMatch(link.TargetId)))
                {
                    var ordinal = duplicateCounts.TryGetValue(link.TargetId, out var current) ? current + 1 : 1;
                    duplicateCounts[link.TargetId] = ordinal;
                    target = new PlannedPhysicalNode($"physical:{link.TargetId}:duplicate:{ordinal}", link.TargetId,
                        PhysicalNodeProjectionMode.DuplicateBranch, source.PhysicalNodeId,
                        nodes[link.TargetId].ProjectId ?? FindExternalOwner(link.TargetId, links),
                        new DuplicationProvenance(link.TargetId, $"Configured duplicate pattern matched '{nodes[link.TargetId].Name}' for link '{link.Id}', ordinal {ordinal}.", source.PhysicalNodeId),
                        nodes[link.TargetId].IsExternal, false)
                    {
                        SemanticName = nodes[link.TargetId].Name,
                        SemanticFullName = nodes[link.TargetId].FullName
                    };
                    physicalNodes.Add(target);
                    nodeMapBuilder[link.TargetId].Add(target.PhysicalNodeId);
                }
                targetUses[link.TargetId] = targetUses.TryGetValue(link.TargetId, out var count) ? count + 1 : 1;
                physicalLinksBuilder.Add(new PlannedPhysicalLink($"physical-link:{link.Id}:{physicalLinksBuilder.Count}", link.Id,
                    source.PhysicalNodeId, target.PhysicalNodeId, source.ProjectId, target.ProjectId) { Kind = link.Kind });
            }
            var physicalLinks = physicalLinksBuilder.ToArray();
            var nodeMap = nodeMapBuilder.ToDictionary(item => item.Key, item => (IReadOnlyList<string>)item.Value.ToArray(), StringComparer.Ordinal);
            var linkMap = links.GroupBy(link => link.Id).ToDictionary(group => group.Key,
                group => (IReadOnlyList<string>)physicalLinks.Where(item => item.SemanticLinkId == group.Key)
                    .Select(item => item.PhysicalLinkId).ToArray(), StringComparer.Ordinal);
            var unaccountedLinks = request.SemanticModel.Links.Where(link => !nodes.ContainsKey(link.SourceId) || !nodes.ContainsKey(link.TargetId))
                .Select(link => link.Id).ToArray();
            foreach (var id in unaccountedLinks)
                diagnostics.Add(new ArchitecturePlanningDiagnostic("UnaccountedSemanticLink", "A semantic link references a node outside the selected semantic scope.", PlanningDiagnosticSubject.SemanticLink, id));
            var unaccountedNodes = request.SemanticModel.Projects.SelectMany(project => project.Nodes).Select(node => node.Id)
                .Where(id => !nodes.ContainsKey(id)).ToArray();
            return new ArchitectureProjectionResult(
                physicalNodes,
                physicalLinks,
                Array.Empty<PhysicalNodePlacementMetadata>(),
                Array.Empty<PlannedPhysicalLinkMetadata>(),
                nodeMap,
                linkMap,
                roots.Select(id => bySemantic[id].PhysicalNodeId).ToArray(),
                physicalNodes.Where(node => node.IsExternal).Select(node => node.PhysicalNodeId).ToArray(),
                physicalNodes.Where(node => node.IsStandalone).Select(node => node.PhysicalNodeId).ToArray(),
                FindCycles(children, roots),
                unaccountedNodes,
                unaccountedLinks,
                diagnostics);
        }

        private void ReadSemanticNodes()
        {
            var selected = new HashSet<string>(request.SelectedScope.SelectedProjectIds ?? Array.Empty<string>(), StringComparer.Ordinal);
            var selectedNodes = new HashSet<string>(request.SemanticModel.Selection?.SelectedNodeIds ?? Array.Empty<string>(), StringComparer.Ordinal);
            foreach (var project in request.SemanticModel.Projects)
            {
                if (selected.Count > 0 && !selected.Contains(project.Id)) continue;
                foreach (var node in project.Nodes)
                {
                    if (selectedNodes.Count > 0 && !selectedNodes.Contains(node.Id)) continue;
                    if (nodes.ContainsKey(node.Id)) continue;
                    nodes.Add(node.Id, new SemanticInfo(node.Id, node.Name, node.FullName, project.Id, false));
                    order.Add(node.Id);
                }
            }
            foreach (var external in request.SemanticModel.ExternalNodes)
            {
                if (nodes.ContainsKey(external.Id)) continue;
                nodes.Add(external.Id, new SemanticInfo(external.Id, external.Name, external.FullName, null, true));
                order.Add(external.Id);
            }
        }

        private string? FindExternalOwner(string id, IReadOnlyList<ArchitectureLink> links) => links
            .Where(link => link.TargetId == id && nodes.TryGetValue(link.SourceId, out var source) && source.ProjectId is not null)
            .Select(link => nodes[link.SourceId].ProjectId).FirstOrDefault();

        private string[] FindRoots(IReadOnlyDictionary<string, ArchitectureLink[]> parents) => order
            .Where(id => !parents.ContainsKey(id) || parents[id].Length == 0)
            .Concat(request.SemanticModel.Selection?.Roots.Select(root => root.SemanticNodeId) ?? Array.Empty<string>())
            .Where(nodes.ContainsKey).Distinct(StringComparer.Ordinal).ToArray();

        private IReadOnlyList<string> FindCycles(IReadOnlyDictionary<string, ArchitectureLink[]> children, IReadOnlyList<string> roots)
        {
            var cycles = new HashSet<string>(StringComparer.Ordinal);
            var visiting = new HashSet<string>(StringComparer.Ordinal);
            var visited = new HashSet<string>(StringComparer.Ordinal);
            void Visit(string id)
            {
                if (visiting.Contains(id)) { cycles.Add(id); return; }
                if (!visited.Add(id)) return;
                visiting.Add(id);
                if (children.TryGetValue(id, out var outgoing))
                    foreach (var link in outgoing) Visit(link.TargetId);
                visiting.Remove(id);
            }
            foreach (var root in roots) Visit(root);
            foreach (var id in order) Visit(id);
            return cycles.OrderBy(id => order.IndexOf(id)).ToArray();
        }

        private sealed record SemanticInfo(string Id, string Name, string FullName, string? ProjectId, bool IsExternal);
    }
}
