using System;
using System.Collections.Generic;
using System.Linq;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal static class CorridorLaneGeometryCompiler
{
    public static IReadOnlyDictionary<string, LinkLayout> Compile(
        IReadOnlyDictionary<string, LinkLayout> links,
        CorridorObservation observation,
        CorridorLaneAllocation allocation)
    {
        var mappingsByEdge = observation.SegmentMappings
            .GroupBy(mapping => mapping.EdgeId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(mapping => mapping.SegmentIndex).ToArray(),
                StringComparer.Ordinal);
        var result = new Dictionary<string, LinkLayout>(StringComparer.Ordinal);

        foreach (var link in links.Values
            .OrderBy(link => link.Link.Order)
            .ThenBy(link => link.Link.Id, StringComparer.Ordinal))
        {
            var completePoints = new[] { link.SourcePoint }
                .Concat(link.Points)
                .Concat(new[] { link.TargetPoint })
                .ToArray();
            var segments = Enumerable.Range(0, Math.Max(0, completePoints.Length - 1))
                .Select(index => new Segment(completePoints[index], completePoints[index + 1]))
                .ToArray();
            var laneCoordinates = new int?[segments.Length];
            if (mappingsByEdge.TryGetValue(link.Link.Id, out var mappings))
            {
                foreach (var mapping in mappings)
                {
                    if (mapping.SegmentIndex < 1 || mapping.SegmentIndex >= segments.Length - 1 ||
                        !allocation.TryGetLane(mapping.CorridorId, link.Link.Id, out var lane))
                    {
                        continue;
                    }

                    var originalCoordinate = mapping.Segment.IsHorizontal
                        ? mapping.Segment.Start.Y
                        : mapping.Segment.Start.X;
                    var firstTerminalCoordinate = mapping.Segment.IsHorizontal
                        ? link.SourcePoint.Y
                        : link.SourcePoint.X;
                    var secondTerminalCoordinate = mapping.Segment.IsHorizontal
                        ? link.TargetPoint.Y
                        : link.TargetPoint.X;
                    if (!PreservesTerminalRegion(
                        originalCoordinate,
                        lane.Coordinate,
                        firstTerminalCoordinate,
                        secondTerminalCoordinate))
                    {
                        continue;
                    }

                    laneCoordinates[mapping.SegmentIndex] = lane.Coordinate;
                }
            }

            var points = link.Points
                .Select((point, index) => CompilePoint(
                    point,
                    segments[index],
                    laneCoordinates[index],
                    segments[index + 1],
                    laneCoordinates[index + 1]))
                .ToArray();

            result[link.Link.Id] = new LinkLayout(
                link.Link,
                link.SourcePoint,
                link.TargetPoint,
                points,
                link.ExitX,
                link.EntryX,
                link.ExitY,
                link.EntryY);
        }

        return result;
    }

    private static bool PreservesTerminalRegion(int original, int candidate, int firstTerminal, int secondTerminal)
    {
        var minimum = Math.Min(firstTerminal, secondTerminal);
        var maximum = Math.Max(firstTerminal, secondTerminal);
        if (original < minimum)
        {
            return candidate < minimum;
        }

        if (original > maximum)
        {
            return candidate > maximum;
        }

        return candidate > minimum && candidate < maximum;
    }

    private static Point CompilePoint(
        Point original,
        Segment incoming,
        int? incomingLane,
        Segment outgoing,
        int? outgoingLane)
    {
        var x = original.X;
        var y = original.Y;

        if (incoming.IsVertical)
        {
            x = incomingLane ?? incoming.Start.X;
        }
        else if (outgoing.IsVertical)
        {
            x = outgoingLane ?? outgoing.Start.X;
        }

        if (incoming.IsHorizontal)
        {
            y = incomingLane ?? incoming.Start.Y;
        }
        else if (outgoing.IsHorizontal)
        {
            y = outgoingLane ?? outgoing.Start.Y;
        }

        return new Point(x, y);
    }
}
