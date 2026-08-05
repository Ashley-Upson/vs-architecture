using System;
using System.Collections.Generic;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core.Models.ArchitectureV6;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.ArchitectureV6;

// Compiles accepted logical tracks into relative geometry. It never changes placement or routes.
internal sealed class ArchitectureV6TrackSizingPlanner
{
    private readonly ArchitecturePlanningRequest request;
    private readonly IReadOnlyList<PlannedPhysicalNode> nodes;
    private readonly IReadOnlyList<PlannedNodePlacement> placements;
    private readonly IReadOnlyList<ProjectRoutingGrid> projectGrids;
    private readonly IReadOnlyList<SubtreeReservation> reservations;
    private readonly List<GridTrackConstraint> constraintsForSizing;
    private readonly DiagramRoutingGrid diagramGrid;
    private readonly List<ArchitecturePlanningDiagnostic> diagnostics = new();

    public ArchitectureV6TrackSizingPlanner(
        ArchitecturePlanningRequest request,
        IReadOnlyList<PlannedPhysicalNode> nodes,
        IReadOnlyList<PlannedNodePlacement> placements,
        IReadOnlyList<PhysicalNodePlacementMetadata> metadata,
        IReadOnlyList<ProjectRoutingGrid> projectGrids,
        IReadOnlyList<SubtreeReservation> reservations,
        IReadOnlyList<GridTrackConstraint> constraints,
        DiagramRoutingGrid diagramGrid)
    {
        this.request = request ?? throw new ArgumentNullException(nameof(request));
        this.nodes = nodes ?? throw new ArgumentNullException(nameof(nodes));
        this.placements = placements ?? throw new ArgumentNullException(nameof(placements));
        this.projectGrids = projectGrids ?? throw new ArgumentNullException(nameof(projectGrids));
        this.reservations = reservations ?? throw new ArgumentNullException(nameof(reservations));
        constraintsForSizing = new List<GridTrackConstraint>(constraints ?? throw new ArgumentNullException(nameof(constraints)));
        this.diagramGrid = diagramGrid ?? throw new ArgumentNullException(nameof(diagramGrid));
    }

    public PhysicalSizingResult Build()
    {
        var grids = new List<PlannedGridGeometry>();
        var relativeNodes = new List<PlannedRelativeNodeGeometry>();
        var relativeProjects = new List<PlannedRelativeProjectGeometry>();
        var relativeSubtrees = new List<PlannedRelativeSubtreeGeometry>();
        var sizingRows = new List<PlanningGridRow>();
        var sizingColumns = new List<PlanningGridColumn>();
        var provenance = new List<GridTrackProvenance>();
        var projectWidths = new Dictionary<string, int>(StringComparer.Ordinal);
        var projectHeights = new Dictionary<string, int>(StringComparer.Ordinal);
        var solverIterations = 0;
        var idempotent = true;

        foreach (var project in projectGrids.OrderBy(item => item.ProjectId, StringComparer.Ordinal))
        {
            var sized = SizeGrid(project.Grid);
            solverIterations += sized.Iterations;
            idempotent &= SameTracks(sized, SizeGrid(project.Grid));
            sizingRows.AddRange(sized.Rows);
            sizingColumns.AddRange(sized.Columns);
            provenance.AddRange(sized.Provenance(project.Grid.Id));
            grids.Add(ToGridGeometry(project.Grid.Id, sized));

            var owned = new HashSet<string>(project.OwnedPhysicalNodeIds, StringComparer.Ordinal);
            var projectPlacements = placements.Where(item => owned.Contains(item.PhysicalNodeId)).ToArray();
            foreach (var placement in projectPlacements)
            {
                var node = nodes.Single(item => item.PhysicalNodeId == placement.PhysicalNodeId);
                var bounds = FootprintBounds(placement, sized);
                relativeNodes.Add(new PlannedRelativeNodeGeometry(node.PhysicalNodeId, node.SemanticNodeId, node.ProjectId,
                    bounds, placement.GridId, placement.AnchorCellId, node.PositionalOwnerId, node.ProjectionMode, node.IsExternal, node.IsStandalone));
            }

            foreach (var reservation in reservations.Where(item => item.GridId.Equals(project.Grid.Id)))
            {
                relativeSubtrees.Add(new PlannedRelativeSubtreeGeometry(reservation.SubtreeId, reservation.PositionalOwnerId,
                    reservation.GridId, FootprintBounds(reservation.Cells, sized), reservation.AncestorReservationId));
            }

            var content = project.Grid.Columns.OrderBy(item => item.LogicalOrder).Sum(item => sized.Columns.Single(column => column.Id.Equals(item.Id)).FinalExtent);
            var height = project.Grid.Rows.OrderBy(item => item.LogicalOrder).Sum(item => sized.Rows.Single(row => row.Id.Equals(item.Id)).FinalExtent);
            var widthWithPadding = checked(content + request.GridSizing.ContainerPadding * 2);
            var heightWithHeader = checked(height + request.GridSizing.ContainerPadding * 2 + request.GridSizing.ProjectHeaderHeight);
            projectWidths[project.ProjectId] = widthWithPadding;
            projectHeights[project.ProjectId] = heightWithHeader;
            relativeProjects.Add(new PlannedRelativeProjectGeometry(project.ProjectId,
                new RelativeRectangle(0, 0, widthWithPadding, heightWithHeader),
                new RelativeRectangle(0, 0, widthWithPadding, request.GridSizing.ProjectHeaderHeight),
                projectPlacements.Select(item => item.PhysicalNodeId).ToArray()));
        }

        var diagramSource = diagramGrid.Grid.Rows.Count == 0 || diagramGrid.Grid.Columns.Count == 0
            ? CreateDiagramGrid(projectWidths, projectHeights)
            : diagramGrid.Grid;
        var diagramSized = SizeGrid(diagramSource);
        solverIterations += diagramSized.Iterations;
        idempotent &= SameTracks(diagramSized, SizeGrid(diagramSource));
        sizingRows.AddRange(diagramSized.Rows);
        sizingColumns.AddRange(diagramSized.Columns);
        provenance.AddRange(diagramSized.Provenance(diagramSource.Id));
        grids.Add(ToGridGeometry(diagramSource.Id, diagramSized));
        var diagramWidth = Math.Max(1, diagramSized.Columns.Sum(item => item.FinalExtent));
        var diagramHeight = Math.Max(1, diagramSized.Rows.Sum(item => item.FinalExtent));
        var diagramBounds = new RelativeRectangle(0, 0, diagramWidth, diagramHeight);
        var relative = new PlannedArchitectureRelativeGeometry(relativeNodes, relativeProjects, grids, relativeSubtrees, diagramBounds);
        var sizing = new GridTrackSizingPlan(sizingRows, sizingColumns, constraintsForSizing, diagramBounds, provenance);
        Validate(relative, projectGrids, diagramSized);
        return new PhysicalSizingResult(sizing, relative, diagnostics, solverIterations, projectWidths, projectHeights,
            sizingRows.Count + sizingColumns.Count, diagramWidth, diagramHeight, idempotent);
    }

    private SizedGrid SizeGrid(PlanningGrid grid)
    {
        var rows = grid.Rows.OrderBy(item => item.LogicalOrder).ToArray();
        var columns = grid.Columns.OrderBy(item => item.LogicalOrder).ToArray();
        var rowExtents = rows.ToDictionary(item => item.Id, item => Math.Max(request.GridSizing.CellHeight, item.MinimumExtent));
        var columnExtents = columns.ToDictionary(item => item.Id, item => Math.Max(request.GridSizing.CellWidth, item.MinimumExtent));
        var rowContributions = new Dictionary<PlanningGridRowId, List<GridTrackContribution>>();
        var columnContributions = new Dictionary<PlanningGridColumnId, List<GridTrackContribution>>();

        foreach (var row in rows) rowContributions[row.Id] = new List<GridTrackContribution>();
        foreach (var column in columns) columnContributions[column.Id] = new List<GridTrackContribution>();
        var gridConstraints = BuildGridConstraints(grid, rows, columns);
        var iterations = 0;
        var changed = true;
        while (changed && iterations++ < Math.Max(1, gridConstraints.Count + 1))
        {
            changed = false;
            foreach (var constraint in gridConstraints)
            {
                var extent = RequiredExtent(constraint);
                if (constraint.Rows.Count == 1 && rowExtents.ContainsKey(constraint.Rows[0]))
                    changed |= Raise(rowExtents, rowContributions, constraint.Rows[0], extent, constraint);
                if (constraint.Columns.Count == 1 && columnExtents.ContainsKey(constraint.Columns[0]))
                    changed |= Raise(columnExtents, columnContributions, constraint.Columns[0], extent, constraint);
                if (constraint.Rows.Count > 1)
                    changed |= SatisfySpan(rowExtents, rowContributions, constraint.Rows, extent, constraint);
                if (constraint.Columns.Count > 1)
                    changed |= SatisfySpan(columnExtents, columnContributions, constraint.Columns, extent, constraint);
                if (constraint.Rows.Count == 0 && constraint.Columns.Count == 0 && constraint.MinimumExtent > 0)
                    diagnostics.Add(new ArchitecturePlanningDiagnostic("SizingConstraintWithoutTracks", "Sizing constraint has no owning tracks.", PlanningDiagnosticSubject.TrackConstraint, constraint.OwnerId));
            }
        }

        if (changed)
            diagnostics.Add(new ArchitecturePlanningDiagnostic("SizingIterationLimit", "Track sizing did not converge within its deterministic iteration bound.", PlanningDiagnosticSubject.Grid, grid.Id.Value));

        var sizedRows = CompileRows(rows, rowExtents, rowContributions);
        var sizedColumns = CompileColumns(columns, columnExtents, columnContributions);
        return new SizedGrid(grid.Id, sizedRows, sizedColumns, iterations, rowContributions, columnContributions);
    }

    private PlanningGrid CreateDiagramGrid(IReadOnlyDictionary<string, int> projectWidths, IReadOnlyDictionary<string, int> projectHeights)
    {
        var projectIds = projectWidths.Keys.OrderBy(item => item, StringComparer.Ordinal).ToArray();
        var gridId = diagramGrid.Grid.Id;
        var rowId = new PlanningGridRowId("diagram:row:projects");
        var rows = new[] { new PlanningGridRow(rowId, 0, request.GridSizing.CellHeight, request.GridSizing.CellHeight,
            request.GridSizing.CellHeight, 0, 0) };
        var columns = projectIds.Select((projectId, index) => new PlanningGridColumn(new PlanningGridColumnId($"diagram:column:{index}"), index,
            request.GridSizing.CellWidth, request.GridSizing.CellWidth, request.GridSizing.CellWidth, 0, 0)).ToArray();
        var cells = new Dictionary<PlanningGridCellId, PlanningGridCell>();
        for (var index = 0; index < columns.Length; index++)
        {
            var cellId = new PlanningGridCellId(gridId, rowId, columns[index].Id);
            cells[cellId] = new PlanningGridCell(cellId, CellCapability.RoutingAllowed | CellCapability.ProjectBoundary,
                CellOccupancy.ProjectFootprint, Array.Empty<string>(), projectIds[index]);
        }
        for (var index = 0; index < projectIds.Length; index++)
        {
            constraintsForSizing.Add(new GridTrackConstraint(TrackConstraintKind.ProjectFootprint, gridId,
                Array.Empty<PlanningGridRowId>(), new[] { columns[index].Id }, projectWidths[projectIds[index]], "project footprint width", projectIds[index]));
            constraintsForSizing.Add(new GridTrackConstraint(TrackConstraintKind.ProjectFootprint, gridId,
                new[] { rowId }, Array.Empty<PlanningGridColumnId>(), projectHeights[projectIds[index]], "project footprint height", projectIds[index]));
        }
        return new PlanningGrid(gridId, rows, columns, cells, new GridTransform(gridId, new RelativePoint(0, 0)));
    }

    private IReadOnlyList<GridTrackConstraint> BuildGridConstraints(PlanningGrid grid, IReadOnlyList<PlanningGridRow> rows, IReadOnlyList<PlanningGridColumn> columns)
    {
        var result = constraintsForSizing.Where(item => item.GridId.Equals(grid.Id)).ToList();
        var rowIds = new HashSet<PlanningGridRowId>(rows.Select(item => item.Id));
        var columnIds = new HashSet<PlanningGridColumnId>(columns.Select(item => item.Id));
        foreach (var placement in placements.Where(item => item.GridId.Equals(grid.Id)))
        {
            result.Add(new GridTrackConstraint(TrackConstraintKind.NodeFootprint, grid.Id,
                placement.Footprint.Select(item => item.RowId).Distinct().Where(rowIds.Contains).ToArray(),
                placement.Footprint.Select(item => item.ColumnId).Distinct().Where(columnIds.Contains).ToArray(),
                request.NodePlacement.MinimumNodeWidth, "node minimum footprint", placement.PhysicalNodeId));
        }
        return result;
    }

    private int RequiredExtent(GridTrackConstraint constraint)
    {
        if (constraint.Kind == TrackConstraintKind.HorizontalLaneEnvelope || constraint.Kind == TrackConstraintKind.VerticalLaneEnvelope)
            return Math.Max(request.GridSizing.CellHeight, checked(constraint.MinimumExtent * request.RoutePlanning.MinimumParallelSpacing));
        return Math.Max(1, constraint.MinimumExtent);
    }

    private static bool Raise<TKey>(IDictionary<TKey, int> extents, IDictionary<TKey, List<GridTrackContribution>> contributions, TKey key,
        int required, GridTrackConstraint constraint) where TKey : notnull
    {
        if (extents[key] >= required) return false;
        var contribution = required - extents[key];
        extents[key] = required;
        contributions[key].Add(new GridTrackContribution(constraint.Kind, constraint.OwnerId, contribution));
        return true;
    }

    private static bool SatisfySpan<TKey>(IDictionary<TKey, int> extents, IDictionary<TKey, List<GridTrackContribution>> contributions,
        IReadOnlyList<TKey> keys, int required, GridTrackConstraint constraint) where TKey : notnull
    {
        var deficit = required - keys.Sum(key => extents[key]);
        if (deficit <= 0) return false;
        for (var index = 0; index < deficit; index++)
        {
            var offset = index / 2;
            var keyIndex = index % 2 == 0 ? (keys.Count - 1) / 2 - offset : keys.Count / 2 + offset + 1;
            if (keyIndex < 0 || keyIndex >= keys.Count) keyIndex = index % keys.Count;
            var key = keys[keyIndex];
            extents[key]++;
            contributions[key].Add(new GridTrackContribution(constraint.Kind, constraint.OwnerId, 1));
        }
        return true;
    }

    private static IReadOnlyList<PlanningGridRow> CompileRows(IReadOnlyList<PlanningGridRow> source, IReadOnlyDictionary<PlanningGridRowId, int> extents,
        IReadOnlyDictionary<PlanningGridRowId, List<GridTrackContribution>> contributions)
    {
        var offset = 0;
        return source.Select(row =>
        {
            var extent = extents[row.Id];
            var result = new PlanningGridRow(row.Id, row.LogicalOrder, row.MinimumExtent, extent, extent, offset, offset);
            offset += extent;
            return result;
        }).ToArray();
    }

    private static IReadOnlyList<PlanningGridColumn> CompileColumns(IReadOnlyList<PlanningGridColumn> source, IReadOnlyDictionary<PlanningGridColumnId, int> extents,
        IReadOnlyDictionary<PlanningGridColumnId, List<GridTrackContribution>> contributions)
    {
        var offset = 0;
        return source.Select(column =>
        {
            var extent = extents[column.Id];
            var result = new PlanningGridColumn(column.Id, column.LogicalOrder, column.MinimumExtent, extent, extent, offset, offset);
            offset += extent;
            return result;
        }).ToArray();
    }

    private static PlannedGridGeometry ToGridGeometry(PlanningGridId id, SizedGrid grid)
    {
        var width = grid.Columns.Sum(item => item.FinalExtent);
        var height = grid.Rows.Sum(item => item.FinalExtent);
        return new PlannedGridGeometry(id, new RelativeRectangle(0, 0, width, height), new AbsoluteRectangle(0, 0, width, height),
            new GridTransform(id, new RelativePoint(0, 0)), grid.Rows, grid.Columns);
    }

    private static RelativeRectangle FootprintBounds(PlannedNodePlacement placement, SizedGrid grid) =>
        FootprintBounds(placement.Footprint, grid);

    private static RelativeRectangle FootprintBounds(IReadOnlyList<PlanningGridCellId> cells, SizedGrid grid)
    {
        if (cells.Count == 0) throw new InvalidOperationException("A footprint must contain at least one cell.");
        var columns = cells.Select(cell => grid.Columns.Single(column => column.Id.Equals(cell.ColumnId))).ToArray();
        var rows = cells.Select(cell => grid.Rows.Single(row => row.Id.Equals(cell.RowId))).ToArray();
        var x = columns.Min(item => item.RelativeOffset);
        var y = rows.Min(item => item.RelativeOffset);
        return new RelativeRectangle(x, y,
            columns.Max(item => item.RelativeOffset + item.FinalExtent) - x,
            rows.Max(item => item.RelativeOffset + item.FinalExtent) - y);
    }

    private void Validate(PlannedArchitectureRelativeGeometry relative, IReadOnlyList<ProjectRoutingGrid> projects, SizedGrid diagram)
    {
        if (relative.Nodes.Count != placements.Count)
            diagnostics.Add(new ArchitecturePlanningDiagnostic("RelativeNodeAccounting", "Every planned node must compile to one relative rectangle.", PlanningDiagnosticSubject.PhysicalNode, null));
        foreach (var group in relative.Nodes.GroupBy(node => node.ProjectId ?? string.Empty))
        {
            foreach (var pair in Overlaps(group.ToArray()))
                diagnostics.Add(new ArchitecturePlanningDiagnostic("RelativeNodeOverlap",
                    $"Relative node rectangles overlap after grid sizing: {pair.Left.PhysicalNodeId} [{pair.Left.Bounds}] and {pair.Right.PhysicalNodeId} [{pair.Right.Bounds}].",
                    PlanningDiagnosticSubject.PhysicalNode, pair.Left.PhysicalNodeId));
        }
        if (relative.Nodes.Any(node => node.Bounds.Width <= 0 || node.Bounds.Height <= 0))
            diagnostics.Add(new ArchitecturePlanningDiagnostic("RelativeInvalidDimension", "A relative node rectangle has a non-positive dimension.", PlanningDiagnosticSubject.PhysicalNode, null));
        if (diagram.Rows.Any(row => row.FinalExtent <= 0) || diagram.Columns.Any(column => column.FinalExtent <= 0))
            diagnostics.Add(new ArchitecturePlanningDiagnostic("SizingInvalidTrack", "A final grid track has a non-positive extent.", PlanningDiagnosticSubject.Grid, diagram.Id.Value));
    }

    private static bool HasOverlap(IEnumerable<RelativeRectangle> rectangles)
    {
        var values = rectangles.ToArray();
        for (var index = 0; index < values.Length; index++)
            for (var other = index + 1; other < values.Length; other++)
                if (values[index].X < values[other].X + values[other].Width && values[other].X < values[index].X + values[index].Width &&
                    values[index].Y < values[other].Y + values[other].Height && values[other].Y < values[index].Y + values[index].Height)
                    return true;
        return false;
    }

    private static bool SameTracks(SizedGrid left, SizedGrid right) =>
        left.Rows.Select(item => (item.Id, item.FinalExtent, item.RelativeOffset)).SequenceEqual(right.Rows.Select(item => (item.Id, item.FinalExtent, item.RelativeOffset))) &&
        left.Columns.Select(item => (item.Id, item.FinalExtent, item.RelativeOffset)).SequenceEqual(right.Columns.Select(item => (item.Id, item.FinalExtent, item.RelativeOffset)));

    private static IEnumerable<(PlannedRelativeNodeGeometry Left, PlannedRelativeNodeGeometry Right)> Overlaps(IReadOnlyList<PlannedRelativeNodeGeometry> nodes)
    {
        for (var index = 0; index < nodes.Count; index++)
            for (var other = index + 1; other < nodes.Count; other++)
                if (HasOverlap(new[] { nodes[index].Bounds, nodes[other].Bounds }))
                    yield return (nodes[index], nodes[other]);
    }

    private sealed record SizedGrid(
        PlanningGridId Id,
        IReadOnlyList<PlanningGridRow> Rows,
        IReadOnlyList<PlanningGridColumn> Columns,
        int Iterations,
        IReadOnlyDictionary<PlanningGridRowId, List<GridTrackContribution>> RowContributions,
        IReadOnlyDictionary<PlanningGridColumnId, List<GridTrackContribution>> ColumnContributions)
    {
        public IEnumerable<GridTrackProvenance> Provenance(PlanningGridId gridId) =>
            Rows.Select(row => new GridTrackProvenance(gridId, "row", row.Id.Value, row.MinimumExtent, row.FinalExtent,
                RowContributions.TryGetValue(row.Id, out var rowContributions) ? rowContributions : Array.Empty<GridTrackContribution>()))
            .Concat(Columns.Select(column => new GridTrackProvenance(gridId, "column", column.Id.Value, column.MinimumExtent, column.FinalExtent,
                ColumnContributions.TryGetValue(column.Id, out var columnContributions) ? columnContributions : Array.Empty<GridTrackContribution>())));
    }
}

internal sealed record PhysicalSizingResult(
    GridTrackSizingPlan Sizing,
    PlannedArchitectureRelativeGeometry RelativeGeometry,
    IReadOnlyList<ArchitecturePlanningDiagnostic> Diagnostics,
    int ReconciliationIterations,
    IReadOnlyDictionary<string, int> ProjectWidths,
    IReadOnlyDictionary<string, int> ProjectHeights,
    int SizedTrackCount,
    int DiagramWidth,
    int DiagramHeight,
    bool SizingIdempotent);
