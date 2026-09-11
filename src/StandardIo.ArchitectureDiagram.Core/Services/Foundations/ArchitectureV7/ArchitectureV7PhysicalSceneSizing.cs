using System;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core.Models.ArchitectureV7;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.ArchitectureV7;

internal static class ArchitectureV7PhysicalSceneSizing
{
    public static double RowMinimum(int row, ArchitectureV7PlacementFreeze placement, ArchitectureV7PhysicalSceneConfiguration configuration)
    {
        var cells = placement.DiagramGrid.Cells.Where(cell => cell.Row == row).ToArray();
        if (cells.Any(cell => (cell.Capabilities & ArchitectureV7CellCapability.HeaderBlocked) != 0))
            return configuration.ProjectHeaderHeight;
        if (cells.Any(cell => (cell.Capabilities & ArchitectureV7CellCapability.NodeAllowed) != 0))
            return configuration.NodeMinimumHeight + 2 * configuration.NodeClearance;
        if (cells.Any(cell => (cell.Capabilities & ArchitectureV7CellCapability.ProjectBoundary) != 0))
            return configuration.BoundaryRowMinimum;
        return configuration.RoutingRowMinimum;
    }

    public static bool IsRoutingRow(int row, ArchitectureV7PlacementFreeze placement)
    {
        var cells = placement.DiagramGrid.Cells.Where(cell => cell.Row == row).ToArray();
        return cells.Any(cell => (cell.Capabilities & ArchitectureV7CellCapability.GeneralRouting) != 0) &&
            !cells.Any(cell => (cell.Capabilities & ArchitectureV7CellCapability.HeaderBlocked) != 0) &&
            !cells.Any(cell => (cell.Capabilities & ArchitectureV7CellCapability.NodeAllowed) != 0);
    }

    public static bool IsNodeBearingRow(int row, ArchitectureV7PlacementFreeze placement) =>
        placement.DiagramGrid.Cells.Any(cell => cell.Row == row && (cell.Capabilities & ArchitectureV7CellCapability.NodeAllowed) != 0);

    public static int LaneEnvelope(int laneCount, ArchitectureV7PhysicalSceneConfiguration configuration) =>
        laneCount <= 0 ? 0 : checked((int)(2 * configuration.RouteClearance + (laneCount - 1) * configuration.ParallelLaneSpacing));

}
