using System;
using System.Collections.Generic;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core.Models.ArchitectureV6;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.ArchitectureV6;

internal sealed class ArchitectureV6TrackSizingPlanner
{
    private readonly ArchitecturePlanningRequest request;
    private readonly IReadOnlyList<PlannedPhysicalNode> nodes;
    private readonly IReadOnlyList<PlannedNodePlacement> placements;
    private readonly IReadOnlyList<PhysicalNodePlacementMetadata> metadata;
    private readonly IReadOnlyList<ProjectRoutingGrid> projectGrids;
    private readonly IReadOnlyList<SubtreeReservation> reservations;
    private readonly IReadOnlyList<GridTrackConstraint> constraints;
    private readonly List<ArchitecturePlanningDiagnostic> diagnostics = new();

    public ArchitectureV6TrackSizingPlanner(
        ArchitecturePlanningRequest request,
        IReadOnlyList<PlannedPhysicalNode> nodes,
        IReadOnlyList<PlannedNodePlacement> placements,
        IReadOnlyList<PhysicalNodePlacementMetadata> metadata,
        IReadOnlyList<ProjectRoutingGrid> projectGrids,
        IReadOnlyList<SubtreeReservation> reservations,
        IReadOnlyList<GridTrackConstraint> constraints)
    {
        this.request = request ?? throw new ArgumentNullException(nameof(request));
        this.nodes = nodes;
        this.placements = placements;
        this.metadata = metadata;
        this.projectGrids = projectGrids;
        this.reservations = reservations;
        this.constraints = constraints;
    }

    public PhysicalSizingResult Build()
    {
        var grids = new List<PlannedGridGeometry>();
        var allRows = new List<PlanningGridRow>();
        var allColumns = new List<PlanningGridColumn>();
        var localNodes = new List<PlannedRelativeNodeGeometry>();
        var localProjects = new List<PlannedRelativeProjectGeometry>();
        var localSubtrees = new List<PlannedRelativeSubtreeGeometry>();
        var projectWidths = new Dictionary<string, int>(StringComparer.Ordinal);
        var projectHeights = new Dictionary<string, int>(StringComparer.Ordinal);
        var trackMap = new Dictionary<PlanningGridId, TrackSet>();

        foreach (var project in projectGrids.OrderBy(item => item.ProjectId, StringComparer.Ordinal))
        {
            var tracks = SizeGrid(project.Grid);
            trackMap[project.Grid.Id] = tracks;
            allRows.AddRange(tracks.Rows);
            allColumns.AddRange(tracks.Columns);
            var owned = nodes.Where(node => project.OwnedPhysicalNodeIds.Contains(node.PhysicalNodeId)).ToArray();
            var projectNodes = placements.Where(placement => owned.Any(node => node.PhysicalNodeId == placement.PhysicalNodeId)).ToArray();
            var content = projectNodes.Select(placement => NodeBounds(placement, tracks)).ToArray();
            var contentWidth = content.Length == 0 ? request.GridSizing.CellWidth : content.Max(item => item.X + item.Width);
            var contentHeight = content.Length == 0 ? request.GridSizing.CellHeight : content.Max(item => item.Y + item.Height);
            var width = contentWidth + request.GridSizing.ContainerPadding * 2;
            var height = contentHeight + request.GridSizing.ContainerPadding * 2 + request.GridSizing.ProjectHeaderHeight;
            projectWidths[project.ProjectId] = width;
            projectHeights[project.ProjectId] = height;
            var transform = new GridTransform(project.Grid.Id, new RelativePoint(0, request.GridSizing.ProjectHeaderHeight + request.GridSizing.ContainerPadding));
            var bounds = new RelativeRectangle(0, 0, contentWidth, contentHeight);
            grids.Add(new PlannedGridGeometry(project.Grid.Id, bounds, new AbsoluteRectangle(0, 0, contentWidth, contentHeight), transform, tracks.Rows, tracks.Columns));
            localProjects.Add(new PlannedRelativeProjectGeometry(project.ProjectId, new RelativeRectangle(0, 0, width, height),
                new RelativeRectangle(0, 0, width, request.GridSizing.ProjectHeaderHeight), owned.Select(node => node.PhysicalNodeId).ToArray()));
            foreach (var placement in projectNodes)
            {
                var node = nodes.Single(item => item.PhysicalNodeId == placement.PhysicalNodeId);
                var boundsForNode = NodeBounds(placement, tracks);
                localNodes.Add(new PlannedRelativeNodeGeometry(node.PhysicalNodeId, node.SemanticNodeId, node.ProjectId,
                    Offset(boundsForNode, request.GridSizing.ContainerPadding, request.GridSizing.ProjectHeaderHeight + request.GridSizing.ContainerPadding),
                    placement.GridId, placement.AnchorCellId, node.PositionalOwnerId, node.ProjectionMode, node.IsExternal, node.IsStandalone));
            }
            foreach (var reservation in reservations.Where(item => item.GridId.Equals(project.Grid.Id)))
            {
                var rectangle = ReservationBounds(reservation, tracks);
                localSubtrees.Add(new PlannedRelativeSubtreeGeometry(reservation.SubtreeId, reservation.PositionalOwnerId, reservation.GridId,
                    Offset(rectangle, request.GridSizing.ContainerPadding, request.GridSizing.ProjectHeaderHeight + request.GridSizing.ContainerPadding), reservation.AncestorReservationId));
            }
        }

        var projectX = 0;
        var placedProjects = new List<PlannedRelativeProjectGeometry>();
        foreach (var project in localProjects.OrderBy(item => item.ProjectId, StringComparer.Ordinal))
        {
            var positioned = project with { Bounds = new RelativeRectangle(projectX, 0, project.Bounds.Width, project.Bounds.Height), LabelBounds = new RelativeRectangle(projectX, 0, project.Bounds.Width, project.LabelBounds.Height) };
            placedProjects.Add(positioned);
            projectX += project.Bounds.Width + request.GridSizing.ContainerPadding * 2;
        }
        var diagramWidth = Math.Max(1, projectX == 0 ? request.GridSizing.CellWidth : projectX - request.GridSizing.ContainerPadding * 2);
        var diagramHeight = Math.Max(1, placedProjects.Count == 0 ? request.GridSizing.CellHeight : placedProjects.Max(item => item.Bounds.Height));
        var diagramBounds = new RelativeRectangle(0, 0, diagramWidth, diagramHeight);
        var diagramGridId = new PlanningGridId("diagram");
        var diagramRows = new[] { new PlanningGridRow(new PlanningGridRowId("row:0"), 0, diagramHeight, diagramHeight, diagramHeight, 0, 0) };
        var diagramColumns = new[] { new PlanningGridColumn(new PlanningGridColumnId("column:0"), 0, diagramWidth, diagramWidth, diagramWidth, 0, 0) };
        grids.Add(new PlannedGridGeometry(diagramGridId, diagramBounds, new AbsoluteRectangle(0, 0, diagramWidth, diagramHeight),
            new GridTransform(diagramGridId, new RelativePoint(0, 0)), diagramRows, diagramColumns));
        allRows.AddRange(diagramRows);
        allColumns.AddRange(diagramColumns);
        foreach (var node in localNodes)
            if (localNodes.Any(other => other.PhysicalNodeId != node.PhysicalNodeId && Intersects(node.Bounds, other.Bounds) && other.ProjectId == node.ProjectId))
                diagnostics.Add(new ArchitecturePlanningDiagnostic("RelativeNodeOverlap", "Relative node rectangles overlap after track sizing.", PlanningDiagnosticSubject.PhysicalNode, node.PhysicalNodeId));
        var sizing = new GridTrackSizingPlan(allRows, allColumns, constraints, diagramBounds);
        var relative = new PlannedArchitectureRelativeGeometry(localNodes, placedProjects, grids, localSubtrees, diagramBounds);
        return new PhysicalSizingResult(sizing, relative, diagnostics, 0, projectWidths, projectHeights);
    }

    private TrackSet SizeGrid(PlanningGrid grid)
    {
        var rowExtents = grid.Rows.ToDictionary(row => row.Id, _ => request.GridSizing.CellHeight);
        var columnExtents = grid.Columns.ToDictionary(column => column.Id, _ => request.GridSizing.CellWidth);
        foreach (var constraint in constraints.Where(item => item.GridId.Equals(grid.Id)))
        {
            if (constraint.Kind == TrackConstraintKind.SingleRowMinimum || constraint.Kind == TrackConstraintKind.HorizontalLaneEnvelope || constraint.Kind == TrackConstraintKind.TurnClearance)
                foreach (var row in constraint.Rows) if (rowExtents.ContainsKey(row)) rowExtents[row] = Math.Max(rowExtents[row], Extent(constraint));
            if (constraint.Kind == TrackConstraintKind.SingleColumnMinimum || constraint.Kind == TrackConstraintKind.VerticalLaneEnvelope || constraint.Kind == TrackConstraintKind.TurnClearance)
                foreach (var column in constraint.Columns) if (columnExtents.ContainsKey(column)) columnExtents[column] = Math.Max(columnExtents[column], Extent(constraint));
            if (constraint.Kind == TrackConstraintKind.MultiColumnSpanMinimum && constraint.Columns.Count > 0)
                Distribute(columnExtents, constraint.Columns, Extent(constraint));
            if (constraint.Kind == TrackConstraintKind.MultiRowSpanMinimum && constraint.Rows.Count > 0)
                Distribute(rowExtents, constraint.Rows, Extent(constraint));
        }
        var rows = grid.Rows.OrderBy(row => row.LogicalOrder).Select((row, index) => new PlanningGridRow(row.Id, row.LogicalOrder, row.MinimumExtent, rowExtents[row.Id], rowExtents[row.Id], Offset(rowExtents, row.Id), Offset(rowExtents, row.Id))).ToArray();
        var columns = grid.Columns.OrderBy(column => column.LogicalOrder).Select((column, index) => new PlanningGridColumn(column.Id, column.LogicalOrder, column.MinimumExtent, columnExtents[column.Id], columnExtents[column.Id], Offset(columnExtents, column.Id), Offset(columnExtents, column.Id))).ToArray();
        return new TrackSet(rows, columns);
    }

    private int Extent(GridTrackConstraint constraint) => constraint.Kind == TrackConstraintKind.HorizontalLaneEnvelope || constraint.Kind == TrackConstraintKind.VerticalLaneEnvelope
        ? Math.Max(request.GridSizing.CellWidth, constraint.MinimumExtent * request.RoutePlanning.MinimumParallelSpacing)
        : Math.Max(request.GridSizing.CellWidth, constraint.MinimumExtent);

    private static void Distribute<TKey>(IDictionary<TKey, int> extents, IReadOnlyList<TKey> keys, int required) where TKey : notnull
    {
        var current = keys.Sum(key => extents[key]);
        var deficit = required - current;
        if (deficit <= 0) return;
        for (var index = 0; index < deficit; index++) extents[keys[(index + keys.Count / 2) % keys.Count]]++;
    }

    private static int Offset<TKey>(IReadOnlyDictionary<TKey, int> extents, TKey key) where TKey : notnull => extents.TakeWhile(item => !EqualityComparer<TKey>.Default.Equals(item.Key, key)).Sum(item => item.Value);
    private static RelativeRectangle NodeBounds(PlannedNodePlacement placement, TrackSet tracks)
    {
        var columns = placement.Footprint.Select(cell => tracks.Columns.Single(column => column.Id.Equals(cell.ColumnId))).ToArray();
        var row = tracks.Rows.Single(item => item.Id.Equals(placement.AnchorCellId.RowId));
        return new RelativeRectangle(columns.Min(column => column.RelativeOffset), row.RelativeOffset, columns.Sum(column => column.FinalExtent), row.FinalExtent);
    }
    private static RelativeRectangle ReservationBounds(SubtreeReservation reservation, TrackSet tracks)
    {
        var cells = reservation.Cells.Select(cell => (Column: tracks.Columns.Single(column => column.Id.Equals(cell.ColumnId)), Row: tracks.Rows.Single(row => row.Id.Equals(cell.RowId)))).ToArray();
        return new RelativeRectangle(cells.Min(item => item.Column.RelativeOffset), cells.Min(item => item.Row.RelativeOffset),
            cells.Max(item => item.Column.RelativeOffset + item.Column.FinalExtent) - cells.Min(item => item.Column.RelativeOffset),
            cells.Max(item => item.Row.RelativeOffset + item.Row.FinalExtent) - cells.Min(item => item.Row.RelativeOffset));
    }
    private static RelativeRectangle Offset(RelativeRectangle rectangle, int x, int y) => new(rectangle.X + x, rectangle.Y + y, rectangle.Width, rectangle.Height);
    private static bool Intersects(RelativeRectangle left, RelativeRectangle right) => left.X < right.X + right.Width && right.X < left.X + left.Width && left.Y < right.Y + right.Height && right.Y < left.Y + left.Height;

    private sealed record TrackSet(IReadOnlyList<PlanningGridRow> Rows, IReadOnlyList<PlanningGridColumn> Columns);
}

internal sealed record PhysicalSizingResult(
    GridTrackSizingPlan Sizing,
    PlannedArchitectureRelativeGeometry RelativeGeometry,
    IReadOnlyList<ArchitecturePlanningDiagnostic> Diagnostics,
    int ReconciliationIterations,
    IReadOnlyDictionary<string, int> ProjectWidths,
    IReadOnlyDictionary<string, int> ProjectHeights);
