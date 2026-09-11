using System;
using System.Collections.Generic;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core.Models.ArchitectureV7;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.ArchitectureV7;

public sealed record ArchitectureV7GridLayerAudit(
    int UpperNodeRow,
    int LowerNodeRow,
    IReadOnlyList<int> InterveningRows,
    IReadOnlyList<int> GeneralRoutingRows,
    IReadOnlyList<int> UpperFreeNodeColumns,
    IReadOnlyList<int> LowerFreeNodeColumns,
    bool ExplicitBoundary,
    bool RoutingRowPresent)
{
    // Kept as a compatibility projection for evidence consumers from the first audit pass.
    public IReadOnlyList<int> FreeVerticalPassthroughColumns => UpperFreeNodeColumns
        .Intersect(LowerFreeNodeColumns)
        .OrderBy(column => column)
        .ToArray();
}

public sealed record ArchitectureV7GridSeparationAudit(
    int Row,
    string LeftNodeId,
    string RightNodeId,
    int Gap);

public sealed record ArchitectureV7GridStructureAuditResult(
    IReadOnlyList<ArchitectureV7GridLayerAudit> Layers,
    IReadOnlyList<ArchitectureV7GridSeparationAudit> Separations,
    IReadOnlyList<string> Findings)
{
    public bool IsValid => Findings.Count == 0;
}

public static class ArchitectureV7GridStructureAudit
{
    public static ArchitectureV7GridStructureAuditResult Audit(ArchitectureV7PlacementFreeze placement)
    {
        if (placement is null) throw new ArgumentNullException(nameof(placement));
        var cells = placement.DiagramGrid.Cells.ToDictionary(cell => (cell.Row, cell.Column));
        var findings = new List<string>();
        var layers = new List<ArchitectureV7GridLayerAudit>();
        var separations = new List<ArchitectureV7GridSeparationAudit>();
        var nodeRows = placement.Nodes.Select(node => node.DiagramRow).Distinct().OrderBy(row => row).ToArray();

        foreach (var (upper, lower) in nodeRows.Zip(nodeRows.Skip(1), (upper, lower) => (upper, lower)))
        {
            var intervening = Enumerable.Range(upper + 1, Math.Max(0, lower - upper - 1)).ToArray();
            var general = intervening.Where(row => HasCapability(cells, row, ArchitectureV7CellCapability.GeneralRouting)).ToArray();
            var explicitBoundary = intervening.Length > 0 && intervening.All(row => HasCapability(cells, row, ArchitectureV7CellCapability.NonRoutingSeparator));
            var upperFree = FreeNodeColumns(cells, upper, placement.DiagramGrid.ColumnCount);
            var lowerFree = FreeNodeColumns(cells, lower, placement.DiagramGrid.ColumnCount);
            var routingPresent = general.Length > 0;
            layers.Add(new ArchitectureV7GridLayerAudit(upper, lower, intervening, general, upperFree, lowerFree, explicitBoundary, routingPresent));

            if (intervening.Length == 0 || (!routingPresent && !explicitBoundary))
                findings.Add($"GRID-MISSING-ROUTING-LAYER:{upper}->{lower}");
            // The two node rows do not need the same passthrough column. A continuation
            // may move columns on an intervening GeneralRouting row. Each node row must,
            // however, expose at least one unoccupied NodeAllowed cell for a vertical
            // continuation through that layer.
            if (upperFree.Length == 0 || lowerFree.Length == 0)
                findings.Add($"GRID-NO-VERTICAL-PASSTHROUGH:{upper}->{lower}:upper={upperFree.Length}:lower={lowerFree.Length}");
        }

        foreach (var group in placement.Nodes.GroupBy(node => node.DiagramRow))
        {
            var ordered = group.OrderBy(node => node.DiagramColumn).ThenBy(node => node.PhysicalNodeId, StringComparer.Ordinal).ToArray();
            for (var index = 1; index < ordered.Length; index++)
            {
                var left = ordered[index - 1];
                var right = ordered[index];
                var gap = right.DiagramColumn - (left.DiagramColumn + left.LogicalSpan);
                separations.Add(new ArchitectureV7GridSeparationAudit(group.Key, left.PhysicalNodeId, right.PhysicalNodeId, gap));
                if (gap < 1) findings.Add($"GRID-NODE-SPAN-TOUCH:{group.Key}:{left.PhysicalNodeId}:{right.PhysicalNodeId}:gap={gap}");
            }
        }

        return new ArchitectureV7GridStructureAuditResult(layers, separations, findings);
    }

    private static bool HasCapability(IReadOnlyDictionary<(int Row, int Column), ArchitectureV7LogicalCell> cells,
        int row, ArchitectureV7CellCapability capability) => cells.Values.Any(cell => cell.Row == row && cell.Capabilities.HasFlag(capability));

    private static int[] FreeNodeColumns(IReadOnlyDictionary<(int Row, int Column), ArchitectureV7LogicalCell> cells,
        int row, int columnCount) => Enumerable.Range(0, columnCount)
        .Where(column => cells.TryGetValue((row, column), out var cell) && cell.OccupantId is null &&
            (cell.Capabilities.HasFlag(ArchitectureV7CellCapability.NodeAllowed) ||
             cell.Capabilities.HasFlag(ArchitectureV7CellCapability.RoutingAllowed)) &&
            !cell.Capabilities.HasFlag(ArchitectureV7CellCapability.Blocked) &&
            !cell.Capabilities.HasFlag(ArchitectureV7CellCapability.HeaderBlocked) &&
            !cell.Capabilities.HasFlag(ArchitectureV7CellCapability.NonRoutingSeparator))
        .ToArray();
}
