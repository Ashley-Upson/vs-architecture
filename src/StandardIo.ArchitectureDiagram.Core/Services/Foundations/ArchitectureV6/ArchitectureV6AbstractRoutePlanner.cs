using System;
using System.Collections.Generic;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core.Models.ArchitectureV6;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.ArchitectureV6;

internal sealed class ArchitectureV6AbstractRoutePlanner
{
    private readonly ArchitecturePlanningRequest request;
    private readonly IReadOnlyList<PlannedPhysicalNode> nodes;
    private readonly IReadOnlyList<PlannedPhysicalLink> links;
    private readonly Dictionary<string, PlannedNodePlacement> placements;
    private readonly Dictionary<string, PhysicalNodePlacementMetadata> metadata;
    private readonly Dictionary<PlanningGridId, MutableGrid> grids;
    private readonly Dictionary<string, int> rowOrderByNode = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> columnOrderByNode = new(StringComparer.Ordinal);
    private readonly List<ArchitecturePlanningDiagnostic> diagnostics = new();

    public ArchitectureV6AbstractRoutePlanner(
        ArchitecturePlanningRequest request,
        IReadOnlyList<PlannedPhysicalNode> nodes,
        IReadOnlyList<PlannedPhysicalLink> links,
        IReadOnlyList<PlannedNodePlacement> placements,
        IReadOnlyList<PhysicalNodePlacementMetadata> metadata,
        IReadOnlyList<ProjectRoutingGrid> projectGrids,
        DiagramRoutingGrid diagramGrid)
    {
        this.request = request ?? throw new ArgumentNullException(nameof(request));
        this.nodes = nodes ?? throw new ArgumentNullException(nameof(nodes));
        this.links = links ?? throw new ArgumentNullException(nameof(links));
        this.placements = placements.ToDictionary(item => item.PhysicalNodeId, StringComparer.Ordinal);
        this.metadata = metadata.ToDictionary(item => item.PhysicalNodeId, StringComparer.Ordinal);
        grids = projectGrids.ToDictionary(item => item.Grid.Id, MutableGrid.From, EqualityComparer<PlanningGridId>.Default);
        grids[diagramGrid.Grid.Id] = MutableGrid.From(diagramGrid.Grid);
        foreach (var placement in placements)
        {
            var grid = projectGrids.Single(item => item.Grid.Id.Equals(placement.GridId)).Grid;
            rowOrderByNode[placement.PhysicalNodeId] = grid.Rows.Single(row => row.Id.Equals(placement.AnchorCellId.RowId)).LogicalOrder;
            columnOrderByNode[placement.PhysicalNodeId] = grid.Columns.Single(column => column.Id.Equals(placement.AnchorCellId.ColumnId)).LogicalOrder;
        }
    }

    public AbstractRoutePlanningResult Build()
    {
        var approaches = BuildApproaches();
        var routes = new List<PlannedGridRoute>();
        var transitions = new List<GridTransition>();
        var routeSteps = new List<PlannedGridRouteStep>();

        foreach (var link in links.OrderBy(item => ProjectOf(item.SourcePhysicalNodeId), StringComparer.Ordinal)
                     .ThenBy(item => Classify(item))
                     .ThenBy(item => rowOrderByNode[item.SourcePhysicalNodeId])
                     .ThenBy(item => columnOrderByNode[item.SourcePhysicalNodeId])
                     .ThenBy(item => rowOrderByNode[item.DestinationPhysicalNodeId])
                     .ThenBy(item => columnOrderByNode[item.DestinationPhysicalNodeId])
                     .ThenBy(item => item.PhysicalLinkId, StringComparer.Ordinal))
        {
            var route = BuildRoute(link, approaches, transitions);
            routes.Add(route);
            routeSteps.AddRange(route.Steps);
        }

        ValidateRoutes(routes);

        var runs = CompileStraightRuns(routes);
        var endpointDemands = routes.SelectMany(route => new[]
        {
            new NodeEndpointDemand(route.PhysicalLinkId, route.Source, 1, null),
            new NodeEndpointDemand(route.PhysicalLinkId, route.Destination, 1, route.DestinationApproachReservationId)
        }).ToArray();
        var turnDemands = runs.Where(run => run.Axis == RouteAxis.Horizontal)
            .SelectMany(run => routes.Where(route => route.PhysicalLinkId == run.RouteId)
                .SelectMany(route => route.Steps.Where(step => step.Role == RouteStepRole.Turn)
                    .Select(step => new TurnDemand(route.PhysicalLinkId, step.CellId.ToString(),
                        new LaneId($"provisional:h:{route.PhysicalLinkId}"), new LaneId($"provisional:v:{route.PhysicalLinkId}")))))
            .ToArray();

        var projectGrids = grids.Values.Where(grid => grid.Id.Value.StartsWith("project:", StringComparison.Ordinal))
            .OrderBy(grid => grid.Id.Value, StringComparer.Ordinal)
            .Select(grid => grid.ToProjectGrid(nodes.Where(node => ProjectOf(node.PhysicalNodeId) == grid.Id.Value.Substring("project:".Length))))
            .ToArray();
        var diagramGrid = BuildDiagramGrid(transitions);
        return new AbstractRoutePlanningResult(projectGrids, diagramGrid, routes, approaches, runs, turnDemands, endpointDemands, diagnostics);
    }

    private void ValidateRoutes(IReadOnlyList<PlannedGridRoute> routes)
    {
        foreach (var duplicate in routes.GroupBy(route => route.PhysicalLinkId, StringComparer.Ordinal).Where(group => group.Count() != 1))
            diagnostics.Add(new ArchitecturePlanningDiagnostic("AbstractRouteCardinality", "Every physical link must have exactly one abstract route.", PlanningDiagnosticSubject.PhysicalLink, duplicate.Key));

        foreach (var link in links)
        {
            var route = routes.SingleOrDefault(item => item.PhysicalLinkId == link.PhysicalLinkId);
            if (route is null)
            {
                diagnostics.Add(new ArchitecturePlanningDiagnostic("MissingAbstractRoute", "A physical link has no abstract route or explicit unsupported result.", PlanningDiagnosticSubject.PhysicalLink, link.PhysicalLinkId));
                continue;
            }
            if (route.Source.PhysicalNodeId != link.SourcePhysicalNodeId || route.Destination.PhysicalNodeId != link.DestinationPhysicalNodeId)
                diagnostics.Add(new ArchitecturePlanningDiagnostic("AbstractRouteEndpointMismatch", "Abstract route endpoints do not match the physical link.", PlanningDiagnosticSubject.PhysicalLink, link.PhysicalLinkId));
            foreach (var step in route.Steps)
            {
                if (!grids.TryGetValue(step.GridId, out var grid) || !grid.Cells.ContainsKey(step.CellId))
                    diagnostics.Add(new ArchitecturePlanningDiagnostic("AbstractRouteUnknownCell", "An abstract route step references a cell absent from its grid.", PlanningDiagnosticSubject.Cell, step.CellId.ToString()));
                else if ((grid.Cells[step.CellId].Capabilities & CellCapability.RoutingAllowed) == 0)
                    diagnostics.Add(new ArchitecturePlanningDiagnostic("AbstractRouteCellNotRoutable", "An abstract route step uses a cell without routing capability.", PlanningDiagnosticSubject.Cell, step.CellId.ToString()));
            }
        }
    }

    private IReadOnlyList<DestinationApproachReservation> BuildApproaches()
    {
        return nodes.OrderBy(node => node.PhysicalNodeId, StringComparer.Ordinal).Select(node =>
        {
            var gridId = GridOf(node);
            var cell = UseCell(gridId, placements[node.PhysicalNodeId].AnchorCellId.RowId,
                ColumnForOwnerRole(gridId, PlanningGridTrackRole.DestinationApproach, node.PhysicalNodeId,
                    placements[node.PhysicalNodeId].AnchorCellId.ColumnId), CellOccupancy.Empty);
            var linkIds = links.Where(link => link.DestinationPhysicalNodeId == node.PhysicalNodeId)
                .OrderBy(link => link.PhysicalLinkId, StringComparer.Ordinal).Select(link => link.PhysicalLinkId).ToArray();
            return new DestinationApproachReservation($"approach:{node.PhysicalNodeId}", node.PhysicalNodeId, gridId,
                new[] { cell }, linkIds, node.IsExternal ? "external-direct" : "direct");
        }).ToArray();
    }

    private PlannedGridRoute BuildRoute(
        PlannedPhysicalLink link,
        IReadOnlyList<DestinationApproachReservation> approaches,
        ICollection<GridTransition> transitions)
    {
        var topology = Classify(link);
        var source = nodes.Single(node => node.PhysicalNodeId == link.SourcePhysicalNodeId);
        var destination = nodes.Single(node => node.PhysicalNodeId == link.DestinationPhysicalNodeId);
        var sourceGrid = GridOf(source);
        var destinationGrid = GridOf(destination);
        var sourceEndpoint = Endpoint(source, GridSide.Bottom, "source", sourceGrid);
        var destinationEndpoint = Endpoint(destination, GridSide.Top, "destination", destinationGrid);
        var approach = approaches.Single(item => item.PhysicalNodeId == destination.PhysicalNodeId);
        var steps = new List<PlannedGridRouteStep>();

        if (!sourceGrid.Equals(destinationGrid))
        {
            var sourceBoundary = UseCell(sourceGrid, placements[source.PhysicalNodeId].AnchorCellId.RowId,
                ColumnForRole(sourceGrid, PlanningGridTrackRole.ProjectBoundaryTransition, ColumnId(source)), CellOccupancy.Empty);
            var diagramId = new PlanningGridId("diagram");
            var diagramBoundary = UseCell(diagramId, grids[diagramId].RowOrder[0], grids[diagramId].ColumnOrder[0], CellOccupancy.Empty);
            var destinationBoundary = UseCell(destinationGrid, placements[destination.PhysicalNodeId].AnchorCellId.RowId,
                ColumnForRole(destinationGrid, PlanningGridTrackRole.ProjectBoundaryTransition, ColumnId(destination)), CellOccupancy.Empty);
            transitions.Add(new GridTransition(sourceGrid, sourceBoundary, diagramId, diagramBoundary, "project-to-diagram", link.SemanticLinkId,
                "exit", link.SourceProjectId, null, true, request.ProjectPlacement.ShowProjectContainers));
            transitions.Add(new GridTransition(diagramId, diagramBoundary, destinationGrid, destinationBoundary, "diagram-to-project", link.SemanticLinkId,
                "entry", null, link.DestinationProjectId, true, request.ProjectPlacement.ShowProjectContainers));
            steps.Add(Step(sourceGrid, placements[source.PhysicalNodeId].AnchorCellId, GridSide.Top, GridSide.Bottom, RouteStepRole.SourceExit, 0, topology));
            steps.Add(Step(sourceGrid, sourceBoundary, GridSide.Top, GridSide.Bottom, RouteStepRole.ProjectExit, 1, topology));
            steps.Add(Step(diagramId, diagramBoundary, GridSide.Top, GridSide.Bottom, RouteStepRole.DiagramGridPassage, 2, topology));
            steps.Add(Step(destinationGrid, destinationBoundary, GridSide.Top, GridSide.Bottom, RouteStepRole.ProjectEntry, 3, topology));
            steps.Add(Step(destinationGrid, approach.Cells[0], GridSide.Top, GridSide.Bottom, RouteStepRole.DestinationEntry, 4, topology));
        }
        else
        {
            var departureColumn = topology == RouteTopologyFamily.SameLayer || topology == RouteTopologyFamily.Upward || topology == RouteTopologyFamily.OwnershipLocalReturn
                ? ColumnForOwnerRole(sourceGrid, PlanningGridTrackRole.OwnershipLocalReturn, source.PhysicalNodeId,
                    ColumnAt(sourceGrid, DepartureColumn(source, destination)))
                : ColumnAt(sourceGrid, DepartureColumn(source, destination));
            var departure = UseCell(sourceGrid, placements[source.PhysicalNodeId].AnchorCellId.RowId,
                departureColumn, CellOccupancy.Empty);
            var routeRow = UseCell(sourceGrid, RoutingRow(sourceGrid, placements[source.PhysicalNodeId].AnchorCellId.RowId,
                placements[destination.PhysicalNodeId].AnchorCellId.RowId), ColumnId(destination), CellOccupancy.Empty);
            steps.Add(Step(sourceGrid, placements[source.PhysicalNodeId].AnchorCellId, GridSide.Top, topology == RouteTopologyFamily.SameLayer || topology == RouteTopologyFamily.Upward || topology == RouteTopologyFamily.OwnershipLocalReturn ? GridSide.Right : GridSide.Bottom,
                RouteStepRole.SourceExit, 0, topology));
            if (topology == RouteTopologyFamily.SameLayer || topology == RouteTopologyFamily.Upward || topology == RouteTopologyFamily.OwnershipLocalReturn)
            {
                steps.Add(Step(sourceGrid, departure, GridSide.Left, GridSide.Bottom, RouteStepRole.Turn, 1, topology));
                steps.Add(Step(sourceGrid, routeRow, GridSide.Top, GridSide.Bottom, RouteStepRole.VerticalPassThrough, 2, topology));
            }
            else
                steps.Add(Step(sourceGrid, departure, GridSide.Top, GridSide.Bottom, RouteStepRole.VerticalPassThrough, 1, topology));
            steps.Add(Step(sourceGrid, approach.Cells[0], GridSide.Top, GridSide.Bottom, RouteStepRole.DestinationEntry,
                steps.Count, topology));
        }

        steps = CompleteTurnCorridor(sourceGrid, steps, topology, link);
        steps = steps.Select((step, index) => step with { Order = index }).ToList();

        var legality = ValidateSteps(steps, link, topology);
        var supported = legality.IsSupported;
        var unsupportedReason = supported ? null : legality.Message;
        if (!supported)
            diagnostics.Add(new ArchitecturePlanningDiagnostic("UnsupportedAbstractRoute", unsupportedReason!, PlanningDiagnosticSubject.PhysicalLink, link.PhysicalLinkId));
        return new PlannedGridRoute(link.PhysicalLinkId, sourceEndpoint, steps, transitions.Where(item => item.SemanticLinkId == link.SemanticLinkId).ToArray(),
            destinationEndpoint, topology, link.SourceProjectId, link.DestinationProjectId,
            $"{topology}: deterministic topology-owned route", approach.ReservationId, supported, unsupportedReason);
    }

    private RouteLegalityResult ValidateSteps(IReadOnlyList<PlannedGridRouteStep> steps, PlannedPhysicalLink link, RouteTopologyFamily topology)
    {
        if (steps.Count == 0) return new RouteLegalityResult(false, "No route steps were produced.");
        foreach (var step in steps)
        {
            if (!grids.TryGetValue(step.GridId, out var grid) && step.GridId.Value != "diagram")
                return new RouteLegalityResult(false, Trace(link, topology, steps, step, "missing grid"));
            if (step.EntrySide == step.ExitSide)
                return new RouteLegalityResult(false, Trace(link, topology, steps, step, "entry and exit sides are identical"));
            if (step.Role != RouteStepRole.SourceExit && step.Role != RouteStepRole.DestinationEntry &&
                grid!.Cells.TryGetValue(step.CellId, out var cell) && cell.FootprintOwnerId is not null)
                return new RouteLegalityResult(false, Trace(link, topology, steps, step,
                    $"unrelated node footprint owner '{cell.FootprintOwnerId}'"));
        }
        if (steps[0].Role != RouteStepRole.SourceExit || steps[steps.Count - 1].Role != RouteStepRole.DestinationEntry)
            return new RouteLegalityResult(false, Trace(link, topology, steps, null, "invalid route terminals"));
        return new RouteLegalityResult(true, null);
    }

    private List<PlannedGridRouteStep> CompleteTurnCorridor(
        PlanningGridId gridId,
        IReadOnlyList<PlannedGridRouteStep> original,
        RouteTopologyFamily topology,
        PlannedPhysicalLink link)
    {
        var turnIndex = original.ToList().FindIndex(step => step.Role == RouteStepRole.Turn);
        if (turnIndex < 0) return original.ToList();

        var turn = original[turnIndex];
        var followingIndex = Enumerable.Range(turnIndex + 1, original.Count - turnIndex - 1)
            .FirstOrDefault(index => original[index].Role == RouteStepRole.VerticalPassThrough);
        if (followingIndex == 0) return original.ToList();

        var following = original[followingIndex];
        if (turn.GridId != following.GridId || turn.CellId.RowId == following.CellId.RowId && turn.CellId.ColumnId == following.CellId.ColumnId)
            return original.ToList();

        var grid = grids[gridId];
        var turnRow = grid.RowOrder.ToList().IndexOf(turn.CellId.RowId);
        var targetRow = grid.RowOrder.ToList().IndexOf(following.CellId.RowId);
        var turnColumn = grid.ColumnOrder.ToList().IndexOf(turn.CellId.ColumnId);
        var targetColumn = grid.ColumnOrder.ToList().IndexOf(following.CellId.ColumnId);
        if (turnRow < 0 || targetRow < 0 || turnColumn < 0 || targetColumn < 0)
            return original.ToList();
        var sameRow = targetRow == turnRow;
        var movingDown = targetRow > turnRow;
        var rowDirection = movingDown ? 1 : -1;
        var sourceExit = original.First(step => step.Role == RouteStepRole.SourceExit);
        var sourceColumn = grid.ColumnOrder.ToList().IndexOf(sourceExit.CellId.ColumnId);
        var destination = original.FirstOrDefault(step => step.Role == RouteStepRole.DestinationEntry);
        var destinationRow = destination is null ? targetRow : grid.RowOrder.ToList().IndexOf(destination.CellId.RowId);
        var selectedBandRow = SelectClearBandRow(grid, turnRow, targetRow, destinationRow, sourceExit.CellId.RowId,
            sourceColumn, targetColumn, turnColumn, link);
        if (selectedBandRow >= 0) targetRow = selectedBandRow;
        var corridorColumn = SelectClearCorridorColumn(grid, turnRow, targetRow, sourceExit.CellId.RowId,
            grid.RowOrder[targetRow], sourceColumn, targetColumn, turnColumn, link);
        if (corridorColumn >= 0)
        {
            turnColumn = corridorColumn;
            turn = turn with { CellId = turn.CellId with { ColumnId = grid.ColumnOrder[corridorColumn] } };
        }
        var columnDirection = targetColumn >= turnColumn ? 1 : -1;
        var completed = original.Take(turnIndex).ToList();

        completed.Add(turn with
        {
            ExitSide = sameRow
                ? (columnDirection > 0 ? GridSide.Right : GridSide.Left)
                : (movingDown ? GridSide.Bottom : GridSide.Top),
            EndpointRelationship = "selected-corridor-turn"
        });

        for (var row = turnRow + rowDirection; !sameRow && row != targetRow; row += rowDirection)
        {
            var rowId = grid.RowOrder[row];
            var cell = UseCell(gridId, rowId, turn.CellId.ColumnId, CellOccupancy.Empty);
            completed.Add(new PlannedGridRouteStep(gridId, cell,
                movingDown ? GridSide.Top : GridSide.Bottom,
                movingDown ? GridSide.Bottom : GridSide.Top,
                RouteStepRole.VerticalPassThrough, 0, topology.ToString(), "corridor-completion"));
        }

        if (turnColumn != targetColumn)
        {
            var bandRow = following.CellId.RowId;
            if (!sameRow)
            {
                var turnBandCell = UseCell(gridId, bandRow, turn.CellId.ColumnId, CellOccupancy.Empty);
                completed.Add(new PlannedGridRouteStep(gridId, turnBandCell,
                    movingDown ? GridSide.Top : GridSide.Bottom,
                    columnDirection > 0 ? GridSide.Right : GridSide.Left,
                    RouteStepRole.Turn, 0, topology.ToString(), "corridor-completion"));
            }

            for (var column = turnColumn + columnDirection; column != targetColumn; column += columnDirection)
            {
                var columnId = grid.ColumnOrder[column];
                var cell = UseCell(gridId, bandRow, columnId, CellOccupancy.Empty);
                completed.Add(new PlannedGridRouteStep(gridId, cell,
                    columnDirection > 0 ? GridSide.Left : GridSide.Right,
                    columnDirection > 0 ? GridSide.Right : GridSide.Left,
                    RouteStepRole.HorizontalPassThrough, 0, topology.ToString(), "corridor-completion"));
            }

            var targetBandCell = UseCell(gridId, bandRow, following.CellId.ColumnId, CellOccupancy.Empty);
            completed.Add(new PlannedGridRouteStep(gridId, targetBandCell,
                columnDirection > 0 ? GridSide.Left : GridSide.Right,
                GridSide.Bottom,
                RouteStepRole.Turn, 0, topology.ToString(), "corridor-completion"));

            // The target-band bend occupies the same cell as the first vertical
            // step. Replace that step instead of emitting the same cell twice.
        }

        AppendDestinationCorridor(completed, original, followingIndex, gridId, topology);
        return completed
            .GroupBy(step => step.GridId.Value + ":" + step.CellId, StringComparer.Ordinal)
            .Select(group => group.OrderBy(step => step.Order).First())
            .OrderBy(step => step.Order)
            .ToList();
    }

    private void AppendDestinationCorridor(
        List<PlannedGridRouteStep> completed,
        IReadOnlyList<PlannedGridRouteStep> original,
        int replacedFollowingIndex,
        PlanningGridId gridId,
        RouteTopologyFamily topology)
    {
        var destinationIndex = Enumerable.Range(replacedFollowingIndex + 1, original.Count - replacedFollowingIndex - 1)
            .FirstOrDefault(index => original[index].Role == RouteStepRole.DestinationEntry);
        if (destinationIndex == 0)
        {
            completed.AddRange(original.Skip(replacedFollowingIndex + 1));
            return;
        }

        var last = completed.Last();
        var destination = original[destinationIndex];
        if (last.GridId != destination.GridId || last.CellId.ColumnId != destination.CellId.ColumnId)
        {
            completed.AddRange(original.Skip(destinationIndex));
            return;
        }

        var grid = grids[gridId];
        var lastRow = grid.RowOrder.ToList().IndexOf(last.CellId.RowId);
        var destinationRow = grid.RowOrder.ToList().IndexOf(destination.CellId.RowId);
        if (lastRow < 0 || destinationRow < 0 || lastRow == destinationRow)
        {
            completed.AddRange(original.Skip(destinationIndex));
            return;
        }

        var direction = destinationRow > lastRow ? 1 : -1;
        for (var row = lastRow + direction; row != destinationRow; row += direction)
        {
            var rowId = grid.RowOrder[row];
            var cell = UseCell(gridId, rowId, last.CellId.ColumnId, CellOccupancy.Empty);
            completed.Add(new PlannedGridRouteStep(gridId, cell,
                direction > 0 ? GridSide.Top : GridSide.Bottom,
                direction > 0 ? GridSide.Bottom : GridSide.Top,
                RouteStepRole.VerticalPassThrough, 0, topology.ToString(), "corridor-completion"));
        }
        completed.AddRange(original.Skip(destinationIndex));
    }

    private int SelectClearCorridorColumn(
        MutableGrid grid,
        int turnRow,
        int targetRow,
        PlanningGridRowId sourceRowId,
        PlanningGridRowId bandRowId,
        int sourceColumn,
        int targetColumn,
        int preferredColumn,
        PlannedPhysicalLink link)
    {
        var sourceRow = grid.RowOrder.ToList().IndexOf(sourceRowId);
        var bandRow = grid.RowOrder.ToList().IndexOf(bandRowId);
        if (sourceRow < 0 || bandRow < 0) return preferredColumn;

        var candidates = grid.ColumnOrder
            .Select((column, index) => (column, index))
            .OrderBy(item => item.index == preferredColumn ? 0 : 1)
            .ThenBy(item => Math.Abs(item.index - preferredColumn))
            .ThenBy(item => item.index)
            .Select(item => item.index);
        foreach (var candidate in candidates)
        {
            if (candidate == sourceColumn || candidate == targetColumn) continue;
            if (!ClearVertical(grid, candidate, turnRow, targetRow, link)) continue;
            if (!ClearHorizontal(grid, sourceRow, sourceColumn, candidate, link)) continue;
            if (!ClearHorizontal(grid, bandRow, candidate, targetColumn, link)) continue;
            return candidate;
        }
        return -1;
    }

    private int SelectClearBandRow(
        MutableGrid grid,
        int turnRow,
        int preferredRow,
        int destinationRow,
        PlanningGridRowId sourceRowId,
        int sourceColumn,
        int targetColumn,
        int preferredColumn,
        PlannedPhysicalLink link)
    {
        var sourceRow = grid.RowOrder.ToList().IndexOf(sourceRowId);
        if (sourceRow < 0) return preferredRow;
        var candidates = grid.Rows.Values
            .Where(row => row.Role == PlanningGridTrackRole.InterLayerRouting)
            .Select(row => grid.RowOrder.ToList().IndexOf(row.Id))
            .Where(index => index >= 0)
            .Distinct()
            .OrderBy(index => index == preferredRow ? 0 : 1)
            .ThenBy(index => Math.Abs(index - preferredRow))
            .ThenBy(index => index)
            .ToArray();
        foreach (var row in candidates)
        {
            var column = SelectClearCorridorColumn(grid, turnRow, row, sourceRowId, grid.RowOrder[row],
                sourceColumn, targetColumn, preferredColumn, link);
            if (column < 0) continue;
            if (destinationRow >= 0 && !ClearVertical(grid, targetColumn, row, destinationRow, link)) continue;
            return row;
        }
        return preferredRow;
    }

    private bool ClearVertical(MutableGrid grid, int column, int startRow, int endRow, PlannedPhysicalLink link)
    {
        var direction = endRow >= startRow ? 1 : -1;
        for (var row = startRow; row != endRow + direction; row += direction)
        {
            if (IsUnrelatedFootprint(grid, grid.RowOrder[row], grid.ColumnOrder[column], link)) return false;
        }
        return true;
    }

    private bool ClearHorizontal(MutableGrid grid, int row, int startColumn, int endColumn, PlannedPhysicalLink link)
    {
        var direction = endColumn >= startColumn ? 1 : -1;
        for (var column = startColumn; column != endColumn + direction; column += direction)
        {
            if (IsUnrelatedFootprint(grid, grid.RowOrder[row], grid.ColumnOrder[column], link)) return false;
        }
        return true;
    }

    private bool IsUnrelatedFootprint(MutableGrid grid, PlanningGridRowId rowId, PlanningGridColumnId columnId, PlannedPhysicalLink link)
    {
        var cellId = new PlanningGridCellId(grid.Id, rowId, columnId);
        return grid.Cells.TryGetValue(cellId, out var cell)
            && cell.FootprintOwnerId is not null
            && !string.Equals(cell.FootprintOwnerId, link.SourcePhysicalNodeId, StringComparison.Ordinal)
            && !string.Equals(cell.FootprintOwnerId, link.DestinationPhysicalNodeId, StringComparison.Ordinal);
    }

    private string Trace(PlannedPhysicalLink link, RouteTopologyFamily topology, IReadOnlyList<PlannedGridRouteStep> steps,
        PlannedGridRouteStep? rejectedStep, string reason)
    {
        var source = nodes.Single(node => node.PhysicalNodeId == link.SourcePhysicalNodeId);
        var destination = nodes.Single(node => node.PhysicalNodeId == link.DestinationPhysicalNodeId);
        var sourcePlacement = placements[source.PhysicalNodeId];
        var destinationPlacement = placements[destination.PhysicalNodeId];
        var stepText = string.Join(" | ", steps.Select(step =>
            $"{step.Order}:{step.GridId.Value}:{step.CellId.RowId.Value}/{step.CellId.ColumnId.Value}:{step.Role}:{step.EntrySide}->{step.ExitSide}"));
        var sourceFootprint = string.Join(",", sourcePlacement.Footprint.Select(cell => cell.ColumnId.Value).Distinct(StringComparer.Ordinal));
        var destinationFootprint = string.Join(",", destinationPlacement.Footprint.Select(cell => cell.ColumnId.Value).Distinct(StringComparer.Ordinal));
        var rejected = rejectedStep is null ? "none" : $"{rejectedStep.GridId.Value}:{rejectedStep.CellId}";
        return $"Unsupported route trace; link={link.PhysicalLinkId}; semantic={link.SemanticLinkId}; " +
            $"source={source.PhysicalNodeId}/{source.SemanticNodeId}; destination={destination.PhysicalNodeId}/{destination.SemanticNodeId}; " +
            $"projects={source.ProjectId}->{destination.ProjectId}; rows={sourcePlacement.AnchorCellId.RowId.Value}->{destinationPlacement.AnchorCellId.RowId.Value}; " +
            $"anchors={sourcePlacement.AnchorCellId.ColumnId.Value}->{destinationPlacement.AnchorCellId.ColumnId.Value}; " +
            $"sourceFootprint=[{sourceFootprint}]; destinationFootprint=[{destinationFootprint}]; " +
            $"sourceOwner={metadata[source.PhysicalNodeId].PositionalOwnerId}; destinationOwner={metadata[destination.PhysicalNodeId].PositionalOwnerId}; " +
            $"sourceSubtree={metadata[source.PhysicalNodeId].SubtreeId}; destinationSubtree={metadata[destination.PhysicalNodeId].SubtreeId}; " +
            $"topology={topology}; rejected={rejected}; reason={reason}; steps={stepText}";
    }

    private IReadOnlyList<PlannedStraightRun> CompileStraightRuns(IReadOnlyList<PlannedGridRoute> routes)
    {
        var result = new List<PlannedStraightRun>();
        foreach (var route in routes)
        {
            var current = new List<PlanningGridCellId>();
            RouteAxis? axis = null;
            foreach (var step in route.Steps)
            {
                var stepAxis = IsVertical(step) ? RouteAxis.Vertical : RouteAxis.Horizontal;
                if (axis is not null && axis != stepAxis && current.Count > 0)
                {
                    result.Add(new PlannedStraightRun(route.PhysicalLinkId, route.Steps.First(item => current.Contains(item.CellId)).GridId,
                        axis.Value, current.ToArray(), "step-start", "step-end", new LaneId($"provisional:{route.PhysicalLinkId}:{result.Count}")));
                    current = new List<PlanningGridCellId>();
                }
                axis = stepAxis;
                current.Add(step.CellId);
            }
            if (axis is not null && current.Count > 0)
                result.Add(new PlannedStraightRun(route.PhysicalLinkId, route.Steps.First(item => current.Contains(item.CellId)).GridId,
                    axis.Value, current.ToArray(), "step-start", "step-end", new LaneId($"provisional:{route.PhysicalLinkId}:{result.Count}")));
        }
        return result;
    }

    private DiagramRoutingGrid BuildDiagramGrid(IReadOnlyList<GridTransition> transitions)
    {
        var grid = grids[new PlanningGridId("diagram")];
        return new DiagramRoutingGrid(grid.ToGrid(), Array.Empty<RelativeRectangle>(), transitions.ToArray());
    }

    private PlannedGridRouteStep Step(PlanningGridId gridId, PlanningGridCellId cell, GridSide entry, GridSide exit,
        RouteStepRole role, int order, RouteTopologyFamily topology) =>
        new(gridId, cell, entry, exit, role, order, topology.ToString(), null);

    private NodeEndpoint Endpoint(PlannedPhysicalNode node, GridSide side, string terminal, PlanningGridId gridId) =>
        new(node.PhysicalNodeId, side, $"{terminal}:{node.PhysicalNodeId}", 0, gridId, ColumnId(node), 1, $"{ProjectOf(node.PhysicalNodeId)}:{node.PhysicalNodeId}");

    private RouteTopologyFamily Classify(PlannedPhysicalLink link)
    {
        if (link.SourceProjectId != link.DestinationProjectId) return RouteTopologyFamily.CrossProject;
        var sourceRow = rowOrderByNode[link.SourcePhysicalNodeId];
        var targetRow = rowOrderByNode[link.DestinationPhysicalNodeId];
        if (nodes.Single(node => node.PhysicalNodeId == link.DestinationPhysicalNodeId).IsExternal) return RouteTopologyFamily.External;
        if (targetRow == sourceRow)
            return metadata[link.SourcePhysicalNodeId].PositionalOwnerId == metadata[link.DestinationPhysicalNodeId].PositionalOwnerId ? RouteTopologyFamily.OwnershipLocalReturn : RouteTopologyFamily.SameLayer;
        if (targetRow > sourceRow)
            return targetRow == sourceRow + 1 ? RouteTopologyFamily.AdjacentDownward : RouteTopologyFamily.LongDownward;
        return RouteTopologyFamily.Upward;
    }

    private PlanningGridCellId UseCell(PlanningGridId gridId, PlanningGridRowId rowId, PlanningGridColumnId columnId, CellOccupancy occupancy)
    {
        if (!grids.TryGetValue(gridId, out var grid) || !grid.RowOrder.Contains(rowId) || !grid.ColumnOrder.Contains(columnId))
        {
            diagnostics.Add(new ArchitecturePlanningDiagnostic("MissingStructuralRegion", "Route requested a row or column not created by placement.", PlanningDiagnosticSubject.Grid, gridId.Value));
            return new PlanningGridCellId(gridId, rowId, columnId);
        }
        var cell = new PlanningGridCellId(gridId, rowId, columnId);
        if (!grid.Cells.ContainsKey(cell))
            grid.Cells[cell] = new PlanningGridCell(cell, CellCapability.RoutingAllowed, occupancy, Array.Empty<string>());
        return cell;
    }

    private PlanningGridId GridOf(PlannedPhysicalNode node) => new($"project:{ProjectOf(node.PhysicalNodeId)}");
    private string ProjectOf(string physicalNodeId) => nodes.Single(node => node.PhysicalNodeId == physicalNodeId).ProjectId ?? "external";
    private static bool IsVertical(PlannedGridRouteStep step) => step.EntrySide == GridSide.Top || step.EntrySide == GridSide.Bottom;
    private int Column(PlannedPhysicalNode node) => columnOrderByNode[node.PhysicalNodeId];
    private PlanningGridColumnId ColumnId(PlannedPhysicalNode node) => placements[node.PhysicalNodeId].AnchorCellId.ColumnId;
    private PlanningGridColumnId ColumnForRole(PlanningGridId gridId, PlanningGridTrackRole role, PlanningGridColumnId fallback)
    {
        var grid = grids[gridId];
            return grid.Columns.Values.OrderBy(column => column.LogicalOrder).FirstOrDefault(column => column.Role == role)?.Id ?? fallback;
    }
    private PlanningGridColumnId ColumnForOwnerRole(PlanningGridId gridId, PlanningGridTrackRole role, string ownerId,
        PlanningGridColumnId fallback)
    {
        var grid = grids[gridId];
        return grid.Columns.Values.OrderBy(column => column.LogicalOrder)
            .FirstOrDefault(column => column.Role == role && string.Equals(column.OwnerId, ownerId, StringComparison.Ordinal))?.Id ?? fallback;
    }
    private PlanningGridColumnId ColumnAt(PlanningGridId gridId, int requested)
    {
        var grid = grids[gridId];
        var index = Math.Max(0, Math.Min(requested, Math.Max(0, grid.ColumnOrder.Count - 1)));
        return grid.ColumnOrder[index];
    }
    private PlanningGridRowId RoutingRow(PlanningGridId gridId, PlanningGridRowId source, PlanningGridRowId destination)
    {
        var grid = grids[gridId];
        var preferred = grid.Rows.Values.Where(row => row.Role == PlanningGridTrackRole.InterLayerRouting)
            .OrderBy(row => Math.Abs(row.LogicalOrder - (grid.Rows[source].LogicalOrder + grid.Rows[destination].LogicalOrder) / 2))
            .FirstOrDefault();
        return preferred?.Id ?? destination;
    }
    private int DepartureColumn(PlannedPhysicalNode source, PlannedPhysicalNode destination)
    {
        var grid = grids[GridOf(source)];
        var footprintColumns = placements[source.PhysicalNodeId].Footprint
            .Select(cell => grid.Columns[cell.ColumnId].LogicalOrder)
            .Distinct()
            .OrderBy(value => value)
            .ToArray();
        var minimum = footprintColumns.First();
        var maximum = footprintColumns.Last();
        var target = Column(destination);
        var candidates = new[] { minimum - 1, maximum + 1 }
            .Where(value => value >= 0 && value < grid.ColumnOrder.Count)
            .Distinct()
            .OrderBy(value => Math.Abs(value - target))
            .ThenBy(value => value == maximum + 1 ? 0 : 1)
            .ToArray();
        if (candidates.Length > 0) return candidates[0];

        return target < minimum ? minimum - 1 : maximum + 1;
    }

    private sealed class MutableGrid
    {
        public MutableGrid(PlanningGridId id) => Id = id;
        public PlanningGridId Id { get; }
        public Dictionary<PlanningGridCellId, PlanningGridCell> Cells { get; } = new();
        public List<PlanningGridRowId> RowOrder { get; } = new();
        public List<PlanningGridColumnId> ColumnOrder { get; } = new();
        public IReadOnlyList<SubtreeReservation> Reservations { get; private set; } = Array.Empty<SubtreeReservation>();
        public string? ProjectId { get; private set; }
        public IReadOnlyList<string> OwnedPhysicalNodeIds { get; private set; } = Array.Empty<string>();
        public IReadOnlyList<string> OwnedExternalNodeIds { get; private set; } = Array.Empty<string>();
        public Dictionary<PlanningGridRowId, PlanningGridRow> Rows { get; } = new();
        public Dictionary<PlanningGridColumnId, PlanningGridColumn> Columns { get; } = new();
        public static MutableGrid From(ProjectRoutingGrid project) => FromProject(project);
        public static MutableGrid From(PlanningGrid source)
        {
            var result = new MutableGrid(source.Id);
            result.RowOrder.AddRange(source.Rows.OrderBy(item => item.LogicalOrder).Select(item => item.Id));
            result.ColumnOrder.AddRange(source.Columns.OrderBy(item => item.LogicalOrder).Select(item => item.Id));
            foreach (var row in source.Rows) result.Rows[row.Id] = row;
            foreach (var column in source.Columns) result.Columns[column.Id] = column;
            foreach (var cell in source.Cells) result.Cells[cell.Key] = cell.Value;
            return result;
        }
        private static MutableGrid FromProject(ProjectRoutingGrid project)
        {
            var result = From(project.Grid);
            result.Reservations = project.SubtreeReservations;
            result.ProjectId = project.ProjectId;
            result.OwnedPhysicalNodeIds = project.OwnedPhysicalNodeIds;
            result.OwnedExternalNodeIds = project.OwnedExternalNodeIds;
            return result;
        }
        public PlanningGrid ToGrid()
        {
            var rows = RowOrder.Distinct().Select(id => Rows[id]).ToArray();
            var columns = ColumnOrder.Distinct().Select(id => Columns[id]).ToArray();
            return new PlanningGrid(Id, rows, columns, new Dictionary<PlanningGridCellId, PlanningGridCell>(Cells), new GridTransform(Id, new RelativePoint(0, 0)));
        }
        public ProjectRoutingGrid ToProjectGrid(IEnumerable<PlannedPhysicalNode> owned) => new(ProjectId ?? owned.FirstOrDefault()?.ProjectId ?? Id.Value.Substring("project:".Length), ToGrid(), Reservations, null,
            OwnedPhysicalNodeIds.Count == 0 ? owned.Select(item => item.PhysicalNodeId).ToArray() : OwnedPhysicalNodeIds,
            OwnedExternalNodeIds.Count == 0 ? owned.Where(item => item.IsExternal).Select(item => item.PhysicalNodeId).ToArray() : OwnedExternalNodeIds);
    }

    private sealed record RouteLegalityResult(bool IsSupported, string? Message);
}

internal sealed record AbstractRoutePlanningResult(
    IReadOnlyList<ProjectRoutingGrid> ProjectGrids,
    DiagramRoutingGrid DiagramGrid,
    IReadOnlyList<PlannedGridRoute> Routes,
    IReadOnlyList<DestinationApproachReservation> DestinationApproaches,
    IReadOnlyList<PlannedStraightRun> StraightRuns,
    IReadOnlyList<TurnDemand> TurnDemands,
    IReadOnlyList<NodeEndpointDemand> EndpointDemands,
    IReadOnlyList<ArchitecturePlanningDiagnostic> Diagnostics);
