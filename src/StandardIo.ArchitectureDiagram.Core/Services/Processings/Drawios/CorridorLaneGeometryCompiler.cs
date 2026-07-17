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

        foreach (var link in links.Values.OrderBy(link => link.Link.Order))
        {
            var points = link.Points.ToArray();
            if (mappingsByEdge.TryGetValue(link.Link.Id, out var mappings))
            {
                foreach (var mapping in mappings)
                {
                    if (mapping.SegmentIndex < 1 || mapping.SegmentIndex >= points.Length ||
                        !allocation.TryGetLane(mapping.CorridorId, link.Link.Id, out var lane))
                    {
                        continue;
                    }

                    var leftIndex = mapping.SegmentIndex - 1;
                    var rightIndex = mapping.SegmentIndex;
                    if (mapping.Segment.IsHorizontal)
                    {
                        points[leftIndex] = points[leftIndex] with { Y = lane.Coordinate };
                        points[rightIndex] = points[rightIndex] with { Y = lane.Coordinate };
                    }
                    else if (mapping.Segment.IsVertical)
                    {
                        points[leftIndex] = points[leftIndex] with { X = lane.Coordinate };
                        points[rightIndex] = points[rightIndex] with { X = lane.Coordinate };
                    }
                }
            }

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
}
