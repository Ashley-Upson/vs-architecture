using System;
using System.Collections.Generic;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core.Models.ArchitectureV6;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.ArchitectureV6;

// All decisions in this stage are arithmetic over frozen logical route cells.
// It never creates, removes or reorders route cells or placement records.
internal sealed class ArchitectureV6PostRoutingPlanner
{
    private readonly ArchitecturePlanningRequest request;
    private readonly ArchitectureProjectionResult projection;
    private readonly LogicalPlacementResult placement;
    private readonly IReadOnlyList<PlannedGridRoute> frozenRoutes;

    public ArchitectureV6PostRoutingPlanner(ArchitecturePlanningRequest request,
        ArchitectureProjectionResult projection, LogicalPlacementResult placement,
        IReadOnlyList<PlannedGridRoute> frozenRoutes)
    {
        this.request = request ?? throw new ArgumentNullException(nameof(request));
        this.projection = projection ?? throw new ArgumentNullException(nameof(projection));
        this.placement = placement ?? throw new ArgumentNullException(nameof(placement));
        this.frozenRoutes = frozenRoutes ?? throw new ArgumentNullException(nameof(frozenRoutes));
    }

    public ArchitectureV6PostRoutingResult Build()
    {
        var findings = new List<ArchitecturePlanningDiagnostic>();
        var runs = BuildRuns();
        var endpointDemands = BuildEndpointDemands();
        var terminalOrders = BuildTerminalOrders();
        var allocation = new ArchitectureV6LaneAllocator(request, projection.PhysicalLinks,
            placement.NodePlacements, placement.NodeMetadata, frozenRoutes, runs,
            endpointDemands, Array.Empty<DestinationApproachReservation>(), terminalOrders).Build();

        // The allocator may attach lane identities to steps, but the cell
        // sequence itself remains the exact frozen sequence.
        EnsureRouteCellsFrozen(allocation.Routes, findings);
        var sizingResult = new ArchitectureV6TrackSizingPlanner(request, projection.PhysicalNodes,
            projection.PhysicalLinks, placement.NodePlacements, placement.NodeMetadata,
            placement.ProjectGrids, placement.SubtreeReservations, allocation.Sizing.Constraints,
            placement.DiagramGrid).Build();
        findings.AddRange(allocation.Diagnostics);
        findings.AddRange(sizingResult.Diagnostics);

        var compiler = new MechanicalCompiler(request, projection, placement, allocation,
            sizingResult, runs);
        var scene = compiler.Compile();
        findings.AddRange(scene.Diagnostics);
        return new ArchitectureV6PostRoutingResult(allocation, sizingResult, scene, findings);
    }

    private IReadOnlyList<PlannedStraightRun> BuildRuns()
    {
        var result = new List<PlannedStraightRun>();
        foreach (var route in frozenRoutes.OrderBy(item => item.PhysicalLinkId, StringComparer.Ordinal))
        {
            var steps = route.Steps.OrderBy(item => item.Order).ToArray();
            var current = new List<PlanningGridCellId>();
            RouteAxis? axis = null;
            PlanningGridId? gridId = null;
            for (var index = 1; index < steps.Length; index++)
            {
                var before = steps[index - 1];
                var after = steps[index];
                if (before.GridId != after.GridId) { Flush(); continue; }
                var nextAxis = before.CellId.RowId.Equals(after.CellId.RowId)
                    ? RouteAxis.Horizontal : RouteAxis.Vertical;
                if (axis is null || axis != nextAxis || gridId != before.GridId ||
                    (current.Count > 0 && !current[current.Count - 1].Equals(before.CellId)))
                {
                    Flush();
                    axis = nextAxis;
                    gridId = before.GridId;
                    current.Add(before.CellId);
                }
                if (!current.Contains(after.CellId)) current.Add(after.CellId);
            }
            Flush();

            void Flush()
            {
                if (axis is not null && gridId is not null && current.Count > 0)
                    result.Add(new PlannedStraightRun(route.PhysicalLinkId, gridId.Value, axis.Value,
                        current.ToArray(), current[0].ToString(), current[current.Count - 1].ToString(),
                        new LaneId($"unallocated:{route.PhysicalLinkId}:{result.Count}")));
                current = new List<PlanningGridCellId>();
                axis = null;
                gridId = null;
            }
        }
        return result;
    }

    private IReadOnlyList<NodeEndpointDemand> BuildEndpointDemands() => frozenRoutes
        .SelectMany(route => new[]
        {
            new NodeEndpointDemand(route.PhysicalLinkId, route.Source, request.RoutePlanning.MinimumPortSpacing, route.DestinationApproachReservationId),
            new NodeEndpointDemand(route.PhysicalLinkId, route.Destination, request.RoutePlanning.MinimumPortSpacing, route.DestinationApproachReservationId)
        }).ToArray();

    private IReadOnlyList<TerminalOrder> BuildTerminalOrders() => frozenRoutes
        .SelectMany(route => new[]
        {
            new TerminalOrder(route.PhysicalLinkId, route.Source.PhysicalNodeId, GridSide.Bottom,
                TerminalDirectionGroupKind.Down, 0, 0, "frozen-route-source-order"),
            new TerminalOrder(route.PhysicalLinkId, route.Destination.PhysicalNodeId, GridSide.Top,
                TerminalDirectionGroupKind.Down, 0, 0, "frozen-route-destination-order")
        }).OrderBy(item => item.PhysicalNodeId, StringComparer.Ordinal)
        .ThenBy(item => item.PhysicalLinkId, StringComparer.Ordinal).ToArray();

    private void EnsureRouteCellsFrozen(IReadOnlyList<PlannedGridRoute> routes,
        ICollection<ArchitecturePlanningDiagnostic> findings)
    {
        foreach (var route in routes)
        {
            var original = frozenRoutes.Single(item => item.PhysicalLinkId == route.PhysicalLinkId);
            var before = original.Steps.Select(item => item.CellId).ToArray();
            var after = route.Steps.Select(item => item.CellId).ToArray();
            if (!before.SequenceEqual(after))
            {
                if (request.Validation.Mode == ArchitectureValidationMode.Strict)
                    throw new InvalidOperationException($"Frozen logical route was mutated after routing: {route.PhysicalLinkId}.");
                findings.Add(new ArchitecturePlanningDiagnostic("V6FrozenRouteMutation",
                    "Post-routing allocation changed a frozen logical route-cell sequence.",
                    PlanningDiagnosticSubject.PhysicalLink, route.PhysicalLinkId));
            }
        }
    }
}

internal sealed record ArchitectureV6PostRoutingResult(
    ArchitectureLaneAllocationResult Allocation,
    PhysicalSizingResult Sizing,
    PlannedArchitecturePhysicalScene Scene,
    IReadOnlyList<ArchitecturePlanningDiagnostic> Diagnostics);

internal sealed class MechanicalCompiler
{
    private readonly ArchitecturePlanningRequest request;
    private readonly ArchitectureProjectionResult projection;
    private readonly LogicalPlacementResult placement;
    private readonly ArchitectureLaneAllocationResult allocation;
    private readonly PhysicalSizingResult sizing;
    private readonly IReadOnlyList<PlannedStraightRun> runs;
    private readonly List<ArchitecturePlanningDiagnostic> findings = new();

    public MechanicalCompiler(ArchitecturePlanningRequest request, ArchitectureProjectionResult projection,
        LogicalPlacementResult placement, ArchitectureLaneAllocationResult allocation,
        PhysicalSizingResult sizing, IReadOnlyList<PlannedStraightRun> runs)
    {
        this.request = request;
        this.projection = projection;
        this.placement = placement;
        this.allocation = allocation;
        this.sizing = sizing;
        this.runs = runs;
    }

    public PlannedArchitecturePhysicalScene Compile()
    {
        var transforms = BuildTransforms();
        var nodes = sizing.RelativeGeometry.Nodes.Select(node =>
        {
            var transform = transforms[node.GridId];
            var absolute = TranslateAbsolute(node.Bounds, transform.Origin);
            return new PlannedPhysicalNodeGeometry(node.PhysicalNodeId, node.SemanticNodeId, node.ProjectId,
                node.Bounds, absolute, node.GridId, node.AnchorCellId, node.PositionalOwnerId,
                node.ProjectionMode, node.IsExternal, node.IsStandalone, node.VisibleBounds,
                node.VisibleBounds is null ? null : TranslateAbsolute(node.VisibleBounds.Value, transform.Origin));
        }).ToArray();
        var projects = sizing.RelativeGeometry.Projects.Select(project =>
        {
            var grid = placement.ProjectGrids.Single(item => item.ProjectId == project.ProjectId).Grid;
            var transform = transforms[grid.Id];
            return new PlannedProjectGeometry(project.ProjectId, project.Bounds,
                TranslateAbsolute(project.Bounds, transform.Origin), project.LabelBounds,
                TranslateAbsolute(project.LabelBounds, transform.Origin), project.PhysicalNodeIds);
        }).ToArray();
        var grids = sizing.RelativeGeometry.Grids.Select(grid => grid with
        {
            AbsoluteBounds = TranslateAbsolute(grid.RelativeBounds, grid.Transform.Origin)
        }).ToArray();
        var terminals = CompileTerminals(nodes);
        var routes = CompileRoutes(nodes, terminals, transforms);
        var turns = allocation.Routes.SelectMany(route => route.Steps
            .Where(step => step.Role == RouteStepRole.Turn)
            .Select(step => new PlannedPhysicalTurn($"turn:{route.PhysicalLinkId}:{step.Order},",
                route.PhysicalLinkId, step.GridId, step.CellId, PointForStep(route.PhysicalLinkId, step, transforms),
                step.StraightRunId, step.StraightRunId, "frozen-cell-turn"))).ToArray();
        var crossings = Array.Empty<PlannedPhysicalCrossing>();
        var transitions = allocation.ProjectTransitions.Select(transition => new PlannedPhysicalTransition(
            transition.PhysicalLinkId, transition.SourceGridId, transition.DestinationGridId,
            new AbsolutePoint(0, 0), new AbsolutePoint(0, 0), transition.BoundaryDomainId,
            transition.Provenance)).ToArray();
        var relativeBounds = sizing.RelativeGeometry.DiagramBounds;
        var absoluteBounds = Bounds(nodes.Select(item => item.AbsoluteBounds).Concat(projects.Select(item => item.AbsoluteBounds)));
        var geometry = new PlannedArchitectureGeometry(nodes, projects, grids, Array.Empty<PlannedSubtreeGeometry>(),
            relativeBounds, absoluteBounds, routes);
        var metrics = new PlannedPhysicalSceneMetrics(
            AbsoluteNodeCount: nodes.Length,
            TerminalCount: terminals.Count,
            PhysicalRouteCount: routes.Count,
            SegmentCount: routes.Sum(route => route.Segments.Count),
            BendCount: routes.Sum(route => route.BendCount),
            CleanCrossingCount: crossings.Length,
            TransitionCount: transitions.Length,
            TotalRouteLength: routes.Sum(route => route.RouteLength),
            MaximumRouteLength: routes.Select(route => route.RouteLength).DefaultIfEmpty(0).Max(),
            NodeOverlapCount: 0,
            RouteNodeIntersectionCount: 0,
            SharedCollinearSegmentCount: 0,
            SharedBendCount: 0,
            InvalidCrossingCount: 0,
            TerminalFindingCount: 0,
            OwnershipFindingCount: 0,
            LabelGeometryUnavailableCount: 0,
            TopologyCounts: routes.GroupBy(route => route.TopologyFamily.ToString()).ToDictionary(group => group.Key, group => group.Count()),
            StageTimingsMilliseconds: new Dictionary<string, long>(),
            InvalidRouteCount: routes.Count(route => route.IsInvalid),
            AttemptedSegmentCount: routes.Sum(route => route.Segments.Count),
            DiagonalSegmentCount: routes.Sum(route => route.Segments.Count(segment => segment.Start.X != segment.End.X && segment.Start.Y != segment.End.Y)));
        return new PlannedArchitecturePhysicalScene(geometry, transforms.Values.ToArray(), terminals,
            turns, crossings, transitions, Array.Empty<PlannedSubtreeGeometry>(), findings, metrics,
            invalidRouteIds: routes.Where(route => route.IsInvalid).Select(route => route.PhysicalLinkId).ToArray());
    }

    private IReadOnlyList<PlannedPhysicalTerminal> CompileTerminals(IReadOnlyList<PlannedPhysicalNodeGeometry> nodes)
    {
        var result = new List<PlannedPhysicalTerminal>();
        foreach (var group in allocation.Endpoints.GroupBy(item => item.PhysicalNodeId + ":" + item.Side, StringComparer.Ordinal))
        {
            var node = nodes.Single(item => item.PhysicalNodeId == group.First().PhysicalNodeId);
            var bounds = node.AbsoluteBounds;
            var ordered = group.OrderBy(item => item.LogicalOrder).ThenBy(item => item.PhysicalLinkId, StringComparer.Ordinal).ToArray();
            var spacing = Math.Max(1, request.RoutePlanning.MinimumPortSpacing);
            var start = bounds.X + bounds.Width / 2 - (ordered.Length - 1) * spacing / 2;
            for (var index = 0; index < ordered.Length; index++)
            {
                var point = new AbsolutePoint(start + index * spacing,
                    group.First().Side == GridSide.Bottom ? bounds.Y + bounds.Height : bounds.Y);
                result.Add(new PlannedPhysicalTerminal($"terminal:{ordered[index].PhysicalLinkId}:{group.First().Side}",
                    ordered[index].PhysicalLinkId, node.PhysicalNodeId, group.First().Side, point, index,
                    group.Key, "collective-centred-terminal"));
            }
        }
        return result;
    }

    private IReadOnlyList<PlannedPhysicalRoute> CompileRoutes(IReadOnlyList<PlannedPhysicalNodeGeometry> nodes,
        IReadOnlyList<PlannedPhysicalTerminal> terminals, IReadOnlyDictionary<PlanningGridId, GridTransform> transforms)
    {
        return allocation.Routes.Select(route =>
        {
            var sourceTerminal = terminals.SingleOrDefault(item => item.PhysicalLinkId == route.PhysicalLinkId && item.PhysicalNodeId == route.Source.PhysicalNodeId && item.Side == GridSide.Bottom);
            var destinationTerminal = terminals.SingleOrDefault(item => item.PhysicalLinkId == route.PhysicalLinkId && item.PhysicalNodeId == route.Destination.PhysicalNodeId && item.Side == GridSide.Top);
            var points = new List<PlannedPhysicalRoutePoint>();
            if (sourceTerminal is not null) points.Add(new PlannedPhysicalRoutePoint(sourceTerminal.TerminalId, route.PhysicalLinkId,
                route.Source.GridId ?? placement.ProjectGrids.Single(item => item.ProjectId == route.SourceProjectId).Grid.Id,
                sourceTerminal.Point, RouteStepRole.SourceExit, "terminal", 0, null, null, null, null, "terminal"));
            var ordinarySteps = route.Steps
                .Where(step => step.Role is not RouteStepRole.SourceExit and not RouteStepRole.DestinationEntry)
                .OrderBy(item => item.Order)
                .ToArray();
            foreach (var step in ordinarySteps)
            {
                var point = PointForStep(route.PhysicalLinkId, step, transforms);
                if (points.Count == 0 || !SamePoint(points[points.Count - 1].Point, point))
                    points.Add(new PlannedPhysicalRoutePoint($"point:{route.PhysicalLinkId}:{step.Order}", route.PhysicalLinkId,
                        step.GridId, point, step.Role, step.TopologyProvenance, step.Order, step.StraightRunId,
                        step.Role == RouteStepRole.Turn ? $"turn:{step.Order}" : null, null, step.CellId, "cell-lane-centreline"));
            }
            if (sourceTerminal is not null && ordinarySteps.Length > 0)
                InsertSourceHandoff(points, sourceTerminal, route, ordinarySteps[0], transforms);
            if (destinationTerminal is not null && ordinarySteps.Length > 0)
                InsertDestinationHandoff(points, destinationTerminal, route, ordinarySteps[ordinarySteps.Length - 1], transforms);
            if (destinationTerminal is not null) points.Add(new PlannedPhysicalRoutePoint(destinationTerminal.TerminalId,
                route.PhysicalLinkId, route.Destination.GridId ?? placement.ProjectGrids.Single(item => item.ProjectId == route.DestinationProjectId).Grid.Id,
                destinationTerminal.Point, RouteStepRole.DestinationEntry, "terminal", int.MaxValue, null, null, null, null, "terminal"));
            var reduced = Reduce(points);
            var segments = reduced.Zip(reduced.Skip(1), (start, end) => (start, end))
                .Where(pair => pair.start.Point.X == pair.end.Point.X || pair.start.Point.Y == pair.end.Point.Y)
                .Select(pair => new PlannedPhysicalRouteSegment(route.PhysicalLinkId, pair.start.RelativePoint,
                    pair.end.RelativePoint, pair.start.Point, pair.end.Point, pair.end.GridId,
                    pair.start.Point.X == pair.end.Point.X ? RouteAxis.Vertical : RouteAxis.Horizontal,
                    pair.end.Role, new LaneId(pair.end.StraightRunId ?? "route"), route.TopologyFamily,
                    pair.end.ComponentId, pair.end.RouteStepOrder, pair.end.StraightRunId, null,
                     SegmentCells(route, pair.start, pair.end))).ToArray();
            var diagonal = reduced.Zip(reduced.Skip(1), (start, end) => (start, end))
                .Any(pair => pair.start.Point.X != pair.end.Point.X && pair.start.Point.Y != pair.end.Point.Y);
            if (diagonal) findings.Add(new ArchitecturePlanningDiagnostic("V6DiagonalMaterialisation",
                "Mechanical route materialisation produced a non-orthogonal point pair.", PlanningDiagnosticSubject.PhysicalLink, route.PhysicalLinkId));
            var length = segments.Sum(segment => Math.Abs(segment.End.X - segment.Start.X) + Math.Abs(segment.End.Y - segment.Start.Y));
            return new PlannedPhysicalRoute(route.PhysicalLinkId,
                projection.PhysicalLinks.Single(item => item.PhysicalLinkId == route.PhysicalLinkId).SemanticLinkId,
                route.Source.PhysicalNodeId, route.Destination.PhysicalNodeId, route.SourceProjectId,
                route.DestinationProjectId, route.TopologyFamily, segments, reduced.Count(item => item.Role == RouteStepRole.Turn),
                length, false, false, false, points, null, reduced.Count, points.Count - reduced.Count,
                points.Count - reduced.Count, diagonal, reduced);
        }).ToArray();
    }

    private void InsertSourceHandoff(IList<PlannedPhysicalRoutePoint> points,
        PlannedPhysicalTerminal terminal, PlannedGridRoute route, PlannedGridRouteStep firstStep,
        IReadOnlyDictionary<PlanningGridId, GridTransform> transforms)
    {
        var first = points.Count > 1 ? points[1] : points[0];
        var allocation = EndpointAllocationFor(route.PhysicalLinkId, terminal.PhysicalNodeId, GridSide.Bottom);
        var handoff = EndpointHandoffPoint(allocation, firstStep, first.Point, transforms);
        var additions = new List<PlannedPhysicalRoutePoint>();
        // Descend from the terminal before moving horizontally so a fan-out
        // does not share a segment along the source node edge.
        var sourceDrop = new AbsolutePoint(terminal.Point.X, handoff.Y);
        AddHandoffPoint(additions, route, firstStep, terminal.Point, sourceDrop, "source terminal drop");
        AddHandoffPoint(additions, route, firstStep, sourceDrop, handoff, "source allocated horizontal handoff");
        AddHandoffPoint(additions, route, firstStep, handoff, new AbsolutePoint(first.Point.X, handoff.Y), "source allocated horizontal lane");
        for (var index = additions.Count - 1; index >= 0; index--)
            points.Insert(1, additions[index]);
    }

    private void InsertDestinationHandoff(IList<PlannedPhysicalRoutePoint> points,
        PlannedPhysicalTerminal terminal, PlannedGridRoute route, PlannedGridRouteStep lastStep,
        IReadOnlyDictionary<PlanningGridId, GridTransform> transforms)
    {
        var last = points[points.Count - 1];
        var allocation = EndpointAllocationFor(route.PhysicalLinkId, terminal.PhysicalNodeId, GridSide.Top);
        var handoff = EndpointHandoffPoint(allocation, lastStep, last.Point, transforms);
        var additions = new List<PlannedPhysicalRoutePoint>();
        AddHandoffPoint(additions, route, lastStep, last.Point, new AbsolutePoint(last.Point.X, handoff.Y), "destination allocated horizontal lane");
        AddHandoffPoint(additions, route, lastStep, new AbsolutePoint(last.Point.X, handoff.Y), handoff, "destination allocated vertical handoff");
        AddHandoffPoint(additions, route, lastStep, handoff, new AbsolutePoint(terminal.Point.X, handoff.Y), "destination terminal approach");
        foreach (var point in additions)
            points.Add(point);
    }

    private PlannedEndpointAllocation? EndpointAllocationFor(string routeId, string nodeId, GridSide side) =>
        allocation.Endpoints.SingleOrDefault(item => item.PhysicalLinkId == routeId &&
            item.PhysicalNodeId == nodeId && item.Side == side);

    private AbsolutePoint EndpointHandoffPoint(PlannedEndpointAllocation? endpoint,
        PlannedGridRouteStep step, AbsolutePoint fallback,
        IReadOnlyDictionary<PlanningGridId, GridTransform> transforms)
    {
        var grid = sizing.RelativeGeometry.Grids.SingleOrDefault(item => item.GridId.Equals(step.GridId));
        var row = grid?.Rows.SingleOrDefault(item => item.Id.Equals(step.CellId.RowId));
        var column = grid?.Columns.SingleOrDefault(item => item.Id.Equals(step.CellId.ColumnId));
        if (row is null || column is null || !transforms.TryGetValue(step.GridId, out var transform)) return fallback;
        var x = fallback.X - transform.Origin.X;
        var y = fallback.Y - transform.Origin.Y;
        if (endpoint?.HandoffHorizontalLane is { } horizontal)
            y = LaneCoordinate(row.RelativeOffset, row.FinalExtent, allocation.HorizontalLanes, horizontal.Value);
        if (endpoint?.HandoffVerticalLane is { } vertical)
            x = LaneCoordinate(column.RelativeOffset, column.FinalExtent, allocation.VerticalLanes, vertical.Value);
        return new AbsolutePoint(x + transform.Origin.X, y + transform.Origin.Y);
    }

    private static void AddHandoffPoint(ICollection<PlannedPhysicalRoutePoint> points,
        PlannedGridRoute route, PlannedGridRouteStep step, AbsolutePoint previous, AbsolutePoint point,
        string provenance)
    {
        if (previous == point) return;
        points.Add(new PlannedPhysicalRoutePoint($"handoff:{route.PhysicalLinkId}:{points.Count}", route.PhysicalLinkId,
            step.GridId, point, provenance.StartsWith("destination", StringComparison.Ordinal)
                ? RouteStepRole.DestinationEntry : RouteStepRole.SourceExit, "endpoint-handoff", step.Order, step.StraightRunId, null,
            null, step.CellId, provenance));
    }

    private AbsolutePoint PointForStep(string routeId, PlannedGridRouteStep step, IReadOnlyDictionary<PlanningGridId, GridTransform> transforms)
    {
        var grid = sizing.Sizing.Rows.Any(row => row.Id.Equals(step.CellId.RowId))
            ? sizing.RelativeGeometry.Grids.SingleOrDefault(item => item.GridId.Equals(step.GridId))
            : null;
        var row = grid?.Rows.SingleOrDefault(item => item.Id.Equals(step.CellId.RowId));
        var column = grid?.Columns.SingleOrDefault(item => item.Id.Equals(step.CellId.ColumnId));
        if (row is null || column is null) return new AbsolutePoint(0, 0);
        var x = column.RelativeOffset + column.FinalExtent / 2;
        var y = row.RelativeOffset + row.FinalExtent / 2;
        if (step.Role == RouteStepRole.Turn)
        {
            var turn = allocation.Turns
                .Where(item => item.RouteId == routeId && item.CellId == step.CellId.ToString())
                .Select(item =>
                {
                    var horizontal = allocation.HorizontalLanes.SingleOrDefault(lane => lane.RunId == item.HorizontalRunId);
                    var vertical = allocation.VerticalLanes.SingleOrDefault(lane => lane.RunId == item.VerticalRunId);
                    return (item, horizontal, vertical, score: TurnLaneMatchScore(routeId, step, horizontal, vertical));
                })
                .OrderByDescending(item => item.score)
                .ThenBy(item => item.item.BendIdentity, StringComparer.Ordinal)
                .FirstOrDefault();
            if (turn.horizontal is not null)
                y = LaneCoordinate(row.RelativeOffset, row.FinalExtent, allocation.HorizontalLanes, turn.horizontal.Lane.Value);
            if (turn.vertical is not null)
                x = LaneCoordinate(column.RelativeOffset, column.FinalExtent, allocation.VerticalLanes, turn.vertical.Lane.Value);
        }
        else if (step.EntrySide is GridSide.Left or GridSide.Right || step.ExitSide is GridSide.Left or GridSide.Right)
        {
            if (step.AllocatedLane is not null)
                y = LaneCoordinate(row.RelativeOffset, row.FinalExtent, allocation.HorizontalLanes, step.AllocatedLane.Value.Value);
        }
        else if (step.Role != RouteStepRole.SourceExit && step.Role != RouteStepRole.DestinationEntry && step.AllocatedLane is not null)
        {
            x = LaneCoordinate(column.RelativeOffset, column.FinalExtent, allocation.VerticalLanes, step.AllocatedLane.Value.Value);
        }
        var transform = transforms[step.GridId].Origin;
        return new AbsolutePoint(x + transform.X, y + transform.Y);
    }

    private int TurnLaneMatchScore(string routeId, PlannedGridRouteStep turn,
        PlannedLaneAllocation? horizontal, PlannedLaneAllocation? vertical)
    {
        var neighbours = allocation.Routes.Single(route => route.PhysicalLinkId == routeId).Steps
            .Where(step => step.Order == turn.Order - 1 || step.Order == turn.Order + 1)
            .ToArray();
        return neighbours.Count(step => horizontal?.Cells.Contains(step.CellId) == true ||
                                        vertical?.Cells.Contains(step.CellId) == true);
    }

    private int LaneCoordinate(int offset, int extent, IReadOnlyList<PlannedLaneAllocation> lanes, string laneId)
    {
        var ordinal = lanes.Where(item => item.Lane.Value == laneId)
            .Select(item => item.Ordinal)
            .DefaultIfEmpty(0)
            .First();
        return ArchitectureV6LaneGeometry.Coordinate(offset, ordinal,
            request.RoutePlanning.MinimumPortSpacing, request.RoutePlanning.MinimumParallelSpacing);
    }

    private static IReadOnlyList<PlanningGridCellId> SegmentCells(PlannedGridRoute route,
        PlannedPhysicalRoutePoint start, PlannedPhysicalRoutePoint end)
    {
        var lower = Math.Min(start.RouteStepOrder, end.RouteStepOrder);
        var upper = Math.Max(start.RouteStepOrder, end.RouteStepOrder);
        return route.Steps.Where(step => step.Order >= lower && step.Order <= upper)
            .Select(step => step.CellId).Distinct().ToArray();
    }

    private IReadOnlyDictionary<PlanningGridId, GridTransform> BuildTransforms()
    {
        var result = new Dictionary<PlanningGridId, GridTransform> { [placement.DiagramGrid.Grid.Id] = placement.DiagramGrid.Grid.Transform };
        var diagram = sizing.RelativeGeometry.Grids.SingleOrDefault(item => item.GridId.Equals(placement.DiagramGrid.Grid.Id));
        foreach (var project in placement.ProjectGrids)
        {
            var projectIndex = placement.ProjectGrids.OrderBy(item => item.ProjectId, StringComparer.Ordinal)
                .Select((item, index) => (item.ProjectId, index)).Single(item => item.ProjectId == project.ProjectId).index;
            var footprint = placement.DiagramGrid.ProjectFootprints.ElementAtOrDefault(projectIndex);
            var columns = diagram?.Columns.OrderBy(item => item.LogicalOrder).ToArray() ?? Array.Empty<PlanningGridColumn>();
            var rows = diagram?.Rows.OrderBy(item => item.LogicalOrder).ToArray() ?? Array.Empty<PlanningGridRow>();
            var columnIndex = Math.Max(0, Math.Min(columns.Length - 1, footprint.X));
            var rowIndex = Math.Max(0, Math.Min(rows.Length - 1, footprint.Y));
            var column = columns.Length == 0 ? null : columns[columnIndex];
            var row = rows.Length == 0 ? null : rows[rowIndex];
            if ((column is null || row is null) && placement.ProjectGrids.Count == 1)
            {
                result[project.Grid.Id] = new GridTransform(project.Grid.Id, placement.DiagramGrid.Grid.Transform.Origin);
                continue;
            }
            if (column is null || row is null)
                throw new InvalidOperationException($"Sized diagram grid has no authoritative placement track for project '{project.ProjectId}'.");
            result[project.Grid.Id] = new GridTransform(project.Grid.Id,
                new RelativePoint(placement.DiagramGrid.Grid.Transform.Origin.X + column.RelativeOffset,
                    placement.DiagramGrid.Grid.Transform.Origin.Y + row.RelativeOffset));
        }
        return result;
    }

    private static RelativeRectangle Translate(RelativeRectangle rectangle, RelativePoint origin) =>
        new(rectangle.X + origin.X, rectangle.Y + origin.Y, rectangle.Width, rectangle.Height);
    private static AbsoluteRectangle TranslateAbsolute(RelativeRectangle rectangle, RelativePoint origin) =>
        new(rectangle.X + origin.X, rectangle.Y + origin.Y, rectangle.Width, rectangle.Height);
    private static bool SamePoint(AbsolutePoint first, AbsolutePoint second) => first.X == second.X && first.Y == second.Y;
    private static AbsoluteRectangle Bounds(IEnumerable<AbsoluteRectangle> rectangles)
    {
        var items = rectangles.ToArray();
        if (items.Length == 0) return new AbsoluteRectangle(0, 0, 1, 1);
        var minX = items.Min(item => item.X); var minY = items.Min(item => item.Y);
        var maxX = items.Max(item => item.X + item.Width); var maxY = items.Max(item => item.Y + item.Height);
        return new AbsoluteRectangle(minX, minY, Math.Max(1, maxX - minX), Math.Max(1, maxY - minY));
    }
    private static IReadOnlyList<PlannedPhysicalRoutePoint> Reduce(IReadOnlyList<PlannedPhysicalRoutePoint> points)
    {
        var result = new List<PlannedPhysicalRoutePoint>();
        foreach (var point in points)
        {
            if (result.Count >= 2)
            {
                var before = result[result.Count - 2].Point; var current = result[result.Count - 1].Point;
                if ((before.X == current.X && current.X == point.Point.X) || (before.Y == current.Y && current.Y == point.Point.Y))
                    result.RemoveAt(result.Count - 1);
            }
            result.Add(point);
        }
        return result;
    }
}
