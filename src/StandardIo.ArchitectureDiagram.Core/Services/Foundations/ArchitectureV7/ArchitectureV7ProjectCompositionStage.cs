using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using StandardIo.ArchitectureDiagram.Core.Models.ArchitectureV7;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.ArchitectureV7;

public sealed class ArchitectureV7ProjectCompositionStage
{
    public ArchitectureV7PlacementFreeze Compose(ArchitectureV7RecursiveTreeGridResult trees)
    {
        if (trees is null) throw new ArgumentNullException(nameof(trees));
        var projection = trees.Sizing.Ownership.Projection;
        var nodes = projection.PhysicalNodes.ToDictionary(node => node.PhysicalNodeId, StringComparer.Ordinal);
        var sizing = trees.Sizing.Requirements.ToDictionary(item => item.PhysicalNodeId, StringComparer.Ordinal);
        var ownership = trees.Sizing.Ownership.Decisions.ToDictionary(item => item.PhysicalNodeId, StringComparer.Ordinal);
        var ordinaryTrees = trees.Trees.Where(tree => tree.Placements.Any(placement => nodes[placement.PhysicalNodeId].ProjectId is not null && !nodes[placement.PhysicalNodeId].IsExternal && !nodes[placement.PhysicalNodeId].IsStandalone)).ToArray();
        var standaloneIds = nodes.Values.Where(node => node.IsStandalone && !node.IsExternal).Select(node => node.PhysicalNodeId).OrderBy(id => id, StringComparer.Ordinal).ToArray();
        var projectIds = nodes.Values.Where(node => node.ProjectId is not null && !node.IsExternal).Select(node => node.ProjectId!).Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal).ToArray();

        var projects = new List<ArchitectureV7ProjectRegion>();
        var transforms = new List<ArchitectureV7ProjectTransform>();
        var placements = new List<ArchitectureV7FrozenNodePlacement>();
        var occupied = new HashSet<(int Row, int Column)>();
        var projectCursor = 0;
        foreach (var projectId in projectIds)
        {
            var projectTrees = ordinaryTrees.Where(tree => nodes[tree.RootPhysicalNodeId].ProjectId == projectId).ToArray();
            var interiorWidth = projectTrees.Length == 0 ? 1 : projectTrees.Sum(tree => tree.Width) + projectTrees.Length - 1;
            var interiorHeight = projectTrees.Length == 0 ? 1 : Math.Max(1, projectTrees.Max(tree => tree.Height) + 1);
            var transform = new ArchitectureV7ProjectTransform(projectId, 0, projectCursor, 2, projectCursor + 2,
                interiorWidth + 4, interiorHeight + 4);
            transforms.Add(transform);
            var projectCells = BuildProjectCells(transform, interiorWidth, interiorHeight);
            projects.Add(new ArchitectureV7ProjectRegion(projectId, transform, projectTrees.Select(tree => tree.TreeId).ToArray(), projectCells, interiorWidth, interiorHeight));
            var treeCursor = 0;
            foreach (var tree in projectTrees)
            {
                foreach (var placement in tree.Placements.Where(item => !nodes[item.PhysicalNodeId].IsExternal && !nodes[item.PhysicalNodeId].IsStandalone))
                {
                    var finalRow = transform.InteriorOriginRow + placement.LocalRow + 1;
                    var finalColumn = transform.InteriorOriginColumn + treeCursor + placement.LocalColumn;
                    var frozen = FreezeNode(nodes[placement.PhysicalNodeId], placement, finalRow, finalColumn, tree.TreeId, sizing);
                    placements.Add(frozen);
                    foreach (var cell in frozen.LogicalFootprint) occupied.Add(cell);
                }
                treeCursor = checked(treeCursor + tree.Width + 1);
            }
            projectCursor = checked(projectCursor + transform.Width + 1);
        }

        var externalRow = ArchitectureV7ReservationCoordinates.FinalCommonNodeRowFromReservedNodeRow(trees.Reservations.External.NodeRow);
        var externalPlacements = new List<ArchitectureV7FrozenNodePlacement>();
        var commonWidth = Math.Max(1, projectCursor == 0 ? 0 : projectCursor - 1);
        foreach (var node in nodes.Values.Where(node => node.IsExternal).OrderBy(node => node.PhysicalNodeId, StringComparer.Ordinal))
        {
            var span = sizing[node.PhysicalNodeId].LogicalSpan;
            var owner = ownership[node.PhysicalNodeId].PositionalParentPhysicalNodeId;
            var preferred = owner is not null ? placements.FirstOrDefault(item => item.PhysicalNodeId == owner)?.CentreCell ?? 0 : 0;
            var centre = FindExternalCentre(preferred, span, externalRow, occupied, ref commonWidth);
            var frozen = FreezeNode(node, null, externalRow, centre - (span - 1) / 2, "external-region", sizing, true, "[External] " + node.Name);
            externalPlacements.Add(frozen);
            placements.Add(frozen);
            foreach (var cell in frozen.LogicalFootprint) occupied.Add(cell);
        }
        var external = new ArchitectureV7ExternalRegion(externalRow, externalPlacements.Select(item => item.PhysicalNodeId).ToArray(), externalPlacements);

        var standalonePlacements = PlaceStandalone(nodes, sizing, standaloneIds, externalRow, occupied, ref commonWidth);
        placements.AddRange(standalonePlacements);
        var standalone = BuildStandaloneRegion(standalonePlacements, externalRow);
        var rowCount = Math.Max(externalRow + 1, Math.Max(projects.Count == 0 ? 0 : projects.Max(project => project.Transform.RegionOriginRow + project.Height), standalonePlacements.Count == 0 ? 0 : standalonePlacements.Max(item => item.DiagramRow) + 1));
        var columnCount = Math.Max(commonWidth, Math.Max(projects.Count == 0 ? 0 : projects.Max(project => project.Transform.RegionOriginColumn + project.Width), standalonePlacements.Count == 0 ? 0 : standalonePlacements.Max(item => item.DiagramColumn + item.LogicalSpan)));
        var grid = BuildDiagramGrid(rowCount, columnCount, projects, external, standalone, placements);
        var orderedNodes = placements.OrderBy(item => item.DiagramRow).ThenBy(item => item.DiagramColumn).ThenBy(item => item.PhysicalNodeId, StringComparer.Ordinal).ToArray();
        ArchitectureV7PlacementAccounting.Validate(projection, orderedNodes, external, standalone);
        var fingerprint = Fingerprint(trees, transforms, orderedNodes, grid);
        return new ArchitectureV7PlacementFreeze(orderedNodes, projects, external, standalone, grid, transforms,
            projection.FreezeFingerprint, trees.Sizing.Ownership.FreezeFingerprint, trees.Sizing.FreezeFingerprint,
            trees.Reservations.Fingerprint, fingerprint);
    }

    private static IReadOnlyList<ArchitectureV7LogicalCell> BuildProjectCells(ArchitectureV7ProjectTransform transform, int interiorWidth, int interiorHeight)
    {
        var cells = new List<ArchitectureV7LogicalCell>();
        for (var row = 0; row < transform.Height; row++)
            for (var column = 0; column < transform.Width; column++)
            {
                var outer = row == 0 || row == transform.Height - 1 || column == 0 || column == transform.Width - 1;
                var inner = row == 1 || row == transform.Height - 2 || column == 1 || column == transform.Width - 2;
                var capability = ArchitectureV7CellCapability.None;
                if (outer && !inner) capability |= ArchitectureV7CellCapability.RoutingAllowed | ArchitectureV7CellCapability.GeneralRouting;
                if (inner) capability |= ArchitectureV7CellCapability.RoutingAllowed | ArchitectureV7CellCapability.ProjectBoundary | ArchitectureV7CellCapability.StraightPassthroughOnly | (row == 1 ? ArchitectureV7CellCapability.HeaderBlocked : ArchitectureV7CellCapability.None);
                if (!outer && !inner) capability |= (row - 2) % 2 == 1
                    ? ArchitectureV7CellCapability.NodeAllowed
                    : ArchitectureV7CellCapability.RoutingAllowed | ArchitectureV7CellCapability.GeneralRouting;
                cells.Add(new ArchitectureV7LogicalCell(transform.RegionOriginRow + row, transform.RegionOriginColumn + column, capability));
            }
        return cells;
    }

    private static ArchitectureV7FrozenNodePlacement FreezeNode(ArchitectureV7PhysicalNode node, ArchitectureV7TreeGridNodePlacement? local, int row, int column, string treeId,
        IReadOnlyDictionary<string, ArchitectureV7NodeSpanRequirement> sizing, bool external = false, string? label = null)
    {
        var span = sizing[node.PhysicalNodeId].LogicalSpan;
        var centre = column + (span - 1) / 2;
        var footprint = Enumerable.Range(column, span).Select(item => (row, item)).ToArray();
        return new ArchitectureV7FrozenNodePlacement(node.PhysicalNodeId, node.SemanticNodeId, node.ProjectId, row, column, span, centre, footprint,
            external || node.IsExternal, node.IsStandalone, local?.IsDetached ?? false, treeId, label ?? node.Name, node.FullName,
            "v7-project-composition;tree=" + treeId);
    }

    private static int FindExternalCentre(int preferred, int span, int row, HashSet<(int Row, int Column)> occupied, ref int width)
    {
        for (var distance = 0; distance < width + span + 100; distance++)
            foreach (var centre in distance == 0 ? new[] { preferred } : new[] { preferred - distance, preferred + distance })
            {
                var left = centre - (span - 1) / 2;
                if (left < 0) continue;
                var right = left + span - 1;
                if (Enumerable.Range(left, span).All(column => !occupied.Contains((row, column))) &&
                    !occupied.Contains((row, left - 1)) && !occupied.Contains((row, right + 1)))
                {
                    width = Math.Max(width, left + span);
                    return centre;
                }
            }
        throw new InvalidOperationException("Unable to place an External node without moving ordinary trees.");
    }

    private static IReadOnlyList<ArchitectureV7FrozenNodePlacement> PlaceStandalone(
        IReadOnlyDictionary<string, ArchitectureV7PhysicalNode> nodes,
        IReadOnlyDictionary<string, ArchitectureV7NodeSpanRequirement> sizing,
        IReadOnlyList<string> standaloneIds,
        int externalRow,
        HashSet<(int Row, int Column)> occupied,
        ref int width)
    {
        if (standaloneIds.Count == 0) return Array.Empty<ArchitectureV7FrozenNodePlacement>();
        var targetWidth = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(standaloneIds.Sum(id => sizing[id].LogicalSpan + 1))));
        var row = externalRow + 2;
        var column = 0;
        var result = new List<ArchitectureV7FrozenNodePlacement>();
        foreach (var id in standaloneIds)
        {
            var span = sizing[id].LogicalSpan;
            if (column > 0 && column + span > targetWidth) { row += 2; column = 0; }
            var placement = FreezeNode(nodes[id], null, row, column, "standalone-region", sizing, false, nodes[id].Name);
            result.Add(placement);
            foreach (var cell in placement.LogicalFootprint) occupied.Add(cell);
            column += span + 1;
            width = Math.Max(width, column == 0 ? 0 : column - 1);
        }
        return result;
    }

    private static ArchitectureV7StandaloneRegion BuildStandaloneRegion(IReadOnlyList<ArchitectureV7FrozenNodePlacement> placements, int externalRow) =>
        new(placements.Count == 0 ? externalRow + 2 : placements.Min(item => item.DiagramRow), placements.Count == 0 ? 0 : placements.Max(item => item.DiagramColumn + item.LogicalSpan),
            placements.Count == 0 ? 0 : placements.Max(item => item.DiagramRow) - placements.Min(item => item.DiagramRow) + 1,
            placements.Select(item => item.PhysicalNodeId).ToArray(), placements);

    private static ArchitectureV7CommonDiagramGrid BuildDiagramGrid(int rows, int columns, IReadOnlyList<ArchitectureV7ProjectRegion> projects,
        ArchitectureV7ExternalRegion external, ArchitectureV7StandaloneRegion standalone, IReadOnlyList<ArchitectureV7FrozenNodePlacement> placements)
    {
        var cells = new Dictionary<(int Row, int Column), ArchitectureV7CellCapability>();
        for (var row = 0; row < rows; row++)
            for (var column = 0; column < columns; column++)
                cells[(row, column)] = ArchitectureV7CellCapability.RoutingAllowed | ArchitectureV7CellCapability.GeneralRouting;
        foreach (var project in projects)
            foreach (var cell in project.Cells) cells[(cell.Row, cell.Column)] = cell.Capabilities;
        foreach (var placement in placements)
        {
            foreach (var cell in placement.LogicalFootprint)
            {
                var existing = cells.TryGetValue(cell, out var capability) ? capability : ArchitectureV7CellCapability.None;
                var restricted = existing & (ArchitectureV7CellCapability.ProjectBoundary | ArchitectureV7CellCapability.StraightPassthroughOnly | ArchitectureV7CellCapability.HeaderBlocked);
                cells[cell] = restricted | ArchitectureV7CellCapability.NodeAllowed;
            }
        }
        return new ArchitectureV7CommonDiagramGrid(rows, columns, cells.OrderBy(item => item.Key.Row).ThenBy(item => item.Key.Column).Select(item => new ArchitectureV7LogicalCell(item.Key.Row, item.Key.Column, item.Value, placements.FirstOrDefault(node => node.LogicalFootprint.Contains(item.Key))?.PhysicalNodeId)).ToArray());
    }

    private static string Fingerprint(ArchitectureV7RecursiveTreeGridResult trees, IReadOnlyList<ArchitectureV7ProjectTransform> transforms,
        IReadOnlyList<ArchitectureV7FrozenNodePlacement> nodes, ArchitectureV7CommonDiagramGrid grid)
    {
        var text = trees.FreezeFingerprint + "#" + string.Join("|", transforms.Select(item => item.ProjectId + ":" + item.RegionOriginColumn + ":" + item.Width)) + "#" +
            string.Join("|", nodes.Select(item => item.PhysicalNodeId + ":" + item.DiagramRow + ":" + item.DiagramColumn + ":" + item.LogicalSpan)) + "#" + grid.RowCount + ":" + grid.ColumnCount;
        using var sha = SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", string.Empty);
    }
}
