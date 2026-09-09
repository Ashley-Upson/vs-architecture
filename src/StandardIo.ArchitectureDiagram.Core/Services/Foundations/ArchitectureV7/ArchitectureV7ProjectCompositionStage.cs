using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using StandardIo.ArchitectureDiagram.Core.Models.ArchitectureV7;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.ArchitectureV7;

public sealed class ArchitectureV7ProjectCompositionStage
{
    public ArchitectureV7PlacementFreeze Compose(ArchitectureV7RecursiveTreeGridResult trees) => Compose(trees, null, null);

    public ArchitectureV7PlacementFreeze Compose(ArchitectureV7RecursiveTreeGridResult trees, ArchitectureV7PrePlacementConfiguration? configuration)
        => Compose(trees, configuration, null);

    public ArchitectureV7PlacementFreeze Compose(ArchitectureV7RecursiveTreeGridResult trees, ArchitectureV7PrePlacementConfiguration? configuration,
        IReadOnlyDictionary<string, string>? projectDisplayNames)
    {
        if (trees is null) throw new ArgumentNullException(nameof(trees));
        var projection = trees.Sizing.Ownership.Projection;
        var nodes = projection.PhysicalNodes.ToDictionary(node => node.PhysicalNodeId, StringComparer.Ordinal);
        var sizing = trees.Sizing.Requirements.ToDictionary(item => item.PhysicalNodeId, StringComparer.Ordinal);
        var ownership = trees.Sizing.Ownership.Decisions.ToDictionary(item => item.PhysicalNodeId, StringComparer.Ordinal);
        var ordinaryTrees = trees.Trees
            .OrderBy(tree => tree.AnalyserOrdinal < 0 ? int.MaxValue : tree.AnalyserOrdinal)
            .ThenBy(tree => tree.TreeId, StringComparer.Ordinal)
            .Where(tree => tree.Placements.Any(placement => nodes[placement.PhysicalNodeId].ProjectId is not null && !nodes[placement.PhysicalNodeId].IsExternal && !nodes[placement.PhysicalNodeId].IsStandalone))
            .ToArray();
        // Standalone classification owns the physical region. A reservation
        // role may still be present on a standalone node, but it must not put
        // that node back inside an ordinary project layer.
        var standaloneIds = nodes.Values.Where(node => node.IsStandalone && !node.IsExternal)
            .Select(node => node.PhysicalNodeId).OrderBy(id => id, StringComparer.Ordinal).ToArray();
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
            var displayName = projectDisplayNames is not null && projectDisplayNames.TryGetValue(projectId, out var suppliedName) && !string.IsNullOrWhiteSpace(suppliedName)
                ? suppliedName : projectId;
            var headerTextWidth = HeaderTextCellCount(displayName, configuration);
            var treeCursor = 0;
            var projectNodeRows = new Dictionary<int, List<string>>();
            foreach (var tree in projectTrees)
            {
                foreach (var placement in tree.Placements.Where(item => !nodes[item.PhysicalNodeId].IsExternal && !nodes[item.PhysicalNodeId].IsStandalone))
                {
                    var finalRow = transform.InteriorOriginRow + placement.LocalRow + 1;
                    var finalColumn = transform.InteriorOriginColumn + treeCursor + placement.LocalColumn;
                    var frozen = FreezeNode(nodes[placement.PhysicalNodeId], placement, finalRow, finalColumn, tree.TreeId, sizing);
                    placements.Add(frozen);
                    if (!projectNodeRows.TryGetValue(finalRow, out var rowNodes))
                        projectNodeRows[finalRow] = rowNodes = new List<string>();
                    rowNodes.Add(frozen.PhysicalNodeId);
                    foreach (var cell in frozen.LogicalFootprint) occupied.Add(cell);
                }
                treeCursor = checked(treeCursor + tree.Width + 1);
            }
            var projectLayers = BuildProjectLayers(transform, projectNodeRows, trees);
            ValidateProjectLayerPlacements(projectLayers, projectNodeRows, nodes, trees);
            var projectCells = BuildProjectCells(transform, interiorWidth, interiorHeight, projectTrees, headerTextWidth, projectLayers);
            projects.Add(new ArchitectureV7ProjectRegion(projectId, transform, projectTrees.Select(tree => tree.TreeId).ToArray(), projectCells, interiorWidth, interiorHeight,
                displayName, projectLayers));
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

        var deepestProjectRow = projects.Count == 0 ? externalRow : projects.Max(project => project.Transform.RegionOriginRow + project.Height);
        var standaloneOriginRow = Math.Max(externalRow + 2, deepestProjectRow + 1);
        var standalonePlacements = PlaceStandalone(nodes, sizing, standaloneIds, standaloneOriginRow, occupied, ref commonWidth);
        placements.AddRange(standalonePlacements);
        var allStandalonePlacements = standalonePlacements.OrderBy(item => item.DiagramRow).ThenBy(item => item.DiagramColumn).ToArray();
        var standalone = BuildStandaloneRegion(allStandalonePlacements, externalRow);
        var rowCount = Math.Max(externalRow + 1, Math.Max(projects.Count == 0 ? 0 : projects.Max(project => project.Transform.RegionOriginRow + project.Height), allStandalonePlacements.Length == 0 ? 0 : allStandalonePlacements.Max(item => item.DiagramRow) + 1));
        var columnCount = Math.Max(commonWidth, Math.Max(projects.Count == 0 ? 0 : projects.Max(project => project.Transform.RegionOriginColumn + project.Width), allStandalonePlacements.Length == 0 ? 0 : allStandalonePlacements.Max(item => item.DiagramColumn + item.LogicalSpan)));
        var grid = BuildDiagramGrid(rowCount, columnCount, projects, external, standalone, placements);
        ValidateNodeFootprints(grid, placements);
        var orderedNodes = placements.OrderBy(item => item.DiagramRow).ThenBy(item => item.DiagramColumn).ThenBy(item => item.PhysicalNodeId, StringComparer.Ordinal).ToArray();
        ArchitectureV7PlacementAccounting.Validate(projection, orderedNodes, external, standalone);
        var fingerprint = Fingerprint(trees, transforms, orderedNodes, grid);
        return new ArchitectureV7PlacementFreeze(orderedNodes, projects, external, standalone, grid, transforms,
            projection.FreezeFingerprint, trees.Sizing.Ownership.FreezeFingerprint, trees.Sizing.FreezeFingerprint,
            trees.Reservations.Fingerprint, fingerprint);
    }

    private static IReadOnlyList<ArchitectureV7ProjectLayer> BuildProjectLayers(
        ArchitectureV7ProjectTransform transform,
        IReadOnlyDictionary<int, List<string>> nodeRows,
        ArchitectureV7RecursiveTreeGridResult trees)
    {
        var reservationByFrozenRow = trees.Reservations.Reservations
            .Where(item => !item.IsExternal)
            .ToDictionary(item => ArchitectureV7ReservationCoordinates.FinalCommonNodeRowFromReservedNodeRow(item.NodeRow), item => item.Name);
        var result = new List<ArchitectureV7ProjectLayer>();
        for (var localRow = 0; localRow < transform.Height; localRow++)
        {
            var logicalRow = transform.RegionOriginRow + localRow;
            var occupants = nodeRows.TryGetValue(logicalRow, out var ids)
                ? (IReadOnlyList<string>)ids.OrderBy(id => id, StringComparer.Ordinal).ToArray()
                : Array.Empty<string>();
            var reservationName = reservationByFrozenRow.TryGetValue(logicalRow, out var name) ? name : null;
            var outer = localRow == 0 || localRow == transform.Height - 1;
            var inner = localRow == 1 || localRow == transform.Height - 2;
            var nodeLayer = !outer && !inner && (localRow - 2) % 2 == 1;
            var type = reservationName is not null
                ? ArchitectureV7ProjectLayerType.HardReserved
                : localRow == 1
                    ? ArchitectureV7ProjectLayerType.ProjectHeader
                    : inner
                        ? ArchitectureV7ProjectLayerType.ProjectBoundary
                        : nodeLayer
                            ? ArchitectureV7ProjectLayerType.Free
                            : ArchitectureV7ProjectLayerType.Routing;
            result.Add(new ArchitectureV7ProjectLayer(logicalRow, localRow, type, reservationName, occupants));
        }
        return result;
    }

    private static void ValidateProjectLayerPlacements(
        IReadOnlyList<ArchitectureV7ProjectLayer> layers,
        IReadOnlyDictionary<int, List<string>> nodeRows,
        IReadOnlyDictionary<string, ArchitectureV7PhysicalNode> nodes,
        ArchitectureV7RecursiveTreeGridResult trees)
    {
        var byRow = layers.ToDictionary(layer => layer.LogicalRow);
        foreach (var item in nodeRows)
        {
            if (!byRow.TryGetValue(item.Key, out var layer) ||
                (layer.Type != ArchitectureV7ProjectLayerType.Free && layer.Type != ArchitectureV7ProjectLayerType.HardReserved))
                throw new InvalidOperationException($"V7 project node placement is illegal on layer row {item.Key}; layerType={layer?.Type.ToString() ?? "missing"};nodes={string.Join(",", item.Value)}");
            foreach (var id in item.Value)
            {
                if (!nodes.TryGetValue(id, out var node)) continue;
                var reserved = trees.ReservedNodeRowByPhysicalNodeId.TryGetValue(id, out var reservedRow);
                if (reserved && layer.Type != ArchitectureV7ProjectLayerType.HardReserved)
                    throw new InvalidOperationException($"V7 reserved node {id} was placed on non-reserved project layer row {item.Key};reservedRow={reservedRow}");
            }
        }
    }

    private static IReadOnlyList<ArchitectureV7LogicalCell> BuildProjectCells(ArchitectureV7ProjectTransform transform, int interiorWidth, int interiorHeight, IReadOnlyList<ArchitectureV7TopLevelTreeGrid> projectTrees, int headerTextWidth, IReadOnlyList<ArchitectureV7ProjectLayer> layers)
    {
        var layerByRow = layers.ToDictionary(layer => layer.LogicalRow);
        var cells = new List<ArchitectureV7LogicalCell>();
        for (var row = 0; row < transform.Height; row++)
            for (var column = 0; column < transform.Width; column++)
            {
                var outer = row == 0 || row == transform.Height - 1 || column == 0 || column == transform.Width - 1;
                var inner = row == 1 || row == transform.Height - 2 || column == 1 || column == transform.Width - 2;
                var layer = layerByRow[transform.RegionOriginRow + row];
                var capability = ArchitectureV7CellCapability.None;
                if (outer && !inner) capability |= ArchitectureV7CellCapability.RoutingAllowed | ArchitectureV7CellCapability.GeneralRouting;
                if (inner) capability |= ArchitectureV7CellCapability.RoutingAllowed | ArchitectureV7CellCapability.ProjectBoundary | ArchitectureV7CellCapability.StraightPassthroughOnly;
                if (row == 1 && column >= 2 && column < 2 + headerTextWidth)
                    capability |= ArchitectureV7CellCapability.Blocked | ArchitectureV7CellCapability.HeaderBlocked;
                if (!outer && !inner) capability |= layer.Type == ArchitectureV7ProjectLayerType.Routing
                    ? ArchitectureV7CellCapability.RoutingAllowed | ArchitectureV7CellCapability.GeneralRouting
                    : ArchitectureV7CellCapability.NodeAllowed;
                cells.Add(new ArchitectureV7LogicalCell(transform.RegionOriginRow + row, transform.RegionOriginColumn + column, capability));
            }
        var treeCursor = 0;
        foreach (var tree in projectTrees)
        {
            foreach (var cell in tree.Cells)
            {
                if (cell.Row >= interiorHeight || cell.Column >= tree.Width) continue;
                var logicalRow = transform.InteriorOriginRow + cell.Row + 1;
                var layer = layerByRow[logicalRow];
                var capability = cell.OccupantId is not null
                    ? ArchitectureV7CellCapability.Blocked
                    : layer.Type == ArchitectureV7ProjectLayerType.Routing
                        ? ArchitectureV7CellCapability.RoutingAllowed | ArchitectureV7CellCapability.GeneralRouting
                        : ArchitectureV7CellCapability.NodeAllowed;
                cells.Add(new ArchitectureV7LogicalCell(
                    logicalRow,
                    transform.InteriorOriginColumn + treeCursor + cell.Column,
                    capability,
                    cell.OccupantId));
            }
            treeCursor = checked(treeCursor + tree.Width + 1);
        }
        return cells;
    }
    private static void ValidateNodeFootprints(ArchitectureV7CommonDiagramGrid grid, IReadOnlyList<ArchitectureV7FrozenNodePlacement> placements)
    {
        var cells = grid.Cells.ToDictionary(cell => (cell.Row, cell.Column));
        foreach (var placement in placements)
            foreach (var coordinate in placement.LogicalFootprint)
            {
                if (!cells.TryGetValue(coordinate, out var cell) ||
                    (cell.Capabilities & (ArchitectureV7CellCapability.HeaderBlocked | ArchitectureV7CellCapability.NonRoutingSeparator)) != 0)
                    throw new InvalidOperationException($"V7 node footprint {placement.PhysicalNodeId} occupies a non-node-capable project/header/boundary cell at ({coordinate.Row},{coordinate.Column}).");
            }
    }

    private static int HeaderTextCellCount(string projectId, ArchitectureV7PrePlacementConfiguration? configuration)
    {
        if (configuration is null) return 1;
        var requiredPixels = projectId.Length * Math.Max(1, configuration.LabelCharacterWidth) + 2 * Math.Max(0, configuration.LabelHorizontalMargin);
        return Math.Max(1, (int)Math.Ceiling(requiredPixels / (double)Math.Max(1, configuration.ConfiguredBaseCellWidth)));
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
        int standaloneOriginRow,
        HashSet<(int Row, int Column)> occupied,
        ref int width)
    {
        if (standaloneIds.Count == 0) return Array.Empty<ArchitectureV7FrozenNodePlacement>();
        var targetWidth = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(standaloneIds.Sum(id => sizing[id].LogicalSpan + 1))));
        var row = standaloneOriginRow;
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
        // Only the ordinary standalone region owns a separator above its first
        // physical row. Reserved standalone placements can be interleaved with
        // ordinary project layers; they remain routable node rows and must not
        // turn their preceding GeneralRouting row into a global separator.
        var separatorRows = new HashSet<int>(standalone.Placements
            .Where(item => string.Equals(item.TreeId, "standalone-region", StringComparison.Ordinal))
            .Select(item => item.DiagramRow - 1));
        if (standalone.Placements.Count > 0) separatorRows.Add(external.NodeRow + 1);
        foreach (var row in separatorRows.Where(row => row >= 0 && row < rows))
            for (var column = 0; column < columns; column++)
                cells[(row, column)] = ArchitectureV7CellCapability.NonRoutingSeparator;
        foreach (var placement in placements)
        {
            foreach (var cell in placement.LogicalFootprint)
            {
                var existing = cells.TryGetValue(cell, out var capability) ? capability : ArchitectureV7CellCapability.None;
                if ((existing & (ArchitectureV7CellCapability.Blocked | ArchitectureV7CellCapability.NonRoutingSeparator)) != 0) continue;
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
