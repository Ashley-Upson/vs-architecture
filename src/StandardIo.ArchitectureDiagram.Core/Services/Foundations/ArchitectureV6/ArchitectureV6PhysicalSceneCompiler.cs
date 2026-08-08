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
    private readonly IReadOnlyList<PlannedRouteBoundaryContract> boundaryContracts;
    private readonly IReadOnlyList<SubtreeReservation> subtreeReservations;
    private readonly List<ArchitecturePlanningDiagnostic> findings = new();
    private readonly List<PlannedPhysicalMaterialisationAttempt> attemptedSegments = new();
    private readonly List<string> invalidRouteIds = new();

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
        IReadOnlyList<SubtreeReservation> subtreeReservations,
        IReadOnlyList<PlannedRouteBoundaryContract>? boundaryContracts = null)
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
        this.boundaryContracts = boundaryContracts ?? allocation.BoundaryValidation?.Routes ?? Array.Empty<PlannedRouteBoundaryContract>();
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
        var absoluteBounds = CalculateAbsoluteBounds(nodeGeometry, projectGeometry, terminals, physicalRoutes, turns, crossings, transitions);
        var geometry = new PlannedArchitectureGeometry(nodeGeometry, projectGeometry, gridGeometry, reservationGeometry,
            relative.DiagramBounds, absoluteBounds, physicalRoutes);
        timings["normalization"] = timer.ElapsedMilliseconds;

        timer.Restart();
        ValidateLaneTrackCapacity();
        Validate(geometry, terminals, physicalRoutes, turns, crossings, transitions);
        timings["physicalValidation"] = timer.ElapsedMilliseconds;
        if (projects.Any(project => project.ProjectLabelReservation is null))
        {
            findings.Add(new ArchitecturePlanningDiagnostic("LabelGeometryUnavailable",
                "Measured project-label geometry was not supplied; no label obstruction was invented.", PlanningDiagnosticSubject.Grid, null));
        }
        var metrics = BuildMetrics(geometry, terminals, physicalRoutes, turns, crossings, transitions, timings) with
        {
            TrackCapacity = BuildTrackCapacityEvidence(),
            RouteEvidence = BuildRouteEvidence(physicalRoutes, terminals)
        };
        return new PlannedArchitecturePhysicalScene(geometry, transforms.Values.OrderBy(item => item.GridId.Value, StringComparer.Ordinal).ToArray(),
            terminals, turns, crossings, transitions, reservationGeometry, findings, metrics, attemptedSegments, invalidRouteIds);
    }

    private static AbsoluteRectangle CalculateAbsoluteBounds(
        IReadOnlyList<PlannedPhysicalNodeGeometry> nodes,
        IReadOnlyList<PlannedProjectGeometry> projects,
        IReadOnlyList<PlannedPhysicalTerminal> terminals,
        IReadOnlyList<PlannedPhysicalRoute> routes,
        IReadOnlyList<PlannedPhysicalTurn> turns,
        IReadOnlyList<PlannedPhysicalCrossing> crossings,
        IReadOnlyList<PlannedPhysicalTransition> transitions)
    {
        var rectangles = nodes.Select(node => node.AbsoluteBounds)
            .Concat(projects.Select(project => project.AbsoluteBounds))
            .ToArray();
        var points = terminals.Select(item => item.Point)
            .Concat(routes.SelectMany(route => route.Segments.SelectMany(segment => new[] { segment.Start, segment.End })))
            .Concat(routes.SelectMany(route => (route.RawPoints ?? Array.Empty<PlannedPhysicalRoutePoint>()).Select(point => point.Point)))
            .Concat(turns.Select(turn => turn.Point))
            .Concat(crossings.Select(crossing => crossing.Point))
            .Concat(transitions.SelectMany(transition => new[] { transition.SourcePoint, transition.DestinationPoint }))
            .ToArray();
        if (rectangles.Length == 0 && points.Length == 0)
            return new AbsoluteRectangle(0, 0, 1, 1);

        var minX = rectangles.Select(rectangle => rectangle.X).Concat(points.Select(point => point.X)).Min();
        var minY = rectangles.Select(rectangle => rectangle.Y).Concat(points.Select(point => point.Y)).Min();
        var maxX = rectangles.Select(rectangle => rectangle.X + rectangle.Width).Concat(points.Select(point => point.X)).Max();
        var maxY = rectangles.Select(rectangle => rectangle.Y + rectangle.Height).Concat(points.Select(point => point.Y)).Max();
        return new AbsoluteRectangle(minX, minY, Math.Max(1, maxX - minX), Math.Max(1, maxY - minY));
    }

    private Dictionary<PlanningGridId, GridTransform> BuildTransforms()
    {
        var result = new Dictionary<PlanningGridId, GridTransform>();
        result[diagramGrid.Grid.Id] = diagramGrid.Grid.Transform;

        // Project origins are owned by the sized diagram grid. Its project-placement
        // columns carry the project owner and their compiled offsets, so this remains
        // a cumulative track transform rather than a second packing algorithm.
        var sizedDiagram = relative.Grids.SingleOrDefault(item => item.GridId.Equals(diagramGrid.Grid.Id));
        var placementRows = sizedDiagram?.Rows
            .Where(item => item.Role == PlanningGridTrackRole.DiagramProjectPlacement)
            .OrderBy(item => item.LogicalOrder)
            .ToArray() ?? Array.Empty<PlanningGridRow>();
        var placementColumns = sizedDiagram?.Columns
            .Where(item => item.Role == PlanningGridTrackRole.DiagramProjectPlacement && item.OwnerId is not null)
            .OrderBy(item => item.LogicalOrder)
            .ToArray() ?? Array.Empty<PlanningGridColumn>();

        foreach (var project in projects.OrderBy(item => item.ProjectId, StringComparer.Ordinal))
        {
            var column = placementColumns.SingleOrDefault(item => string.Equals(item.OwnerId, project.ProjectId, StringComparison.Ordinal));
            var row = placementRows.FirstOrDefault();
            if (column is null || row is null)
            {
                findings.Add(new ArchitecturePlanningDiagnostic("MissingDiagramProjectTransform",
                    "The project has no sized diagram-grid placement track.", PlanningDiagnosticSubject.Grid, project.ProjectId));
                continue;
            }

            var origin = new RelativePoint(
                diagramGrid.Grid.Transform.Origin.X + column.RelativeOffset,
                diagramGrid.Grid.Transform.Origin.Y + row.RelativeOffset);
            result[project.Grid.Id] = new GridTransform(project.Grid.Id, origin);
            if (project.ProjectLabelReservation is null)
                findings.Add(new ArchitecturePlanningDiagnostic("LabelGeometryUnavailable", "Project label measurement is unavailable.", PlanningDiagnosticSubject.Grid, project.ProjectId));
        }
        return result;
    }

    private void ValidateEndpointDirection(PlannedGridRoute route, PlannedPhysicalTerminal sourceTerminal,
        PlannedPhysicalTerminal destinationTerminal, IReadOnlyList<PlannedPhysicalRouteComponent> components, ref bool invalid)
    {
        var sourceDeparture = components.FirstOrDefault(item => item.ComponentId.EndsWith(":source-departure", StringComparison.Ordinal));
        var destinationApproach = components.FirstOrDefault(item => item.ComponentId.EndsWith(":destination-approach", StringComparison.Ordinal));
        var sourcePath = EndpointPoints(components,
            PlannedRouteComponentKind.SourceTerminal,
            PlannedRouteComponentKind.SourceNodeAnchor,
            PlannedRouteComponentKind.SourceDeparture);
        var destinationPath = EndpointPoints(components,
            PlannedRouteComponentKind.DestinationApproach,
            PlannedRouteComponentKind.DestinationNodeAnchor,
            PlannedRouteComponentKind.DestinationTerminal);
        var sourceDepartureInvalid = sourcePath.Length >= 2 &&
            (sourcePath[1].Point.X != sourcePath[0].Point.X || sourcePath[1].Point.Y <= sourcePath[0].Point.Y);
        if (sourceDepartureInvalid && sourceDeparture is not null)
        {
            invalid = true;
            attemptedSegments.Add(new PlannedPhysicalMaterialisationAttempt(route.PhysicalLinkId, sourceDeparture.ComponentId,
                sourceDeparture.PrecedingComponentId, sourceDeparture.FollowingComponentId, sourcePath[0].Point, sourcePath[1].Point,
                "SourceDepartureDirectionInvalid", "Source departures must descend vertically from the source bottom terminal.", sourceDeparture.AllocatedCells));
            findings.Add(new ArchitecturePlanningDiagnostic("SourceDepartureDirectionInvalid", "Source departures must descend vertically from the source bottom terminal.", PlanningDiagnosticSubject.PhysicalLink, route.PhysicalLinkId));
        }
        var destinationDepartureInvalid = destinationPath.Length >= 2 &&
            (destinationPath[destinationPath.Length - 1].Point.X != destinationPath[destinationPath.Length - 2].Point.X ||
             destinationPath[destinationPath.Length - 1].Point.Y <= destinationPath[destinationPath.Length - 2].Point.Y);
        if (destinationDepartureInvalid && destinationApproach is not null)
        {
            invalid = true;
            attemptedSegments.Add(new PlannedPhysicalMaterialisationAttempt(route.PhysicalLinkId, destinationApproach.ComponentId,
                destinationApproach.PrecedingComponentId, destinationApproach.FollowingComponentId,
                destinationPath[destinationPath.Length - 2].Point, destinationPath[destinationPath.Length - 1].Point,
                "DestinationApproachDirectionInvalid", "Destination approaches must reach the destination top terminal vertically.", destinationApproach.AllocatedCells));
            findings.Add(new ArchitecturePlanningDiagnostic("DestinationApproachDirectionInvalid", "Destination approaches must reach the destination top terminal vertically.", PlanningDiagnosticSubject.PhysicalLink, route.PhysicalLinkId));
        }
        if (sourceTerminal.Side != GridSide.Bottom || destinationTerminal.Side != GridSide.Top)
        {
            invalid = true;
            findings.Add(new ArchitecturePlanningDiagnostic("InvalidPhysicalTerminalDirection", "Physical routes must leave source bottoms and enter destination tops.", PlanningDiagnosticSubject.PhysicalLink, route.PhysicalLinkId));
        }
    }

    private static PlannedPhysicalRoutePoint[] EndpointPoints(
        IReadOnlyList<PlannedPhysicalRouteComponent> components,
        params PlannedRouteComponentKind[] kinds)
    {
        var result = new List<PlannedPhysicalRoutePoint>();
        var ordered = components.OrderBy(item => item.RouteStepOrder).ToArray();
        for (var kindIndex = 0; kindIndex < kinds.Length; kindIndex++)
        {
            var kind = kinds[kindIndex];
            var matches = components.Where(item => ComponentKind(item) == kind).OrderBy(item => item.RouteStepOrder).ToArray();
            var selected = matches.Length > 0
                ? matches
                : kindIndex < ordered.Length
                    ? new[] { ordered[kindIndex] }
                    : Array.Empty<PlannedPhysicalRouteComponent>();
            foreach (var component in selected)
            {
                foreach (var point in component.Points)
                {
                    if (result.Count == 0 || result[result.Count - 1].Point != point.Point)
                        result.Add(point);
                }
            }
        }
        return result.ToArray();
    }

    private static PlannedRouteComponentKind ComponentKind(PlannedPhysicalRouteComponent component) =>
        component.ComponentId.EndsWith(":source-terminal", StringComparison.Ordinal) ? PlannedRouteComponentKind.SourceTerminal :
        component.ComponentId.EndsWith(":source-node-anchor", StringComparison.Ordinal) ? PlannedRouteComponentKind.SourceNodeAnchor :
        component.ComponentId.EndsWith(":source-departure", StringComparison.Ordinal) ? PlannedRouteComponentKind.SourceDeparture :
        component.ComponentId.EndsWith(":destination-approach", StringComparison.Ordinal) ? PlannedRouteComponentKind.DestinationApproach :
        component.ComponentId.EndsWith(":destination-node-anchor", StringComparison.Ordinal) ? PlannedRouteComponentKind.DestinationNodeAnchor :
        component.ComponentId.EndsWith(":destination-terminal", StringComparison.Ordinal) ? PlannedRouteComponentKind.DestinationTerminal :
        PlannedRouteComponentKind.HorizontalStraightRun;

    private void AddMaterialisationFinding(PlannedGridRoute route, string code, string message)
    {
        findings.Add(new ArchitecturePlanningDiagnostic(code, message, PlanningDiagnosticSubject.PhysicalLink, route.PhysicalLinkId));
    }

    private void AddMaterialisationAttempt(PlannedGridRoute route, PlannedPhysicalRouteComponent before,
        PlannedPhysicalRouteComponent after, AbsolutePoint start, AbsolutePoint end, string code, string message,
        IReadOnlyList<PlanningGridCellId>? cells = null)
    {
        attemptedSegments.Add(new PlannedPhysicalMaterialisationAttempt(route.PhysicalLinkId, after.ComponentId,
            before.ComponentId, after.ComponentId, start, end, code, message, cells ?? after.AllocatedCells,
            after.Points.FirstOrDefault()?.PointId, after.StraightRunId));
        AddMaterialisationFinding(route, code,
            $"{message} component={after.ComponentId}; start=({start.X},{start.Y}); end=({end.X},{end.Y}); " +
            $"cells={string.Join("|", (cells ?? after.AllocatedCells).Select(cell => cell.ToString()))}");
    }

    private bool SegmentWithinCells(AbsolutePoint start, AbsolutePoint end, IReadOnlyList<PlanningGridCellId> cells,
        IReadOnlyDictionary<PlanningGridId, GridTransform> transforms, int boundaryTolerance = 0,
        IReadOnlyList<AbsoluteRectangle>? additionalBounds = null)
    {
        var cellRectangles = cells.Select(cell => CellBounds(cell, transforms)).ToArray();
        if (cellRectangles.Length != cells.Count || cellRectangles.Any(rectangle => rectangle.Width <= 0 || rectangle.Height <= 0))
            return false;
        var rectangles = cellRectangles.Concat(additionalBounds ?? Array.Empty<AbsoluteRectangle>())
            .Where(rectangle => rectangle.Width > 0 && rectangle.Height > 0).ToArray();
        if (start.X == end.X)
        {
            var y0 = Math.Min(start.Y, end.Y);
            var y1 = Math.Max(start.Y, end.Y);
            return CoversInterval(rectangles.Where(rectangle => start.X >= rectangle.X - boundaryTolerance &&
                    start.X <= rectangle.X + rectangle.Width + boundaryTolerance)
                .Select(rectangle => (Start: rectangle.Y - boundaryTolerance, End: rectangle.Y + rectangle.Height + boundaryTolerance)), y0, y1);
        }
        if (start.Y == end.Y)
        {
            var x0 = Math.Min(start.X, end.X);
            var x1 = Math.Max(start.X, end.X);
            return CoversInterval(rectangles.Where(rectangle => start.Y >= rectangle.Y - boundaryTolerance &&
                    start.Y <= rectangle.Y + rectangle.Height + boundaryTolerance)
                .Select(rectangle => (Start: rectangle.X - boundaryTolerance, End: rectangle.X + rectangle.Width + boundaryTolerance)), x0, x1);
        }
        return false;
    }

    private static bool CoversInterval(IEnumerable<(int Start, int End)> intervals, int start, int end)
    {
        var cursor = start;
        foreach (var interval in intervals.Where(item => item.End >= start && item.Start <= end).OrderBy(item => item.Start))
        {
            if (interval.Start > cursor) return false;
            cursor = Math.Max(cursor, interval.End);
            if (cursor >= end) return true;
        }
        return cursor >= end;
    }

    private AbsoluteRectangle CellBounds(PlanningGridCellId cell, IReadOnlyDictionary<PlanningGridId, GridTransform> transforms)
    {
        var grid = relative.Grids.SingleOrDefault(item => item.GridId.Equals(cell.GridId));
        var row = grid?.Rows.SingleOrDefault(item => item.Id.Equals(cell.RowId));
        var column = grid?.Columns.SingleOrDefault(item => item.Id.Equals(cell.ColumnId));
        if (row is null || column is null || !transforms.TryGetValue(cell.GridId, out var transform)) return new AbsoluteRectangle(0, 0, 0, 0);
        return new AbsoluteRectangle(transform.Origin.X + column.RelativeOffset, transform.Origin.Y + row.RelativeOffset, column.FinalExtent, row.FinalExtent);
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
            var visibleBounds = node.VisibleBounds ?? node.Bounds;
            var absolute = Translate(visibleBounds, transform.Origin);
            var routingAbsolute = Translate(node.Bounds, transform.Origin);
            var source = nodes.SingleOrDefault(item => item.PhysicalNodeId == node.PhysicalNodeId);
            result.Add(new PlannedPhysicalNodeGeometry(node.PhysicalNodeId, node.SemanticNodeId, node.ProjectId, visibleBounds, absolute,
                node.GridId, node.AnchorCellId, node.PositionalOwnerId, node.ProjectionMode, node.IsExternal, node.IsStandalone,
                node.Bounds, routingAbsolute));
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
            var requestedX = endpoint.TerminalLane is { } terminalLane && endpoint.TerminalColumnId is { } terminalColumnId
                ? TerminalLaneCoordinate(node.GridId, terminalColumnId, terminalLane.Value, transform)
                : endpoint.TerminalLane is { } fallbackLane
                    ? LaneCoordinate(node.AbsoluteBounds.X - transform.Origin.X, node.AbsoluteBounds.Width,
                        allocation.VerticalLanes, fallbackLane.Value) + transform.Origin.X
                    : node.AbsoluteBounds.X + node.AbsoluteBounds.Width / 2 + endpoint.TrackOffset * spacing;
            var inset = Math.Min(Math.Max(1, node.AbsoluteBounds.Width / 2 - 1),
                Math.Max(spacing, request.GridSizing.NodeToRouteClearance));
            var minX = node.AbsoluteBounds.X + inset;
            var maxX = node.AbsoluteBounds.X + node.AbsoluteBounds.Width - inset;
            if (requestedX < minX || requestedX > maxX)
                findings.Add(new ArchitecturePlanningDiagnostic("TerminalCapacityOverflow",
                    $"Allocated terminal demand does not fit the final node edge; requestedX={requestedX:0.###}, edge=[{minX:0.###},{maxX:0.###}], bounds={node.AbsoluteBounds}, column={endpoint.TerminalColumnId}, lane={endpoint.TerminalLane}; terminal placement was not silently accepted by clamping.",
                    PlanningDiagnosticSubject.PhysicalNode, endpoint.PhysicalNodeId));
            // Capacity is a planning concern. Preserve the allocated slot so
            // an unresolved overflow remains visible to final validation rather
            // than silently changing the terminal geometry here.
            var x = requestedX;
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
            // A turn owns both the horizontal and vertical lane. CellPoint
            // can resolve only one axis from the step sides, so using it here
            // collapses distinct lane intersections onto the same bend.
            var point = TurnPoint(route, step, transform);
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
            var route = routes.SingleOrDefault(item => item.PhysicalLinkId == (crossing.HorizontalPhysicalLinkId ?? crossing.VerticalPhysicalLinkId) &&
                item.Steps.Any(step => step.CellId.ToString() == crossing.CellId));
            var step = route?.Steps.FirstOrDefault(item => item.CellId.ToString() == crossing.CellId);
            var point = step is null || !transforms.TryGetValue(step.GridId, out var transform) ? new AbsolutePoint(0, 0) : CellPoint(step, route!, transform);
            return new PlannedPhysicalCrossing(route?.PhysicalLinkId ?? string.Empty, crossing.VerticalPhysicalLinkId ?? string.Empty, step?.GridId ?? new PlanningGridId("unknown"),
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
        var compiledBoundaries = new Dictionary<GridBoundaryIdentity, AbsolutePoint>();
        var boundaryContradictions = new HashSet<string>(StringComparer.Ordinal);
        foreach (var route in allocation.Routes.OrderBy(item => item.PhysicalLinkId, StringComparer.Ordinal))
        {
            var sourceTerminal = terminals.SingleOrDefault(item => item.PhysicalLinkId == route.PhysicalLinkId && item.Side == GridSide.Bottom);
            var destinationTerminal = terminals.SingleOrDefault(item => item.PhysicalLinkId == route.PhysicalLinkId && item.Side == GridSide.Top);
            if (sourceTerminal is null || destinationTerminal is null)
            {
                findings.Add(new ArchitecturePlanningDiagnostic("RouteTerminalMissing", "Route could not be materialised without both terminals.", PlanningDiagnosticSubject.PhysicalLink, route.PhysicalLinkId));
                continue;
            }
            var contract = boundaryContracts.SingleOrDefault(item => item.PhysicalLinkId == route.PhysicalLinkId);
            if (contract is not null)
            {
                var sourceBoundary = contract.Components.FirstOrDefault(item => item.Kind == PlannedRouteComponentKind.SourceTerminal)?.EntryBoundary;
                var destinationBoundary = contract.Components.FirstOrDefault(item => item.Kind == PlannedRouteComponentKind.DestinationTerminal)?.EntryBoundary;
                RegisterBoundary(sourceBoundary, sourceTerminal.Point, compiledBoundaries, boundaryContradictions);
                RegisterBoundary(destinationBoundary, destinationTerminal.Point, compiledBoundaries, boundaryContradictions);
            }
            var components = contract is null
                ? Array.Empty<PlannedPhysicalRouteComponent>()
                : BuildComponentsFromContract(route, contract, sourceTerminal, destinationTerminal, transforms, compiledBoundaries, boundaryContradictions);
            if (contract is not null && route.Transitions.Count >= 2)
                components = InsertCrossProjectTransitionComponents(route, components, transforms);
            if (contract is null)
            {
                findings.Add(new ArchitecturePlanningDiagnostic("MissingPhysicalBoundaryContract", "A route has no accepted component boundary contract.", PlanningDiagnosticSubject.PhysicalLink, route.PhysicalLinkId));
                invalidRouteIds.Add(route.PhysicalLinkId);
                continue;
            }
            var rawPoints = components.SelectMany(component => component.Points).ToArray();
            var segments = new List<PlannedPhysicalRouteSegment>();
            var routeInvalid = !route.IsStructurallySupported;
            if (routeInvalid)
                AddMaterialisationFinding(route, "UnsupportedAbstractRoute", route.UnsupportedReason ?? "The abstract route is not structurally supported.");

            for (var index = 1; index < components.Count; index++)
            {
                var before = components[index - 1];
                var after = components[index];
                if (before.ExitPoint is null || after.EntryPoint is null || before.ExitPoint.Value != after.EntryPoint.Value)
                {
                    routeInvalid = true;
                    AddMaterialisationAttempt(route, before, after, before.ExitPoint ?? default, after.EntryPoint ?? default,
                        "ComponentContinuityMismatch", "Adjacent physical components do not share an exact boundary point.");
                    continue;
                }
                // Component endpoints are canonical boundary points. There is
                // no connector to invent between two accepted components.
            }

            // Materialise one complete centreline for the route. Component
            // boundaries remain provenance, but are not independent point-to-
            // point paths: a turn or endpoint bend is part of the same ordered
            // orthogonal sequence as the ordinary cell runs.
            ValidateEndpointBacktracking(route, rawPoints, ref routeInvalid);
            var reducedPoints = RemoveRedundantCollinearPoints(rawPoints);
            ValidateEndpointBacktracking(route, reducedPoints, ref routeInvalid);
            var rawPointIndexes = rawPoints.Select((point, index) => new { point.PointId, index })
                .ToDictionary(item => item.PointId, item => item.index, StringComparer.Ordinal);
            for (var pointIndex = 1; pointIndex < reducedPoints.Count; pointIndex++)
            {
                var before = reducedPoints[pointIndex - 1];
                var after = reducedPoints[pointIndex];
                if (before.Point == after.Point) continue;

                var owner = components.First(component => component.ComponentId == after.ComponentId);
                var preceding = components.First(component => component.ComponentId == before.ComponentId);
                var beforeRawIndex = rawPointIndexes[before.PointId];
                var afterRawIndex = rawPointIndexes[after.PointId];
                var spanCells = rawPoints
                    .Skip(beforeRawIndex)
                    .Take(afterRawIndex - beforeRawIndex + 1)
                    .Select(point => point.CellId)
                    .Where(cell => cell is not null)
                    .Select(cell => cell!.Value)
                    .Concat(owner.AllocatedCells)
                    .Distinct()
                    .ToArray();
                var start = before.Point;
                var end = after.Point;
                if (start.X != end.X && start.Y != end.Y)
                {
                    routeInvalid = true;
                    AddMaterialisationAttempt(route, preceding, owner, start, end,
                        "DiagonalComponentConnection",
                        $"A complete cell-and-lane centreline must be orthogonal. precedingRole={preceding.Role}; ownerRole={owner.Role}; precedingSides={preceding.EntrySide}->{preceding.ExitSide}; ownerSides={owner.EntrySide}->{owner.ExitSide}; precedingLane={preceding.Lane?.Value ?? "none"}; ownerLane={owner.Lane?.Value ?? "none"}; before={before.Provenance}; after={after.Provenance}.", spanCells);
                    continue;
                }
                var axis = start.X == end.X ? RouteAxis.Vertical : RouteAxis.Horizontal;
                var boundaryTolerance = route.Transitions.Count > 0 && owner.ComponentId.EndsWith(":destination-approach", StringComparison.Ordinal)
                    ? request.RoutePlanning.MinimumParallelSpacing + request.GridSizing.ContainerPadding
                    : 0;
                var endpointBounds = owner.ComponentId.EndsWith(":destination-approach", StringComparison.Ordinal)
                    ? placements.SingleOrDefault(item => item.PhysicalNodeId == route.Destination.PhysicalNodeId) is { } destinationPlacement &&
                      relative.Nodes.SingleOrDefault(item => item.PhysicalNodeId == destinationPlacement.PhysicalNodeId) is { } destinationNode &&
                      transforms.TryGetValue(destinationNode.GridId, out var destinationTransform)
                        ? new[] { Translate(destinationNode.Bounds, destinationTransform.Origin) }
                        : Array.Empty<AbsoluteRectangle>()
                    : owner.ComponentId.EndsWith(":source-departure", StringComparison.Ordinal)
                        ? placements.SingleOrDefault(item => item.PhysicalNodeId == route.Source.PhysicalNodeId) is { } sourcePlacement &&
                          relative.Nodes.SingleOrDefault(item => item.PhysicalNodeId == sourcePlacement.PhysicalNodeId) is { } sourceNode &&
                          transforms.TryGetValue(sourceNode.GridId, out var sourceTransform)
                            ? new[] { Translate(sourceNode.Bounds, sourceTransform.Origin) }
                            : Array.Empty<AbsoluteRectangle>()
                        : Array.Empty<AbsoluteRectangle>();
                if (spanCells.Length == 0 || !SegmentWithinCells(start, end, spanCells, transforms, boundaryTolerance, endpointBounds))
                {
                    routeInvalid = true;
                    AddMaterialisationAttempt(route, preceding, owner, start, end,
                        "ComponentCorridorEscape", "A materialised centreline segment leaves its allocated cell corridor.", spanCells);
                    continue;
                }
                segments.Add(new PlannedPhysicalRouteSegment(route.PhysicalLinkId,
                    RelativePointFor(start, before.GridId, transforms), RelativePointFor(end, after.GridId, transforms),
                    start, end, after.GridId, axis, owner.Role,
                    owner.Lane ?? new LaneId("component:" + owner.ComponentId), route.TopologyFamily,
                    owner.ComponentId, owner.RouteStepOrder, owner.StraightRunId, owner.LaneDomainId, spanCells,
                    spanCells.Select(cell => cell.RowId).Distinct().Count() == 1 ? spanCells.Select(cell => cell.RowId).Distinct().First() : null,
                    spanCells.Select(cell => cell.ColumnId).Distinct().Count() == 1 ? spanCells.Select(cell => cell.ColumnId).Distinct().First() : null,
                    before.Provenance, after.Provenance));
            }
            ValidateEndpointDirection(route, sourceTerminal, destinationTerminal, components, ref routeInvalid);
            if (routeInvalid)
            {
                invalidRouteIds.Add(route.PhysicalLinkId);
            }
            var length = segments.Sum(segment => Math.Abs(segment.End.X - segment.Start.X) + Math.Abs(segment.End.Y - segment.Start.Y));
            result.Add(new PlannedPhysicalRoute(route.PhysicalLinkId, links.SingleOrDefault(item => item.PhysicalLinkId == route.PhysicalLinkId)?.SemanticLinkId ?? string.Empty,
                route.Source.PhysicalNodeId, route.Destination.PhysicalNodeId, route.Source.PhysicalNodeId, route.Destination.PhysicalNodeId,
                route.TopologyFamily, segments, components.Count(component => component.Role == RouteStepRole.Turn), length, false, false, false,
                rawPoints, components, reducedPoints.Count, rawPoints.Length - reducedPoints.Count,
                rawPoints.Length - reducedPoints.Count, routeInvalid, reducedPoints));
        }
        return result;
    }

    private void ValidateEndpointBacktracking(PlannedGridRoute route,
        IReadOnlyList<PlannedPhysicalRoutePoint> points, ref bool routeInvalid)
    {
        if (points.Count < 3) return;
        var findingsInWindow = new HashSet<int>();
        for (var index = 1; index < points.Count - 1; index++)
        {
            var previous = points[index - 1].Point;
            var current = points[index].Point;
            var next = points[index + 1].Point;
            var horizontalReversal = previous.Y == current.Y && current.Y == next.Y &&
                (current.X < Math.Min(previous.X, next.X) || current.X > Math.Max(previous.X, next.X));
            var verticalReversal = previous.X == current.X && current.X == next.X &&
                (current.Y < Math.Min(previous.Y, next.Y) || current.Y > Math.Max(previous.Y, next.Y));
            if (!(horizontalReversal || verticalReversal)) continue;
            if (!findingsInWindow.Add(index)) continue;
            routeInvalid = true;
            findings.Add(new ArchitecturePlanningDiagnostic("RedundantRouteBacktracking",
                $"Route geometry reverses after overshooting its target axis at point {index}; the complete final polyline is invalid.",
                PlanningDiagnosticSubject.PhysicalLink, route.PhysicalLinkId));
        }
    }

    private static IReadOnlyList<PlannedPhysicalRoutePoint> RemoveRedundantCollinearPoints(
        IReadOnlyList<PlannedPhysicalRoutePoint> rawPoints)
    {
        if (rawPoints.Count < 3) return rawPoints.ToArray();

        var compact = new List<PlannedPhysicalRoutePoint>();
        foreach (var point in rawPoints)
        {
            if (compact.Count > 0 && SamePoint(compact[compact.Count - 1].Point, point.Point))
                continue;
            if (compact.Count >= 2 && SamePoint(compact[compact.Count - 2].Point, point.Point))
            {
                compact.RemoveAt(compact.Count - 1);
                continue;
            }
            compact.Add(point);
        }
        if (compact.Count < 3) return compact.ToArray();

        var reduced = new List<PlannedPhysicalRoutePoint> { compact[0] };
        for (var index = 1; index < compact.Count - 1; index++)
        {
            var previous = reduced[reduced.Count - 1];
            var current = compact[index];
            var next = compact[index + 1];
            var sameHorizontal = previous.Point.Y == current.Point.Y && current.Point.Y == next.Point.Y &&
                current.Point.X >= Math.Min(previous.Point.X, next.Point.X) && current.Point.X <= Math.Max(previous.Point.X, next.Point.X);
            var sameVertical = previous.Point.X == current.Point.X && current.Point.X == next.Point.X &&
                current.Point.Y >= Math.Min(previous.Point.Y, next.Point.Y) && current.Point.Y <= Math.Max(previous.Point.Y, next.Point.Y);
            var collinearTurn = current.Role == RouteStepRole.Turn &&
                (previous.Point.Y == current.Point.Y && current.Point.Y == next.Point.Y ||
                 previous.Point.X == current.Point.X && current.Point.X == next.Point.X);
            var isProtected = current.Role is RouteStepRole.Turn or RouteStepRole.SourceExit or RouteStepRole.DestinationEntry ||
                              current.TurnIdentity is not null || current.TransitionIdentity is not null;
            if (collinearTurn)
            {
                // A turn marker that does not change axis is an artefact of
                // boundary reconciliation, not a required renderer waypoint.
                // Removing it preserves the continuous run and avoids a tiny
                // reversal at the approach boundary.
                continue;
            }
            if (!(sameHorizontal || sameVertical) || isProtected)
                reduced.Add(current);
        }
        reduced.Add(compact[compact.Count - 1]);
        return reduced;
    }

    private static bool SamePoint(AbsolutePoint left, AbsolutePoint right) =>
        left.X == right.X && left.Y == right.Y;

    private IReadOnlyList<PlannedPhysicalRouteComponent> BuildComponentsFromContract(
        PlannedGridRoute route,
        PlannedRouteBoundaryContract contract,
        PlannedPhysicalTerminal sourceTerminal,
        PlannedPhysicalTerminal destinationTerminal,
        IReadOnlyDictionary<PlanningGridId, GridTransform> transforms,
        IDictionary<GridBoundaryIdentity, AbsolutePoint> compiledBoundaries,
        ISet<string> boundaryContradictions)
    {
        // Turn aliases are registered before ordinary component boundaries so
        // every run touching a turn receives the exact turn point, rather than
        // independently calculating a cell edge and a cell centre.
        foreach (var turn in contract.Components.Where(item => item.Kind == PlannedRouteComponentKind.Turn))
        {
            if (turn.Cells.Count == 0 || !transforms.TryGetValue(turn.EntryBoundary?.GridId ?? new PlanningGridId("missing"), out var transform))
                continue;
            var step = route.Steps.SingleOrDefault(item => item.CellId.Equals(turn.Cells[0]));
            var turnPoint = step is null ? (AbsolutePoint?)null : TurnPoint(route, step, transform);
            if (turnPoint is null) continue;
            RegisterBoundary(turn.EntryBoundary, turnPoint.Value, compiledBoundaries, boundaryContradictions);
            RegisterBoundary(turn.ExitBoundary, turnPoint.Value, compiledBoundaries, boundaryContradictions);
        }

        var result = new List<PlannedPhysicalRouteComponent>();
        foreach (var component in contract.Components.OrderBy(item => item.Order))
        {
            var points = new List<PlannedPhysicalRoutePoint>();
            var ownedCells = component.Cells
                .Concat(component.EntryBoundary is null ? Array.Empty<PlanningGridCellId>() : new[] { component.EntryBoundary.CellId })
                .Concat(component.EntryBoundary?.AdjacentCellId is { } entryAdjacent ? new[] { entryAdjacent } : Array.Empty<PlanningGridCellId>())
                .Concat(component.ExitBoundary is null ? Array.Empty<PlanningGridCellId>() : new[] { component.ExitBoundary.CellId })
                .Concat(component.ExitBoundary?.AdjacentCellId is { } exitAdjacent ? new[] { exitAdjacent } : Array.Empty<PlanningGridCellId>())
                .Distinct()
                .ToArray();
            if (component.Kind == PlannedRouteComponentKind.SourceTerminal)
            {
                points.Add(Point(route, route.Source.GridId ?? new PlanningGridId("unknown"), sourceTerminal.Point,
                    RouteStepRole.SourceExit, component.ComponentId, component.Order, null, null, null,
                    "canonical source terminal", component.ComponentId));
            }
            else if (component.Kind == PlannedRouteComponentKind.DestinationTerminal)
            {
                points.Add(Point(route, route.Destination.GridId ?? new PlanningGridId("unknown"), destinationTerminal.Point,
                    RouteStepRole.DestinationEntry, component.ComponentId, component.Order, null, null, null,
                    "canonical destination terminal", component.ComponentId));
            }
            else if (component.Kind == PlannedRouteComponentKind.Turn)
            {
                var boundary = component.EntryBoundary ?? component.ExitBoundary;
                var point = CompileBoundary(boundary, transforms, compiledBoundaries, boundaryContradictions);
                if (point is null)
                {
                    findings.Add(new ArchitecturePlanningDiagnostic("UnresolvedCanonicalBoundary", "A turn has no compilable canonical boundary.", PlanningDiagnosticSubject.RouteStep, component.ComponentId));
                    continue;
                }
                points.Add(Point(route, component.Cells[0].GridId, point.Value, RouteStepRole.Turn, component.ComponentId,
                    component.Order, component.RunId, component.TurnId, component.Cells[0], "canonical turn point", component.ComponentId));
            }
            else
            {
                points.AddRange(BuildRawComponentPoints(route, component, sourceTerminal, destinationTerminal,
                    transforms, compiledBoundaries, boundaryContradictions));
            }

            result.Add(new PlannedPhysicalRouteComponent(component.ComponentId, component.PhysicalLinkId,
                component.Kind == PlannedRouteComponentKind.Turn ? RouteStepRole.Turn : RoleForComponent(component.Kind),
                component.Order, component.RunId, null, component.Lane,
                component.Kind == PlannedRouteComponentKind.Turn ? component.Cells : ownedCells, points,
                component.TurnId, null, component.OwnershipScope, "accepted boundary contract",
                points.FirstOrDefault()?.Point, points.LastOrDefault()?.Point, component.EntrySide, component.ExitSide,
                component.PrecedingComponentId, component.FollowingComponentId, component.EntryBoundary?.ToString()));
        }
        return result;
    }

    // Non-production historical repair helper. The active compiler never calls
    // this method because component boundaries must already agree upstream.
    [Obsolete("Non-production historical boundary-repair helper.")]
    private void AlignDestinationApproachBoundary(PlannedGridRoute route, IList<PlannedPhysicalRouteComponent> components)
    {
        for (var index = 1; index < components.Count; index++)
        {
            var component = components[index];
            if (!component.ComponentId.EndsWith(":destination-approach", StringComparison.Ordinal)) continue;
            var previous = components[index - 1];
            if (previous.ExitPoint is null) continue;

            var points = component.Points.ToList();
            if (points.Count == 0) continue;
            var entry = previous.ExitPoint.Value;
            var following = index + 1 < components.Count ? components[index + 1] : null;
            var terminal = points[points.Count - 1];
            var target = following?.EntryPoint ?? terminal.Point;
            var aligned = new List<PlannedPhysicalRoutePoint>
            {
                points[0] with { Point = entry, Provenance = "shared boundary from preceding ordinary component" }
            };
            if (entry.X != target.X && entry.Y != target.Y)
            {
                aligned.Add(Point(route, terminal.GridId, new AbsolutePoint(target.X, entry.Y),
                    RouteStepRole.DestinationEntry, component.ComponentId + ":aligned-terminal-bend", component.RouteStepOrder,
                    component.StraightRunId, component.TurnIdentity, terminal.CellId,
                    "orthogonal destination terminal alignment", component.ComponentId));
            }
            aligned.Add(Point(route, terminal.GridId, target, RouteStepRole.DestinationEntry,
                component.ComponentId + ":aligned-terminal", component.RouteStepOrder, component.StraightRunId,
                component.TurnIdentity, terminal.CellId, "destination node-anchor shared boundary", component.ComponentId));
            components[index] = component with
            {
                Points = aligned,
                EntryPoint = entry,
                ExitPoint = aligned[aligned.Count - 1].Point
            };
        }
    }

    // Cross-project transitions are component planning, not route repair. The
    // abstract route supplies the transition records; this stage materialises
    // their owned source-project, diagram-grid and destination-project legs
    // before segments are compiled.
    private IReadOnlyList<PlannedPhysicalRouteComponent> InsertCrossProjectTransitionComponents(
        PlannedGridRoute route,
        IReadOnlyList<PlannedPhysicalRouteComponent> components,
        IReadOnlyDictionary<PlanningGridId, GridTransform> transforms)
    {
        if (route.Transitions.Count < 2 || components.Count == 0)
            return components;

        // The contract builder retains transition records for diagnostics. The
        // physical route uses the explicit source, diagram and destination
        // components below as its sole cross-grid representation.
        components = components
            .Where(component => component.Role != RouteStepRole.ProjectTransition ||
                !component.ComponentId.Contains(":transition:", StringComparison.Ordinal))
            .ToArray();

        var sourceGrid = route.Transitions[0].SourceGridId;
        var destinationGrid = route.Transitions[1].DestinationGridId;
        var firstDestinationIndex = -1;
        for (var index = 0; index < components.Count; index++)
        {
            if (components[index].AllocatedCells.Any(cell => cell.GridId == destinationGrid))
            {
                firstDestinationIndex = index;
                break;
            }
        }

        if (firstDestinationIndex <= 0 || firstDestinationIndex >= components.Count)
            return components;

        var sourceComponent = components[firstDestinationIndex - 1];
        var destinationComponent = components[firstDestinationIndex];
        if (sourceComponent.ExitPoint is null || destinationComponent.EntryPoint is null ||
            !transforms.TryGetValue(route.Transitions[0].DestinationGridId, out var sourceDiagramTransform) ||
            !transforms.TryGetValue(route.Transitions[1].SourceGridId, out var destinationDiagramTransform))
            return components;

        var sourceDiagramCell = route.Transitions[0].DestinationBoundaryCellId;
        var destinationDiagramCell = route.Transitions[1].SourceBoundaryCellId;

        var sourceSide = route.Transitions[0].SourceBoundarySide ?? throw new InvalidOperationException(
            $"Cross-project route {route.PhysicalLinkId} has no planner-owned source transition side.");
        var destinationSide = route.Transitions[1].DestinationBoundarySide ?? throw new InvalidOperationException(
            $"Cross-project route {route.PhysicalLinkId} has no planner-owned destination transition side.");
        var sourceBoundary = BoundaryPoint(new GridBoundaryIdentity(sourceDiagramCell.GridId, sourceDiagramCell,
            sourceSide, null, sourceDiagramCell.GridId.Value, "diagram-project-boundary"), sourceDiagramTransform);
        var destinationBoundary = BoundaryPoint(new GridBoundaryIdentity(destinationDiagramCell.GridId, destinationDiagramCell,
            destinationSide, null, destinationDiagramCell.GridId.Value, "diagram-project-boundary"), destinationDiagramTransform);

        var transitionOrdinal = allocation.ProjectTransitions
            .Where(item => item.PhysicalLinkId == route.PhysicalLinkId)
            .Select(item => item.Ordinal)
            .DefaultIfEmpty(0)
            .First();
        var diagramRow = relative.Grids.Single(item => item.GridId == sourceDiagramCell.GridId).Rows
            .Single(item => item.Id == sourceDiagramCell.RowId);
        var transitionY = Math.Min(diagramRow.RelativeOffset + diagramRow.FinalExtent - 1,
            diagramRow.RelativeOffset + transitionOrdinal);
        sourceBoundary = new AbsolutePoint(sourceBoundary.X, sourceDiagramTransform.Origin.Y + transitionY);
        destinationBoundary = new AbsolutePoint(destinationBoundary.X, destinationDiagramTransform.Origin.Y + transitionY);
        var sourceEdge = sourceSide == GridSide.Left
            ? new AbsolutePoint(sourceComponent.ExitPoint.Value.X, sourceBoundary.Y)
            : new AbsolutePoint(sourceBoundary.X, sourceComponent.ExitPoint.Value.Y);
        var destinationEdge = new AbsolutePoint(destinationBoundary.X, destinationComponent.EntryPoint.Value.Y);
        var firstTransition = TransitionComponent(route, "source-project-transition", sourceComponent.ExitPoint.Value,
            sourceEdge, sourceBoundary, sourceGrid, sourceGrid, sourceGrid, route.Transitions[0].SourceBoundaryCellId, sourceDiagramCell,
            sourceComponent, sourceComponent.RouteStepOrder);
        var diagramTransition = TransitionComponent(route, "diagram-transition", sourceBoundary, destinationBoundary,
            destinationBoundary, route.Transitions[0].DestinationGridId, route.Transitions[0].DestinationGridId, route.Transitions[0].DestinationGridId,
            sourceDiagramCell, destinationDiagramCell,
            null, sourceComponent.RouteStepOrder + 1);
        var diagramCells = route.Steps
            .Where(step => step.GridId == route.Transitions[0].DestinationGridId)
            .OrderBy(step => step.Order)
            .Select(step => step.CellId)
            .Distinct()
            .ToArray();
        if (diagramCells.Length > 0)
            diagramTransition = diagramTransition with { AllocatedCells = diagramCells };
        var destinationTransition = TransitionComponent(route, "destination-project-transition", destinationBoundary,
            destinationEdge, destinationComponent.EntryPoint.Value, route.Transitions[1].SourceGridId, destinationGrid, destinationGrid,
            destinationDiagramCell,
            route.Transitions[1].DestinationBoundaryCellId, destinationComponent, destinationComponent.RouteStepOrder - 1);

        var result = new List<PlannedPhysicalRouteComponent>(components.Count + 3);
        var mutableComponents = components.ToArray();
        var approachIndex = Array.FindIndex(mutableComponents, item => item.ComponentId.EndsWith(":destination-approach", StringComparison.Ordinal));
        if (approachIndex >= 0)
        {
            var firstDestinationOrder = destinationComponent.RouteStepOrder;
            var destinationCells = route.Steps
                .Where(step => step.GridId == destinationGrid && step.Order >= firstDestinationOrder - 1)
                .Select(step => step.CellId)
                .Distinct()
                .ToArray();
            if (destinationCells.Length > 0)
            {
                var approach = mutableComponents[approachIndex];
                var destinationPlacement = placements.SingleOrDefault(item => item.PhysicalNodeId == route.Destination.PhysicalNodeId);
                if (destinationPlacement is not null)
                {
                    destinationCells = destinationCells
                        .Concat(destinationPlacement.Footprint.Select(cell => cell))
                        .Distinct()
                        .ToArray();
                }
                mutableComponents[approachIndex] = approach with
                {
                    AllocatedCells = destinationCells,
                };
            }
        }
        result.AddRange(mutableComponents.Take(firstDestinationIndex));
        result.Add(firstTransition);
        result.Add(diagramTransition);
        result.Add(destinationTransition);
        result.AddRange(mutableComponents.Skip(firstDestinationIndex));
        for (var index = 0; index < result.Count; index++)
            result[index] = result[index] with
            {
                PrecedingComponentId = index == 0 ? null : result[index - 1].ComponentId,
                FollowingComponentId = index == result.Count - 1 ? null : result[index + 1].ComponentId
            };
        return result;
    }

    private PlannedPhysicalRouteComponent TransitionComponent(
        PlannedGridRoute route,
        string suffix,
        AbsolutePoint entry,
        AbsolutePoint bend,
        AbsolutePoint exit,
        PlanningGridId entryGridId,
        PlanningGridId bendGridId,
        PlanningGridId exitGridId,
        PlanningGridCellId firstCell,
        PlanningGridCellId secondCell,
        PlannedPhysicalRouteComponent? adjacent,
        int order)
    {
        var id = route.PhysicalLinkId + ":" + suffix;
        var points = new List<PlannedPhysicalRoutePoint>
        {
            Point(route, entryGridId, entry, RouteStepRole.ProjectTransition, id + ":entry", order, null, suffix, firstCell,
                "explicit project-grid transition entry", id)
        };
        if (bend != entry && bend != exit)
            points.Add(Point(route, bendGridId, bend, RouteStepRole.ProjectTransition, id + ":bend", order, null, suffix, firstCell,
                "explicit transition bend", id));
        points.Add(Point(route, exitGridId, exit, RouteStepRole.ProjectTransition, id + ":exit", order, null, suffix, secondCell,
            "explicit project-grid transition exit", id));
        return new PlannedPhysicalRouteComponent(id, route.PhysicalLinkId, RouteStepRole.ProjectTransition, order, null, null, null,
            new[] { firstCell, secondCell }.Distinct().ToArray(), points, null, suffix, entryGridId.Value, "explicit cross-project transition",
            entry, exit, adjacent?.ExitSide, adjacent?.ExitSide, null, null, "explicit shared transition boundary");
    }

    private PlanningGridCellId? DiagramProjectCell(string? projectId)
    {
        if (string.IsNullOrWhiteSpace(projectId)) return null;
        var column = diagramGrid.Grid.Columns.FirstOrDefault(item => string.Equals(item.OwnerId, projectId, StringComparison.Ordinal));
        return column is null ? null : new PlanningGridCellId(diagramGrid.Grid.Id, diagramGrid.Grid.Rows[0].Id, column.Id);
    }

    private IReadOnlyList<PlannedPhysicalRoutePoint> BuildRawComponentPoints(
        PlannedGridRoute route,
        PlannedRouteComponentContract component,
        PlannedPhysicalTerminal sourceTerminal,
        PlannedPhysicalTerminal destinationTerminal,
        IReadOnlyDictionary<PlanningGridId, GridTransform> transforms,
        IDictionary<GridBoundaryIdentity, AbsolutePoint> compiledBoundaries,
        ISet<string> boundaryContradictions)
    {
        var role = RoleForComponent(component.Kind);
        var entry = CompileBoundary(component.EntryBoundary, transforms, compiledBoundaries, boundaryContradictions);
        var exit = CompileBoundary(component.ExitBoundary, transforms, compiledBoundaries, boundaryContradictions);
        if (entry is null || exit is null)
        {
            findings.Add(new ArchitecturePlanningDiagnostic("UnresolvedCanonicalBoundary", "A route component has an unresolved canonical boundary.", PlanningDiagnosticSubject.RouteStep, component.ComponentId));
            return Array.Empty<PlannedPhysicalRoutePoint>();
        }

        var orderedSteps = ComponentSteps(route, component);
        var firstCell = orderedSteps.FirstOrDefault()?.CellId ?? component.Cells.FirstOrDefault();
        var lastCell = orderedSteps.LastOrDefault()?.CellId ?? component.Cells.LastOrDefault();
        var points = new List<PlannedPhysicalRoutePoint>();
        if (component.Kind == PlannedRouteComponentKind.SourceNodeAnchor)
        {
            var boundary = NodeFootprintBoundary(route.Source.PhysicalNodeId, GridSide.Bottom, sourceTerminal, transforms);
            points.Add(Point(route, route.Source.GridId ?? boundary.GridId, sourceTerminal.Point, RouteStepRole.SourceExit,
                component.ComponentId + ":terminal", component.Order, component.RunId, component.TurnId,
                component.Cells.FirstOrDefault(), "source terminal to complete footprint bottom edge", component.ComponentId));
            points.Add(Point(route, boundary.GridId, boundary.Point, RouteStepRole.SourceExit,
                component.ComponentId + ":footprint-bottom", component.Order, component.RunId, component.TurnId,
                component.Cells.FirstOrDefault(), "source complete footprint bottom edge", component.ComponentId));
            return points;
        }

        if (component.Kind == PlannedRouteComponentKind.DestinationNodeAnchor)
        {
            var boundary = NodeFootprintBoundary(route.Destination.PhysicalNodeId, GridSide.Top, destinationTerminal, transforms);
            points.Add(Point(route, boundary.GridId, boundary.Point, RouteStepRole.DestinationEntry,
                component.ComponentId + ":footprint-top", component.Order, component.RunId, component.TurnId,
                component.Cells.FirstOrDefault(), "destination complete footprint top edge", component.ComponentId));
            points.Add(Point(route, route.Destination.GridId ?? boundary.GridId, destinationTerminal.Point, RouteStepRole.DestinationEntry,
                component.ComponentId + ":terminal", component.Order, component.RunId, component.TurnId,
                component.Cells.FirstOrDefault(), "destination footprint top edge to terminal", component.ComponentId));
            return points;
        }

        var start = entry.Value;
        var end = exit.Value;
        if (component.Kind == PlannedRouteComponentKind.SourceDeparture)
        {
            var boundary = NodeFootprintBoundary(route.Source.PhysicalNodeId, GridSide.Bottom, sourceTerminal, transforms);
            start = boundary.Point;
            points.Add(Point(route, boundary.GridId, start, role, component.ComponentId + ":footprint-bottom", component.Order, component.RunId, component.TurnId,
                component.EntryBoundary?.CellId, "source footprint bottom exterior boundary", component.ComponentId));
            if (orderedSteps.Length > 0)
            {
                var firstTransform = transforms[orderedSteps[0].GridId];
                var handoff = EndpointHandoffPoint(route, orderedSteps[0], firstTransform);
                points.Add(Point(route, firstCell.GridId, new AbsolutePoint(start.X, handoff.Y), role,
                    component.ComponentId + ":vertical-departure", component.Order, component.RunId, component.TurnId,
                    firstCell, "dedicated source endpoint-local vertical", component.ComponentId));
                points.Add(Point(route, firstCell.GridId, handoff, role, component.ComponentId + ":handoff", component.Order,
                    component.RunId, component.TurnId, firstCell, "source endpoint-local orthogonal handoff", component.ComponentId));
                return points;
            }
            if (component.Lane is not null)
            {
                var firstLanePoint = ComponentLanePoint(component, orderedSteps[0], start, transforms);
                var verticalDeparture = new AbsolutePoint(start.X, firstLanePoint.Y);
                points.Add(Point(route, boundary.GridId, verticalDeparture, role, component.ComponentId + ":vertical-departure", component.Order,
                    component.RunId, component.TurnId, firstCell, "source bottom vertical departure", component.ComponentId));
            }
        }
        else if (component.Kind == PlannedRouteComponentKind.DestinationApproach && orderedSteps.Length > 0)
        {
            var firstStep = orderedSteps[0];
            points.Add(Point(route, component.EntryBoundary?.GridId ?? firstStep.GridId, start, role,
                component.ComponentId + ":entry", component.Order, component.RunId, component.TurnId,
                component.EntryBoundary?.CellId ?? firstStep.CellId, "authoritative destination approach entry boundary", component.ComponentId));
            points.Add(Point(route, firstStep.GridId, end, role, component.ComponentId + ":exit", component.Order,
                component.RunId, component.TurnId, firstStep.CellId, "authoritative destination approach exit boundary", component.ComponentId));
            return points;
        }
        else
        {
            if (component.PrecedingComponentId?.EndsWith(":source-departure", StringComparison.Ordinal) == true && orderedSteps.Length > 0 &&
                transforms.TryGetValue(orderedSteps[0].GridId, out var sourceHandoffTransform))
                start = CellPoint(orderedSteps[0], route, sourceHandoffTransform);
            points.Add(Point(route, firstCell.GridId, start, role, component.ComponentId + ":entry", component.Order,
                component.RunId, component.TurnId, firstCell,
                component.Kind == PlannedRouteComponentKind.DestinationApproach
                    ? "destination approach entry boundary"
                    : "canonical entry boundary", component.ComponentId));
        }

        if (orderedSteps.Length == 0)
        {
            if (component.Kind == PlannedRouteComponentKind.DestinationApproach)
            {
                // When the destination anchor is the final logical step there is
                // no ordinary approach cell to enumerate. Recompile the entry
                // boundary from its own cell authority so the approach retains
                // the exact shared boundary with the preceding run.
                if (component.EntryBoundary is not null && transforms.TryGetValue(component.EntryBoundary.GridId, out var approachTransform))
                    start = BoundaryPoint(component.EntryBoundary, approachTransform);
            }
            if (component.Kind is PlannedRouteComponentKind.SourceDeparture or PlannedRouteComponentKind.DestinationApproach)
                points.Add(Point(route, lastCell.GridId, end, role, component.ComponentId + ":boundary", component.Order,
                    component.RunId, component.TurnId, lastCell,
                    "unreconciled endpoint boundary from authoritative plan", component.ComponentId));
            else
                points.Add(Point(route, lastCell.GridId, end, role, component.ComponentId + ":exit", component.Order,
                    component.RunId, component.TurnId, lastCell, "canonical exit boundary", component.ComponentId));
            return points;
        }

        if (orderedSteps.Length > 0 && component.Lane is not null)
        {
            var entryLanePoint = ComponentLanePoint(component, orderedSteps[0], start, transforms);
            points.Add(Point(route, orderedSteps[0].GridId, entryLanePoint, role, component.ComponentId + ":lane-entry",
                component.Order, component.RunId, component.TurnId, firstCell, "entry aligned to component lane", component.ComponentId));
        }
        for (var index = 0; index < orderedSteps.Length - 1; index++)
        {
            var current = orderedSteps[index];
            var next = orderedSteps[index + 1];
            var transition = CompileCellTransition(route, component, current, next, transforms, compiledBoundaries, boundaryContradictions);
            if (transition is null)
                continue;
            points.Add(transition);
        }
        if (orderedSteps.Length > 0 && component.Lane is not null)
        {
            var exitLanePoint = ComponentLanePoint(component, orderedSteps[orderedSteps.Length - 1], end, transforms);
            points.Add(Point(route, orderedSteps[orderedSteps.Length - 1].GridId, exitLanePoint, role, component.ComponentId + ":lane-exit",
                component.Order, component.RunId, component.TurnId, lastCell, "exit aligned to component lane", component.ComponentId));
        }
        points.Add(Point(route, lastCell.GridId, end, role, component.ComponentId + ":exit", component.Order,
            component.RunId, component.TurnId, lastCell, "canonical exit boundary", component.ComponentId));
        return points;
    }

    private PlannedGridRouteStep[] ComponentSteps(PlannedGridRoute route, PlannedRouteComponentContract component)
    {
        var cells = component.Kind == PlannedRouteComponentKind.DestinationApproach
            ? new HashSet<PlanningGridCellId>()
            : new HashSet<PlanningGridCellId>(component.Cells);
        if (component.Kind == PlannedRouteComponentKind.DestinationApproach)
        {
            var destinationAnchor = placements.SingleOrDefault(item => item.PhysicalNodeId == route.Destination.PhysicalNodeId)?.AnchorCellId;
            foreach (var step in route.Steps.Where(step => step.Role == RouteStepRole.DestinationEntry &&
                         (!destinationAnchor.HasValue || !step.CellId.Equals(destinationAnchor.Value))))
                cells.Add(step.CellId);
        }

        return route.Steps
            .Where(step => cells.Contains(step.CellId))
            .OrderBy(step => step.Order)
            .ToArray();
    }

    private (PlanningGridId GridId, AbsolutePoint Point) NodeFootprintBoundary(
        string physicalNodeId,
        GridSide side,
        PlannedPhysicalTerminal terminal,
        IReadOnlyDictionary<PlanningGridId, GridTransform> transforms)
    {
        var node = relative.Nodes.SingleOrDefault(item => item.PhysicalNodeId == physicalNodeId);
        if (node is null || !transforms.TryGetValue(node.GridId, out var transform))
            return (new PlanningGridId("unknown"), terminal.Point);

        var y = side == GridSide.Bottom
            ? node.Bounds.Y + node.Bounds.Height + transform.Origin.Y
            : node.Bounds.Y + transform.Origin.Y;
        return (node.GridId, new AbsolutePoint(terminal.Point.X, y));
    }

    private AbsolutePoint ComponentLanePoint(PlannedRouteComponentContract component,
        PlannedGridRouteStep step, AbsolutePoint boundary,
        IReadOnlyDictionary<PlanningGridId, GridTransform> transforms)
    {
        var grid = relative.Grids.SingleOrDefault(item => item.GridId.Equals(step.GridId));
        var lane = component.Lane ?? step.AllocatedLane;
        // Lane coordinates are local to the cell being compiled. The lane
        // allocation may span many cells, so its first cell is not a valid
        // geometric authority for this step.
        var row = grid?.Rows.SingleOrDefault(item => item.Id.Equals(step.CellId.RowId));
        var column = grid?.Columns.SingleOrDefault(item => item.Id.Equals(step.CellId.ColumnId));
        if (row is null || column is null || lane is null || !transforms.TryGetValue(step.GridId, out var transform))
            return boundary;
        var horizontal = component.Kind == PlannedRouteComponentKind.HorizontalStraightRun;
        if (horizontal)
        {
            var y = LaneCoordinate(row.RelativeOffset, row.FinalExtent, allocation.HorizontalLanes, lane.Value.Value) + transform.Origin.Y;
            return new AbsolutePoint(boundary.X, y);
        }
        var x = LaneCoordinate(column.RelativeOffset, column.FinalExtent, allocation.VerticalLanes, lane.Value.Value) + transform.Origin.X;
        return new AbsolutePoint(x, boundary.Y);
    }

    private PlannedPhysicalRoutePoint? CompileCellTransition(PlannedGridRoute route,
        PlannedRouteComponentContract component, PlannedGridRouteStep current, PlannedGridRouteStep next,
        IReadOnlyDictionary<PlanningGridId, GridTransform> transforms,
        IDictionary<GridBoundaryIdentity, AbsolutePoint> compiledBoundaries,
        ISet<string> boundaryContradictions)
    {
        if (current.GridId != next.GridId)
        {
            findings.Add(new ArchitecturePlanningDiagnostic("ComponentCellGridTransition", "A component changes grid ownership without an explicit transition component.", PlanningDiagnosticSubject.RouteStep, component.ComponentId));
            return null;
        }
        var currentColumn = GridColumn(current.CellId);
        var nextColumn = GridColumn(next.CellId);
        var currentRow = GridRow(current.CellId);
        var nextRow = GridRow(next.CellId);
        GridSide side;
        if (current.CellId.RowId == next.CellId.RowId && currentColumn is not null && nextColumn is not null)
            side = currentColumn.LogicalOrder < nextColumn.LogicalOrder ? GridSide.Right : GridSide.Left;
        else if (current.CellId.ColumnId == next.CellId.ColumnId && currentRow is not null && nextRow is not null)
            side = currentRow.LogicalOrder < nextRow.LogicalOrder ? GridSide.Bottom : GridSide.Top;
        else
        {
            findings.Add(new ArchitecturePlanningDiagnostic("NonOrthogonalCellTransition", "Adjacent component cells do not share a row or column.", PlanningDiagnosticSubject.RouteStep, component.ComponentId));
            return null;
        }
        var lane = component.Lane;
        var identity = new GridBoundaryIdentity(current.GridId, current.CellId, side, lane,
            component.OwnershipScope, "cell-transition", next.CellId);
        var point = CompileBoundary(identity, transforms, compiledBoundaries, boundaryContradictions);
        if (point is null) return null;
        var role = side is GridSide.Left or GridSide.Right ? RouteStepRole.HorizontalPassThrough : RouteStepRole.VerticalPassThrough;
        return Point(route, current.GridId, point.Value, role, component.ComponentId + ":cell:" + current.Order,
            current.Order, component.RunId, component.TurnId, current.CellId, "shared cell boundary lane crossing", component.ComponentId);
    }

    private PlanningGridColumn? GridColumn(PlanningGridCellId cell) => relative.Grids
        .SingleOrDefault(grid => grid.GridId.Equals(cell.GridId))?.Columns.SingleOrDefault(column => column.Id.Equals(cell.ColumnId));

    private PlanningGridRow? GridRow(PlanningGridCellId cell) => relative.Grids
        .SingleOrDefault(grid => grid.GridId.Equals(cell.GridId))?.Rows.SingleOrDefault(row => row.Id.Equals(cell.RowId));

    private void RegisterBoundary(GridBoundaryIdentity? boundary, AbsolutePoint point,
        IDictionary<GridBoundaryIdentity, AbsolutePoint> compiledBoundaries, ISet<string> contradictions)
    {
        if (boundary is null) return;
        if (compiledBoundaries.TryGetValue(boundary, out var existing) && existing != point && contradictions.Add(boundary.ToString()))
        {
            findings.Add(new ArchitecturePlanningDiagnostic("CanonicalBoundaryContradiction", "Equivalent canonical boundaries compiled to different physical points.", PlanningDiagnosticSubject.PhysicalSegment, boundary.ToString()));
            return;
        }
        compiledBoundaries[boundary] = point;
    }

    private AbsolutePoint? CompileBoundary(GridBoundaryIdentity? boundary,
        IReadOnlyDictionary<PlanningGridId, GridTransform> transforms,
        IDictionary<GridBoundaryIdentity, AbsolutePoint> compiledBoundaries,
        ISet<string> contradictions)
    {
        if (boundary is null || !transforms.TryGetValue(boundary.GridId, out var transform)) return null;
        if (compiledBoundaries.TryGetValue(boundary, out var cached)) return cached;
        var point = BoundaryPoint(boundary, transform);
        RegisterBoundary(boundary, point, compiledBoundaries, contradictions);
        return point;
    }

    private AbsolutePoint BoundaryPoint(GridBoundaryIdentity boundary, GridTransform transform)
    {
        var grid = relative.Grids.SingleOrDefault(item => item.GridId.Equals(boundary.GridId));
        var row = grid?.Rows.SingleOrDefault(item => item.Id.Equals(boundary.CellId.RowId));
        var column = grid?.Columns.SingleOrDefault(item => item.Id.Equals(boundary.CellId.ColumnId));
        if (row is null || column is null) return new AbsolutePoint(transform.Origin.X, transform.Origin.Y);
        var x = column.RelativeOffset + column.FinalExtent / 2;
        var y = row.RelativeOffset + row.FinalExtent / 2;
        if (boundary.Orientation == GridBoundaryOrientation.Horizontal)
            x = LaneCoordinate(column.RelativeOffset, column.FinalExtent, allocation.VerticalLanes, boundary.Lane?.Value ?? string.Empty);
        else
            y = LaneCoordinate(row.RelativeOffset, row.FinalExtent, allocation.HorizontalLanes, boundary.Lane?.Value ?? string.Empty);
        if (boundary.Side == GridSide.Left) x = column.RelativeOffset;
        if (boundary.Side == GridSide.Right) x = column.RelativeOffset + column.FinalExtent;
        if (boundary.Side == GridSide.Top) y = row.RelativeOffset;
        if (boundary.Side == GridSide.Bottom) y = row.RelativeOffset + row.FinalExtent;
        return new AbsolutePoint(transform.Origin.X + x, transform.Origin.Y + y);
    }

    private static RouteStepRole RoleForComponent(PlannedRouteComponentKind kind) => kind switch
    {
        PlannedRouteComponentKind.SourceTerminal => RouteStepRole.SourceExit,
        PlannedRouteComponentKind.SourceNodeAnchor => RouteStepRole.SourceExit,
        PlannedRouteComponentKind.SourceDeparture => RouteStepRole.SourceExit,
        PlannedRouteComponentKind.DestinationNodeAnchor => RouteStepRole.DestinationEntry,
        PlannedRouteComponentKind.DestinationTerminal => RouteStepRole.DestinationEntry,
        PlannedRouteComponentKind.DestinationApproach => RouteStepRole.DestinationEntry,
        PlannedRouteComponentKind.ProjectTransition => RouteStepRole.ProjectTransition,
        _ => kind == PlannedRouteComponentKind.HorizontalStraightRun ? RouteStepRole.HorizontalPassThrough : RouteStepRole.VerticalPassThrough
    };

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
            ? sourceTerminal.Point : BoundaryPoint(route, firstStep, firstStep.EntrySide, firstTransform);
        var sourceTerminalComponent = route.PhysicalLinkId + ":source-terminal";
        result.Add(new PlannedPhysicalRouteComponent(sourceTerminalComponent, route.PhysicalLinkId, RouteStepRole.SourceExit, -1, null, null, null,
            Array.Empty<PlanningGridCellId>(), new[] { Point(route, route.Steps.FirstOrDefault()?.GridId ?? new PlanningGridId("unknown"), sourceTerminal.Point, RouteStepRole.SourceExit, sourceTerminalComponent, -1, null, null, null, "source terminal", sourceTerminalComponent) }, null, null, route.SourceProjectId ?? "diagram", "source terminal",
            sourceTerminal.Point, sourceTerminal.Point, GridSide.Bottom, GridSide.Bottom, null, route.PhysicalLinkId + ":source-stub", "terminal"));
        var sourceStubComponent = route.PhysicalLinkId + ":source-stub";
        result.Add(new PlannedPhysicalRouteComponent(sourceStubComponent, route.PhysicalLinkId, RouteStepRole.SourceExit, -1, null, null, null,
            firstStep is null ? Array.Empty<PlanningGridCellId>() : new[] { firstStep.CellId }, StubPoints(route, sourceTerminal.Point, firstPoint, firstStep?.GridId ?? new PlanningGridId("unknown"), RouteStepRole.SourceExit, sourceStubComponent, "source stub"), null, null, route.SourceProjectId ?? "diagram", "source stub",
            sourceTerminal.Point, firstPoint, GridSide.Bottom, firstStep?.EntrySide, sourceTerminalComponent, null, "source-stub"));
        foreach (var group in groups)
        {
            var first = group[0];
            if (!transforms.TryGetValue(first.GridId, out var transform)) continue;
            if (!transforms.TryGetValue(group[group.Count - 1].GridId, out var groupLastTransform)) groupLastTransform = transform;
            var componentId = route.PhysicalLinkId + ":component:" + first.Order;
            var allocatedTurn = first.Role == RouteStepRole.Turn
                ? allocation.Turns.SingleOrDefault(turn => turn.RouteId == route.PhysicalLinkId && turn.CellId == first.CellId.ToString())
                : null;
            if (first.Role == RouteStepRole.Turn && allocatedTurn is null)
                findings.Add(new ArchitecturePlanningDiagnostic("MissingAllocatedTurn", "A turn route step has no allocated turn identity and cannot be materialised as an owned turn.", PlanningDiagnosticSubject.PhysicalLink, route.PhysicalLinkId));
            var points = group[0].Role == RouteStepRole.Turn
                ? new[] { Point(route, first.GridId, TurnPoint(route, first, transform), RouteStepRole.Turn, componentId + ":turn", first.Order, first.StraightRunId, allocatedTurn?.BendIdentity, first.CellId, "allocated turn", componentId) }
                : new[]
                {
                    Point(route, first.GridId, BoundaryPoint(route, first, first.EntrySide, transform), first.Role, componentId + ":entry", first.Order, first.StraightRunId, null, first.CellId, "allocated run entry boundary", componentId),
                    Point(route, group[group.Count - 1].GridId, BoundaryPoint(route, group[group.Count - 1], first.Role == RouteStepRole.DestinationEntry ? group[group.Count - 1].EntrySide : group[group.Count - 1].ExitSide, groupLastTransform), group[group.Count - 1].Role, componentId + ":exit", group[group.Count - 1].Order, group[group.Count - 1].StraightRunId, null, group[group.Count - 1].CellId, "allocated run exit boundary", componentId)
                };
            var lane = first.AllocatedLane;
            var allocationForLane = lane is null ? null : allocation.HorizontalLanes.Concat(allocation.VerticalLanes).FirstOrDefault(item => item.Lane.Value == lane.Value.Value);
            result.Add(new PlannedPhysicalRouteComponent(componentId, route.PhysicalLinkId, first.Role, first.Order,
                first.StraightRunId, allocationForLane?.DomainId, lane, group.Select(step => step.CellId).ToArray(), points,
                allocatedTurn?.BendIdentity,
                null, first.GridId.Value, "ordered route-step component",
                points[0].Point, points[points.Length - 1].Point, first.EntrySide, first.Role == RouteStepRole.DestinationEntry ? group[group.Count - 1].EntrySide : group[group.Count - 1].ExitSide,
                result.LastOrDefault()?.ComponentId, null, "allocated-component-boundary"));
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
        var lastPoint = last is null || !transforms.TryGetValue(last.GridId, out var lastTransform) ? destinationTerminal.Point : BoundaryPoint(route, last, last.EntrySide, lastTransform);
        var destinationStubComponent = route.PhysicalLinkId + ":destination-stub";
        result.Add(new PlannedPhysicalRouteComponent(destinationStubComponent, route.PhysicalLinkId, RouteStepRole.DestinationEntry, int.MaxValue, null, null, null,
            last is null ? Array.Empty<PlanningGridCellId>() : new[] { last.CellId }, StubPoints(route, lastPoint, destinationTerminal.Point, last?.GridId ?? new PlanningGridId("unknown"), RouteStepRole.DestinationEntry, destinationStubComponent, "destination stub"), null, null, route.DestinationProjectId ?? "diagram", "destination stub",
            lastPoint, destinationTerminal.Point, last?.EntrySide, GridSide.Top, result.LastOrDefault()?.ComponentId, route.PhysicalLinkId + ":destination-terminal", "destination-stub"));
        var destinationTerminalComponent = route.PhysicalLinkId + ":destination-terminal";
        result.Add(new PlannedPhysicalRouteComponent(destinationTerminalComponent, route.PhysicalLinkId, RouteStepRole.DestinationEntry, int.MaxValue, null, null, null,
            Array.Empty<PlanningGridCellId>(), new[] { Point(route, route.Steps.LastOrDefault()?.GridId ?? new PlanningGridId("unknown"), destinationTerminal.Point, RouteStepRole.DestinationEntry, destinationTerminalComponent, int.MaxValue, null, null, null, "destination terminal", destinationTerminalComponent) }, null, null, route.DestinationProjectId ?? "diagram", "destination terminal",
            destinationTerminal.Point, destinationTerminal.Point, GridSide.Top, GridSide.Top, destinationStubComponent, null, "terminal"));
        for (var index = 0; index < result.Count; index++)
            result[index] = result[index] with
            {
                PrecedingComponentId = index == 0 ? null : result[index - 1].ComponentId,
                FollowingComponentId = index == result.Count - 1 ? null : result[index + 1].ComponentId
            };
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

    private AbsolutePoint BoundaryPoint(PlannedGridRoute route, PlannedGridRouteStep step, GridSide side, GridTransform transform)
    {
        var grid = relative.Grids.SingleOrDefault(item => item.GridId.Equals(step.GridId));
        var row = grid?.Rows.SingleOrDefault(item => item.Id.Equals(step.CellId.RowId));
        var column = grid?.Columns.SingleOrDefault(item => item.Id.Equals(step.CellId.ColumnId));
        if (row is null || column is null) return new AbsolutePoint(transform.Origin.X, transform.Origin.Y);
        var horizontal = side is GridSide.Left or GridSide.Right;
        var lane = step.AllocatedLane;
        var x = column.RelativeOffset + column.FinalExtent / 2;
        var y = row.RelativeOffset + row.FinalExtent / 2;
        if (!horizontal && lane is not null)
            x = LaneCoordinate(column.RelativeOffset, column.FinalExtent, allocation.VerticalLanes, lane.Value.Value);
        if (horizontal && lane is not null)
            y = LaneCoordinate(row.RelativeOffset, row.FinalExtent, allocation.HorizontalLanes, lane.Value.Value);
        if (side == GridSide.Left) x = column.RelativeOffset;
        if (side == GridSide.Right) x = column.RelativeOffset + column.FinalExtent;
        if (side == GridSide.Top) y = row.RelativeOffset;
        if (side == GridSide.Bottom) y = row.RelativeOffset + row.FinalExtent;
        return new AbsolutePoint(transform.Origin.X + x, transform.Origin.Y + y);
    }

    private int TerminalLaneCoordinate(PlanningGridId nodeGridId, PlanningGridColumnId columnId,
        string laneId, GridTransform transform)
    {
        var grid = relative.Grids.SingleOrDefault(item => item.GridId.Equals(nodeGridId));
        var column = grid?.Columns.SingleOrDefault(item => item.Id.Equals(columnId));
        return column is null
            ? transform.Origin.X
            : LaneCoordinate(column.RelativeOffset, column.FinalExtent, allocation.VerticalLanes, laneId) + transform.Origin.X;
    }

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

    private AbsolutePoint EndpointHandoffPoint(PlannedGridRoute route, PlannedGridRouteStep step, GridTransform transform) =>
        step.Role == RouteStepRole.Turn ? TurnPoint(route, step, transform) : CellPoint(step, route, transform);

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
        var coordinate = ArchitectureV6LaneGeometry.Coordinate(offset, ordinal,
            request.RoutePlanning.MinimumPortSpacing,
            request.RoutePlanning.MinimumParallelSpacing);
        if (coordinate < offset || coordinate > offset + extent)
            findings.Add(new ArchitecturePlanningDiagnostic("LaneCoordinateOutsideTrack", "Allocated lane coordinate falls outside its sized track; materialisation was not clamped.", PlanningDiagnosticSubject.PhysicalSegment, laneId));
        return coordinate;
    }

    private void ValidateLaneTrackCapacity()
    {
        foreach (var track in BuildTrackCapacityEvidence())
        {
            if (track.Deficit <= 0) continue;
            findings.Add(new ArchitecturePlanningDiagnostic("LaneTrackCapacityDeficit",
                $"Track {track.TrackId} requires {track.RequiredExtent}px for {track.LaneCount} lanes but provides {track.ActualExtent}px.",
                track.Axis == RouteAxis.Horizontal ? PlanningDiagnosticSubject.Row : PlanningDiagnosticSubject.Column,
                track.TrackId));
        }
    }

    private IReadOnlyList<ArchitectureV6TrackCapacityEvidence> BuildTrackCapacityEvidence()
    {
        var result = new List<ArchitectureV6TrackCapacityEvidence>();
        foreach (var group in allocation.HorizontalLanes.GroupBy(item => item.GridId.Value + ":" + item.DomainId, StringComparer.Ordinal))
        {
            foreach (var rowId in group.SelectMany(item => item.Cells).Select(cell => cell.RowId).Distinct())
            {
                var grid = relative.Grids.SingleOrDefault(item => item.GridId.Equals(group.First().GridId));
                var row = grid?.Rows.SingleOrDefault(item => item.Id.Equals(rowId));
                if (row is null) continue;
                var lanes = group.Select(item => item.Ordinal).Distinct().OrderBy(item => item).ToArray();
                var required = ArchitectureV6LaneGeometry.RequiredEnvelope(lanes.Length, request.GridSizing.RoutingRowMinimum,
                    request.RoutePlanning.MinimumPortSpacing, request.RoutePlanning.MinimumParallelSpacing);
                result.Add(new ArchitectureV6TrackCapacityEvidence(group.First().GridId.Value, RouteAxis.Horizontal,
                    row.Id.Value, lanes.Length, lanes.Length == 0 ? null : group.OrderBy(item => item.Ordinal).First().Lane.Value,
                    lanes.Length == 0 ? null : group.OrderBy(item => item.Ordinal).Last().Lane.Value, required, row.FinalExtent,
                    required - row.FinalExtent, row.FinalExtent - required, group.Select(item => item.RouteId).Distinct().OrderBy(item => item, StringComparer.Ordinal).ToArray()));
            }
        }
        foreach (var group in allocation.VerticalLanes.GroupBy(item => item.GridId.Value + ":" + item.DomainId, StringComparer.Ordinal))
        {
            foreach (var columnId in group.SelectMany(item => item.Cells).Select(cell => cell.ColumnId).Distinct())
            {
                var grid = relative.Grids.SingleOrDefault(item => item.GridId.Equals(group.First().GridId));
                var column = grid?.Columns.SingleOrDefault(item => item.Id.Equals(columnId));
                if (column is null) continue;
                var lanes = group.Select(item => item.Ordinal).Distinct().OrderBy(item => item).ToArray();
                var required = ArchitectureV6LaneGeometry.RequiredEnvelope(lanes.Length, request.GridSizing.StructuralColumnMinimum,
                    request.RoutePlanning.MinimumPortSpacing, request.RoutePlanning.MinimumParallelSpacing);
                result.Add(new ArchitectureV6TrackCapacityEvidence(group.First().GridId.Value, RouteAxis.Vertical,
                    column.Id.Value, lanes.Length, lanes.Length == 0 ? null : group.OrderBy(item => item.Ordinal).First().Lane.Value,
                    lanes.Length == 0 ? null : group.OrderBy(item => item.Ordinal).Last().Lane.Value, required, column.FinalExtent,
                    required - column.FinalExtent, column.FinalExtent - required, group.Select(item => item.RouteId).Distinct().OrderBy(item => item, StringComparer.Ordinal).ToArray()));
            }
        }
        return result.OrderBy(item => item.GridId, StringComparer.Ordinal).ThenBy(item => item.Axis).ThenBy(item => item.TrackId, StringComparer.Ordinal).ToArray();
    }

    private IReadOnlyList<ArchitectureV6PhysicalRouteEvidence> BuildRouteEvidence(
        IReadOnlyList<PlannedPhysicalRoute> physicalRoutes, IReadOnlyList<PlannedPhysicalTerminal> terminals)
    {
        return physicalRoutes.OrderBy(route => route.PhysicalLinkId, StringComparer.Ordinal).Select(route =>
        {
            var attempts = attemptedSegments.Where(item => item.PhysicalLinkId == route.PhysicalLinkId).ToArray();
            var routeLanes = (route.Components ?? Array.Empty<PlannedPhysicalRouteComponent>()).Where(item => item.Lane is not null)
                .Select(item => item.Lane!.Value.Value).Distinct(StringComparer.Ordinal).ToArray();
            var routeNodes = new[] { route.SourceProjection, route.DestinationProjection };
            var routeFindings = findings.Where(item => item.SubjectId == route.PhysicalLinkId ||
                    (item.Code.Contains("Terminal", StringComparison.Ordinal) && routeNodes.Any(node => item.SubjectId?.Contains(node, StringComparison.Ordinal) == true)) ||
                    (item.Code == "LaneCoordinateOutsideTrack" && routeLanes.Contains(item.SubjectId, StringComparer.Ordinal)))
                .Select(item => item.Code).Concat(attempts.Select(item => item.FailureCode)).Distinct(StringComparer.Ordinal)
                .OrderBy(item => item, StringComparer.Ordinal).ToArray();
            var components = (route.Components ?? Array.Empty<PlannedPhysicalRouteComponent>()).OrderBy(item => item.RouteStepOrder)
                .ThenBy(item => item.ComponentId, StringComparer.Ordinal).ToArray();
            var cells = components.SelectMany(item => item.AllocatedCells).Distinct().Select(item => item.ToString()).ToArray();
            var lanes = routeLanes.OrderBy(item => item, StringComparer.Ordinal).ToArray();
            var logicalRoute = routes.SingleOrDefault(item => item.PhysicalLinkId == route.PhysicalLinkId);
            var routeStepDetails = (logicalRoute?.Steps ?? Array.Empty<PlannedGridRouteStep>()).OrderBy(item => item.Order)
                .Select(item => $"{item.Order}:{item.Role}:{item.CellId}:{item.EntrySide}->{item.ExitSide}:lane={item.AllocatedLane?.Value ?? "none"}")
                .ToArray();
            var componentBoundaryDetails = (boundaryContracts.SingleOrDefault(item => item.PhysicalLinkId == route.PhysicalLinkId)?.Components ?? Array.Empty<PlannedRouteComponentContract>())
                .OrderBy(item => item.Order)
                .Select(item => $"{item.ComponentId}:entry={item.EntryBoundary}:exit={item.ExitBoundary}")
                .ToArray();
            var componentPointDetails = components
                .Select(item => $"{item.ComponentId}:entry={item.EntryPoint}:exit={item.ExitPoint}")
                .ToArray();
            var componentPointProvenance = components
                .SelectMany(item => item.Points.Select(point => $"{item.ComponentId}:{point.Point}:{point.Provenance}"))
                .ToArray();
            var tracks = components.SelectMany(item => item.AllocatedCells).Select(item => item.RowId.Value).Concat(components.SelectMany(item => item.AllocatedCells).Select(item => item.ColumnId.Value))
                .Distinct(StringComparer.Ordinal).OrderBy(item => item, StringComparer.Ordinal).ToArray();
            var source = terminals.SingleOrDefault(item => item.PhysicalLinkId == route.PhysicalLinkId && item.Side == GridSide.Bottom);
            var destination = terminals.SingleOrDefault(item => item.PhysicalLinkId == route.PhysicalLinkId && item.Side == GridSide.Top);
            var firstInvalidStage = FirstInvalidStage(route, attempts, routeFindings);
            return new ArchitectureV6PhysicalRouteEvidence(route.PhysicalLinkId, !route.IsInvalid, routeFindings,
                components.Select(item => item.ComponentId).ToArray(),
                components.Select(item => item.Role.ToString()).ToArray(), cells, lanes, tracks,
                (route.RawPoints ?? Array.Empty<PlannedPhysicalRoutePoint>()).Select(item => item.Point.ToString()).ToArray(),
                (route.ReducedPoints ?? Array.Empty<PlannedPhysicalRoutePoint>()).Select(item => item.Point.ToString()).ToArray(),
                source?.Point.ToString(), destination?.Point.ToString(),
                attempts.Select((item, index) => new ArchitectureV6PhysicalRouteFindingEvidence(route.PhysicalLinkId, item.FailureCode,
                    item.ComponentId, index, item.AllocatedCells.Select(cell => cell.ToString()).ToArray(), item.Start.ToString(), item.End.ToString())).ToArray(),
                firstInvalidStage, routeStepDetails, componentBoundaryDetails, componentPointDetails, componentPointProvenance);
        }).ToArray();
    }

    private static string? FirstInvalidStage(
        PlannedPhysicalRoute route,
        IReadOnlyList<PlannedPhysicalMaterialisationAttempt> attempts,
        IReadOnlyList<string> routeFindings)
    {
        if (routeFindings.Count == 0) return null;
        // Materialisation attempts are the first concrete stage evidence for a
        // route. Prefer their ordered failure over a later aggregate finding;
        // otherwise a final validator can make an upstream defect appear to
        // have originated in the final polyline.
        var firstAttempt = attempts.OrderBy(item => item.ComponentId, StringComparer.Ordinal).FirstOrDefault();
        if (firstAttempt is not null)
        {
            return firstAttempt.FailureCode switch
            {
                "ComponentContinuityMismatch" or "BoundaryMismatch" => "component-boundary-reconciliation",
                "ComponentCorridorEscape" or "LaneCoordinateOutsideTrack" => "raw-physical-geometry",
                "DiagonalComponentConnection" => "raw-physical-geometry",
                "InvalidRouteComponent" => "allocated-route",
                _ => "raw-physical-geometry"
            };
        }
        if (routeFindings.Any(item => item.Contains("Boundary", StringComparison.Ordinal)))
            return "component-boundary-reconciliation";
        if (routeFindings.Any(item => item is "RedundantRouteBacktracking" or "TerminalJoinDiagonal" or "RouteNodeIntersection" or "SharedCollinearSegment"))
        {
            var points = route.RawPoints ?? Array.Empty<PlannedPhysicalRoutePoint>();
            for (var index = 1; index < points.Count - 1; index++)
            {
                var previous = points[index - 1];
                var current = points[index];
                var next = points[index + 1];
                var horizontalReversal = previous.Point.Y == current.Point.Y && current.Point.Y == next.Point.Y &&
                    Math.Sign(current.Point.X - previous.Point.X) != Math.Sign(next.Point.X - current.Point.X);
                var verticalReversal = previous.Point.X == current.Point.X && current.Point.X == next.Point.X &&
                    Math.Sign(current.Point.Y - previous.Point.Y) != Math.Sign(next.Point.Y - current.Point.Y);
                if (horizontalReversal || verticalReversal)
                    return previous.ComponentId == current.ComponentId && current.ComponentId == next.ComponentId
                        ? "component-materialisation"
                        : "component-boundary-reconciliation";
            }
            return "final-physical-polyline";
        }
        if (routeFindings.Any(item => item is "ComponentCorridorEscape" or "LaneCoordinateOutsideTrack"))
            return "raw-physical-geometry";
        if (routeFindings.Any(item => item.Contains("Terminal", StringComparison.Ordinal))) return "terminal-allocation";
        return "final-physical-polyline";
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
        {
            var routeIds = group.Select(item => item.route.PhysicalLinkId).Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal).ToArray();
            if (routeIds.Length > 1)
                findings.Add(new ArchitecturePlanningDiagnostic("SharedCollinearSegment",
                    $"Routes {routeIds[0]} and {routeIds[1]} share non-zero physical segment {group.Key}.",
                    PlanningDiagnosticSubject.PhysicalSegment, string.Join("|", routeIds.Take(2)) + "|" + group.Key));
        }
        ValidateSharedBends(turns);
        ValidateParallelClearance(routes);
        ValidateCrossings(routes);
        foreach (var terminal in terminals)
        {
            var node = geometry.Nodes.SingleOrDefault(item => item.PhysicalNodeId == terminal.PhysicalNodeId);
            if (node is null) continue;
            var valid = terminal.Side == GridSide.Bottom ? terminal.Point.Y == node.AbsoluteBounds.Y + node.AbsoluteBounds.Height : terminal.Point.Y == node.AbsoluteBounds.Y;
            if (!valid) findings.Add(new ArchitecturePlanningDiagnostic("TerminalEdgeMismatch", "Terminal does not lie on its expected node edge.", PlanningDiagnosticSubject.PhysicalNode, terminal.PhysicalNodeId));
            var inset = Math.Min(Math.Max(1, node.AbsoluteBounds.Width / 2 - 1),
                Math.Max(request.RoutePlanning.MinimumPortSpacing, request.GridSizing.NodeToRouteClearance));
            if (terminal.Point.X <= node.AbsoluteBounds.X || terminal.Point.X >= node.AbsoluteBounds.X + node.AbsoluteBounds.Width ||
                terminal.Point.X < node.AbsoluteBounds.X + inset || terminal.Point.X > node.AbsoluteBounds.X + node.AbsoluteBounds.Width - inset)
                findings.Add(new ArchitecturePlanningDiagnostic("TerminalCornerOrInsetViolation", "A top/bottom terminal lies outside the configured edge inset.", PlanningDiagnosticSubject.PhysicalNode, terminal.TerminalId));
        }
        foreach (var route in routes)
        {
            var source = terminals.SingleOrDefault(item => item.PhysicalLinkId == route.PhysicalLinkId && item.Side == GridSide.Bottom);
            var destination = terminals.SingleOrDefault(item => item.PhysicalLinkId == route.PhysicalLinkId && item.Side == GridSide.Top);
            if (source is null || destination is null) continue;
            var points = route.ReducedPoints ?? route.RawPoints ?? Array.Empty<PlannedPhysicalRoutePoint>();
            var ordered = new[] { source.Point }
                .Concat(points.Select(item => item.Point))
                .Concat(new[] { destination.Point })
                .ToArray();
            ValidatePolyline(route.PhysicalLinkId, "Raw", new[] { source.Point }
                .Concat((route.RawPoints ?? Array.Empty<PlannedPhysicalRoutePoint>()).Select(item => item.Point))
                .Concat(new[] { destination.Point }).ToArray());
            ValidatePolyline(route.PhysicalLinkId, "Reduced", ordered);
            for (var index = 1; index < ordered.Length; index++)
                if (ordered[index - 1].X != ordered[index].X && ordered[index - 1].Y != ordered[index].Y)
                    findings.Add(new ArchitecturePlanningDiagnostic("TerminalJoinDiagonal", "A final route segment, including a terminal join, is diagonal.", PlanningDiagnosticSubject.PhysicalLink, route.PhysicalLinkId));
        }
        ValidateTerminalOrdering(geometry, terminals, routes);
    }

    private void ValidatePolyline(string routeId, string stage, IReadOnlyList<AbsolutePoint> points)
    {
        for (var index = 1; index < points.Count; index++)
            if (points[index - 1].X != points[index].X && points[index - 1].Y != points[index].Y)
                findings.Add(new ArchitecturePlanningDiagnostic(stage + "PolylineDiagonal",
                    $"The {stage.ToLowerInvariant()} final route polyline contains a diagonal segment.",
                    PlanningDiagnosticSubject.PhysicalLink, routeId));
    }

    private void ValidateTerminalOrdering(
        PlannedArchitectureGeometry geometry,
        IReadOnlyList<PlannedPhysicalTerminal> terminals,
        IReadOnlyList<PlannedPhysicalRoute> routes)
    {
        foreach (var group in terminals.GroupBy(item => item.PhysicalNodeId + ":" + item.Side, StringComparer.Ordinal))
        {
            var side = group.First().Side;
            var ordered = group
                .Select(terminal =>
                {
                    var route = routes.SingleOrDefault(item => item.PhysicalLinkId == terminal.PhysicalLinkId);
                    var routePoints = (route?.ReducedPoints ?? route?.RawPoints ?? Array.Empty<PlannedPhysicalRoutePoint>())
                        .Select(point => point.Point).ToArray();
                    var routeX = side == GridSide.Bottom ? routePoints.FirstOrDefault().X : routePoints.LastOrDefault().X;
                    return (terminal, routeX);
                })
                .OrderBy(item => item.routeX)
                .ThenBy(item => item.terminal.PhysicalLinkId, StringComparer.Ordinal)
                .ToArray();
            for (var index = 1; index < ordered.Length; index++)
            {
                if (ordered[index - 1].terminal.Point.X <= ordered[index].terminal.Point.X) continue;
                findings.Add(new ArchitecturePlanningDiagnostic(
                    side == GridSide.Bottom ? "SourceTerminalOrderInversion" : "DestinationTerminalOrderInversion",
                    "Terminal X order does not follow the final geometric approach order.",
                    PlanningDiagnosticSubject.PhysicalNode, group.Key));
                break;
            }
        }
    }

    private void ValidateSharedBends(IReadOnlyList<PlannedPhysicalTurn> turns)
    {
        foreach (var group in turns.GroupBy(turn => turn.Point).Where(group => group.Select(turn => turn.PhysicalLinkId).Distinct(StringComparer.Ordinal).Count() > 1))
        {
            var routesAtBend = group.Select(turn => turn.PhysicalLinkId).Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal).ToArray();
            var details = group.Select(turn =>
            {
                var horizontal = allocation.HorizontalLanes.SingleOrDefault(lane => lane.RunId == turn.HorizontalRunId);
                var vertical = allocation.VerticalLanes.SingleOrDefault(lane => lane.RunId == turn.VerticalRunId);
                return $"{turn.PhysicalLinkId}:cell={turn.CellId};horizontalLane={horizontal?.Lane.Value ?? "none"};verticalLane={vertical?.Lane.Value ?? "none"}";
            }).ToArray();
            findings.Add(new ArchitecturePlanningDiagnostic("SharedBend",
                $"Routes {routesAtBend[0]} and {routesAtBend[1]} share bend {group.Key}; {string.Join("; ", details)}.",
                PlanningDiagnosticSubject.PhysicalSegment, string.Join("|", routesAtBend.Take(2))));
        }
    }

    private void ValidateParallelClearance(IReadOnlyList<PlannedPhysicalRoute> routes)
    {
        var minimum = Math.Max(1, request.RoutePlanning.MinimumParallelSpacing);
        var segments = routes.SelectMany(route => route.Segments.Select(segment => (route, segment))).ToArray();
        for (var left = 0; left < segments.Length; left++)
        for (var right = left + 1; right < segments.Length; right++)
        {
            var a = segments[left];
            var b = segments[right];
            if (a.route.PhysicalLinkId == b.route.PhysicalLinkId || a.segment.Axis != b.segment.Axis) continue;
            if (a.segment.Axis == RouteAxis.Horizontal && a.segment.Start.Y == b.segment.Start.Y) continue;
            if (a.segment.Axis == RouteAxis.Vertical && a.segment.Start.X == b.segment.Start.X) continue;
            var overlap = a.segment.Axis == RouteAxis.Horizontal
                ? Math.Min(Math.Max(a.segment.Start.X, a.segment.End.X), Math.Max(b.segment.Start.X, b.segment.End.X)) -
                  Math.Max(Math.Min(a.segment.Start.X, a.segment.End.X), Math.Min(b.segment.Start.X, b.segment.End.X))
                : Math.Min(Math.Max(a.segment.Start.Y, a.segment.End.Y), Math.Max(b.segment.Start.Y, b.segment.End.Y)) -
                  Math.Max(Math.Min(a.segment.Start.Y, a.segment.End.Y), Math.Min(b.segment.Start.Y, b.segment.End.Y));
            var distance = a.segment.Axis == RouteAxis.Horizontal
                ? Math.Abs(a.segment.Start.Y - b.segment.Start.Y)
                : Math.Abs(a.segment.Start.X - b.segment.Start.X);
            if (overlap > 0 && distance < minimum)
                findings.Add(new ArchitecturePlanningDiagnostic("ParallelClearanceViolation",
                    $"Routes {a.route.PhysicalLinkId} and {b.route.PhysicalLinkId} have only {distance}px parallel clearance over {overlap}px.",
                    PlanningDiagnosticSubject.PhysicalSegment, a.route.PhysicalLinkId + "|" + b.route.PhysicalLinkId));
        }
    }

    private void ValidateCrossings(IReadOnlyList<PlannedPhysicalRoute> routes)
    {
        var segments = routes.SelectMany(route => route.Segments.Select(segment => (route, segment))).ToArray();
        for (var left = 0; left < segments.Length; left++)
        for (var right = left + 1; right < segments.Length; right++)
        {
            var a = segments[left];
            var b = segments[right];
            if (a.route.PhysicalLinkId == b.route.PhysicalLinkId || a.segment.Axis == b.segment.Axis) continue;
            var horizontal = a.segment.Axis == RouteAxis.Horizontal ? a.segment : b.segment;
            var vertical = a.segment.Axis == RouteAxis.Vertical ? a.segment : b.segment;
            var x = vertical.Start.X;
            var y = horizontal.Start.Y;
            var insideHorizontal = x > Math.Min(horizontal.Start.X, horizontal.End.X) && x < Math.Max(horizontal.Start.X, horizontal.End.X);
            var insideVertical = y > Math.Min(vertical.Start.Y, vertical.End.Y) && y < Math.Max(vertical.Start.Y, vertical.End.Y);
            var touches = x >= Math.Min(horizontal.Start.X, horizontal.End.X) && x <= Math.Max(horizontal.Start.X, horizontal.End.X) &&
                          y >= Math.Min(vertical.Start.Y, vertical.End.Y) && y <= Math.Max(vertical.Start.Y, vertical.End.Y);
            if (touches && !(insideHorizontal && insideVertical))
                findings.Add(new ArchitecturePlanningDiagnostic("InvalidCrossing",
                    $"Routes {a.route.PhysicalLinkId} and {b.route.PhysicalLinkId} meet at a non-interior crossing ({x},{y}).",
                    PlanningDiagnosticSubject.PhysicalSegment, a.route.PhysicalLinkId + "|" + b.route.PhysicalLinkId));
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
            findings.Count(item => item.Code == "LabelGeometryUnavailable"), routes.GroupBy(route => route.TopologyFamily.ToString()).ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal), timings,
            InvalidRouteCount: invalidRouteIds.Count,
            AttemptedSegmentCount: attemptedSegments.Count + routes.Sum(route => route.Segments.Count),
            DiagonalSegmentCount: attemptedSegments.Count(item => item.FailureCode == "DiagonalComponentConnection") +
                findings.Count(item => item.Code == "TerminalJoinDiagonal"),
            CorridorEscapeCount: attemptedSegments.Count(item => item.FailureCode == "ComponentCorridorEscape"),
            ComponentContinuityFailureCount: attemptedSegments.Count(item => item.FailureCode == "ComponentContinuityMismatch"),
            SourceStubDirectionFailureCount: findings.Count(item => item.Code == "SourceStubDirectionInvalid" || item.Code == "SourceDepartureDirectionInvalid"),
            DestinationStubDirectionFailureCount: findings.Count(item => item.Code == "DestinationStubDirectionInvalid" || item.Code == "DestinationApproachDirectionInvalid"));
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
