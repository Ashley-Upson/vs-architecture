using System;
using System.Collections.Generic;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core.Models.ArchitectureV7;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.ArchitectureV7;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core.Tests;

public sealed class ArchitectureV7CorridorDiscoveryTests
{
    private static readonly ArchitectureV7CellCapability General = ArchitectureV7CellCapability.RoutingAllowed | ArchitectureV7CellCapability.GeneralRouting;

    [Fact]
    public void Discovers_single_horizontal_and_vertical_corridors()
    {
        var result = Discover(Grid(5, 5, (_, _) => General));

        Assert.Contains(result.Horizontal, corridor => corridor.CorridorId == "corridor:H:0:0-4");
        Assert.Contains(result.Vertical, corridor => corridor.CorridorId == "corridor:V:0:0-4");
        Assert.Equal(5, result.Horizontal.Single(item => item.CorridorId == "corridor:H:0:0-4").CellCount);
        Assert.Equal(5, result.Vertical.Single(item => item.CorridorId == "corridor:V:0:0-4").CellCount);
    }

    [Fact]
    public void Blocked_node_and_non_routing_separator_split_horizontal_and_vertical_corridors()
    {
        var result = Discover(Grid(5, 5, (row, column) =>
            row == 2 && column == 2 ? ArchitectureV7CellCapability.Blocked :
            row == 1 && column == 3 ? ArchitectureV7CellCapability.NonRoutingSeparator : General));

        Assert.Equal(2, result.Horizontal.Count(item => item.FixedCoordinate == 2));
        Assert.Equal(2, result.Vertical.Count(item => item.FixedCoordinate == 2));
        Assert.DoesNotContain(result.Horizontal, item => item.Cells.Any(cell => cell.Row == 1 && cell.Column == 3));
        Assert.DoesNotContain(result.Vertical, item => item.Cells.Any(cell => cell.Row == 1 && cell.Column == 3));
    }

    [Fact]
    public void Vertical_corridor_crosses_empty_node_rows_but_horizontal_does_not()
    {
        var result = Discover(Grid(5, 3, (row, column) => row is 1 or 3 && column == 1
            ? ArchitectureV7CellCapability.NodeAllowed
            : General));

        var vertical = Assert.Single(result.Vertical.Where(item => item.FixedCoordinate == 1));
        Assert.Equal(5, vertical.CellCount);
        Assert.Equal(1, result.Horizontal.Count(item => item.FixedCoordinate == 0));
        Assert.Equal(1, result.Horizontal.Count(item => item.FixedCoordinate == 2));
        Assert.Empty(result.Horizontal.Where(item => item.FixedCoordinate is 1 or 3));
    }

    [Fact]
    public void Straight_only_boundary_and_header_cells_are_discovered_only_on_straight_axis()
    {
        var straight = ArchitectureV7CellCapability.ProjectBoundary | ArchitectureV7CellCapability.StraightPassthroughOnly;
        var result = Discover(Grid(3, 3, (row, column) => row == 1 || column == 1 ? straight : General));

        Assert.Contains(result.Horizontal, item => item.FixedCoordinate == 1 && item.StartCoordinate == 0 && item.EndCoordinate == 2);
        Assert.Contains(result.Vertical, item => item.FixedCoordinate == 1 && item.StartCoordinate == 0 && item.EndCoordinate == 2);
        Assert.Contains(result.Horizontal, item => item.FixedCoordinate == 0 && item.StartCoordinate == 0 && item.EndCoordinate == 2);
        Assert.Contains(result.Vertical, item => item.FixedCoordinate == 0 && item.StartCoordinate == 0 && item.EndCoordinate == 2);
    }

    [Fact]
    public void Horizontal_and_vertical_corridors_intersect_without_conflict()
    {
        var result = Discover(Grid(5, 5, (row, column) => General));

        var horizontal = Assert.Single(result.Horizontal.Where(item => item.FixedCoordinate == 2));
        var vertical = Assert.Single(result.Vertical.Where(item => item.FixedCoordinate == 2));
        Assert.Contains(new ArchitectureV7RouteCell(2, 2), horizontal.Cells);
        Assert.Contains(new ArchitectureV7RouteCell(2, 2), vertical.Cells);
    }

    [Fact]
    public void Multiple_disjoint_corridors_on_one_row_and_column_remain_distinct()
    {
        var result = Discover(Grid(7, 7, (row, column) =>
            row == 3 && column is 2 or 5 || column == 4 && row is 2 or 5
                ? ArchitectureV7CellCapability.Blocked
                : General));

        Assert.Equal(2, result.Horizontal.Count(item => item.FixedCoordinate == 3));
        Assert.Equal(2, result.Vertical.Count(item => item.FixedCoordinate == 4));
    }

    [Fact]
    public void Shuffled_grid_enumeration_has_the_same_coordinate_fingerprint()
    {
        var ordered = Grid(6, 6, (row, column) => row == 2 && column == 2 ? ArchitectureV7CellCapability.Blocked : General);
        var shuffled = ordered.OrderByDescending(cell => cell.Row * 100 + cell.Column).ToArray();

        var first = Discover(ordered);
        var second = Discover(shuffled);

        Assert.Equal(first.Fingerprint, second.Fingerprint);
        Assert.Equal(first.All.Select(item => item.CorridorId), second.All.Select(item => item.CorridorId));
    }

    [Fact]
    public void Discovery_is_analysis_only_and_does_not_mutate_frozen_placement()
    {
        var cells = Grid(4, 4, (_, _) => General);
        var placement = Freeze(cells);
        var before = (placement.PlacementFingerprint, placement.DiagramGrid.Cells.ToArray());

        _ = new ArchitectureV7CapabilityCorridorDiscoveryStage().Discover(placement);

        Assert.Equal(before.PlacementFingerprint, placement.PlacementFingerprint);
        Assert.Equal(before.Item2, placement.DiagramGrid.Cells);
    }

    [Fact]
    public void Node_footprint_rejects_horizontal_traversal_on_a_node_bearing_row()
    {
        Assert.False(ArchitectureV7CellTraversalPolicy.Allows(
            ArchitectureV7CellCapability.Blocked,
            ArchitectureV7TraversalDirection.Left,
            ArchitectureV7TraversalDirection.Right));
    }

    [Fact]
    public void Empty_node_row_passthrough_rejects_an_illegal_horizontal_bend()
    {
        var capability = ArchitectureV7CellCapability.NodeAllowed;

        Assert.False(ArchitectureV7CellTraversalPolicy.Allows(capability,
            ArchitectureV7TraversalDirection.Up,
            ArchitectureV7TraversalDirection.Right));
        Assert.False(ArchitectureV7CellTraversalPolicy.Allows(capability,
            ArchitectureV7TraversalDirection.Left,
            ArchitectureV7TraversalDirection.Down));
    }

    [Fact]
    public void Empty_node_row_passthrough_rejects_horizontal_straight_traversal()
    {
        Assert.False(ArchitectureV7CellTraversalPolicy.Allows(
            ArchitectureV7CellCapability.NodeAllowed,
            ArchitectureV7TraversalDirection.Left,
            ArchitectureV7TraversalDirection.Right));
        Assert.True(ArchitectureV7CellTraversalPolicy.Allows(
            ArchitectureV7CellCapability.NodeAllowed,
            ArchitectureV7TraversalDirection.Down,
            ArchitectureV7TraversalDirection.Down));
    }

    private static ArchitectureV7CorridorDiscoveryResult Discover(IReadOnlyList<ArchitectureV7LogicalCell> cells) =>
        new ArchitectureV7CapabilityCorridorDiscoveryStage().Discover(Freeze(cells));

    private static IReadOnlyList<ArchitectureV7LogicalCell> Grid(int rows, int columns, Func<int, int, ArchitectureV7CellCapability> capability)
    {
        return Enumerable.Range(0, rows).SelectMany(row => Enumerable.Range(0, columns)
            .Select(column => new ArchitectureV7LogicalCell(row, column, capability(row, column), null))).ToArray();
    }

    private static ArchitectureV7PlacementFreeze Freeze(IReadOnlyList<ArchitectureV7LogicalCell> cells) =>
        new(Array.Empty<ArchitectureV7FrozenNodePlacement>(), Array.Empty<ArchitectureV7ProjectRegion>(),
            new ArchitectureV7ExternalRegion(0, Array.Empty<string>(), Array.Empty<ArchitectureV7FrozenNodePlacement>()),
            new ArchitectureV7StandaloneRegion(0, 0, 0, Array.Empty<string>(), Array.Empty<ArchitectureV7FrozenNodePlacement>()),
            new ArchitectureV7CommonDiagramGrid(cells.Max(cell => cell.Row) + 1, cells.Max(cell => cell.Column) + 1, cells),
            Array.Empty<ArchitectureV7ProjectTransform>(), "projection", "ownership", "sizing", "reservation", "placement");
}
