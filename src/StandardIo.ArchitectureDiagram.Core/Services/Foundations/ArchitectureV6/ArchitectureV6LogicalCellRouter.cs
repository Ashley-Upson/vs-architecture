using System;
using System.Collections.Generic;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core.Models.ArchitectureV6;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.ArchitectureV6;

internal sealed class ArchitectureV6LogicalCellRouter
{
    private readonly IReadOnlyList<PlannedPhysicalNode> nodes;
    private readonly IReadOnlyList<PlannedPhysicalLink> links;
    private readonly IReadOnlyList<PlannedNodePlacement> placements;
    private readonly IReadOnlyList<ProjectRoutingGrid> projects;
    private readonly DiagramRoutingGrid diagram;
    private readonly IReadOnlyList<PhysicalNodePlacementMetadata> metadata;

    public ArchitectureV6LogicalCellRouter(
        IReadOnlyList<PlannedPhysicalNode> nodes,
        IReadOnlyList<PlannedPhysicalLink> links,
        IReadOnlyList<PlannedNodePlacement> placements,
        IReadOnlyList<ProjectRoutingGrid> projects,
        DiagramRoutingGrid diagram,
        IReadOnlyList<PhysicalNodePlacementMetadata> metadata)
    {
        this.nodes = nodes;
        this.links = links;
        this.placements = placements;
        this.projects = projects;
        this.diagram = diagram;
        this.metadata = metadata;
    }

    public ArchitectureV6LogicalRouteResult Build()
    {
        var routes = new List<PlannedGridRoute>();
        var findings = new List<ArchitecturePlanningDiagnostic>();
        foreach (var link in links.OrderBy(item => item.PhysicalLinkId, StringComparer.Ordinal))
        {
            var source = nodes.Single(item => item.PhysicalNodeId == link.SourcePhysicalNodeId);
            var target = nodes.Single(item => item.PhysicalNodeId == link.DestinationPhysicalNodeId);
            var sourcePlacement = placements.Single(item => item.PhysicalNodeId == source.PhysicalNodeId);
            var targetPlacement = placements.Single(item => item.PhysicalNodeId == target.PhysicalNodeId);
            var topology = Classify(source, target, sourcePlacement, targetPlacement);
            var result = source.ProjectId == target.ProjectId
                ? RouteWithinProject(link, source, target, sourcePlacement, targetPlacement, topology)
                : RouteAcrossProjects(link, source, target, sourcePlacement, targetPlacement, topology);
            routes.Add(result.Route);
            if (!result.Route.IsStructurallySupported)
            {
                findings.Add(new ArchitecturePlanningDiagnostic(
                    "V6RoutePlanningFailed",
                    result.Route.UnsupportedReason ?? "No capability-legal route exists on the frozen grid.",
                    PlanningDiagnosticSubject.PhysicalLink,
                    link.PhysicalLinkId));
            }
            findings.AddRange(result.Findings);
        }
        return new ArchitectureV6LogicalRouteResult(routes, findings);
    }

    private RouteBuildResult RouteWithinProject(PlannedPhysicalLink link, PlannedPhysicalNode source, PlannedPhysicalNode target,
        PlannedNodePlacement sourcePlacement, PlannedNodePlacement targetPlacement, RouteTopologyFamily topology)
    {
        var grid = projects.Single(item => item.ProjectId == source.ProjectId).Grid;
        var sourceCell = sourcePlacement.AnchorCellId;
        var targetCell = targetPlacement.AnchorCellId;
        var sourceNode = new NodeEndpoint(source.PhysicalNodeId, GridSide.Bottom, link.PhysicalLinkId + ":source", 0, grid.Id, sourcePlacement.CentreColumnId);
        var targetNode = new NodeEndpoint(target.PhysicalNodeId, GridSide.Top, link.PhysicalLinkId + ":destination", 0, grid.Id, targetPlacement.CentreColumnId);
        var path = FindPath(grid, sourceCell, targetCell, sourcePlacement, targetPlacement, topology);
        return CreateRoute(link, sourceNode, targetNode, topology, path, source.ProjectId, target.ProjectId);
    }

    private RouteBuildResult RouteAcrossProjects(PlannedPhysicalLink link, PlannedPhysicalNode source, PlannedPhysicalNode target,
        PlannedNodePlacement sourcePlacement, PlannedNodePlacement targetPlacement, RouteTopologyFamily topology)
    {
        var sourceGrid = projects.Single(item => item.ProjectId == source.ProjectId).Grid;
        var targetGrid = projects.Single(item => item.ProjectId == target.ProjectId).Grid;
        var sourceBoundary = BoundaryCell(sourceGrid, sourcePlacement, towardRight: true);
        var targetBoundary = BoundaryCell(targetGrid, targetPlacement, towardRight: false);
        var diagramSource = DiagramCell(sourceBoundary, source.ProjectId!, true);
        var diagramTarget = DiagramCell(targetBoundary, target.ProjectId!, false);
        var sourcePath = FindPath(sourceGrid, sourcePlacement.AnchorCellId, sourceBoundary, sourcePlacement, null, topology);
        var diagramPath = FindPath(diagram.Grid, diagramSource, diagramTarget, null, null, topology);
        var targetPath = FindPath(targetGrid, targetBoundary, targetPlacement.AnchorCellId, null, targetPlacement, topology);
        var sourceNode = new NodeEndpoint(source.PhysicalNodeId, GridSide.Bottom, link.PhysicalLinkId + ":source", 0, sourceGrid.Id, sourcePlacement.CentreColumnId);
        var targetNode = new NodeEndpoint(target.PhysicalNodeId, GridSide.Top, link.PhysicalLinkId + ":destination", 0, targetGrid.Id, targetPlacement.CentreColumnId);
        if (sourcePath.Count == 0 || diagramPath.Count == 0 || targetPath.Count == 0)
            return CreateRoute(link, sourceNode, targetNode, topology, Array.Empty<PlanningGridCellId>(), source.ProjectId, target.ProjectId,
                "The frozen project/diagram grids do not contain a complete capability-legal cross-project path.");

        var steps = new List<PlannedGridRouteStep>();
        AddSteps(steps, sourcePath, RouteStepRole.ProjectExit, link.SemanticLinkId + ":source-project");
        AddSteps(steps, diagramPath, RouteStepRole.DiagramGridPassage, link.SemanticLinkId + ":diagram");
        AddSteps(steps, targetPath, RouteStepRole.ProjectEntry, link.SemanticLinkId + ":destination-project");
        var transitions = new[]
        {
            new GridTransition(sourceGrid.Id, sourceBoundary, diagram.Grid.Id, diagramSource,
                "project-exit:" + source.ProjectId, link.SemanticLinkId, "cross-project", source.ProjectId, target.ProjectId),
            new GridTransition(diagram.Grid.Id, diagramTarget, targetGrid.Id, targetBoundary,
                "project-entry:" + target.ProjectId, link.SemanticLinkId, "cross-project", source.ProjectId, target.ProjectId)
        };
        return new RouteBuildResult(new PlannedGridRoute(link.PhysicalLinkId, sourceNode, steps, transitions, targetNode,
            topology, source.ProjectId, target.ProjectId, "capability-grid-cross-project", IsStructurallySupported: true), Array.Empty<ArchitecturePlanningDiagnostic>());
    }

    private static RouteBuildResult CreateRoute(PlannedPhysicalLink link, NodeEndpoint source, NodeEndpoint target,
        RouteTopologyFamily topology, IReadOnlyList<PlanningGridCellId> path, string? sourceProject, string? targetProject,
        string? failure = null)
    {
        if (path.Count == 0)
            return new RouteBuildResult(new PlannedGridRoute(link.PhysicalLinkId, source, Array.Empty<PlannedGridRouteStep>(),
                Array.Empty<GridTransition>(), target, topology, sourceProject, targetProject,
                "capability-grid", IsStructurallySupported: false, UnsupportedReason: failure ?? "No capability-legal route exists."), Array.Empty<ArchitecturePlanningDiagnostic>());
        var steps = path.Select((cell, index) => new PlannedGridRouteStep(cell.GridId, cell,
            index == 0 ? GridSide.Top : SideBetween(path[index - 1], cell),
            index == path.Count - 1 ? GridSide.Bottom : SideBetween(cell, path[index + 1]),
            index == 0 ? RouteStepRole.SourceExit : index == path.Count - 1 ? RouteStepRole.DestinationEntry :
                SideBetween(path[index - 1], cell) != SideBetween(cell, path[index + 1]) ? RouteStepRole.Turn :
                IsHorizontal(path[index - 1], cell) ? RouteStepRole.HorizontalPassThrough : RouteStepRole.VerticalPassThrough,
            index, "capability-grid", link.PhysicalLinkId)).ToArray();
        return new RouteBuildResult(new PlannedGridRoute(link.PhysicalLinkId, source, steps, Array.Empty<GridTransition>(), target,
            topology, sourceProject, targetProject, "capability-grid", IsStructurallySupported: true), Array.Empty<ArchitecturePlanningDiagnostic>());
    }

    private IReadOnlyList<PlanningGridCellId> FindPath(PlanningGrid grid, PlanningGridCellId start, PlanningGridCellId target,
        PlannedNodePlacement? sourcePlacement, PlannedNodePlacement? targetPlacement, RouteTopologyFamily topology)
    {
        if (start.GridId != grid.Id || target.GridId != grid.Id) return Array.Empty<PlanningGridCellId>();
        if (start.Equals(target)) return new[] { start };
        var rows = grid.Rows.OrderBy(item => item.LogicalOrder).ToArray();
        var columns = grid.Columns.OrderBy(item => item.LogicalOrder).ToArray();
        var rowIndex = rows.Select((row, index) => (row.Id, index)).ToDictionary(item => item.Id, item => item.index);
        var columnIndex = columns.Select((column, index) => (column.Id, index)).ToDictionary(item => item.Id, item => item.index);
        var blocked = new HashSet<PlanningGridCellId>(grid.Cells.Values.Where(cell => cell.Occupancy != CellOccupancy.Empty)
            .Select(cell => cell.Id));
        blocked.Remove(start);
        blocked.Remove(target);
        var sourceRow = rowIndex[start.RowId];
        var targetRow = rowIndex[target.RowId];
        var sourceFootprint = sourcePlacement?.Footprint.Select(cell => columnIndex[cell.ColumnId]).ToArray() ?? Array.Empty<int>();
        var sourceMin = sourceFootprint.DefaultIfEmpty(columnIndex[start.ColumnId]).Min();
        var sourceMax = sourceFootprint.DefaultIfEmpty(columnIndex[start.ColumnId]).Max();
        var queue = new Queue<PlanningGridCellId>();
        var previous = new Dictionary<PlanningGridCellId, PlanningGridCellId>();
        var visited = new HashSet<PlanningGridCellId> { start };
        queue.Enqueue(start);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var next in Neighbours(current, rows, columns, rowIndex, columnIndex, topology, sourceRow, targetRow, sourceMin, sourceMax))
            {
                if (!grid.Cells.TryGetValue(next, out var cell) || blocked.Contains(next) || !CanTraverse(cell, current, next, grid, target)) continue;
                if (topology == RouteTopologyFamily.Upward && current.Equals(start) && rowIndex[next.RowId] <= sourceRow) continue;
                if (!visited.Add(next)) continue;
                previous[next] = current;
                if (next.Equals(target)) return Reconstruct(previous, start, target);
                queue.Enqueue(next);
            }
        }
        return Array.Empty<PlanningGridCellId>();
    }

    private static IEnumerable<PlanningGridCellId> Neighbours(PlanningGridCellId current, PlanningGridRow[] rows, PlanningGridColumn[] columns,
        IReadOnlyDictionary<PlanningGridRowId, int> rowIndex, IReadOnlyDictionary<PlanningGridColumnId, int> columnIndex,
        RouteTopologyFamily topology, int sourceRow, int targetRow, int sourceMin, int sourceMax)
    {
        var row = rowIndex[current.RowId];
        var column = columnIndex[current.ColumnId];
        var candidates = new List<(int row, int column, int preference)>();
        void Add(int r, int c, int preference) { if (r >= 0 && r < rows.Length && c >= 0 && c < columns.Length) candidates.Add((r, c, preference)); }
        var downward = targetRow >= sourceRow;
        Add(row + 1, column, downward ? 0 : 1);
        Add(row - 1, column, downward ? 3 : 0);
        Add(row, column - 1, 1);
        Add(row, column + 1, 2);
        // A +/-2 bypass is a search distance, not a route edge. The BFS
        // expands through the intervening +/-1 cells so every emitted step
        // remains orthogonally adjacent and retains cell-by-cell provenance.
        foreach (var candidate in candidates.OrderBy(item => item.preference).ThenBy(item => Math.Abs(item.column - column)).ThenBy(item => item.column))
        {
            if (topology == RouteTopologyFamily.Upward && candidate.row < row && column >= sourceMin && column <= sourceMax) continue;
            yield return new PlanningGridCellId(current.GridId, rows[candidate.row].Id, columns[candidate.column].Id);
        }
    }

    private static bool CanTraverse(PlanningGridCell cell, PlanningGridCellId current, PlanningGridCellId next, PlanningGrid grid, PlanningGridCellId target)
    {
        if (next.Equals(target)) return true;
        if (cell.Occupancy != CellOccupancy.Empty) return false;

        var horizontal = current.RowId.Equals(next.RowId);
        var vertical = current.ColumnId.Equals(next.ColumnId);
        if (!horizontal && !vertical) return false;
        if (cell.Capabilities.HasFlag(CellCapability.ProjectBoundary)) return true;
        if (horizontal) return cell.Capabilities.HasFlag(CellCapability.RoutingAllowed);

        // Empty node-capable cells are vertical pass-through only. They are
        // part of the frozen grid corridor even though they cannot accept a
        // horizontal move or a turn.
        return cell.Capabilities.HasFlag(CellCapability.RoutingAllowed)
            || cell.Capabilities.HasFlag(CellCapability.NodeAllowed);
    }

    private static IReadOnlyList<PlanningGridCellId> Reconstruct(IReadOnlyDictionary<PlanningGridCellId, PlanningGridCellId> previous,
        PlanningGridCellId start, PlanningGridCellId target)
    {
        var result = new List<PlanningGridCellId> { target };
        var current = target;
        while (!current.Equals(start)) { current = previous[current]; result.Add(current); }
        result.Reverse();
        return result;
    }

    private PlanningGridCellId BoundaryCell(PlanningGrid grid, PlannedNodePlacement placement, bool towardRight)
    {
        var ordered = grid.Columns.OrderBy(item => item.LogicalOrder).ToArray();
        var index = ordered.Select((column, i) => (column.Id, i)).ToDictionary(item => item.Id, item => item.i);
        var footprint = placement.Footprint.Select(cell => index[cell.ColumnId]).ToArray();
        var desired = towardRight ? footprint.Max() + 1 : footprint.Min() - 1;
        desired = Math.Max(0, Math.Min(ordered.Length - 1, desired));
        return new PlanningGridCellId(grid.Id, placement.AnchorCellId.RowId, ordered[desired].Id);
    }

    private PlanningGridCellId DiagramCell(PlanningGridCellId boundary, string projectId, bool right)
    {
        var footprint = diagram.ProjectFootprints[projects.Select(item => item.ProjectId).ToList().IndexOf(projectId)];
        var columns = diagram.Grid.Columns.OrderBy(item => item.LogicalOrder).ToArray();
        var column = right ? (int)footprint.X + (int)footprint.Width : (int)footprint.X;
        column = Math.Max(0, Math.Min(columns.Length - 1, column));
        var rows = diagram.Grid.Rows.OrderBy(item => item.LogicalOrder).ToArray();
        var requestedRow = boundary.RowId.Value.StartsWith("r", StringComparison.Ordinal) &&
            int.TryParse(boundary.RowId.Value.Substring(1), out var parsedRow) ? parsedRow : 0;
        var row = rows[Math.Max(0, Math.Min(rows.Length - 1, requestedRow))];
        return new PlanningGridCellId(diagram.Grid.Id, row.Id, columns[column].Id);
    }

    private static void AddSteps(ICollection<PlannedGridRouteStep> steps, IReadOnlyList<PlanningGridCellId> cells, RouteStepRole role, string provenance)
    {
        foreach (var cell in cells)
            steps.Add(new PlannedGridRouteStep(cell.GridId, cell, GridSide.Top, GridSide.Bottom, role, steps.Count, provenance));
    }

    private static RouteTopologyFamily Classify(PlannedPhysicalNode source, PlannedPhysicalNode target, PlannedNodePlacement sourcePlacement, PlannedNodePlacement targetPlacement)
    {
        if (source.ProjectId != target.ProjectId) return RouteTopologyFamily.CrossProject;
        if (target.IsExternal) return RouteTopologyFamily.External;
        if (targetPlacement.AnchorCellId.RowId.Equals(sourcePlacement.AnchorCellId.RowId)) return RouteTopologyFamily.SameLayer;
        return RowOrdinal(targetPlacement.AnchorCellId.RowId) > RowOrdinal(sourcePlacement.AnchorCellId.RowId)
            ? (targetPlacement.AnchorCellId.ColumnId.Equals(sourcePlacement.CentreColumnId) ? RouteTopologyFamily.AdjacentDownward : RouteTopologyFamily.LongDownward)
            : RouteTopologyFamily.Upward;
    }

    private static bool IsHorizontal(PlanningGridCellId first, PlanningGridCellId second) => first.RowId.Equals(second.RowId);
    private static GridSide SideBetween(PlanningGridCellId first, PlanningGridCellId second)
    {
        if (first.RowId.Equals(second.RowId)) return ColumnOrdinal(second.ColumnId) > ColumnOrdinal(first.ColumnId) ? GridSide.Right : GridSide.Left;
        return RowOrdinal(second.RowId) > RowOrdinal(first.RowId) ? GridSide.Bottom : GridSide.Top;
    }

    private static int RowOrdinal(PlanningGridRowId id) => ParseOrdinal(id.Value);
    private static int ColumnOrdinal(PlanningGridColumnId id) => ParseOrdinal(id.Value);
    private static int ParseOrdinal(string value) => int.TryParse(value.TrimStart('r', 'c'), out var ordinal) ? ordinal : 0;

    private sealed record RouteBuildResult(PlannedGridRoute Route, IReadOnlyList<ArchitecturePlanningDiagnostic> Findings);
}

internal sealed record ArchitectureV6LogicalRouteResult(
    IReadOnlyList<PlannedGridRoute> Routes,
    IReadOnlyList<ArchitecturePlanningDiagnostic> Findings);
