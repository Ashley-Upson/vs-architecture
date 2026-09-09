using System;
using System.Collections.Generic;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core.Models.ArchitectureV7;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.ArchitectureV7;

/// <summary>Physical resource compatibility, evaluated before scene materialisation.</summary>
public static class ArchitectureV7EndpointCorridorConflictAudit
{
    public sealed record Conflict(string CorridorId, string PhysicalLinkId, string OrdinaryRunId,
        string OtherPhysicalLinkId, double X, double StartY, double EndY);

    public static IReadOnlyList<Conflict> Find(
        ArchitectureV7CollectiveAllocationFreeze allocation,
        IReadOnlyList<ArchitectureV7EndpointCorridor> corridors,
        IReadOnlyList<ArchitectureV7PhysicalTrackDimension> rows,
        IReadOnlyList<ArchitectureV7PhysicalTrackDimension> columns,
        IReadOnlyDictionary<string, int>? candidateLanes = null)
    {
        var result = new List<Conflict>();
        foreach (var run in allocation.Runs.Where(r => r.Orientation == ArchitectureV7RunOrientation.Vertical &&
            (candidateLanes is null || candidateLanes.ContainsKey(r.RunId))))
        {
            var associated = corridors.Where(c => c.OrdinaryRunId == run.RunId).ToArray();
            if (associated.Length != 0 && run.EndRouteIndex - run.StartRouteIndex == 1) continue;
            var assignment = allocation.RunAssignments.Single(a => a.RunId == run.RunId);
            var ordinal = candidateLanes is null ? assignment.LaneOrdinal : candidateLanes[run.RunId];
            var x = columns[run.Cells[0].Column].LaneCoordinates[ordinal];
            var y1 = EndY(true);
            var y2 = EndY(false);
            foreach (var corridor in corridors.Where(c => c.PhysicalLinkId != run.PhysicalLinkId && Math.Abs(c.X - x) < .001))
            {
                var start = Math.Max(Math.Min(y1, y2), Math.Min(corridor.NodeEdgeY, corridor.RoutingY));
                var end = Math.Min(Math.Max(y1, y2), Math.Max(corridor.NodeEdgeY, corridor.RoutingY));
                if (end > start + .001)
                    result.Add(new(corridor.ResourceId, corridor.PhysicalLinkId, run.RunId, run.PhysicalLinkId, x, start, end));
            }

            double EndY(bool start)
            {
                var endpoint = associated.FirstOrDefault(c => c.EndpointKind == (start ? ArchitectureV7EndpointKind.SourceDeparture : ArchitectureV7EndpointKind.DestinationArrival));
                if (endpoint is not null) return endpoint.RoutingY;
                var index = start ? run.StartRouteIndex : run.EndRouteIndex;
                var horizontal = allocation.Runs.FirstOrDefault(r => !r.IsEndpointTransition && r.PhysicalLinkId == run.PhysicalLinkId &&
                    r.Orientation == ArchitectureV7RunOrientation.Horizontal && (start ? r.EndRouteIndex == index : r.StartRouteIndex == index));
                if (horizontal is not null)
                {
                    var lane = allocation.RunAssignments.Single(a => a.RunId == horizontal.RunId);
                    return rows[horizontal.Cells[0].Row].LaneCoordinates[lane.LaneOrdinal];
                }
                var cell = start ? run.Cells[0] : run.Cells[run.Cells.Count - 1];
                return (rows[cell.Row].Start + rows[cell.Row].End) / 2d;
            }
        }
        return result;
    }
}
