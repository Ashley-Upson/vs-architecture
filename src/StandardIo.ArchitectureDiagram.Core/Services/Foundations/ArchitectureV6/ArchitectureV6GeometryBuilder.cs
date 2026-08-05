using System;
using System.Collections.Generic;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core.Models.ArchitectureV6;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.ArchitectureV6;

internal sealed class ArchitectureV6GeometryBuilder
{
    private readonly ArchitecturePlanningRequest request;
    private readonly IReadOnlyList<PlannedPhysicalNode> nodes;
    private readonly IReadOnlyList<PlannedNodePlacement> placements;
    private readonly IReadOnlyList<PhysicalNodePlacementMetadata> metadata;
    private readonly IReadOnlyList<SubtreeReservation> reservations;
    private readonly IReadOnlyList<ProjectRoutingGrid> sourceGrids;
    private readonly Dictionary<string, PlannedPhysicalNode> nodesById;
    private readonly Dictionary<string, PlannedNodePlacement> placementById;
    private readonly Dictionary<string, PhysicalNodePlacementMetadata> metadataById;
    private readonly List<ArchitecturePlanningDiagnostic> findings = new();

    public ArchitectureV6GeometryBuilder(
        ArchitecturePlanningRequest request,
        IReadOnlyList<PlannedPhysicalNode> nodes,
        IReadOnlyList<PlannedNodePlacement> placements,
        IReadOnlyList<PhysicalNodePlacementMetadata> metadata,
        IReadOnlyList<SubtreeReservation> reservations,
        IReadOnlyList<ProjectRoutingGrid> sourceGrids)
    {
        this.request = request ?? throw new ArgumentNullException(nameof(request));
        this.nodes = nodes ?? throw new ArgumentNullException(nameof(nodes));
        this.placements = placements ?? throw new ArgumentNullException(nameof(placements));
        this.metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        this.reservations = reservations ?? throw new ArgumentNullException(nameof(reservations));
        this.sourceGrids = sourceGrids ?? throw new ArgumentNullException(nameof(sourceGrids));
        nodesById = nodes.ToDictionary(node => node.PhysicalNodeId, StringComparer.Ordinal);
        placementById = placements.ToDictionary(placement => placement.PhysicalNodeId, StringComparer.Ordinal);
        metadataById = metadata.ToDictionary(item => item.PhysicalNodeId, StringComparer.Ordinal);
    }

    public GeometryBuildResult Build()
    {
        var projectIds = ProjectOrder();
        var projectOutputs = new List<ProjectOutput>();
        var diagramX = 0;
        foreach (var projectId in projectIds)
        {
            var output = BuildProject(projectId, diagramX);
            projectOutputs.Add(output);
            diagramX += output.RelativeBounds.Width + Spacing.ProjectBoundary;
        }

        var width = Math.Max(1, projectOutputs.Count == 0 ? request.GridSizing.ContainerPadding * 2 : diagramX - Spacing.ProjectBoundary);
        var height = Math.Max(1, projectOutputs.Select(output => output.RelativeBounds.Height).DefaultIfEmpty(1).Max());
        var nodeGeometry = projectOutputs.SelectMany(output => output.Nodes).ToArray();
        var projectGeometry = projectOutputs.Select(output => output.Geometry).ToArray();
        var gridGeometry = projectOutputs.SelectMany(output => output.Grids).ToArray();
        var subtreeGeometry = BuildSubtreeGeometry(nodeGeometry, projectOutputs);
        var geometry = new PlannedArchitectureGeometry(
            nodeGeometry,
            projectGeometry,
            gridGeometry,
            subtreeGeometry,
            new RelativeRectangle(0, 0, width, height),
            new AbsoluteRectangle(0, 0, width, height));
        ValidateGeometry(nodeGeometry, projectGeometry, width, height);
        var sizedGrids = projectOutputs.Select(output => output.Grid).ToArray();
        var diagramGrid = new DiagramRoutingGrid(
            new PlanningGrid(new PlanningGridId("diagram"),
                Enumerable.Range(0, 1).Select(index => new PlanningGridRow(new PlanningGridRowId("row:0"), 0, height, height, height, 0, 0)).ToArray(),
                Enumerable.Range(0, Math.Max(1, width)).Select(index => new PlanningGridColumn(new PlanningGridColumnId($"column:{index}"), index, 1, 1, 1, index, index)).ToArray(),
                new Dictionary<PlanningGridCellId, PlanningGridCell>(),
                new GridTransform(new PlanningGridId("diagram"), new RelativePoint(0, 0))),
            projectGeometry.Select(item => new RelativeRectangle(item.RelativeBounds.X, item.RelativeBounds.Y, item.RelativeBounds.Width, item.RelativeBounds.Height)).ToArray(),
            Array.Empty<GridTransition>());
        var sizing = new GridTrackSizingPlan(
            sizedGrids.SelectMany(grid => grid.Grid.Rows).ToArray(),
            sizedGrids.SelectMany(grid => grid.Grid.Columns).ToArray(),
            BuildConstraints(projectOutputs),
            new RelativeRectangle(0, 0, width, height));
        return new GeometryBuildResult(geometry, sizedGrids, diagramGrid, sizing, findings);
    }

    private void ValidateGeometry(
        IReadOnlyList<PlannedPhysicalNodeGeometry> nodeGeometry,
        IReadOnlyList<PlannedProjectGeometry> projectGeometry,
        int width,
        int height)
    {
        foreach (var node in nodeGeometry)
        {
            if (node.RelativeBounds.Width <= 0 || node.RelativeBounds.Height <= 0 || node.AbsoluteBounds.Width <= 0 || node.AbsoluteBounds.Height <= 0)
                findings.Add(new ArchitecturePlanningDiagnostic("InvalidGeometryDimension", "A planned node has a non-positive dimension.", PlanningDiagnosticSubject.PhysicalNode, node.PhysicalNodeId));
            if (node.AbsoluteBounds.X < 0 || node.AbsoluteBounds.Y < 0 || node.AbsoluteBounds.X + node.AbsoluteBounds.Width > width || node.AbsoluteBounds.Y + node.AbsoluteBounds.Height > height)
                findings.Add(new ArchitecturePlanningDiagnostic("GeometryBoundsError", "A planned node falls outside the diagram bounds.", PlanningDiagnosticSubject.PhysicalNode, node.PhysicalNodeId));
        }
        foreach (var project in projectGeometry)
        {
            if (project.RelativeBounds.Width <= 0 || project.RelativeBounds.Height <= 0)
                findings.Add(new ArchitecturePlanningDiagnostic("InvalidGeometryDimension", "A project has a non-positive dimension.", PlanningDiagnosticSubject.Grid, project.ProjectId));
            var owned = nodeGeometry.Where(node => node.ProjectId == project.ProjectId).ToArray();
            foreach (var node in owned)
                if (!Contains(project.RelativeBounds, node.RelativeBounds))
                    findings.Add(new ArchitecturePlanningDiagnostic("GeometryContainmentViolation", "A project does not contain one of its planned nodes.", PlanningDiagnosticSubject.PhysicalNode, node.PhysicalNodeId));
            for (var left = 0; left < owned.Length; left++)
                for (var right = left + 1; right < owned.Length; right++)
                    if (Intersects(owned[left].RelativeBounds, owned[right].RelativeBounds))
                        findings.Add(new ArchitecturePlanningDiagnostic("GeometryCollision", "Two planned node rectangles overlap.", PlanningDiagnosticSubject.PhysicalNode, owned[left].PhysicalNodeId + ":" + owned[right].PhysicalNodeId));
        }
    }

    private static bool Contains(RelativeRectangle outer, RelativeRectangle inner) =>
        inner.X >= outer.X && inner.Y >= outer.Y && inner.X + inner.Width <= outer.X + outer.Width && inner.Y + inner.Height <= outer.Y + outer.Height;

    private static bool Intersects(RelativeRectangle left, RelativeRectangle right) =>
        left.X < right.X + right.Width && right.X < left.X + left.Width && left.Y < right.Y + right.Height && right.Y < left.Y + left.Height;

    private ProjectOutput BuildProject(string projectId, int diagramX)
    {
        var projectPlacements = placements.Where(item => GridProject(item.GridId) == projectId).ToArray();
        var source = sourceGrids.FirstOrDefault(item => item.ProjectId == projectId);
        var columnCount = Math.Max(1, projectPlacements.SelectMany(item => item.Footprint).Select(item => ParseColumn(item.ColumnId)).DefaultIfEmpty(0).Max() + 1);
        var rowCount = Math.Max(1, projectPlacements.Select(item => ParseRow(item.AnchorCellId.RowId)).DefaultIfEmpty(0).Max() + 1);
        // Covered columns are logical tracks, not visible gaps. Their extents are
        // established by node sizing below; empty separator columns carry spacing.
        var columnExtents = Enumerable.Repeat(1, columnCount).ToArray();
        var coveredColumns = new HashSet<int>(projectPlacements.SelectMany(item => item.Footprint).Select(item => ParseColumn(item.ColumnId)));
        for (var column = 0; column < columnExtents.Length; column++)
            if (!coveredColumns.Contains(column))
                columnExtents[column] = Math.Max(1, request.NodePlacement.HorizontalSpacing);
        var rowExtents = Enumerable.Repeat(Math.Max(1, request.GridSizing.CellHeight), rowCount).ToArray();
        foreach (var placement in projectPlacements)
        {
            var node = nodesById[placement.PhysicalNodeId];
            var requiredWidth = NodeWidth(node);
            var footprintColumns = placement.Footprint.Select(item => ParseColumn(item.ColumnId)).Distinct().OrderBy(item => item).ToArray();
            var perColumn = (requiredWidth + footprintColumns.Length - 1) / footprintColumns.Length;
            foreach (var column in footprintColumns) columnExtents[column] = Math.Max(columnExtents[column], perColumn);
            rowExtents[ParseRow(placement.AnchorCellId.RowId)] = Math.Max(rowExtents[ParseRow(placement.AnchorCellId.RowId)], NodeHeight(node));
        }
        // Logical columns inside a footprint are contiguous. Unoccupied separator columns
        // carry the normal horizontal gap, preventing spacing from multiplying per footprint cell.
        var columnOffsets = Offsets(columnExtents, 0);
        var rowOffsets = RowOffsets(rowExtents, projectPlacements);
        var contentWidth = columnOffsets.Last() + columnExtents.Last();
        var contentHeight = rowOffsets.Last() + rowExtents.Last();
        var padding = Math.Max(0, request.GridSizing.ContainerPadding);
        var header = Math.Max(0, request.GridSizing.ProjectHeaderHeight);
        var gridId = new PlanningGridId($"project:{projectId}");
        var rows = rowExtents.Select((extent, index) => new PlanningGridRow(new PlanningGridRowId($"row:{index}"), index, extent, extent, extent, rowOffsets[index], rowOffsets[index])).ToArray();
        var columns = columnExtents.Select((extent, index) => new PlanningGridColumn(new PlanningGridColumnId($"column:{index}"), index, extent, extent, extent, columnOffsets[index], columnOffsets[index])).ToArray();
        var sizedCells = BuildCells(gridId, rows, columns, source);
        var grid = new PlanningGrid(gridId, rows, columns, sizedCells, new GridTransform(gridId, new RelativePoint(diagramX + padding, header + padding)));
        var nodeOutput = NormalizeHorizontalGaps(projectPlacements.Select(placement => BuildNodeGeometry(placement, grid, diagramX, padding, header)).ToArray(), diagramX);
        contentWidth = Math.Max(contentWidth, nodeOutput.Select(node => node.RelativeBounds.X + node.RelativeBounds.Width).DefaultIfEmpty(padding).Max() - padding);
        var relativeBounds = new RelativeRectangle(0, 0, contentWidth + padding * 2, contentHeight + header + padding * 2);
        var absoluteBounds = new AbsoluteRectangle(diagramX, 0, relativeBounds.Width, relativeBounds.Height);
        var relativeLabel = new RelativeRectangle(padding, padding, Math.Max(1, contentWidth), header);
        var absoluteLabel = new AbsoluteRectangle(diagramX + relativeLabel.X, relativeLabel.Y, relativeLabel.Width, relativeLabel.Height);
        var gridOutput = new PlannedGridGeometry(gridId, new RelativeRectangle(padding, header + padding, contentWidth, contentHeight), new AbsoluteRectangle(diagramX + padding, header + padding, contentWidth, contentHeight), grid.Transform, rows, columns);
        var projectNodes = projectPlacements.Select(item => item.PhysicalNodeId).ToArray();
        var projectGrid = new ProjectRoutingGrid(projectId, grid,
            reservations.Where(item => item.GridId.Equals(gridId)).ToArray(), relativeLabel,
            projectNodes, projectNodes.Where(id => nodesById[id].IsExternal).ToArray(), $"project:{projectId}");
        return new ProjectOutput(projectId, relativeBounds, new PlannedProjectGeometry(projectId, relativeBounds, absoluteBounds, relativeLabel, absoluteLabel, projectNodes), projectGrid, nodeOutput, new[] { gridOutput });
    }

    private IReadOnlyList<PlannedPhysicalNodeGeometry> NormalizeHorizontalGaps(
        IReadOnlyList<PlannedPhysicalNodeGeometry> nodes,
        int diagramX)
    {
        var result = nodes.ToArray();
        foreach (var group in result.GroupBy(node => node.RelativeBounds.Y).OrderBy(group => group.Key))
        {
            PlannedPhysicalNodeGeometry? previous = null;
            foreach (var node in group.OrderBy(item => item.RelativeBounds.X).ToArray())
            {
                var current = node;
                if (previous is not null)
                {
                    var desiredX = previous.RelativeBounds.X + previous.RelativeBounds.Width + Gap(previous.PhysicalNodeId, current.PhysicalNodeId).Value;
                    var delta = desiredX - node.RelativeBounds.X;
                    if (delta != 0)
                    {
                        var relative = node.RelativeBounds with { X = desiredX };
                        var absolute = node.AbsoluteBounds with { X = diagramX + desiredX };
                        var index = Array.IndexOf(result, node);
                        result[index] = node with { RelativeBounds = relative, AbsoluteBounds = absolute };
                        current = result[index];
                    }
                }
                previous = current;
            }
        }
        return result;
    }

    private (int Value, string Policy) Gap(string leftId, string rightId)
    {
        if (!metadataById.TryGetValue(leftId, out var left) || !metadataById.TryGetValue(rightId, out var right))
            return (Spacing.Sibling, "sibling");
        if (left.IsExternal || right.IsExternal) return (Spacing.External, "external");
        if (Role(left.RoleSelector) == "Broker" || Role(right.RoleSelector) == "Broker") return (Spacing.Broker, "broker");
        if (!string.Equals(left.OwnershipGroup, right.OwnershipGroup, StringComparison.Ordinal)) return (Spacing.OwnershipBoundary, "ownership-boundary");
        if (left.RoleBand != right.RoleBand) return (Spacing.RoleBand, "role-band");
        if (!string.Equals(left.SiblingGroup, right.SiblingGroup, StringComparison.Ordinal)) return (Spacing.SiblingGroup, "sibling-group");
        return (Spacing.Sibling, "sibling");
    }

    private static string Role(string selector) => selector.EndsWith("Broker", StringComparison.OrdinalIgnoreCase) ? "Broker" : selector;

    private int[] RowOffsets(IReadOnlyList<int> extents, IReadOnlyList<PlannedNodePlacement> projectPlacements)
    {
        var rows = projectPlacements.GroupBy(item => ParseRow(item.AnchorCellId.RowId))
            .ToDictionary(group => group.Key, group => group.Select(item => metadataById[item.PhysicalNodeId]).ToArray());
        var offsets = new int[extents.Count];
        for (var row = 1; row < extents.Count; row++)
        {
            var previous = rows.TryGetValue(row - 1, out var previousItems) ? previousItems : Array.Empty<PhysicalNodePlacementMetadata>();
            var current = rows.TryGetValue(row, out var currentItems) ? currentItems : Array.Empty<PhysicalNodePlacementMetadata>();
            var sameLayer = previous.Length > 0 && current.Length > 0 && previous[0].LogicalLayer == current[0].LogicalLayer;
            var policy = current.Any(item => item.IsExternal) ? Spacing.External : sameLayer ? Spacing.RoleBand : Spacing.LogicalLayer;
            offsets[row] = offsets[row - 1] + extents[row - 1] + policy;
        }
        return offsets;
    }

    private ArchitectureV6SpacingPolicy Spacing => request.NodePlacement.Spacing ?? ArchitectureV6SpacingPolicy.From(request.NodePlacement.HorizontalSpacing);

    private PlannedPhysicalNodeGeometry BuildNodeGeometry(PlannedNodePlacement placement, PlanningGrid grid, int diagramX, int padding, int header)
    {
        var node = nodesById[placement.PhysicalNodeId];
        var footprintColumns = placement.Footprint.Select(item => ParseColumn(item.ColumnId)).Distinct().OrderBy(item => item).ToArray();
        var first = footprintColumns.First();
        var last = footprintColumns.Last();
        var left = grid.Columns[first].RelativeOffset;
        var right = grid.Columns[last].RelativeOffset + grid.Columns[last].FinalExtent;
        var row = ParseRow(placement.AnchorCellId.RowId);
        var top = grid.Rows[row].RelativeOffset;
        var availableWidth = right - left;
        var availableHeight = grid.Rows[row].FinalExtent;
        var width = Math.Min(availableWidth, NodeWidth(node));
        var height = Math.Min(availableHeight, NodeHeight(node));
        var relative = new RelativeRectangle(padding + left + (availableWidth - width) / 2, header + padding + top + (availableHeight - height) / 2, width, height);
        var absolute = new AbsoluteRectangle(diagramX + relative.X, relative.Y, relative.Width, relative.Height);
        var metadataItem = metadata.FirstOrDefault(item => item.PhysicalNodeId == node.PhysicalNodeId);
        return new PlannedPhysicalNodeGeometry(node.PhysicalNodeId, node.SemanticNodeId, node.ProjectId, relative, absolute, placement.GridId, placement.AnchorCellId, metadataItem?.PositionalOwnerId, node.ProjectionMode, node.IsExternal, node.IsStandalone);
    }

    private IReadOnlyList<PlannedSubtreeGeometry> BuildSubtreeGeometry(IReadOnlyList<PlannedPhysicalNodeGeometry> nodeGeometry, IReadOnlyList<ProjectOutput> outputs)
    {
        var result = new List<PlannedSubtreeGeometry>();
        foreach (var reservation in reservations)
        {
            var members = reservation.Cells.Select(cell => nodeGeometry.FirstOrDefault(node => node.GridId.Equals(reservation.GridId) && node.AnchorCellId.Equals(cell)))
                .Where(node => node is not null).Cast<PlannedPhysicalNodeGeometry>().ToArray();
            if (members.Length == 0) continue;
            var bounds = Bounds(members.Select(node => node.RelativeBounds));
            var projectOffset = outputs.First(output => output.ProjectId == GridProject(reservation.GridId)).Geometry.AbsoluteBounds;
            var absolute = new AbsoluteRectangle(projectOffset.X + bounds.X, projectOffset.Y + bounds.Y, bounds.Width, bounds.Height);
            result.Add(new PlannedSubtreeGeometry(reservation.SubtreeId, reservation.PositionalOwnerId, reservation.GridId, bounds, absolute, reservation.AncestorReservationId));
        }
        return result;
    }

    private Dictionary<PlanningGridCellId, PlanningGridCell> BuildCells(PlanningGridId gridId, IReadOnlyList<PlanningGridRow> rows, IReadOnlyList<PlanningGridColumn> columns, ProjectRoutingGrid? source)
    {
        var cells = new Dictionary<PlanningGridCellId, PlanningGridCell>();
        foreach (var row in rows)
            foreach (var column in columns)
            {
                var id = new PlanningGridCellId(gridId, row.Id, column.Id);
                var old = source?.Grid.Cells.FirstOrDefault(item => item.Key.Equals(id)).Value;
                cells[id] = old is null ? new PlanningGridCell(id, CellCapability.RoutingAllowed | CellCapability.NodeAllowed, CellOccupancy.Empty, Array.Empty<string>()) : old with { Id = id };
            }
        return cells;
    }

    private IReadOnlyList<GridTrackConstraint> BuildConstraints(IReadOnlyList<ProjectOutput> outputs)
    {
        return outputs.SelectMany(output => output.Grid.Grid.Columns.Select(column => new GridTrackConstraint(TrackConstraintKind.SingleColumnMinimum, output.Grid.Grid.Id, Array.Empty<PlanningGridRowId>(), new[] { column.Id }, column.FinalExtent, "Node and label sizing", null))).ToArray();
    }

    private IReadOnlyList<string> ProjectOrder()
    {
        var selected = request.SelectedScope.SelectedProjectIds ?? Array.Empty<string>();
        var all = sourceGrids.Select(grid => grid.ProjectId).Distinct(StringComparer.Ordinal).ToArray();
        return selected.Concat(all).Distinct(StringComparer.Ordinal).ToArray();
    }

    private string GridProject(PlanningGridId gridId) => gridId.Value.StartsWith("project:", StringComparison.Ordinal) ? gridId.Value.Substring("project:".Length) : gridId.Value;
    private int NodeWidth(PlannedPhysicalNode node) => Math.Max(request.NodePlacement.MinimumNodeWidth, (node.SemanticName ?? node.SemanticNodeId).Length * 7 + request.GridSizing.ContainerPadding * 2);
    private int NodeHeight(PlannedPhysicalNode node) => Math.Max(request.NodePlacement.MinimumNodeHeight, request.GridSizing.CellHeight);
    private static int[] Offsets(IReadOnlyList<int> extents, int gap)
    {
        var offsets = new int[extents.Count];
        var current = 0;
        for (var index = 0; index < extents.Count; index++) { offsets[index] = current; current += extents[index] + Math.Max(0, gap); }
        return offsets;
    }
    private static RelativeRectangle Bounds(IEnumerable<RelativeRectangle> rectangles)
    {
        var values = rectangles.ToArray();
        var minX = values.Min(item => item.X); var minY = values.Min(item => item.Y);
        var maxX = values.Max(item => item.X + item.Width); var maxY = values.Max(item => item.Y + item.Height);
        return new RelativeRectangle(minX, minY, maxX - minX, maxY - minY);
    }
    private static int ParseRow(PlanningGridRowId id) => int.Parse(id.Value.Substring(id.Value.LastIndexOf(':') + 1));
    private static int ParseColumn(PlanningGridColumnId id) => int.Parse(id.Value.Substring(id.Value.LastIndexOf(':') + 1));

    internal sealed record GeometryBuildResult(
        PlannedArchitectureGeometry Geometry,
        IReadOnlyList<ProjectRoutingGrid> ProjectGrids,
        DiagramRoutingGrid DiagramGrid,
        GridTrackSizingPlan Sizing,
        IReadOnlyList<ArchitecturePlanningDiagnostic> Findings);

    private sealed record ProjectOutput(
        string ProjectId,
        RelativeRectangle RelativeBounds,
        PlannedProjectGeometry Geometry,
        ProjectRoutingGrid Grid,
        IReadOnlyList<PlannedPhysicalNodeGeometry> Nodes,
        IReadOnlyList<PlannedGridGeometry> Grids);
}
