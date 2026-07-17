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
    Segment Segment);

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
