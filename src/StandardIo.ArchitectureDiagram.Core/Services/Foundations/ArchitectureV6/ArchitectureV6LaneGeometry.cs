using System;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.ArchitectureV6;

internal static class ArchitectureV6LaneGeometry
{
    public static int RequiredEnvelope(int laneCount, int minimumTrackExtent, int portSpacing, int parallelSpacing)
    {
        if (laneCount <= 0) return Math.Max(1, minimumTrackExtent);
        var lastLaneOffset = checked(portSpacing + (laneCount - 1) * parallelSpacing);
        var trailingClearance = portSpacing;
        return Math.Max(Math.Max(1, minimumTrackExtent), checked(lastLaneOffset + trailingClearance));
    }

    public static int Coordinate(int trackOffset, int laneOrdinal, int portSpacing, int parallelSpacing) =>
        checked(trackOffset + portSpacing + laneOrdinal * parallelSpacing);
}
