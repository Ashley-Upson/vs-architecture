using System;
using System.Collections.Generic;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core.Models;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal static class CorridorLaneAllocator
{
    public static CorridorLaneAllocation Allocate(CorridorObservation observation)
    {
        PerformanceAudit.Increment("lane allocations built");
        var allocations = new Dictionary<string, IReadOnlyDictionary<string, AllocatedCorridorLane>>(StringComparer.Ordinal);
        var failures = new List<string>();
        var requests = new List<CapacityRequest>();

        foreach (var usage in observation.Usage.Values.OrderBy(item => item.Corridor.Id, StringComparer.Ordinal))
        {
            if (usage.IsOverCapacity)
            {
                failures.Add(usage.Corridor.Id);
                var revisions = observation.SegmentMappings
                    .Where(mapping => string.Equals(mapping.CorridorId, usage.Corridor.Id, StringComparison.Ordinal))
                    .GroupBy(mapping => mapping.EdgeId, StringComparer.Ordinal)
                    .ToDictionary(
                        group => group.Key,
                        group => group.Select(mapping => mapping.RouteRevision).Distinct().Single(),
                        StringComparer.Ordinal);
                var requiredExtent = Math.Max(
                    1,
                    (usage.RequiredLanes - 1) * usage.Corridor.LaneSpacing + 1 + usage.Corridor.Clearance * 2);
                var currentExtent = usage.Corridor.Orientation == CorridorOrientation.Horizontal
                    ? usage.Corridor.Bounds.Height
                    : usage.Corridor.Bounds.Width;
                requests.Add(new CapacityRequest(
                    usage.Corridor.Id,
                    usage.Corridor.Role,
                    revisions,
                    usage.RequiredLanes,
                    usage.Corridor.Capacity,
                    requiredExtent,
                    usage.Corridor.Bounds,
                    Math.Max(0, requiredExtent - currentExtent)));
                continue;
            }

            var edgeIds = usage.EdgeIds.ToArray();
            PerformanceAudit.Increment("corridor lanes allocated", edgeIds.Length);
            var horizontal = usage.Corridor.Orientation == CorridorOrientation.Horizontal;
            var occupied = horizontal
                ? new AxisInterval(usage.Corridor.Bounds.X, usage.Corridor.Bounds.Right)
                : new AxisInterval(usage.Corridor.Bounds.Y, usage.Corridor.Bounds.Bottom);
            var allowed = horizontal
                ? new AxisInterval(usage.Corridor.Bounds.Y, usage.Corridor.Bounds.Bottom)
                : new AxisInterval(usage.Corridor.Bounds.X, usage.Corridor.Bounds.Right);
            var orientation = horizontal ? RailOrientation.Horizontal : RailOrientation.Vertical;
            var commonDemands = edgeIds.Select((edgeId, index) => new RailDemand(
                $"{usage.Corridor.Id}:{edgeId}:legacy-lane", edgeId, orientation, occupied, allowed,
                null, RailSemanticRole.Through, index, null, null, default,
                new RouteRevision(observation.SegmentMappings
                    .Where(item => item.EdgeId == edgeId && item.CorridorId == usage.Corridor.Id)
                    .Select(item => item.RouteRevision).Distinct().Single()))).ToArray();
            var common = DeterministicRailAllocator.Assign(
                new RailAllocationRegionIdentity(orientation, allowed, usage.Corridor.Id, null, default),
                commonDemands, new RailAssignmentOptions(usage.Corridor.LaneSpacing, usage.Corridor.Clearance));
            var lanes = new Dictionary<string, AllocatedCorridorLane>(StringComparer.Ordinal);
            for (var index = 0; index < edgeIds.Length; index++)
            {
                var laneIndex = common.RailsByDemandId[$"{usage.Corridor.Id}:{edgeIds[index]}:legacy-lane"].LaneIndex;
                var coordinate = LaneCoordinate(usage.Corridor, laneIndex, edgeIds.Length);
                lanes[edgeIds[index]] = new AllocatedCorridorLane(
                    usage.Corridor.Id,
                    edgeIds[index],
                    laneIndex,
                    coordinate);
            }

            allocations[usage.Corridor.Id] = lanes;
        }

        return new CorridorLaneAllocation(allocations, failures, requests);
    }

    private static int LaneCoordinate(RoutingCorridor corridor, int index, int laneCount)
    {
        var center = corridor.Orientation == CorridorOrientation.Horizontal
            ? corridor.Bounds.Y + corridor.Bounds.Height / 2
            : corridor.Bounds.X + corridor.Bounds.Width / 2;
        var doubledOffset = (2 * index - (laneCount - 1)) * corridor.LaneSpacing;
        return center + doubledOffset / 2;
    }
}
