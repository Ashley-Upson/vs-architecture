using System;
using System.Collections.Generic;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core.Models.ArchitectureV7;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.ArchitectureV7;

/// <summary>
/// Discovers maximal straight corridors from the frozen common grid. The
/// stage has no route, lane, terminal, bend, crossing, or placement authority.
/// </summary>
public sealed class ArchitectureV7CapabilityCorridorDiscoveryStage
{
    public ArchitectureV7CorridorDiscoveryResult Discover(ArchitectureV7PlacementFreeze placement)
    {
        if (placement is null)
            throw new ArgumentNullException(nameof(placement));

        var cells = placement.DiagramGrid.Cells
            .GroupBy(cell => (cell.Row, cell.Column))
            .ToArray();
        if (cells.Any(group => group.Count() != 1))
            throw new InvalidOperationException("The frozen diagram grid contains duplicate logical cell coordinates.");

        var byCoordinate = cells.ToDictionary(group => group.Key, group => group.Single());
        return new ArchitectureV7CorridorDiscoveryResult(
            DiscoverHorizontal(byCoordinate),
            DiscoverVertical(byCoordinate));
    }

    private static IReadOnlyList<ArchitectureV7DiscoveredCorridor> DiscoverHorizontal(
        IReadOnlyDictionary<(int Row, int Column), ArchitectureV7LogicalCell> cells)
    {
        var result = new List<ArchitectureV7DiscoveredCorridor>();
        foreach (var row in cells.Keys.Select(key => key.Row).Distinct().OrderBy(value => value))
        {
            var rowCells = cells.Where(item => item.Key.Row == row).OrderBy(item => item.Key.Column).ToArray();
            foreach (var interval in DiscoverIntervals(rowCells, coordinate: item => item.Key.Column, supports: cell => SupportsHorizontal(cell.Capabilities)))
            {
                result.Add(CreateCorridor(ArchitectureV7RunOrientation.Horizontal, row, interval));
            }
        }
        return result;
    }

    private static IReadOnlyList<ArchitectureV7DiscoveredCorridor> DiscoverVertical(
        IReadOnlyDictionary<(int Row, int Column), ArchitectureV7LogicalCell> cells)
    {
        var result = new List<ArchitectureV7DiscoveredCorridor>();
        foreach (var column in cells.Keys.Select(key => key.Column).Distinct().OrderBy(value => value))
        {
            var columnCells = cells.Where(item => item.Key.Column == column).OrderBy(item => item.Key.Row).ToArray();
            foreach (var interval in DiscoverIntervals(columnCells, coordinate: item => item.Key.Row, supports: cell => SupportsVertical(cell.Capabilities)))
            {
                result.Add(CreateCorridor(ArchitectureV7RunOrientation.Vertical, column, interval));
            }
        }
        return result;
    }

    private static IEnumerable<IReadOnlyList<ArchitectureV7LogicalCell>> DiscoverIntervals(
        IReadOnlyList<KeyValuePair<(int Row, int Column), ArchitectureV7LogicalCell>> ordered,
        Func<KeyValuePair<(int Row, int Column), ArchitectureV7LogicalCell>, int> coordinate,
        Func<ArchitectureV7LogicalCell, bool> supports)
    {
        var current = new List<ArchitectureV7LogicalCell>();
        var previousCoordinate = int.MinValue;
        foreach (var item in ordered)
        {
            var currentCoordinate = coordinate(item);
            var contiguous = currentCoordinate == previousCoordinate + 1 || previousCoordinate == int.MinValue;
            if (!contiguous || !supports(item.Value))
            {
                if (current.Count >= 2)
                    yield return current.ToArray();
                current.Clear();
                previousCoordinate = int.MinValue;
                continue;
            }

            current.Add(item.Value);
            previousCoordinate = currentCoordinate;
        }
        if (current.Count >= 2)
            yield return current.ToArray();
    }

    private static ArchitectureV7DiscoveredCorridor CreateCorridor(
        ArchitectureV7RunOrientation orientation,
        int fixedCoordinate,
        IReadOnlyList<ArchitectureV7LogicalCell> cells)
    {
        var routeCells = cells.Select(cell => new ArchitectureV7RouteCell(cell.Row, cell.Column)).ToArray();
        var start = orientation == ArchitectureV7RunOrientation.Horizontal ? routeCells[0].Column : routeCells[0].Row;
        var lastCell = routeCells[routeCells.Length - 1];
        var end = orientation == ArchitectureV7RunOrientation.Horizontal ? lastCell.Column : lastCell.Row;
        var id = orientation == ArchitectureV7RunOrientation.Horizontal
            ? $"corridor:H:{fixedCoordinate}:{start}-{end}"
            : $"corridor:V:{fixedCoordinate}:{start}-{end}";
        var capabilities = cells.Select(cell => cell.Capabilities)
            .Distinct()
            .OrderBy(capability => (int)capability)
            .ToArray();
        return new ArchitectureV7DiscoveredCorridor(id, orientation, fixedCoordinate, start, end,
            Array.AsReadOnly(routeCells), Array.AsReadOnly(capabilities),
            $"capability-derived;axis={orientation};fixed={fixedCoordinate};start={start};end={end};cells={routeCells.Length}");
    }

    private static bool SupportsHorizontal(ArchitectureV7CellCapability capability) =>
        ArchitectureV7CellTraversalPolicy.Allows(capability, ArchitectureV7TraversalDirection.Left, ArchitectureV7TraversalDirection.Left)
        || ArchitectureV7CellTraversalPolicy.Allows(capability, ArchitectureV7TraversalDirection.Right, ArchitectureV7TraversalDirection.Right);

    private static bool SupportsVertical(ArchitectureV7CellCapability capability) =>
        ArchitectureV7CellTraversalPolicy.Allows(capability, ArchitectureV7TraversalDirection.Up, ArchitectureV7TraversalDirection.Up)
        || ArchitectureV7CellTraversalPolicy.Allows(capability, ArchitectureV7TraversalDirection.Down, ArchitectureV7TraversalDirection.Down);
}
