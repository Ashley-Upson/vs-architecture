using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core.Models.ArchitectureV6;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.ArchitectureV6;

internal sealed class ArchitectureV6PhysicalSceneCompiler
{
    private readonly ArchitecturePlanningRequest request;
    private readonly IReadOnlyList<PlannedPhysicalNode> nodes;
    private readonly IReadOnlyList<PlannedPhysicalLink> links;
    private readonly DiagramRoutingGrid diagramGrid;
    private readonly IReadOnlyList<ProjectRoutingGrid> projects;
    private readonly IReadOnlyList<PlannedNodePlacement> placements;
    private readonly IReadOnlyList<PlannedGridRoute> routes;
    private readonly GridTrackSizingPlan sizing;
    private readonly PlannedArchitectureRelativeGeometry relative;
    private readonly ArchitectureLaneAllocationResult allocation;
    private readonly IReadOnlyList<SubtreeReservation> subtreeReservations;
    private readonly List<ArchitecturePlanningDiagnostic> findings = new();

    public ArchitectureV6PhysicalSceneCompiler(
        ArchitecturePlanningRequest request,
        IReadOnlyList<PlannedPhysicalNode> nodes,
        IReadOnlyList<PlannedPhysicalLink> links,
        DiagramRoutingGrid diagramGrid,
        IReadOnlyList<ProjectRoutingGrid> projects,
        IReadOnlyList<PlannedNodePlacement> placements,
        IReadOnlyList<PlannedGridRoute> routes,
        GridTrackSizingPlan sizing,
        PlannedArchitectureRelativeGeometry relative,
        ArchitectureLaneAllocationResult allocation,
        IReadOnlyList<SubtreeReservation> subtreeReservations)
    {
        this.request = request ?? throw new ArgumentNullException(nameof(request));
        this.nodes = nodes ?? throw new ArgumentNullException(nameof(nodes));
        this.links = links ?? throw new ArgumentNullException(nameof(links));
        this.diagramGrid = diagramGrid ?? throw new ArgumentNullException(nameof(diagramGrid));
        this.projects = projects ?? throw new ArgumentNullException(nameof(projects));
        this.placements = placements ?? throw new ArgumentNullException(nameof(placements));
        this.routes = routes ?? throw new ArgumentNullException(nameof(routes));
        this.sizing = sizing ?? throw new ArgumentNullException(nameof(sizing));
        this.relative = relative ?? throw new ArgumentNullException(nameof(relative));
        this.allocation = allocation ?? throw new ArgumentNullException(nameof(allocation));
        this.subtreeReservations = subtreeReservations ?? throw new ArgumentNullException(nameof(subtreeReservations));
    }

    public PlannedArchitecturePhysicalScene Compile()
    {
        var timings = new Dictionary<string, long>(StringComparer.Ordinal);
        var timer = Stopwatch.StartNew();
        var transforms = BuildTransforms();
        timings["transformConstruction"] = timer.ElapsedMilliseconds;

        timer.Restart();
        var nodeGeometry = CompileNodes(transforms);
        var projectGeometry = CompileProjects(transforms);
        var gridGeometry = CompileGrids(transforms);
        var reservationGeometry = CompileReservations(transforms);
        timings["nodeCompilation"] = timer.ElapsedMilliseconds;

        timer.Restart();
        var terminals = CompileTerminals(nodeGeometry, transforms);
        timings["terminalCompilation"] = timer.ElapsedMilliseconds;

        timer.Restart();
        var turns = CompileTurns(transforms);
        var crossings = CompileCrossings(transforms);
        timings["laneCoordinateCompilation"] = timer.ElapsedMilliseconds;

        timer.Restart();
        var physicalRoutes = CompileRoutes(nodeGeometry, terminals, transforms);
        var transitions = CompileTransitions(transforms);
        timings["routeMaterialisation"] = timer.ElapsedMilliseconds;

        timer.Restart();
        var geometry = new PlannedArchitectureGeometry(nodeGeometry, projectGeometry, gridGeometry, reservationGeometry,
            relative.DiagramBounds, new AbsoluteRectangle(0, 0, relative.DiagramBounds.Width, relative.DiagramBounds.Height), physicalRoutes);
        timings["normalization"] = timer.ElapsedMilliseconds;

        timer.Restart();
        Validate(geometry, terminals, physicalRoutes, turns, crossings, transitions);
        timings["physicalValidation"] = timer.ElapsedMilliseconds;
        if (projects.Any(project => project.ProjectLabelReservation is null))
        {
            findings.Add(new ArchitecturePlanningDiagnostic("LabelGeometryUnavailable",
                "Measured project-label geometry was not supplied; no label obstruction was invented.", PlanningDiagnosticSubject.Grid, null));
        }
        var metrics = BuildMetrics(geometry, terminals, physicalRoutes, turns, crossings, transitions, timings);
        return new PlannedArchitecturePhysicalScene(geometry, transforms.Values.OrderBy(item => item.GridId.Value, StringComparer.Ordinal).ToArray(),
            terminals, turns, crossings, transitions, reservationGeometry, findings, metrics);
    }

    private Dictionary<PlanningGridId, GridTransform> BuildTransforms()
    {
        var result = new Dictionary<PlanningGridId, GridTransform>();
        result[diagramGrid.Grid.Id] = diagramGrid.Grid.Transform;
        var footprints = diagramGrid.ProjectFootprints.OrderBy(item => item.X).ThenBy(item => item.Y).ToArray();
        foreach (var project in projects.OrderBy(item => item.ProjectId, StringComparer.Ordinal).Select((item, index) => (item, index)))
        {
            var footprint = project.index < footprints.Length ? footprints[project.index] : new RelativeRectangle(0, 0, 0, 0);
            var origin = new RelativePoint(footprint.X, footprint.Y);
            result[project.item.Grid.Id] = new GridTransform(project.item.Grid.Id, origin);
            if (project.item.ProjectLabelReservation is null)
                findings.Add(new ArchitecturePlanningDiagnostic("LabelGeometryUnavailable", "Project label measurement is unavailable.", PlanningDiagnosticSubject.Grid, project.item.ProjectId));
        }
        return result;
    }

    private IReadOnlyList<PlannedPhysicalNodeGeometry> CompileNodes(IReadOnlyDictionary<PlanningGridId, GridTransform> transforms)
    {
        var result = new List<PlannedPhysicalNodeGeometry>();
        foreach (var node in relative.Nodes.OrderBy(item => item.PhysicalNodeId, StringComparer.Ordinal))
        {
            if (!transforms.TryGetValue(node.GridId, out var transform))
            {
                findings.Add(new ArchitecturePlanningDiagnostic("MissingGridTransform", "Node grid has no accepted transform.", PlanningDiagnosticSubject.PhysicalNode, node.PhysicalNodeId));
                continue;
            }
            var absolute = Translate(node.Bounds, transform.Origin);
            var source = nodes.SingleOrDefault(item => item.PhysicalNodeId == node.PhysicalNodeId);
            result.Add(new PlannedPhysicalNodeGeometry(node.PhysicalNodeId, node.SemanticNodeId, node.ProjectId, node.Bounds, absolute,
                node.GridId, node.AnchorCellId, node.PositionalOwnerId, node.ProjectionMode, node.IsExternal, node.IsStandalone));
            if (source is null)
                findings.Add(new ArchitecturePlanningDiagnostic("PhysicalNodeGeometrySourceMissing", "Geometry has no physical node source.", PlanningDiagnosticSubject.PhysicalNode, node.PhysicalNodeId));
        }
        if (result.Count != nodes.Count)
            findings.Add(new ArchitecturePlanningDiagnostic("AbsoluteNodeAccounting", "Every physical node must compile to one absolute node rectangle.", PlanningDiagnosticSubject.PhysicalNode, null));
        return result;
    }

    private IReadOnlyList<PlannedProjectGeometry> CompileProjects(IReadOnlyDictionary<PlanningGridId, GridTransform> transforms)
    {
        var result = new List<PlannedProjectGeometry>();
        foreach (var project in relative.Projects.OrderBy(item => item.ProjectId, StringComparer.Ordinal))
        {
            var grid = projects.SingleOrDefault(item => item.ProjectId == project.ProjectId)?.Grid;
            if (grid is null || !transforms.TryGetValue(grid.Id, out var transform)) continue;
            result.Add(new PlannedProjectGeometry(project.ProjectId, project.Bounds, Translate(project.Bounds, transform.Origin), project.LabelBounds,
                Translate(project.LabelBounds, transform.Origin), project.PhysicalNodeIds));
        }
        return result;
    }

    private IReadOnlyList<PlannedGridGeometry> CompileGrids(IReadOnlyDictionary<PlanningGridId, GridTransform> transforms) =>
        relative.Grids.OrderBy(item => item.GridId.Value, StringComparer.Ordinal).Select(grid =>
        {
            var transform = transforms.TryGetValue(grid.GridId, out var value) ? value : new GridTransform(grid.GridId, new RelativePoint(0, 0));
            return grid with { Transform = transform, AbsoluteBounds = Translate(grid.RelativeBounds, transform.Origin) };
        }).ToArray();

    private IReadOnlyList<PlannedSubtreeGeometry> CompileReservations(IReadOnlyDictionary<PlanningGridId, GridTransform> transforms)
    {
        foreach (var reservation in subtreeReservations)
            if (!transforms.ContainsKey(reservation.GridId))
                findings.Add(new ArchitecturePlanningDiagnostic("MissingReservationGridTransform", "Reservation grid has no accepted transform.", PlanningDiagnosticSubject.Grid, reservation.GridId.Value));
        return relative.Subtrees.OrderBy(item => item.SubtreeId, StringComparer.Ordinal).Select(item =>
        {
            var origin = transforms.TryGetValue(item.GridId, out var transform) ? transform.Origin : new RelativePoint(0, 0);
            return new PlannedSubtreeGeometry(item.SubtreeId, item.PositionalOwnerId, item.GridId, item.Bounds,
                Translate(item.Bounds, origin), item.AncestorSubtreeId);
        }).ToArray();
    }

    private IReadOnlyList<PlannedPhysicalTerminal> CompileTerminals(IReadOnlyList<PlannedPhysicalNodeGeometry> nodeGeometry,
        IReadOnlyDictionary<PlanningGridId, GridTransform> transforms)
    {
        var result = new List<PlannedPhysicalTerminal>();
        foreach (var endpoint in allocation.Endpoints.OrderBy(item => item.PhysicalLinkId, StringComparer.Ordinal).ThenBy(item => item.Side))
        {
            var node = nodeGeometry.SingleOrDefault(item => item.PhysicalNodeId == endpoint.PhysicalNodeId);
            if (node is null || !transforms.TryGetValue(node.GridId, out var transform)) continue;
            var spacing = Math.Max(1, request.RoutePlanning.MinimumPortSpacing);
            var x = node.AbsoluteBounds.X + node.AbsoluteBounds.Width / 2 + endpoint.TrackOffset * spacing;
            var y = endpoint.Side == GridSide.Bottom ? node.AbsoluteBounds.Y + node.AbsoluteBounds.Height : node.AbsoluteBounds.Y;
            var point = new AbsolutePoint(x, y);
            var id = endpoint.PhysicalLinkId + ":" + endpoint.Side;
            result.Add(new PlannedPhysicalTerminal(id, endpoint.PhysicalLinkId, endpoint.PhysicalNodeId, endpoint.Side, point,
                endpoint.LogicalOrder, endpoint.DomainId, endpoint.Provenance));
            if ((endpoint.Side == GridSide.Bottom && y != node.AbsoluteBounds.Y + node.AbsoluteBounds.Height) ||
                (endpoint.Side == GridSide.Top && y != node.AbsoluteBounds.Y))
                findings.Add(new ArchitecturePlanningDiagnostic("TerminalNotOnExpectedEdge", "Terminal is not on its allocated node edge.", PlanningDiagnosticSubject.PhysicalNode, endpoint.PhysicalNodeId));
        }
        ValidateTerminalSeparation(result);
        return result;
    }

    private IReadOnlyList<PlannedPhysicalTurn> CompileTurns(IReadOnlyDictionary<PlanningGridId, GridTransform> transforms)
    {
        var result = new List<PlannedPhysicalTurn>();
        foreach (var turn in allocation.Turns.OrderBy(item => item.BendIdentity, StringComparer.Ordinal))
        {
            var route = routes.SingleOrDefault(item => item.PhysicalLinkId == turn.RouteId);
            var step = route?.Steps.SingleOrDefault(item => item.CellId.ToString() == turn.CellId);
            if (route is null || step is null || !transforms.TryGetValue(step.GridId, out var transform)) continue;
            var point = CellPoint(step, route, transform);
            result.Add(new PlannedPhysicalTurn(turn.BendIdentity, turn.RouteId, step.GridId, step.CellId, point,
                turn.HorizontalRunId, turn.VerticalRunId, turn.Provenance));
        }
        return result;
    }

    private IReadOnlyList<PlannedPhysicalCrossing> CompileCrossings(IReadOnlyDictionary<PlanningGridId, GridTransform> transforms)
    {
        return allocation.CleanCrossings.OrderBy(item => item.CellId, StringComparer.Ordinal).Select(crossing =>
        {
            var parts = crossing.CellId.Split(new[] { ':' }, StringSplitOptions.RemoveEmptyEntries);
            var grid = transforms.Keys.SingleOrDefault(item => item.Value == parts.FirstOrDefault());
            var route = routes.SingleOrDefault(item => item.Steps.Any(step => step.CellId.ToString() == crossing.CellId));
            var step = route?.Steps.FirstOrDefault(item => item.CellId.ToString() == crossing.CellId);
            var point = step is null || !transforms.TryGetValue(step.GridId, out var transform) ? new AbsolutePoint(0, 0) : CellPoint(step, route!, transform);
            return new PlannedPhysicalCrossing(route?.PhysicalLinkId ?? string.Empty, string.Empty, step?.GridId ?? new PlanningGridId("unknown"),
                step?.CellId ?? new PlanningGridCellId(new PlanningGridId("unknown"), new PlanningGridRowId("unknown"), new PlanningGridColumnId("unknown")), point, crossing.Provenance);
        }).ToArray();
    }

    private IReadOnlyList<PlannedPhysicalTransition> CompileTransitions(IReadOnlyDictionary<PlanningGridId, GridTransform> transforms)
    {
        return allocation.ProjectTransitions.OrderBy(item => item.PhysicalLinkId, StringComparer.Ordinal).Select(item =>
        {
            var route = routes.SingleOrDefault(route => route.PhysicalLinkId == item.PhysicalLinkId);
            var transition = route?.Transitions.FirstOrDefault(value => value.SourceGridId.Equals(item.SourceGridId) && value.DestinationGridId.Equals(item.DestinationGridId));
            var source = transition is null || !transforms.TryGetValue(transition.SourceGridId, out var sourceTransform) ? new AbsolutePoint(0, 0) : CellCentre(transition.SourceBoundaryCellId, sourceTransform);
            var destination = transition is null || !transforms.TryGetValue(transition.DestinationGridId, out var destinationTransform) ? new AbsolutePoint(0, 0) : CellCentre(transition.DestinationBoundaryCellId, destinationTransform);
            return new PlannedPhysicalTransition(item.PhysicalLinkId, item.SourceGridId, item.DestinationGridId, source, destination,
                transition?.OwnershipTransition ?? "project-transition", item.Provenance);
        }).ToArray();
    }

    private IReadOnlyList<PlannedPhysicalRoute> CompileRoutes(IReadOnlyList<PlannedPhysicalNodeGeometry> nodeGeometry,
        IReadOnlyList<PlannedPhysicalTerminal> terminals, IReadOnlyDictionary<PlanningGridId, GridTransform> transforms)
    {
        var result = new List<PlannedPhysicalRoute>();
        foreach (var route in allocation.Routes.OrderBy(item => item.PhysicalLinkId, StringComparer.Ordinal))
        {
            var sourceTerminal = terminals.SingleOrDefault(item => item.PhysicalLinkId == route.PhysicalLinkId && item.Side == GridSide.Bottom);
            var destinationTerminal = terminals.SingleOrDefault(item => item.PhysicalLinkId == route.PhysicalLinkId && item.Side == GridSide.Top);
            if (sourceTerminal is null || destinationTerminal is null)
            {
                findings.Add(new ArchitecturePlanningDiagnostic("RouteTerminalMissing", "Route could not be materialised without both terminals.", PlanningDiagnosticSubject.PhysicalLink, route.PhysicalLinkId));
                continue;
            }
            var components = BuildComponents(route, sourceTerminal, destinationTerminal, transforms);
            var rawPoints = components.SelectMany(component => component.Points).ToArray();
            var pointEntries = components.SelectMany(component => component.Points.Select(point => (Point: point, Component: component))).ToArray();
            var segments = new List<PlannedPhysicalRouteSegment>();
            for (var index = 1; index < pointEntries.Length; index++)
            {
                var before = pointEntries[index - 1];
                var after = pointEntries[index];
                if (before.Point.Point == after.Point.Point) continue;
                var axis = before.Point.Point.X == after.Point.Point.X ? RouteAxis.Vertical : RouteAxis.Horizontal;
                if (before.Point.Point.X != after.Point.Point.X && before.Point.Point.Y != after.Point.Point.Y)
                    findings.Add(new ArchitecturePlanningDiagnostic("DiagonalComponentConnection", "Adjacent accepted route components do not connect orthogonally.", PlanningDiagnosticSubject.PhysicalSegment, route.PhysicalLinkId));
                var component = after.Component;
                segments.Add(new PlannedPhysicalRouteSegment(route.PhysicalLinkId, RelativePointFor(before.Point.Point, before.Point.GridId, transforms), RelativePointFor(after.Point.Point, after.Point.GridId, transforms), before.Point.Point, after.Point.Point,
                    after.Point.GridId, axis, after.Point.Role, component.Lane ?? new LaneId("component:" + component.ComponentId), route.TopologyFamily,
                    component.ComponentId, component.RouteStepOrder, component.StraightRunId, component.LaneDomainId, component.AllocatedCells,
                    component.AllocatedCells.Select(cell => cell.RowId).Distinct().Count() == 1 ? component.AllocatedCells.Select(cell => cell.RowId).Distinct().First() : null,
                    component.AllocatedCells.Select(cell => cell.ColumnId).Distinct().Count() == 1 ? component.AllocatedCells.Select(cell => cell.ColumnId).Distinct().First() : null,
                    before.Point.Provenance, after.Point.Provenance));
            }
            var length = segments.Sum(segment => Math.Abs(segment.End.X - segment.Start.X) + Math.Abs(segment.End.Y - segment.Start.Y));
            result.Add(new PlannedPhysicalRoute(route.PhysicalLinkId, links.SingleOrDefault(item => item.PhysicalLinkId == route.PhysicalLinkId)?.SemanticLinkId ?? string.Empty,
                route.Source.PhysicalNodeId, route.Destination.PhysicalNodeId, route.Source.PhysicalNodeId, route.Destination.PhysicalNodeId,
                route.TopologyFamily, segments, components.Count(component => component.Role == RouteStepRole.Turn), length, false, false, false,
                rawPoints, components, rawPoints.Length, 0, 0));
        }
        return result;
    }

    private IReadOnlyList<PlannedPhysicalRouteComponent> BuildComponents(PlannedGridRoute route,
        PlannedPhysicalTerminal sourceTerminal, PlannedPhysicalTerminal destinationTerminal,
        IReadOnlyDictionary<PlanningGridId, GridTransform> transforms)
    {
        var result = new List<PlannedPhysicalRouteComponent>();
        var ordered = route.Steps.OrderBy(step => step.Order).ToArray();
        var groups = new List<IReadOnlyList<PlannedGridRouteStep>>();
        foreach (var step in ordered)
        {
            var lastGroupIndex = groups.Count - 1;
            if (step.Role != RouteStepRole.Turn && groups.Count > 0 && groups[lastGroupIndex].Count > 0 &&
                groups[lastGroupIndex][groups[lastGroupIndex].Count - 1].Role != RouteStepRole.Turn &&
                groups[lastGroupIndex][groups[lastGroupIndex].Count - 1].StraightRunId == step.StraightRunId && step.StraightRunId is not null)
            {
                groups[lastGroupIndex] = groups[lastGroupIndex].Concat(new[] { step }).ToArray();
            }
            else groups.Add(new[] { step });
        }
        var firstStep = ordered.FirstOrDefault();
        var firstPoint = firstStep is null || !transforms.TryGetValue(firstStep.GridId, out var firstTransform)
            ? sourceTerminal.Point : PointForStep(route, firstStep, firstTransform);
        result.Add(new PlannedPhysicalRouteComponent(route.PhysicalLinkId + ":source-terminal", route.PhysicalLinkId, RouteStepRole.SourceExit, -1, null, null, null,
            Array.Empty<PlanningGridCellId>(), new[] { Point(route, route.Steps.FirstOrDefault()?.GridId ?? new PlanningGridId("unknown"), sourceTerminal.Point, RouteStepRole.SourceExit, route.PhysicalLinkId + ":source-terminal", -1, null, null, null, "source terminal", route.PhysicalLinkId + ":source-terminal") }, null, null, route.SourceProjectId ?? "diagram", "source terminal"));
        result.Add(new PlannedPhysicalRouteComponent(route.PhysicalLinkId + ":source-stub", route.PhysicalLinkId, RouteStepRole.SourceExit, -1, null, null, null,
            Array.Empty<PlanningGridCellId>(), StubPoints(route, sourceTerminal.Point, firstPoint, firstStep?.GridId ?? new PlanningGridId("unknown"), RouteStepRole.SourceExit, route.PhysicalLinkId + ":source-stub", "source stub"), null, null, route.SourceProjectId ?? "diagram", "source stub"));
        foreach (var group in groups)
        {
            var first = group[0];
            if (!transforms.TryGetValue(first.GridId, out var transform)) continue;
            var componentId = route.PhysicalLinkId + ":component:" + first.Order;
            var allocatedTurn = first.Role == RouteStepRole.Turn
                ? allocation.Turns.SingleOrDefault(turn => turn.RouteId == route.PhysicalLinkId && turn.CellId == first.CellId.ToString())
                : null;
            if (first.Role == RouteStepRole.Turn && allocatedTurn is null)
                findings.Add(new ArchitecturePlanningDiagnostic("MissingAllocatedTurn", "A turn route step has no allocated turn identity and cannot be materialised as an owned turn.", PlanningDiagnosticSubject.PhysicalLink, route.PhysicalLinkId));
            var points = group[0].Role == RouteStepRole.Turn
                ? new[] { Point(route, first.GridId, TurnPoint(route, first, transform), RouteStepRole.Turn, componentId + ":turn", first.Order, first.StraightRunId, allocatedTurn?.BendIdentity, first.CellId, "allocated turn", componentId) }
                : group.Select(step => Point(route, step.GridId, CellPoint(step, route, transform), step.Role, componentId + ":cell:" + step.Order, step.Order, step.StraightRunId, null, step.CellId, "allocated straight-run cell", componentId)).ToArray();
            var lane = first.AllocatedLane;
            var allocationForLane = lane is null ? null : allocation.HorizontalLanes.Concat(allocation.VerticalLanes).FirstOrDefault(item => item.Lane.Value == lane.Value.Value);
            result.Add(new PlannedPhysicalRouteComponent(componentId, route.PhysicalLinkId, first.Role, first.Order,
                first.StraightRunId, allocationForLane?.DomainId, lane, group.Select(step => step.CellId).ToArray(), points,
                allocatedTurn?.BendIdentity,
                null, first.GridId.Value, "ordered route-step component"));
        }
        foreach (var transition in route.Transitions.OrderBy(item => item.SourceGridId.Value, StringComparer.Ordinal).ThenBy(item => item.DestinationGridId.Value, StringComparer.Ordinal))
        {
            var representedByRouteSteps = route.Steps.Any(step => step.CellId.Equals(transition.SourceBoundaryCellId) || step.CellId.Equals(transition.DestinationBoundaryCellId));
            if (representedByRouteSteps)
                continue;
            if (!transforms.TryGetValue(transition.SourceGridId, out var sourceTransform) || !transforms.TryGetValue(transition.DestinationGridId, out var destinationTransform))
                continue;
            var componentId = route.PhysicalLinkId + ":transition:" + transition.SourceGridId.Value + ":" + transition.DestinationGridId.Value;
            var points = new[]
            {
                Point(route, transition.SourceGridId, CellCentre(transition.SourceBoundaryCellId, sourceTransform), RouteStepRole.ProjectTransition, componentId + ":source", int.MaxValue - 1, null, transition.OwnershipTransition, transition.SourceBoundaryCellId, "source project transition", componentId),
                Point(route, transition.DestinationGridId, CellCentre(transition.DestinationBoundaryCellId, destinationTransform), RouteStepRole.ProjectTransition, componentId + ":destination", int.MaxValue - 1, null, transition.OwnershipTransition, transition.DestinationBoundaryCellId, "destination project transition", componentId)
            };
            result.Add(new PlannedPhysicalRouteComponent(componentId, route.PhysicalLinkId, RouteStepRole.ProjectTransition, int.MaxValue - 1,
                null, null, null, new[] { transition.SourceBoundaryCellId, transition.DestinationBoundaryCellId }, points,
                null, transition.OwnershipTransition, transition.DestinationGridId.Value, "explicit grid transition"));
        }
        var last = ordered.LastOrDefault();
        var lastPoint = last is null || !transforms.TryGetValue(last.GridId, out var lastTransform) ? destinationTerminal.Point : PointForStep(route, last, lastTransform);
        result.Add(new PlannedPhysicalRouteComponent(route.PhysicalLinkId + ":destination-stub", route.PhysicalLinkId, RouteStepRole.DestinationEntry, int.MaxValue, null, null, null,
            Array.Empty<PlanningGridCellId>(), StubPoints(route, lastPoint, destinationTerminal.Point, last?.GridId ?? new PlanningGridId("unknown"), RouteStepRole.DestinationEntry, route.PhysicalLinkId + ":destination-stub", "destination stub"), null, null, route.DestinationProjectId ?? "diagram", "destination stub"));
        result.Add(new PlannedPhysicalRouteComponent(route.PhysicalLinkId + ":destination-terminal", route.PhysicalLinkId, RouteStepRole.DestinationEntry, int.MaxValue, null, null, null,
            Array.Empty<PlanningGridCellId>(), new[] { Point(route, route.Steps.LastOrDefault()?.GridId ?? new PlanningGridId("unknown"), destinationTerminal.Point, RouteStepRole.DestinationEntry, route.PhysicalLinkId + ":destination-terminal", int.MaxValue, null, null, null, "destination terminal", route.PhysicalLinkId + ":destination-terminal") }, null, null, route.DestinationProjectId ?? "diagram", "destination terminal"));
        return result;
    }

    private PlannedPhysicalRoutePoint[] StubPoints(PlannedGridRoute route, AbsolutePoint start, AbsolutePoint end, PlanningGridId gridId, RouteStepRole role, string componentId, string provenance)
    {
        var points = new List<PlannedPhysicalRoutePoint> { Point(route, gridId, start, role, componentId + ":start", -1, null, null, null, provenance + " start", componentId) };
        points.Add(Point(route, gridId, end, role, componentId + ":end", -1, null, null, null, provenance + " end", componentId));
        return points.ToArray();
    }

    private PlannedPhysicalRoutePoint Point(PlannedGridRoute route, PlanningGridId gridId, AbsolutePoint point, RouteStepRole role, string pointId, int order, string? runId, string? turnId, PlanningGridCellId? cellId, string provenance, string? componentIdentity = null)
    {
        var separator = pointId.LastIndexOf(':');
        var derivedComponentId = separator < 0 ? pointId : pointId.Substring(0, separator);
        return new PlannedPhysicalRoutePoint(pointId, route.PhysicalLinkId, gridId, point, role, componentIdentity ?? derivedComponentId, order, runId, turnId, null, cellId, provenance);
    }

    private RelativePoint? RelativePointFor(AbsolutePoint point, PlanningGridId gridId, IReadOnlyDictionary<PlanningGridId, GridTransform> transforms) =>
        transforms.TryGetValue(gridId, out var transform) ? new RelativePoint(point.X - transform.Origin.X, point.Y - transform.Origin.Y) : (RelativePoint?)null;

    private AbsolutePoint PointForStep(PlannedGridRoute route, PlannedGridRouteStep step, GridTransform transform) =>
        step.Role == RouteStepRole.Turn ? TurnPoint(route, step, transform) : CellPoint(step, route, transform);

    private AbsolutePoint CellPoint(PlannedGridRouteStep step, PlannedGridRoute route, GridTransform transform)
    {
        var sizedGrid = relative.Grids.SingleOrDefault(item => item.GridId.Equals(step.GridId));
        var row = sizedGrid?.Rows.SingleOrDefault(item => item.Id.Equals(step.CellId.RowId));
        var column = sizedGrid?.Columns.SingleOrDefault(item => item.Id.Equals(step.CellId.ColumnId));
        if (row is null || column is null) return new AbsolutePoint(transform.Origin.X, transform.Origin.Y);
        var horizontal = step.EntrySide == GridSide.Left || step.EntrySide == GridSide.Right ||
            step.ExitSide == GridSide.Left || step.ExitSide == GridSide.Right;
        var lane = step.AllocatedLane;
        var x = column.RelativeOffset + column.FinalExtent / 2;
        var y = row.RelativeOffset + row.FinalExtent / 2;
        if (horizontal && lane is not null) y = LaneCoordinate(row.RelativeOffset, row.FinalExtent, allocation.HorizontalLanes, lane.Value.Value);
        if (!horizontal && lane is not null) x = LaneCoordinate(column.RelativeOffset, column.FinalExtent, allocation.VerticalLanes, lane.Value.Value);
        var point = new RelativePoint(x, y);
        return new AbsolutePoint(point.X + transform.Origin.X, point.Y + transform.Origin.Y);
    }

    private AbsolutePoint TurnPoint(PlannedGridRoute route, PlannedGridRouteStep step, GridTransform transform)
    {
        var sizedGrid = relative.Grids.SingleOrDefault(item => item.GridId.Equals(step.GridId));
        var row = sizedGrid?.Rows.SingleOrDefault(item => item.Id.Equals(step.CellId.RowId));
        var column = sizedGrid?.Columns.SingleOrDefault(item => item.Id.Equals(step.CellId.ColumnId));
        var turn = allocation.Turns.SingleOrDefault(item => item.RouteId == route.PhysicalLinkId && item.CellId == step.CellId.ToString());
        if (row is null || column is null || turn is null) return CellPoint(step, route, transform);
        var horizontal = allocation.HorizontalLanes.SingleOrDefault(item => item.RunId == turn.HorizontalRunId);
        var vertical = allocation.VerticalLanes.SingleOrDefault(item => item.RunId == turn.VerticalRunId);
        var x = vertical is null ? column.RelativeOffset + column.FinalExtent / 2 : LaneCoordinate(column.RelativeOffset, column.FinalExtent, allocation.VerticalLanes, vertical.Lane.Value);
        var y = horizontal is null ? row.RelativeOffset + row.FinalExtent / 2 : LaneCoordinate(row.RelativeOffset, row.FinalExtent, allocation.HorizontalLanes, horizontal.Lane.Value);
        return new AbsolutePoint(transform.Origin.X + x, transform.Origin.Y + y);
    }

    private int LaneCoordinate(int offset, int extent, IReadOnlyList<PlannedLaneAllocation> lanes, string laneId)
    {
        var ordinal = lanes.Where(item => item.Lane.Value == laneId).Select(item => item.Ordinal).DefaultIfEmpty(0).First();
        var spacing = Math.Max(1, request.RoutePlanning.MinimumParallelSpacing);
        var coordinate = offset + request.RoutePlanning.MinimumPortSpacing + ordinal * spacing;
        if (coordinate < offset || coordinate > offset + extent)
            findings.Add(new ArchitecturePlanningDiagnostic("LaneCoordinateOutsideTrack", "Allocated lane coordinate falls outside its sized track; materialisation was not clamped.", PlanningDiagnosticSubject.PhysicalSegment, laneId));
        return coordinate;
    }
    private AbsolutePoint CellCentre(PlanningGridCellId cell, GridTransform transform)
    {
        var sizedGrid = relative.Grids.SingleOrDefault(item => item.GridId.Equals(cell.GridId));
        var row = sizedGrid?.Rows.SingleOrDefault(item => item.Id.Equals(cell.RowId));
        var column = sizedGrid?.Columns.SingleOrDefault(item => item.Id.Equals(cell.ColumnId));
        return row is null || column is null ? new AbsolutePoint(transform.Origin.X, transform.Origin.Y) :
            new AbsolutePoint(transform.Origin.X + column.RelativeOffset + column.FinalExtent / 2, transform.Origin.Y + row.RelativeOffset + row.FinalExtent / 2);
    }

    private void ValidateTerminalSeparation(IReadOnlyList<PlannedPhysicalTerminal> terminals)
    {
        foreach (var group in terminals.GroupBy(item => item.PhysicalNodeId + ":" + item.Side, StringComparer.Ordinal))
            if (group.GroupBy(item => item.Point).Any(items => items.Count() > 1))
                findings.Add(new ArchitecturePlanningDiagnostic("DuplicateTerminalCoordinate", "Terminals on one node edge share a coordinate.", PlanningDiagnosticSubject.PhysicalNode, group.Key));
    }

    private void Validate(PlannedArchitectureGeometry geometry, IReadOnlyList<PlannedPhysicalTerminal> terminals,
        IReadOnlyList<PlannedPhysicalRoute> routes, IReadOnlyList<PlannedPhysicalTurn> turns,
        IReadOnlyList<PlannedPhysicalCrossing> crossings, IReadOnlyList<PlannedPhysicalTransition> transitions)
    {
        foreach (var node in geometry.Nodes)
            if (node.AbsoluteBounds.Width <= 0 || node.AbsoluteBounds.Height <= 0)
                findings.Add(new ArchitecturePlanningDiagnostic("InvalidAbsoluteNodeBounds", "Absolute node bounds must be positive.", PlanningDiagnosticSubject.PhysicalNode, node.PhysicalNodeId));
        foreach (var pair in geometry.Nodes.SelectMany((left, index) => geometry.Nodes.Skip(index + 1).Select(right => (left, right))))
            if (Intersects(pair.left.AbsoluteBounds, pair.right.AbsoluteBounds))
                findings.Add(new ArchitecturePlanningDiagnostic("AbsoluteNodeOverlap", "Absolute node rectangles overlap.", PlanningDiagnosticSubject.PhysicalNode, pair.left.PhysicalNodeId));
        foreach (var segment in routes.SelectMany(route => route.Segments))
            foreach (var node in geometry.Nodes.Where(node => node.PhysicalNodeId != SourceNode(segment, routes) && node.PhysicalNodeId != DestinationNode(segment, routes)))
                if (Intersects(segment, node.AbsoluteBounds))
                    findings.Add(new ArchitecturePlanningDiagnostic("RouteNodeIntersection", "A physical route segment intersects an unrelated node.", PlanningDiagnosticSubject.PhysicalSegment, segment.PhysicalLinkId));
        foreach (var group in routes.SelectMany(route => route.Segments.Select(segment => (route, segment))).GroupBy(item => NormalizedSegment(item.segment)))
            if (group.Count() > 1) findings.Add(new ArchitecturePlanningDiagnostic("SharedCollinearSegment", "Routes share a non-zero physical segment.", PlanningDiagnosticSubject.PhysicalSegment, group.Key));
        foreach (var terminal in terminals)
        {
            var node = geometry.Nodes.SingleOrDefault(item => item.PhysicalNodeId == terminal.PhysicalNodeId);
            if (node is null) continue;
            var valid = terminal.Side == GridSide.Bottom ? terminal.Point.Y == node.AbsoluteBounds.Y + node.AbsoluteBounds.Height : terminal.Point.Y == node.AbsoluteBounds.Y;
            if (!valid) findings.Add(new ArchitecturePlanningDiagnostic("TerminalEdgeMismatch", "Terminal does not lie on its expected node edge.", PlanningDiagnosticSubject.PhysicalNode, terminal.PhysicalNodeId));
        }
    }

    private PlannedPhysicalSceneMetrics BuildMetrics(PlannedArchitectureGeometry geometry, IReadOnlyList<PlannedPhysicalTerminal> terminals,
        IReadOnlyList<PlannedPhysicalRoute> routes, IReadOnlyList<PlannedPhysicalTurn> turns, IReadOnlyList<PlannedPhysicalCrossing> crossings,
        IReadOnlyList<PlannedPhysicalTransition> transitions, IReadOnlyDictionary<string, long> timings)
    {
        return new PlannedPhysicalSceneMetrics(geometry.Nodes.Count, terminals.Count, routes.Count, routes.Sum(route => route.Segments.Count), turns.Count,
            crossings.Count, transitions.Count, routes.Sum(route => route.RouteLength), routes.Select(route => route.RouteLength).DefaultIfEmpty(0).Max(),
            findings.Count(item => item.Code == "AbsoluteNodeOverlap"), findings.Count(item => item.Code == "RouteNodeIntersection"),
            findings.Count(item => item.Code == "SharedCollinearSegment"), findings.Count(item => item.Code == "SharedBend"),
            findings.Count(item => item.Code == "InvalidCrossing"), findings.Count(item => item.Code.Contains("Terminal", StringComparison.Ordinal)),
            findings.Count(item => item.Code.Contains("Ownership", StringComparison.Ordinal) || item.Code.Contains("Transform", StringComparison.Ordinal)),
            findings.Count(item => item.Code == "LabelGeometryUnavailable"), routes.GroupBy(route => route.TopologyFamily.ToString()).ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal), timings);
    }

    private static AbsoluteRectangle Translate(RelativeRectangle rectangle, RelativePoint origin) => new(rectangle.X + origin.X, rectangle.Y + origin.Y, rectangle.Width, rectangle.Height);
    private static bool Intersects(AbsoluteRectangle left, AbsoluteRectangle right) => left.X < right.X + right.Width && right.X < left.X + left.Width && left.Y < right.Y + right.Height && right.Y < left.Y + left.Height;
    private static bool Intersects(PlannedPhysicalRouteSegment segment, AbsoluteRectangle rectangle) =>
        segment.Axis == RouteAxis.Horizontal
            ? segment.Start.Y > rectangle.Y && segment.Start.Y < rectangle.Y + rectangle.Height && Math.Max(segment.Start.X, segment.End.X) > rectangle.X && Math.Min(segment.Start.X, segment.End.X) < rectangle.X + rectangle.Width
            : segment.Start.X > rectangle.X && segment.Start.X < rectangle.X + rectangle.Width && Math.Max(segment.Start.Y, segment.End.Y) > rectangle.Y && Math.Min(segment.Start.Y, segment.End.Y) < rectangle.Y + rectangle.Height;
    private static string SourceNode(PlannedPhysicalRouteSegment segment, IReadOnlyList<PlannedPhysicalRoute> routes) => routes.FirstOrDefault(route => route.PhysicalLinkId == segment.PhysicalLinkId)?.SourceProjection ?? string.Empty;
    private static string DestinationNode(PlannedPhysicalRouteSegment segment, IReadOnlyList<PlannedPhysicalRoute> routes) => routes.FirstOrDefault(route => route.PhysicalLinkId == segment.PhysicalLinkId)?.DestinationProjection ?? string.Empty;
    private static string NormalizedSegment(PlannedPhysicalRouteSegment segment) => segment.Axis + ":" + Math.Min(segment.Start.X, segment.End.X) + ":" + Math.Min(segment.Start.Y, segment.End.Y) + ":" + Math.Max(segment.Start.X, segment.End.X) + ":" + Math.Max(segment.Start.Y, segment.End.Y);
}
