using System;
using System.Collections.Generic;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core.Models.ArchitectureV7;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.ArchitectureV7;

/// <summary>
/// Projects frozen logical straight runs onto capability-derived corridors.
/// Endpoint node cells are attachments and never become corridor traversal
/// usages. This stage does not alter route cells or allocate lanes.
/// </summary>
public sealed class ArchitectureV7RouteCorridorProjectionStage
{
    public ArchitectureV7RouteCorridorProjectionFreeze Project(
        ArchitectureV7PlacementFreeze placement,
        ArchitectureV7LogicalRouteFreeze routes,
        ArchitectureV7CorridorDiscoveryResult corridors)
    {
        if (placement is null) throw new ArgumentNullException(nameof(placement));
        if (routes is null) throw new ArgumentNullException(nameof(routes));
        if (corridors is null) throw new ArgumentNullException(nameof(corridors));
        if (!string.Equals(placement.PlacementFingerprint, routes.PlacementFingerprint, StringComparison.Ordinal))
            throw new ArgumentException("The route freeze does not belong to the supplied placement freeze.", nameof(routes));

        var usages = new List<ArchitectureV7CorridorUsage>();
        var unprojected = new List<ArchitectureV7UnprojectedCorridorRun>();
        foreach (var route in routes.Routes.OrderBy(route => route.PhysicalLinkId, StringComparer.Ordinal))
        {
            if (!route.IsComplete || route.Cells.Count < 2) continue;
            foreach (var run in Runs(route))
            {
                var traversalCells = run.Cells
                    .Where((_, index) => !(run.StartRouteIndex == 0 && index == 0) &&
                        !(run.EndRouteIndex == route.Cells.Count - 1 && index == run.Cells.Count - 1))
                    .ToArray();
                if (traversalCells.Length == 0) continue;
                var corridor = FindContaining(corridors, run.Orientation, traversalCells);
                if (corridor is null)
                {
                    unprojected.Add(new(route.PhysicalLinkId, run.Orientation, run.StartRouteIndex, run.EndRouteIndex,
                        Array.AsReadOnly(traversalCells), "no-capability-corridor-contains-frozen-run"));
                    continue;
                }
                usages.Add(new(
                    "usage:" + route.PhysicalLinkId + ":" + run.StartRouteIndex,
                    route.PhysicalLinkId,
                    corridor.CorridorId,
                    run.Orientation,
                    run.StartRouteIndex,
                    run.EndRouteIndex,
                    Array.AsReadOnly(traversalCells),
                    "frozen-route;capability-corridor;endpoint-cells-attachments-only"));
            }
        }
        return new ArchitectureV7RouteCorridorProjectionFreeze(usages, unprojected,
            placement.PlacementFingerprint, routes.RouteFingerprint, corridors.Fingerprint);
    }

    private static ArchitectureV7DiscoveredCorridor? FindContaining(
        ArchitectureV7CorridorDiscoveryResult corridors,
        ArchitectureV7RunOrientation orientation,
        IReadOnlyList<ArchitectureV7RouteCell> cells) =>
        (orientation == ArchitectureV7RunOrientation.Horizontal ? corridors.Horizontal : corridors.Vertical)
            .Where(corridor => cells.All(corridor.Cells.Contains))
            .OrderBy(corridor => corridor.CorridorId, StringComparer.Ordinal)
            .FirstOrDefault();

    private static IEnumerable<RunValue> Runs(ArchitectureV7LogicalRoute route)
    {
        var start = 0;
        while (start < route.Cells.Count - 1)
        {
            var orientation = Orientation(route.Cells[start], route.Cells[start + 1]);
            var end = start + 1;
            while (end + 1 < route.Cells.Count && Orientation(route.Cells[end], route.Cells[end + 1]) == orientation) end++;
            yield return new(orientation, start, end, route.Cells.Skip(start).Take(end - start + 1).ToArray());
            start = end;
        }
    }

    private static ArchitectureV7RunOrientation Orientation(ArchitectureV7RouteCell left, ArchitectureV7RouteCell right) =>
        left.Row == right.Row ? ArchitectureV7RunOrientation.Horizontal : ArchitectureV7RunOrientation.Vertical;

    private sealed record RunValue(ArchitectureV7RunOrientation Orientation, int StartRouteIndex, int EndRouteIndex,
        IReadOnlyList<ArchitectureV7RouteCell> Cells);
}
