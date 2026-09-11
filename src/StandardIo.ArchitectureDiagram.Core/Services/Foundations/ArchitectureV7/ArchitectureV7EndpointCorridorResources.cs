using System;
using System.Collections.Generic;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core.Models.ArchitectureV7;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.ArchitectureV7;

/// <summary>Splits resource ownership at the routing row without mutating logical route cells.</summary>
public static class ArchitectureV7EndpointCorridorResources
{
    public static IReadOnlyList<ArchitectureV7StraightRun> AddTransitions(
        ArchitectureV7PlacementFreeze placement, ArchitectureV7LogicalRouteFreeze routes,
        IReadOnlyList<ArchitectureV7StraightRun> runs, ICollection<ArchitectureV7AllocationDiagnostic> diagnostics)
    {
        var result = runs.ToList();
        foreach (var route in routes.Routes.Where(route => route.IsComplete))
        foreach (var kind in new[] { ArchitectureV7EndpointKind.SourceDeparture, ArchitectureV7EndpointKind.DestinationArrival })
        {
            var source = kind == ArchitectureV7EndpointKind.SourceDeparture;
            var run = source ? runs.FirstOrDefault(r => r.PhysicalLinkId == route.PhysicalLinkId && r.StartRouteIndex == 0)
                : runs.FirstOrDefault(r => r.PhysicalLinkId == route.PhysicalLinkId && r.EndRouteIndex == route.Cells.Count - 1);
            if (run is null || run.Orientation != ArchitectureV7RunOrientation.Vertical) continue;
            var node = placement.Nodes.Single(n => n.PhysicalNodeId == (source ? route.SourcePhysicalNodeId : route.DestinationPhysicalNodeId));
            var left = node.LogicalFootprint.Min(c => c.Column);
            var right = node.LogicalFootprint.Max(c => c.Column);
            var index = source ? run.StartRouteIndex + 1 : run.EndRouteIndex - 1;
            var cell = route.Cells[index];
            // Existing one-cell approaches already join an ordinary horizontal run.
            if (run.EndRouteIndex - run.StartRouteIndex == 1)
            {
                var horizontal = runs.FirstOrDefault(r => r.PhysicalLinkId == route.PhysicalLinkId &&
                    r.Orientation == ArchitectureV7RunOrientation.Horizontal &&
                    (source ? r.StartRouteIndex == index : r.EndRouteIndex == index));
                if (horizontal is not null)
                {
                    var replacement = new ArchitectureV7StraightRun(horizontal.RunId, horizontal.PhysicalLinkId, horizontal.Orientation,
                        horizontal.Cells, horizontal.StartRouteIndex, horizontal.EndRouteIndex, horizontal.EndpointContext, horizontal.Provenance)
                    {
                        ConflictStart = Math.Min(left, horizontal.ConflictStart ?? horizontal.Cells.Min(c => c.Column)),
                        ConflictEnd = Math.Max(right, horizontal.ConflictEnd ?? horizontal.Cells.Max(c => c.Column))
                    };
                    var old = result.FindIndex(r => r.RunId == horizontal.RunId);
                    // Both endpoints can enlarge the same horizontal interval.
                    var previous = result[old];
                    result[old] = new ArchitectureV7StraightRun(replacement.RunId, replacement.PhysicalLinkId, replacement.Orientation,
                        replacement.Cells, replacement.StartRouteIndex, replacement.EndRouteIndex, replacement.EndpointContext, replacement.Provenance)
                    { ConflictStart = Math.Min(replacement.ConflictStart.Value, previous.ConflictStart ?? int.MaxValue),
                      ConflictEnd = Math.Max(replacement.ConflictEnd.Value, previous.ConflictEnd ?? int.MinValue) };
                }
                continue;
            }
            var gridCell = placement.DiagramGrid.Cells.FirstOrDefault(c => c.Row == cell.Row && c.Column == cell.Column);
            if (gridCell is not null && ((gridCell.Capabilities & ArchitectureV7CellCapability.GeneralRouting) == 0 ||
                (gridCell.Capabilities & (ArchitectureV7CellCapability.Blocked | ArchitectureV7CellCapability.HeaderBlocked)) != 0))
            {
                diagnostics.Add(new("ENDPOINT-CORRIDOR-ROUTING-ROW-CONFLICT",
                    $"Endpoint {kind} requires a transition at ({cell.Row},{cell.Column}), whose capabilities are {gridCell.Capabilities}; owner={gridCell.OccupantId}.",
                    true, route.PhysicalLinkId, run.RunId, node.PhysicalNodeId, kind.ToString()));
                continue;
            }
            result.Add(new ArchitectureV7StraightRun("endpoint-transition:" + route.PhysicalLinkId + ":" + kind,
                route.PhysicalLinkId, ArchitectureV7RunOrientation.Horizontal,
                new[] { new ArchitectureV7RouteCell(cell.Row, Math.Min(left, cell.Column)), new ArchitectureV7RouteCell(cell.Row, Math.Max(right, cell.Column)) },
                index, index, "endpoint-transition:" + kind, "node-span-corridor;collective-horizontal-interval;frozen-route-resource-split"));
        }
        return result;
    }
}
