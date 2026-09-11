using System;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core.Models.ArchitectureV7;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.ArchitectureV7;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core.Tests;

public sealed class ArchitectureV7GridStructureTests
{
    [Fact]
    public void Layer_crossing_can_change_column_on_an_intervening_general_routing_row()
    {
        var placement = Freeze(
            Node("upper", 2, 0),
            Node("lower", 4, 2),
            Grid(5, 5, new[] { (2, 0), (4, 2) }, Array.Empty<int>()));

        var audit = ArchitectureV7GridStructureAudit.Audit(placement);

        Assert.DoesNotContain(audit.Findings, finding => finding.StartsWith("GRID-NO-VERTICAL-PASSTHROUGH", StringComparison.Ordinal));
        Assert.Equal(new[] { 1, 2, 3, 4 }, audit.Layers.Single().UpperFreeNodeColumns);
        Assert.Equal(new[] { 0, 1, 3, 4 }, audit.Layers.Single().LowerFreeNodeColumns);
    }

    [Fact]
    public void Occupied_node_row_without_a_free_cell_is_rejected()
    {
        var placement = Freeze(
            Node("upper", 2, 0),
            Node("lower-a", 4, 0),
            Node("lower-b", 4, 1),
            Grid(5, 2, new[] { (2, 0), (4, 0), (4, 1) }, Array.Empty<int>()));

        var audit = ArchitectureV7GridStructureAudit.Audit(placement);

        Assert.Contains(audit.Findings, finding => finding.StartsWith("GRID-NO-VERTICAL-PASSTHROUGH:2->4", StringComparison.Ordinal));
    }

    [Fact]
    public void Same_row_node_spans_require_one_free_logical_cell()
    {
        var placement = Freeze(
            Node("left", 2, 0, 2),
            Node("right", 2, 2),
            Grid(3, 3, new[] { (2, 0), (2, 1), (2, 2) }, Array.Empty<int>()));

        var audit = ArchitectureV7GridStructureAudit.Audit(placement);

        Assert.Contains(audit.Findings, finding => finding.Contains("GRID-NODE-SPAN-TOUCH", StringComparison.Ordinal));
    }

    private static ArchitectureV7FrozenNodePlacement Node(string id, int row, int column, int span = 1) =>
        new(id, id, "p", row, column, span, column + (span - 1) / 2,
            Enumerable.Range(column, span).Select(item => (row, item)).ToArray(), false, false, false, "tree", id, id, "test");

    private static ArchitectureV7CommonDiagramGrid Grid(int rows, int columns, params (int Row, int Column)[] occupied) =>
        Grid(rows, columns, occupied, Array.Empty<int>());

    private static ArchitectureV7CommonDiagramGrid Grid(int rows, int columns, (int Row, int Column)[] occupied, int[] separatorRows)
    {
        var occupiedSet = occupied.ToHashSet();
        var cells = Enumerable.Range(0, rows).SelectMany(row => Enumerable.Range(0, columns).Select(column =>
        {
            var capability = separatorRows.Contains(row)
                ? ArchitectureV7CellCapability.NonRoutingSeparator
                : row % 2 == 0
                    ? ArchitectureV7CellCapability.NodeAllowed
                    : ArchitectureV7CellCapability.RoutingAllowed | ArchitectureV7CellCapability.GeneralRouting;
            return new ArchitectureV7LogicalCell(row, column, capability, occupiedSet.Contains((row, column)) ? "occupied" : null);
        })).ToArray();
        return new ArchitectureV7CommonDiagramGrid(rows, columns, cells);
    }

    private static ArchitectureV7PlacementFreeze Freeze(params object[] values)
    {
        var nodes = values.OfType<ArchitectureV7FrozenNodePlacement>().ToArray();
        var grid = values.OfType<ArchitectureV7CommonDiagramGrid>().Single();
        return new ArchitectureV7PlacementFreeze(nodes, Array.Empty<ArchitectureV7ProjectRegion>(),
            new ArchitectureV7ExternalRegion(0, Array.Empty<string>(), Array.Empty<ArchitectureV7FrozenNodePlacement>()),
            new ArchitectureV7StandaloneRegion(0, 0, 0, Array.Empty<string>(), Array.Empty<ArchitectureV7FrozenNodePlacement>()),
            grid, Array.Empty<ArchitectureV7ProjectTransform>(), "p", "o", "s", "r", "f");
    }
}
