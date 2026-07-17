using System.Collections.Generic;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal enum CorridorOrientation
{
    Horizontal,
    Vertical
}

internal sealed record RoutingCorridor(
    string Id,
    CorridorOrientation Orientation,
    Rect Bounds,
    int LaneSpacing,
    int Capacity);

internal sealed record CorridorJunction(
    string Id,
    Rect Bounds,
    IReadOnlyList<string> CorridorIds);

internal sealed record CorridorSegmentMapping(
    string EdgeId,
    int SegmentIndex,
    string CorridorId,
    Segment Segment,
    int RouteRevision = 0);

internal sealed record CorridorUsage(
    RoutingCorridor Corridor,
    IReadOnlyList<string> EdgeIds,
    int RequiredLanes)
{
    public int RemainingCapacity => Corridor.Capacity - RequiredLanes;

    public bool IsOverCapacity => RemainingCapacity < 0;
}

internal sealed record CorridorObservation(
    IReadOnlyDictionary<string, RoutingCorridor> Corridors,
    IReadOnlyDictionary<string, CorridorJunction> Junctions,
    IReadOnlyList<CorridorSegmentMapping> SegmentMappings,
    IReadOnlyDictionary<string, CorridorUsage> Usage);

internal sealed record AllocatedCorridorLane(
    string CorridorId,
    string EdgeId,
    int LaneIndex,
    int Coordinate);

internal sealed record CorridorLaneAllocation(
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, AllocatedCorridorLane>> Corridors,
    IReadOnlyList<string> FailedCorridorIds)
{
    public bool IsSuccessful => FailedCorridorIds.Count == 0;

    public bool TryGetLane(string corridorId, string edgeId, out AllocatedCorridorLane lane)
    {
        if (Corridors.TryGetValue(corridorId, out var corridor) &&
            corridor.TryGetValue(edgeId, out lane!))
        {
            return true;
        }

        lane = null!;
        return false;
    }
}
