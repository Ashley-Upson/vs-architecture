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
    private readonly ArchitectureV6OccupancyAuthority occupancy;
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
        occupancy = new ArchitectureV6OccupancyAuthority(nodes, placements, projectGrids.Select(item => item.Grid).Append(diagramGrid.Grid));
        diagnostics.AddRange(occupancy.Diagnostics);
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
            var placement = placements[node.PhysicalNodeId];
            var grid = grids[gridId];
            var approachRow = ExteriorRowAbove(grid, placement);
            var cell = UseCell(gridId, approachRow,
                ColumnForOwnerRole(gridId, PlanningGridTrackRole.DestinationApproach, node.PhysicalNodeId,
                    placement.AnchorCellId.ColumnId), CellOccupancy.Empty);
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
            var sourceBoundary = UseCell(sourceGrid, ExteriorRowBelow(grids[sourceGrid], placements[source.PhysicalNodeId]),
                ColumnForRole(sourceGrid, PlanningGridTrackRole.ProjectBoundaryTransition, ColumnId(source)), CellOccupancy.Empty);
            var diagramId = new PlanningGridId("diagram");
            var diagramBoundary = UseCell(diagramId, grids[diagramId].RowOrder[0], grids[diagramId].ColumnOrder[0], CellOccupancy.Empty);
            var destinationBoundary = UseCell(destinationGrid, ExteriorRowAbove(grids[destinationGrid], placements[destination.PhysicalNodeId]),
                ColumnForRole(destinationGrid, PlanningGridTrackRole.ProjectBoundaryTransition, ColumnId(destination)), CellOccupancy.Empty);
            transitions.Add(new GridTransition(sourceGrid, sourceBoundary, diagramId, diagramBoundary, "project-to-diagram", link.SemanticLinkId,
                "exit", link.SourceProjectId, null, true, request.ProjectPlacement.ShowProjectContainers));
            transitions.Add(new GridTransition(diagramId, diagramBoundary, destinationGrid, destinationBoundary, "diagram-to-project", link.SemanticLinkId,
                "entry", null, link.DestinationProjectId, true, request.ProjectPlacement.ShowProjectContainers));
            var sourceMutable = grids[sourceGrid];
            var destinationMutable = grids[destinationGrid];
            var sourcePath = new List<PlanningGridCellId> { placements[source.PhysicalNodeId].AnchorCellId };
            var sourceRow = sourceMutable.RowOrder.IndexOf(sourcePath[0].RowId);
            var sourceBoundaryRow = sourceMutable.RowOrder.IndexOf(sourceBoundary.RowId);
            var sourceColumn = sourceMutable.ColumnOrder.IndexOf(sourcePath[0].ColumnId);
            var sourceBoundaryColumn = sourceMutable.ColumnOrder.IndexOf(sourceBoundary.ColumnId);
            var sourcePathValid = sourceRow >= 0 && sourceBoundaryRow >= 0 && sourceColumn >= 0 && sourceBoundaryColumn >= 0 &&
                AppendVertical(sourcePath, sourceGrid, sourceMutable, sourceRow, sourceBoundaryRow, sourceColumn, link) &&
                AppendHorizontal(sourcePath, sourceGrid, sourceMutable, sourceBoundaryRow, sourceColumn, sourceBoundaryColumn, link);

            var destinationPath = new List<PlanningGridCellId> { destinationBoundary };
            var destinationRow = destinationMutable.RowOrder.IndexOf(destinationPath[0].RowId);
            var destinationAnchorRow = destinationMutable.RowOrder.IndexOf(placements[destination.PhysicalNodeId].AnchorCellId.RowId);
            var destinationColumn = destinationMutable.ColumnOrder.IndexOf(destinationPath[0].ColumnId);
            var destinationAnchorColumn = destinationMutable.ColumnOrder.IndexOf(placements[destination.PhysicalNodeId].AnchorCellId.ColumnId);
            var destinationPathValid = destinationRow >= 0 && destinationAnchorRow > destinationRow && destinationColumn >= 0 && destinationAnchorColumn >= 0 &&
                AppendHorizontal(destinationPath, destinationGrid, destinationMutable, destinationRow, destinationColumn, destinationAnchorColumn, link) &&
                AppendVertical(destinationPath, destinationGrid, destinationMutable, destinationRow, destinationAnchorRow - 1, destinationAnchorColumn, link);
            if (destinationPathValid) destinationPath.Add(placements[destination.PhysicalNodeId].AnchorCellId);
            if (!sourcePathValid || !destinationPathValid)
            {
                steps.Clear();
            }
            else
            {
                steps.AddRange(BuildStepsFromPath(sourcePath, topology, grids[sourceGrid]));
                steps[steps.Count - 1] = steps[steps.Count - 1] with { Role = RouteStepRole.ProjectExit };
                steps.Add(Step(diagramId, diagramBoundary, GridSide.Top, GridSide.Bottom, RouteStepRole.DiagramGridPassage, steps.Count, topology));
                var destinationSteps = BuildStepsFromPath(destinationPath, topology, grids[destinationGrid]).ToList();
                destinationSteps[0] = destinationSteps[0] with { Role = RouteStepRole.ProjectEntry };
                steps.AddRange(destinationSteps.Select((step, index) => step with { Order = steps.Count + index }));
            }
        }
        else
        {
            var path = FindOrthogonalPath(grids[sourceGrid], placements[source.PhysicalNodeId].AnchorCellId,
                placements[destination.PhysicalNodeId].AnchorCellId, link);
            if (path.Count == 0)
            {
                diagnostics.Add(new ArchitecturePlanningDiagnostic("UnsupportedAbstractRoute", "No contiguous orthogonal path exists in the authoritative project grid.", PlanningDiagnosticSubject.PhysicalLink, link.PhysicalLinkId));
                steps.Clear();
            }
            else
                steps.AddRange(BuildStepsFromPath(path, topology, grids[sourceGrid]));
        }

        var completion = steps.Count == 0
            ? new CompletionResult(steps, new PlannedRouteCompletionEvidence("grid-search", null, null, null, null,
                Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), "none", false,
                "No contiguous orthogonal path exists in the authoritative project grid."))
            : CompletionResult.Unchanged(steps, "authoritative-grid-path", "The route was constructed from a contiguous authoritative cell path.");
        steps = completion.Steps.ToList();
        steps = steps.Select((step, index) => step with { Order = index }).ToList();
        if (completion.Evidence.OriginalGapType == "alternate-column" &&
            completion.Evidence.SelectedTransitionColumn is { } selectedColumn)
            destinationEndpoint = destinationEndpoint with { PreferredTrack = new PlanningGridColumnId(selectedColumn) };

        var legality = ValidateSteps(steps, link, topology);
        var supported = legality.IsSupported && completion.Evidence.IsValid;
        var unsupportedReason = supported ? null : completion.Evidence.ValidationMessage ?? legality.Message;
        if (!supported)
            diagnostics.Add(new ArchitecturePlanningDiagnostic("UnsupportedAbstractRoute", unsupportedReason!, PlanningDiagnosticSubject.PhysicalLink, link.PhysicalLinkId));
        return new PlannedGridRoute(link.PhysicalLinkId, sourceEndpoint, steps, transitions.Where(item => item.SemanticLinkId == link.SemanticLinkId).ToArray(),
            destinationEndpoint, topology, link.SourceProjectId, link.DestinationProjectId,
            $"{topology}: deterministic topology-owned route", approach.ReservationId, supported, unsupportedReason,
            completion.Evidence with { IsValid = supported && completion.Evidence.IsValid, ValidationMessage = supported ? completion.Evidence.ValidationMessage : legality.Message });
    }

    private IReadOnlyList<PlanningGridCellId> FindOrthogonalPath(
        MutableGrid grid,
        PlanningGridCellId source,
        PlanningGridCellId destination,
        PlannedPhysicalLink link,
        bool destinationRequiresTopEntry = true)
    {
        if (source == destination) return new[] { source };
        var queue = new SortedSet<PathCandidate>(Comparer<PathCandidate>.Create((left, right) =>
        {
            var score = left.Score.CompareTo(right.Score);
            return score != 0 ? score : left.Sequence.CompareTo(right.Sequence);
        }));
        var previous = new Dictionary<PlanningGridCellId, PlanningGridCellId?>();
        var distances = new Dictionary<PlanningGridCellId, int> { [source] = 0 };
        var sequence = 0L;
        queue.Add(new PathCandidate(source, 0, Manhattan(grid, source, destination), sequence++));
        previous[source] = null;
        while (queue.Count > 0)
        {
            var candidate = queue.Min!;
            queue.Remove(candidate);
            var current = candidate.Cell;
            if (!distances.TryGetValue(current, out var currentDistance) || currentDistance != candidate.Distance) continue;
            foreach (var next in Neighbours(grid, current, source, destination, destinationRequiresTopEntry))
            {
                if (!CanTraversePathCell(grid, next, source, destination)) continue;
                var distance = currentDistance + 1;
                if (distances.TryGetValue(next, out var existingDistance) && existingDistance <= distance) continue;
                distances[next] = distance;
                previous[next] = current;
                if (next == destination)
                {
                    var path = new List<PlanningGridCellId>();
                    PlanningGridCellId? cursor = next;
                    while (cursor.HasValue)
                    {
                        path.Add(cursor.Value);
                        cursor = previous[cursor.Value];
                    }
                    path.Reverse();
                    return path;
                }
                queue.Add(new PathCandidate(next, distance, distance + Manhattan(grid, next, destination), sequence++));
            }
        }
        diagnostics.Add(new ArchitecturePlanningDiagnostic("NoContiguousOrthogonalPath", "The authoritative grid has no contiguous orthogonal path for the relationship.", PlanningDiagnosticSubject.PhysicalLink, link.PhysicalLinkId));
        return Array.Empty<PlanningGridCellId>();
    }

    private static int Manhattan(MutableGrid grid, PlanningGridCellId left, PlanningGridCellId right) =>
        Math.Abs(grid.RowOrder.IndexOf(left.RowId) - grid.RowOrder.IndexOf(right.RowId)) +
        Math.Abs(grid.ColumnOrder.IndexOf(left.ColumnId) - grid.ColumnOrder.IndexOf(right.ColumnId));

    private IEnumerable<PlanningGridCellId> Neighbours(
        MutableGrid grid,
        PlanningGridCellId current,
        PlanningGridCellId source,
        PlanningGridCellId destination,
        bool destinationRequiresTopEntry)
    {
        var row = grid.RowOrder.IndexOf(current.RowId);
        var column = grid.ColumnOrder.IndexOf(current.ColumnId);
        var candidates = new List<(int row, int column)>();
        if (current == source)
            candidates.Add((row + 1, column));
        else
        {
            candidates.Add((row + 1, column));
            candidates.Add((row, column - 1));
            candidates.Add((row, column + 1));
            candidates.Add((row - 1, column));
        }

        foreach (var candidate in candidates)
        {
            if (candidate.row < 0 || candidate.row >= grid.RowOrder.Count || candidate.column < 0 || candidate.column >= grid.ColumnOrder.Count)
                continue;
            var cell = new PlanningGridCellId(grid.Id, grid.RowOrder[candidate.row], grid.ColumnOrder[candidate.column]);
            if (cell == destination)
            {
                var destinationRow = grid.RowOrder.IndexOf(destination.RowId);
                var destinationColumn = grid.ColumnOrder.IndexOf(destination.ColumnId);
                var topEntry = column == destinationColumn && row == destinationRow - 1;
                var horizontalEntry = candidate.row == destinationRow && candidate.column != destinationColumn;
                if (destinationRequiresTopEntry ? !topEntry : !topEntry && !horizontalEntry) continue;
            }
            yield return cell;
        }
    }

    private bool CanTraversePathCell(MutableGrid grid, PlanningGridCellId cell, PlanningGridCellId source, PlanningGridCellId destination)
    {
        if (cell == source || cell == destination) return true;
        if (IsOccupiedFootprint(grid, cell.RowId, cell.ColumnId)) return false;
        return !grid.Cells.TryGetValue(cell, out var existing) || (existing.Capabilities & CellCapability.RoutingAllowed) != 0;
    }

    private IReadOnlyList<PlannedGridRouteStep> BuildStepsFromPath(
        IReadOnlyList<PlanningGridCellId> path,
        RouteTopologyFamily topology,
        MutableGrid grid)
    {
        var result = new List<PlannedGridRouteStep>(path.Count);
        for (var index = 0; index < path.Count; index++)
        {
            var previous = index == 0 ? (PlanningGridCellId?)null : path[index - 1];
            var next = index == path.Count - 1 ? (PlanningGridCellId?)null : path[index + 1];
            var entry = previous is null ? GridSide.Top : EntrySide(previous.Value, path[index], grid);
            var exit = next is null ? GridSide.Bottom : ExitSide(path[index], next.Value, grid);
            var role = index == 0 ? RouteStepRole.SourceExit : index == path.Count - 1 ? RouteStepRole.DestinationEntry :
                IsHorizontal(entry, exit) ? RouteStepRole.HorizontalPassThrough :
                entry is GridSide.Top or GridSide.Bottom && exit is GridSide.Top or GridSide.Bottom
                    ? RouteStepRole.VerticalPassThrough : RouteStepRole.Turn;
            result.Add(Step(grid.Id, path[index], entry, exit, role, index, topology));
        }
        return result;
    }

    private RouteLegalityResult ValidateSteps(IReadOnlyList<PlannedGridRouteStep> steps, PlannedPhysicalLink link, RouteTopologyFamily topology)
    {
        if (steps.Count == 0) return new RouteLegalityResult(false, "No route steps were produced.");
        for (var index = 1; index < steps.Count; index++)
            if (steps[index - 1].GridId == steps[index].GridId && !AreAdjacent(steps[index - 1].CellId, steps[index].CellId))
                return new RouteLegalityResult(false, Trace(link, topology, steps, steps[index], "non-contiguous cell transition"));
        for (var index = 2; index < steps.Count; index++)
        {
            if (steps[index - 2].GridId != steps[index - 1].GridId || steps[index - 1].GridId != steps[index].GridId)
                continue;
            if (!grids.TryGetValue(steps[index - 1].GridId, out var grid))
                continue;
            var first = Delta(steps[index - 2].CellId, steps[index - 1].CellId, grid);
            var second = Delta(steps[index - 1].CellId, steps[index].CellId, grid);
            if (first.Row == -second.Row && first.Column == -second.Column && (first.Row != 0 || first.Column != 0))
                return new RouteLegalityResult(false, Trace(link, topology, steps, steps[index], "immediate topology reversal"));
        }
        foreach (var step in steps)
        {
            if (!grids.TryGetValue(step.GridId, out var grid) && step.GridId.Value != "diagram")
                return new RouteLegalityResult(false, Trace(link, topology, steps, step, "missing grid"));
            if (step.EntrySide == step.ExitSide)
                return new RouteLegalityResult(false, Trace(link, topology, steps, step, "entry and exit sides are identical"));
            if (grid is not null)
            {
                var resolution = occupancy.Resolve(step.CellId);
                if (resolution.Status is ArchitectureV6OccupancyStatus.Ambiguous or ArchitectureV6OccupancyStatus.Inconsistent)
                    return new RouteLegalityResult(false, Trace(link, topology, steps, step,
                        resolution.Message ?? "invalid node footprint ownership"));
                if (resolution.IsOccupied && !occupancy.IsExactEndpointCell(step.CellId, step, link))
                    return new RouteLegalityResult(false, Trace(link, topology, steps, step,
                        $"occupied node footprint '{resolution.PhysicalNodeIds.SingleOrDefault() ?? "unknown"}'"));
            }
        }
        if (steps[0].Role != RouteStepRole.SourceExit || steps[steps.Count - 1].Role != RouteStepRole.DestinationEntry)
            return new RouteLegalityResult(false, Trace(link, topology, steps, null, "invalid route terminals"));
        return new RouteLegalityResult(true, null);
    }

    private static (int Row, int Column) Delta(PlanningGridCellId from, PlanningGridCellId to, MutableGrid grid) =>
        (grid.RowOrder.IndexOf(to.RowId) - grid.RowOrder.IndexOf(from.RowId),
         grid.ColumnOrder.IndexOf(to.ColumnId) - grid.ColumnOrder.IndexOf(from.ColumnId));

    private CompletionResult CompleteTurnCorridor(
        PlanningGridId gridId,
        IReadOnlyList<PlannedGridRouteStep> original,
        RouteTopologyFamily topology,
        PlannedPhysicalLink link)
    {
        // The explicit destination anchor is an endpoint-owned occupied cell,
        // not another ordinary corridor target. Once an exterior approach is
        // already present immediately before it, preserve that endpoint-local
        // handoff and leave ordinary completion unchanged.
        if (original.Count >= 2 &&
            original[original.Count - 1].Role == RouteStepRole.DestinationEntry &&
            original[original.Count - 2].Role == RouteStepRole.DestinationEntry &&
            original[original.Count - 1].CellId != original[original.Count - 2].CellId)
            return CompletionResult.Unchanged(original, "endpoint-anchor", "The explicit destination anchor already follows an exterior approach.");

        var turnIndex = original.ToList().FindIndex(step => step.Role == RouteStepRole.Turn);
        if (turnIndex < 0)
            return CompletionResult.Unchanged(original, "none", "Route has no turn requiring corridor completion.");

        var turn = original[turnIndex];
        var followingIndex = Enumerable.Range(turnIndex + 1, original.Count - turnIndex - 1)
            .FirstOrDefault(index => original[index].Role == RouteStepRole.VerticalPassThrough);
        if (followingIndex == 0)
            return CompletionResult.Unchanged(original, "none", "Route has no destination-side vertical corridor.");

        var following = original[followingIndex];
        if (turn.GridId != following.GridId || turn.CellId.RowId == following.CellId.RowId && turn.CellId.ColumnId == following.CellId.ColumnId)
            return CompletionResult.Unchanged(original, "none", "The existing turn and vertical corridor require no completion.");

        var grid = grids[gridId];
        var turnRow = grid.RowOrder.ToList().IndexOf(turn.CellId.RowId);
        var turnColumn = grid.ColumnOrder.ToList().IndexOf(turn.CellId.ColumnId);
        var targetRow = grid.RowOrder.ToList().IndexOf(following.CellId.RowId);
        var targetColumn = grid.ColumnOrder.ToList().IndexOf(following.CellId.ColumnId);
        var destinationIndex = Enumerable.Range(followingIndex + 1, original.Count - followingIndex - 1)
            .LastOrDefault(index => original[index].Role == RouteStepRole.DestinationEntry);
        if (destinationIndex == 0 || turnRow < 0 || targetRow < 0 || turnColumn < 0 || targetColumn < 0)
            return CompletionResult.Unchanged(original, "none", "The route does not expose complete structural corridor coordinates.");

        var destination = original[destinationIndex];
        var destinationRow = grid.RowOrder.ToList().IndexOf(destination.CellId.RowId);
        var destinationColumn = grid.ColumnOrder.ToList().IndexOf(destination.CellId.ColumnId);
        if (destinationRow < 0 || destinationColumn < 0)
            return CompletionResult.Unsupported(original, "two-axis", turn, following, "The destination approach does not expose a structural row and column.");

        var originalTargetColumn = grid.ColumnOrder.ToList().IndexOf(following.CellId.ColumnId);
        if (turnColumn == originalTargetColumn)
        {
            var verticalPath = new List<PlanningGridCellId> { turn.CellId };
            if (!CanTraverseVertical(grid, turnRow, targetRow, turnColumn))
            {
                var alternate = SelectAlternateColumn(grid, turnRow, targetRow, destinationRow, turnColumn, link);
                if (alternate.ColumnIndex < 0)
                    return CompletionResult.Unsupported(original, "same-column", turn, following,
                        "The same-column corridor is blocked and no authorised existing alternate column can complete the route.",
                        alternate.Candidates, alternate.Rejections);

                var alternatePath = new List<PlanningGridCellId> { turn.CellId };
                if (!AppendVertical(alternatePath, gridId, grid, turnRow, alternate.TransitionRowIndex, turnColumn, link) ||
                    !AppendHorizontal(alternatePath, gridId, grid, alternate.TransitionRowIndex, turnColumn, alternate.ColumnIndex, link) ||
                    !AppendVertical(alternatePath, gridId, grid, alternate.TransitionRowIndex, targetRow, alternate.ColumnIndex, link))
                    return CompletionResult.Unsupported(original, "alternate-column", turn, following,
                        "The selected alternate column became unavailable while materialising the authorised traversal.",
                        alternate.Candidates, alternate.Rejections);

                var alternateGenerated = DeriveTraversalSteps(alternatePath, turn, following, topology, grid, false);
                var approachCell = UseCell(gridId, destination.CellId.RowId, grid.ColumnOrder[alternate.ColumnIndex], CellOccupancy.Empty);
                var completedAlternate = original.Take(turnIndex).ToList();
                completedAlternate.AddRange(alternateGenerated);
                completedAlternate.Add(destination with { CellId = approachCell });
                var alternateOriginalCells = new HashSet<PlanningGridCellId>(original.Select(step => step.CellId));
                var alternateInserted = alternatePath.Where(cell => !alternateOriginalCells.Contains(cell)).Select(cell => cell.ToString()).Append(approachCell.ToString()).ToArray();
                var alternateInsertedTurns = alternateGenerated.Where(step => step.Role == RouteStepRole.Turn && step.CellId != turn.CellId)
                    .Select(step => step.CellId.ToString()).ToArray();
                diagnostics.Add(new ArchitecturePlanningDiagnostic(
                    "AlternateColumnCompletion",
                    $"Selected existing transition row {grid.RowOrder[alternate.TransitionRowIndex].Value} and column {grid.ColumnOrder[alternate.ColumnIndex].Value}; " +
                    $"candidates={string.Join(",", alternate.Candidates)}; rejected={string.Join(";", alternate.Rejections)}; " +
                    $"cells={string.Join("|", alternatePath.Select(cell => cell.ToString()))}|{approachCell}",
                    PlanningDiagnosticSubject.PhysicalLink,
                    link.PhysicalLinkId));
                return new CompletionResult(completedAlternate, new PlannedRouteCompletionEvidence(
                    "alternate-column", turn.CellId.ToString(), following.CellId.ToString(),
                    grid.RowOrder[alternate.TransitionRowIndex].Value, grid.ColumnOrder[alternate.ColumnIndex].Value,
                    alternateInserted, alternateInsertedTurns.Select(cell => $"run:{cell}").ToArray(), alternateInsertedTurns,
                    $"{topology}:existing-transition-row-alternate-column", true,
                    "The blocked destination corridor was rebound to the nearest authorised existing column.",
                    alternate.Candidates, alternate.Rejections));
            }

            AppendVertical(verticalPath, gridId, grid, turnRow, targetRow, turnColumn, link);

            var completedSameColumn = original.Take(turnIndex).ToList();
            completedSameColumn.AddRange(DeriveTraversalSteps(verticalPath, turn, following, topology, grid, false));
            completedSameColumn.AddRange(original.Skip(followingIndex + 1));
            var sameOriginalCells = new HashSet<PlanningGridCellId>(original.Select(step => step.CellId));
            var sameInserted = verticalPath.Where(cell => !sameOriginalCells.Contains(cell)).Select(cell => cell.ToString()).ToArray();
            return new CompletionResult(completedSameColumn, new PlannedRouteCompletionEvidence(
                "same-column", turn.CellId.ToString(), following.CellId.ToString(), following.CellId.RowId.Value,
                following.CellId.ColumnId.Value, sameInserted, Array.Empty<string>(), Array.Empty<string>(),
                $"{topology}:ordered-vertical-traversal", true, null));
        }

        // The destination approach reservation is the authoritative final
        // vertical corridor. The original anchor-column step is the incomplete
        // predecessor that this traversal replaces.
        targetColumn = destinationColumn;

        var transitionRow = SelectTransitionRow(grid, turnRow, targetRow, destinationRow, turnColumn, targetColumn, link);
        if (transitionRow < 0)
            return CompletionResult.Unsupported(original, turnColumn == targetColumn ? "same-column" : "two-axis", turn, following,
                "No authorised existing transition row can connect the source and destination corridors.");

        var transitionRowId = grid.RowOrder[transitionRow];
        var path = new List<PlanningGridCellId> { turn.CellId };
        if (!AppendVertical(path, gridId, grid, turnRow, transitionRow, turnColumn, link) ||
            !AppendHorizontal(path, gridId, grid, transitionRow, turnColumn, targetColumn, link) ||
            !AppendVertical(path, gridId, grid, transitionRow, destinationRow, targetColumn, link))
        {
            return CompletionResult.Unsupported(original, turnColumn == targetColumn ? "same-column" : "two-axis", turn, following,
                "The selected traversal contains a non-routable or unrelated occupied cell.");
        }

        var completed = original.Take(turnIndex).ToList();
        var generated = DeriveTraversalSteps(path, turn, destination, topology, grid, true);
        completed.AddRange(generated);
        var originalCells = new HashSet<PlanningGridCellId>(original.Select(step => step.CellId));
        var inserted = path.Where(cell => !originalCells.Contains(cell)).Select(cell => cell.ToString()).ToArray();
        var insertedTurns = generated
            .Where(step => step.Role == RouteStepRole.Turn && step.CellId != turn.CellId)
            .Select(step => step.CellId.ToString()).ToArray();
        var evidence = new PlannedRouteCompletionEvidence(
            turnColumn == targetColumn ? "same-column" : "two-axis",
            turn.CellId.ToString(),
            following.CellId.ToString(),
            transitionRowId.Value,
            grid.ColumnOrder[targetColumn].Value,
            inserted,
            insertedTurns.Select(cell => $"run:{cell}").ToArray(),
            insertedTurns,
            $"{topology}:ordered-manhattan-traversal",
            true,
            null);
        return new CompletionResult(completed, evidence);
    }

    private int SelectTransitionRow(MutableGrid grid, int startRow, int targetRow, int destinationRow,
        int startColumn, int targetColumn, PlannedPhysicalLink link)
    {
        var preferred = grid.Rows.Values
            .Where(row => row.Role == PlanningGridTrackRole.InterLayerRouting)
            .Select(row => grid.RowOrder.IndexOf(row.Id))
            .Where(index => index >= 0)
            .OrderBy(index => index == targetRow ? 0 : 1)
            .ThenBy(index => Math.Abs(index - targetRow))
            .ThenBy(index => index)
            .ToArray();
        foreach (var row in preferred
            .Where(row => Math.Abs(row - startRow) >= 2 && Math.Abs(targetColumn - startColumn) >= 2)
            .Concat(preferred)
            .Distinct())
        {
            if (ClearVertical(grid, startRow, row, startColumn, link) &&
                ClearHorizontal(grid, row, startColumn, targetColumn, link) &&
                ClearVertical(grid, row, destinationRow, targetColumn, link))
                return row;
        }
        return -1;
    }

    private bool AppendVertical(List<PlanningGridCellId> path, PlanningGridId gridId, MutableGrid grid,
        int startRow, int endRow, int column, PlannedPhysicalLink link)
    {
        var direction = endRow >= startRow ? 1 : -1;
        for (var row = startRow + direction; row != endRow + direction; row += direction)
        {
            if (!TryUseAuthorisedCell(gridId, grid, grid.RowOrder[row], grid.ColumnOrder[column], link, out var cell)) return false;
            path.Add(cell);
        }
        return true;
    }

    private bool AppendHorizontal(List<PlanningGridCellId> path, PlanningGridId gridId, MutableGrid grid,
        int row, int startColumn, int endColumn, PlannedPhysicalLink link)
    {
        var direction = endColumn >= startColumn ? 1 : -1;
        for (var column = startColumn + direction; column != endColumn + direction; column += direction)
        {
            if (!TryUseAuthorisedCell(gridId, grid, grid.RowOrder[row], grid.ColumnOrder[column], link, out var cell)) return false;
            path.Add(cell);
        }
        return true;
    }

    private bool ClearVertical(MutableGrid grid, int startRow, int endRow, int column, PlannedPhysicalLink link)
    {
        var direction = endRow >= startRow ? 1 : -1;
        for (var row = startRow; row != endRow + direction; row += direction)
            if (IsOccupiedFootprint(grid, grid.RowOrder[row], grid.ColumnOrder[column])) return false;
        return true;
    }

    private bool ClearHorizontal(MutableGrid grid, int row, int startColumn, int endColumn, PlannedPhysicalLink link)
    {
        var direction = endColumn >= startColumn ? 1 : -1;
        for (var column = startColumn; column != endColumn + direction; column += direction)
            if (IsOccupiedFootprint(grid, grid.RowOrder[row], grid.ColumnOrder[column])) return false;
        return true;
    }

    private bool CanTraverseVertical(MutableGrid grid, int startRow, int endRow, int column)
    {
        var direction = endRow >= startRow ? 1 : -1;
        for (var row = startRow; row != endRow + direction; row += direction)
            if (!CanUseAuthorisedCell(grid, grid.RowOrder[row], grid.ColumnOrder[column])) return false;
        return true;
    }

    private bool CanTraverseHorizontal(MutableGrid grid, int row, int startColumn, int endColumn)
    {
        var direction = endColumn >= startColumn ? 1 : -1;
        for (var column = startColumn; column != endColumn + direction; column += direction)
            if (!CanUseAuthorisedCell(grid, grid.RowOrder[row], grid.ColumnOrder[column])) return false;
        return true;
    }

    private bool CanUseAuthorisedCell(MutableGrid grid, PlanningGridRowId rowId, PlanningGridColumnId columnId)
    {
        var cellId = new PlanningGridCellId(grid.Id, rowId, columnId);
        if (!grid.RowOrder.Contains(rowId) || !grid.ColumnOrder.Contains(columnId)) return false;
        if (occupancy.Resolve(cellId).IsOccupied) return false;
        return !grid.Cells.TryGetValue(cellId, out var existing) || (existing.Capabilities & CellCapability.RoutingAllowed) != 0;
    }

    private AlternateColumnSelection SelectAlternateColumn(
        MutableGrid grid,
        int startRow,
        int targetRow,
        int destinationRow,
        int originalColumn,
        PlannedPhysicalLink link)
    {
        var low = Math.Min(startRow, targetRow);
        var high = Math.Max(startRow, targetRow);
        var transitionRow = grid.Rows.Values
            .Where(row => row.Role == PlanningGridTrackRole.InterLayerRouting)
            .Select(row => grid.RowOrder.IndexOf(row.Id))
            .Where(index => index >= low && index <= high)
            .OrderBy(index => Math.Abs(index - destinationRow))
            .ThenBy(index => index)
            .DefaultIfEmpty(-1)
            .First();
        if (transitionRow < 0)
            return new AlternateColumnSelection(-1, -1, Array.Empty<string>(), new[] { "no-existing-inter-layer-transition-row" });

        var originalLogicalOrder = grid.Columns[grid.ColumnOrder[originalColumn]].LogicalOrder;
        var ordered = grid.Columns.Values.OrderBy(column => column.LogicalOrder).ToArray();
        var candidates = ordered.Where(column => column.LogicalOrder > originalLogicalOrder)
            .Concat(ordered.Where(column => column.LogicalOrder < originalLogicalOrder).Reverse())
            .ToArray();
        var considered = new List<string>();
        var rejections = new List<string>();
        foreach (var column in candidates)
        {
            if (column.Id == grid.ColumnOrder[originalColumn]) continue;
            considered.Add(column.Id.Value);
            var index = grid.ColumnOrder.IndexOf(column.Id);
            var reason = !CanTraverseVertical(grid, startRow, transitionRow, originalColumn) ? "source-to-transition blocked" :
                !CanTraverseHorizontal(grid, transitionRow, originalColumn, index) ? "transition row blocked" :
                !CanTraverseVertical(grid, transitionRow, targetRow, index) ? "destination corridor blocked" : null;
            if (reason is null) return new AlternateColumnSelection(index, transitionRow, considered, rejections);
            rejections.Add($"{column.Id.Value}={reason}");
        }
        return new AlternateColumnSelection(-1, transitionRow, considered, rejections);
    }

    private bool TryUseAuthorisedCell(PlanningGridId gridId, MutableGrid grid, PlanningGridRowId rowId,
        PlanningGridColumnId columnId, PlannedPhysicalLink link, out PlanningGridCellId cell)
    {
        cell = new PlanningGridCellId(gridId, rowId, columnId);
        if (!grid.RowOrder.Contains(rowId) || !grid.ColumnOrder.Contains(columnId)) return false;
        if (IsOccupiedFootprint(grid, rowId, columnId)) return false;
        if (grid.Cells.TryGetValue(cell, out var existing) && (existing.Capabilities & CellCapability.RoutingAllowed) == 0) return false;
        UseCell(gridId, rowId, columnId, CellOccupancy.Empty);
        return true;
    }

    private IReadOnlyList<PlannedGridRouteStep> DeriveTraversalSteps(
        IReadOnlyList<PlanningGridCellId> path,
        PlannedGridRouteStep originalTurn,
        PlannedGridRouteStep originalDestination,
        RouteTopologyFamily topology,
        MutableGrid grid,
        bool destinationEntry)
    {
        var result = new List<PlannedGridRouteStep>();
        for (var index = 0; index < path.Count; index++)
        {
            var previous = index == 0 ? (PlanningGridCellId?)null : path[index - 1];
            var next = index == path.Count - 1 ? (PlanningGridCellId?)null : path[index + 1];
            var entry = index == 0 ? originalTurn.EntrySide : EntrySide(previous!.Value, path[index], grid);
            var exit = index == path.Count - 1
                ? destinationEntry
                    ? entry is GridSide.Top ? GridSide.Bottom : entry is GridSide.Bottom ? GridSide.Top : originalDestination.ExitSide
                    : entry is GridSide.Top ? GridSide.Bottom : entry is GridSide.Bottom ? GridSide.Top : originalDestination.ExitSide
                : ExitSide(path[index], next!.Value, grid);
            var role = index == 0 ? RouteStepRole.Turn : index == path.Count - 1 && destinationEntry ? RouteStepRole.DestinationEntry :
                IsHorizontal(entry, exit) ? RouteStepRole.HorizontalPassThrough :
                entry is GridSide.Top or GridSide.Bottom && exit is GridSide.Top or GridSide.Bottom
                    ? RouteStepRole.VerticalPassThrough
                    : RouteStepRole.Turn;
            result.Add(new PlannedGridRouteStep(path[index].GridId, path[index], entry, exit, role, index,
                topology.ToString(), "ordered-corridor-completion"));
        }
        return result;
    }

    private static bool IsHorizontal(GridSide entry, GridSide exit) =>
        entry is GridSide.Left or GridSide.Right && exit is GridSide.Left or GridSide.Right;

    private static GridSide EntrySide(PlanningGridCellId previous, PlanningGridCellId current, MutableGrid grid)
    {
        var previousRow = grid.RowOrder.IndexOf(previous.RowId);
        var currentRow = grid.RowOrder.IndexOf(current.RowId);
        var previousColumn = grid.ColumnOrder.IndexOf(previous.ColumnId);
        var currentColumn = grid.ColumnOrder.IndexOf(current.ColumnId);
        if (currentRow > previousRow) return GridSide.Top;
        if (currentRow < previousRow) return GridSide.Bottom;
        return currentColumn > previousColumn ? GridSide.Left : GridSide.Right;
    }

    private static GridSide ExitSide(PlanningGridCellId current, PlanningGridCellId next, MutableGrid grid)
    {
        var currentRow = grid.RowOrder.IndexOf(current.RowId);
        var nextRow = grid.RowOrder.IndexOf(next.RowId);
        var currentColumn = grid.ColumnOrder.IndexOf(current.ColumnId);
        var nextColumn = grid.ColumnOrder.IndexOf(next.ColumnId);
        if (nextRow > currentRow) return GridSide.Bottom;
        if (nextRow < currentRow) return GridSide.Top;
        return nextColumn > currentColumn ? GridSide.Right : GridSide.Left;
    }

    private sealed record CompletionResult(
        IReadOnlyList<PlannedGridRouteStep> Steps,
        PlannedRouteCompletionEvidence Evidence)
    {
        public static CompletionResult Unchanged(IReadOnlyList<PlannedGridRouteStep> steps, string gapType, string message) =>
            new(steps.ToArray(), new PlannedRouteCompletionEvidence(
                gapType, null, null, null, null, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(),
                "none", true, message));

        public static CompletionResult Unsupported(IReadOnlyList<PlannedGridRouteStep> steps, string gapType,
            PlannedGridRouteStep turn, PlannedGridRouteStep target, string message,
            IReadOnlyList<string>? candidates = null, IReadOnlyList<string>? rejections = null) =>
            new(steps.ToArray(), new PlannedRouteCompletionEvidence(
                gapType, turn.CellId.ToString(), target.CellId.ToString(), target.CellId.RowId.Value,
                target.CellId.ColumnId.Value, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(),
                "ordered-manhattan-traversal", false, message, candidates, rejections));
    }

    private sealed record PathCandidate(PlanningGridCellId Cell, int Distance, int Score, long Sequence);

    private sealed record AlternateColumnSelection(
        int ColumnIndex,
        int TransitionRowIndex,
        IReadOnlyList<string> Candidates,
        IReadOnlyList<string> Rejections);

    private bool IsUnrelatedFootprint(MutableGrid grid, PlanningGridRowId rowId, PlanningGridColumnId columnId, PlannedPhysicalLink link)
    {
        var cellId = new PlanningGridCellId(grid.Id, rowId, columnId);
        var resolution = occupancy.Resolve(cellId);
        return resolution.IsOccupied &&
            resolution.PhysicalNodeIds.All(owner =>
                !string.Equals(owner, link.SourcePhysicalNodeId, StringComparison.Ordinal) &&
                !string.Equals(owner, link.DestinationPhysicalNodeId, StringComparison.Ordinal));
    }

    private bool IsOccupiedFootprint(MutableGrid grid, PlanningGridRowId rowId, PlanningGridColumnId columnId)
    {
        var cellId = new PlanningGridCellId(grid.Id, rowId, columnId);
        return occupancy.Resolve(cellId).IsOccupied;
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
            void Flush()
            {
                if (axis is null || current.Count == 0) return;
                result.Add(new PlannedStraightRun(route.PhysicalLinkId, current[0].GridId, axis.Value, current.ToArray(),
                    "step-start", "step-end", new LaneId($"provisional:{route.PhysicalLinkId}:{result.Count}")));
                current = new List<PlanningGridCellId>();
            }

            foreach (var step in route.Steps.OrderBy(item => item.Order))
            {
                if (step.Role == RouteStepRole.Turn)
                {
                    var incoming = step.EntrySide is GridSide.Left or GridSide.Right ? RouteAxis.Horizontal : RouteAxis.Vertical;
                    var outgoing = step.ExitSide is GridSide.Left or GridSide.Right ? RouteAxis.Horizontal : RouteAxis.Vertical;
                    if (axis is not null && axis != incoming) Flush();
                    axis = incoming;
                    current.Add(step.CellId);
                    Flush();
                    axis = outgoing;
                    current.Add(step.CellId);
                    continue;
                }

                var stepAxis = IsVertical(step) ? RouteAxis.Vertical : RouteAxis.Horizontal;
                if (axis is not null && axis != stepAxis) Flush();
                axis = stepAxis;
                current.Add(step.CellId);
            }
            Flush();
        }
        return result;
    }

    private bool AreAdjacent(PlanningGridCellId first, PlanningGridCellId second)
    {
        if (first.GridId != second.GridId) return false;
        if (!grids.TryGetValue(first.GridId, out var grid)) return false;
        var sameRow = first.RowId == second.RowId;
        var sameColumn = first.ColumnId == second.ColumnId;
        if (sameRow)
        {
            var firstIndex = grid.ColumnOrder.IndexOf(first.ColumnId);
            var secondIndex = grid.ColumnOrder.IndexOf(second.ColumnId);
            return firstIndex >= 0 && secondIndex >= 0 && Math.Abs(firstIndex - secondIndex) == 1;
        }
        if (sameColumn)
        {
            var firstIndex = grid.RowOrder.IndexOf(first.RowId);
            var secondIndex = grid.RowOrder.IndexOf(second.RowId);
            return firstIndex >= 0 && secondIndex >= 0 && Math.Abs(firstIndex - secondIndex) == 1;
        }
        return false;
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

    private PlanningGridRowId ExteriorRowBelow(MutableGrid grid, PlannedNodePlacement placement)
    {
        var placementRow = grid.Rows[placement.AnchorCellId.RowId];
        var lastFootprintRow = placementRow.LogicalOrder + Math.Max(0, placement.RowSpan - 1);
        return grid.Rows.Values
            .Where(row => row.Role == PlanningGridTrackRole.InterLayerRouting && row.LogicalOrder > lastFootprintRow)
            .OrderBy(row => row.LogicalOrder)
            .FirstOrDefault()?.Id ?? placement.AnchorCellId.RowId;
    }

    private PlanningGridRowId ExteriorRowAbove(MutableGrid grid, PlannedNodePlacement placement)
    {
        var placementRow = grid.Rows[placement.AnchorCellId.RowId];
        var firstFootprintRow = placementRow.LogicalOrder;
        return grid.Rows.Values
            .Where(row => row.Role == PlanningGridTrackRole.InterLayerRouting && row.LogicalOrder < firstFootprintRow)
            .OrderByDescending(row => row.LogicalOrder)
            .FirstOrDefault()?.Id ?? placement.AnchorCellId.RowId;
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
