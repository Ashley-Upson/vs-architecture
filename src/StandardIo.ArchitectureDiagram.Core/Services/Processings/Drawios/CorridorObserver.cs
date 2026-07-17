using System;
using System.Collections.Generic;
using System.Linq;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal static class CorridorObserver
{
    public static CorridorObservation Observe(
        IReadOnlyDictionary<string, NodeLayout> nodes,
        IReadOnlyDictionary<string, LinkLayout> links,
        int laneSpacing,
        int clearance)
    {
        laneSpacing = Math.Max(1, laneSpacing);
        clearance = Math.Max(0, clearance);
        var observed = links.Values
            .OrderBy(link => link.Link.Order)
            .SelectMany(link => CompleteSegments(link)
                .Select((segment, index) => Describe(link, segment, index, nodes, laneSpacing)))
            .Where(segment => segment is not null)
            .Select(segment => segment!)
            .ToArray();
        var spatialGroups = observed
            .GroupBy(segment => segment.CorridorKey, StringComparer.Ordinal)
            .SelectMany(group => SplitSpatially(group.Key, group))
            .ToArray();
        var corridors = spatialGroups
            .Select(group => BuildCorridor(group.Id, group.Segments, laneSpacing, clearance))
            .ToDictionary(corridor => corridor.Id, StringComparer.Ordinal);
        var corridorIdBySegment = spatialGroups
            .SelectMany(group => group.Segments.Select(segment => new { Segment = segment, group.Id }))
            .ToDictionary(item => item.Segment, item => item.Id);
        var mappings = observed
            .Select(segment => new CorridorSegmentMapping(
                segment.EdgeId,
                segment.SegmentIndex,
                corridorIdBySegment[segment],
                segment.Segment))
            .ToArray();
        var usage = mappings
            .GroupBy(mapping => mapping.CorridorId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    var edgeIds = group
                        .GroupBy(mapping => mapping.EdgeId, StringComparer.Ordinal)
                        .Select(edge => edge.OrderBy(mapping => mapping.SegmentIndex).First())
                        .OrderBy(mapping => FanOutDirection(mapping))
                        .ThenBy(mapping => FanOutOrder(mapping))
                        .ThenBy(mapping => mapping.EdgeId, StringComparer.Ordinal)
                        .Select(mapping => mapping.EdgeId)
                        .ToArray();
                    return new CorridorUsage(corridors[group.Key], edgeIds, edgeIds.Length);
                },
                StringComparer.Ordinal);
        var junctions = BuildJunctions(corridors.Values);

        return new CorridorObservation(corridors, junctions, mappings, usage);
    }

    private static int FanOutDirection(CorridorSegmentMapping mapping) =>
        LongitudinalTarget(mapping) < LongitudinalSource(mapping) ? -1 : 1;

    private static int FanOutOrder(CorridorSegmentMapping mapping)
    {
        var target = LongitudinalTarget(mapping);
        // Ports progress from near to far on each side. Horizontal/vertical fan-out lanes
        // must nest in the opposite order so a farther route clears nearer terminal stubs.
        return FanOutDirection(mapping) < 0 ? target : -target;
    }

    private static int LongitudinalSource(CorridorSegmentMapping mapping) =>
        mapping.Segment.IsHorizontal ? mapping.Segment.Start.X : mapping.Segment.Start.Y;

    private static int LongitudinalTarget(CorridorSegmentMapping mapping) =>
        mapping.Segment.IsHorizontal ? mapping.Segment.End.X : mapping.Segment.End.Y;

    private static IEnumerable<SpatialCorridorGroup> SplitSpatially(
        string bandId,
        IEnumerable<ObservedSegment> segments)
    {
        var remaining = segments
            .OrderBy(segment => LongitudinalStart(segment))
            .ThenBy(segment => LongitudinalEnd(segment))
            .ThenBy(segment => segment.EdgeId, StringComparer.Ordinal)
            .ToArray();
        var component = new List<ObservedSegment>();
        var componentEnd = int.MinValue;

        foreach (var segment in remaining)
        {
            var start = LongitudinalStart(segment);
            var end = LongitudinalEnd(segment);
            if (component.Count > 0 && start > componentEnd)
            {
                yield return SpatialGroup(bandId, component);
                component = new List<ObservedSegment>();
                componentEnd = int.MinValue;
            }

            component.Add(segment);
            componentEnd = Math.Max(componentEnd, end);
        }

        if (component.Count > 0)
        {
            yield return SpatialGroup(bandId, component);
        }
    }

    private static SpatialCorridorGroup SpatialGroup(string bandId, IReadOnlyList<ObservedSegment> segments)
    {
        var start = segments.Min(LongitudinalStart);
        var end = segments.Max(LongitudinalEnd);
        return new SpatialCorridorGroup($"{bandId}:{start}:{end}", segments.ToArray());
    }

    private static int LongitudinalStart(ObservedSegment segment) =>
        segment.Orientation == CorridorOrientation.Horizontal
            ? Math.Min(segment.Segment.Start.X, segment.Segment.End.X)
            : Math.Min(segment.Segment.Start.Y, segment.Segment.End.Y);

    private static int LongitudinalEnd(ObservedSegment segment) =>
        segment.Orientation == CorridorOrientation.Horizontal
            ? Math.Max(segment.Segment.Start.X, segment.Segment.End.X)
            : Math.Max(segment.Segment.Start.Y, segment.Segment.End.Y);

    private static ObservedSegment? Describe(
        LinkLayout link,
        Segment segment,
        int segmentIndex,
        IReadOnlyDictionary<string, NodeLayout> nodes,
        int laneSpacing)
    {
        if (segment.Length == 0 || (!segment.IsHorizontal && !segment.IsVertical))
        {
            return null;
        }

        // Terminals are corridor boundaries even though they are not collision obstacles for
        // their own edge. Omitting them lets a lane escape the source/target band.
        var obstacles = nodes.Values.Select(node => node.Rect).ToArray();
        if (segment.IsHorizontal)
        {
            var left = Math.Min(segment.Start.X, segment.End.X);
            var right = Math.Max(segment.Start.X, segment.End.X);
            var relevant = obstacles.Where(rect => RangesOverlap(left, right, rect.X, rect.Right)).ToArray();
            var top = relevant.Where(rect => rect.Bottom <= segment.Start.Y)
                .Select(rect => rect.Bottom)
                .DefaultIfEmpty(segment.Start.Y - laneSpacing * 2)
                .Max();
            var bottom = relevant.Where(rect => rect.Y >= segment.Start.Y)
                .Select(rect => rect.Y)
                .DefaultIfEmpty(segment.Start.Y + laneSpacing * 2)
                .Min();
            NormalizeBounds(segment.Start.Y, laneSpacing, ref top, ref bottom);
            return new ObservedSegment(link.Link.Id, segmentIndex, $"H:{top}:{bottom}", CorridorOrientation.Horizontal, segment);
        }

        var upper = Math.Min(segment.Start.Y, segment.End.Y);
        var lower = Math.Max(segment.Start.Y, segment.End.Y);
        var verticalRelevant = obstacles.Where(rect => RangesOverlap(upper, lower, rect.Y, rect.Bottom)).ToArray();
        var corridorLeft = verticalRelevant.Where(rect => rect.Right <= segment.Start.X)
            .Select(rect => rect.Right)
            .DefaultIfEmpty(segment.Start.X - laneSpacing * 2)
            .Max();
        var corridorRight = verticalRelevant.Where(rect => rect.X >= segment.Start.X)
            .Select(rect => rect.X)
            .DefaultIfEmpty(segment.Start.X + laneSpacing * 2)
            .Min();
        NormalizeBounds(segment.Start.X, laneSpacing, ref corridorLeft, ref corridorRight);
        return new ObservedSegment(link.Link.Id, segmentIndex, $"V:{corridorLeft}:{corridorRight}", CorridorOrientation.Vertical, segment);
    }

    private static RoutingCorridor BuildCorridor(
        string id,
        IEnumerable<ObservedSegment> segments,
        int laneSpacing,
        int clearance)
    {
        var items = segments.ToArray();
        var orientation = items[0].Orientation;
        var parts = id.Split(':');
        var lowerBoundary = int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture);
        var upperBoundary = int.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture);
        Rect bounds;
        int perpendicularSize;
        if (orientation == CorridorOrientation.Horizontal)
        {
            var left = items.Min(item => Math.Min(item.Segment.Start.X, item.Segment.End.X));
            var right = items.Max(item => Math.Max(item.Segment.Start.X, item.Segment.End.X));
            bounds = new Rect(left, lowerBoundary, Math.Max(1, right - left), Math.Max(1, upperBoundary - lowerBoundary));
            perpendicularSize = bounds.Height;
        }
        else
        {
            var top = items.Min(item => Math.Min(item.Segment.Start.Y, item.Segment.End.Y));
            var bottom = items.Max(item => Math.Max(item.Segment.Start.Y, item.Segment.End.Y));
            bounds = new Rect(lowerBoundary, top, Math.Max(1, upperBoundary - lowerBoundary), Math.Max(1, bottom - top));
            perpendicularSize = bounds.Width;
        }

        var usableSize = Math.Max(0, perpendicularSize - clearance * 2);
        var capacity = Math.Max(1, 1 + Math.Max(0, usableSize - 1) / laneSpacing);
        return new RoutingCorridor(id, orientation, bounds, laneSpacing, capacity);
    }

    private static IReadOnlyDictionary<string, CorridorJunction> BuildJunctions(IEnumerable<RoutingCorridor> corridors)
    {
        var horizontal = corridors.Where(corridor => corridor.Orientation == CorridorOrientation.Horizontal).ToArray();
        var vertical = corridors
            .Where(corridor => corridor.Orientation == CorridorOrientation.Vertical)
            .OrderBy(corridor => corridor.Bounds.X)
            .ToArray();
        var result = new Dictionary<string, CorridorJunction>(StringComparer.Ordinal);
        foreach (var left in horizontal)
        {
            foreach (var right in vertical
                .TakeWhile(corridor => corridor.Bounds.X < left.Bounds.Right)
                .Where(corridor => corridor.Bounds.Right > left.Bounds.X))
            {
                var intersectionLeft = Math.Max(left.Bounds.X, right.Bounds.X);
                var intersectionTop = Math.Max(left.Bounds.Y, right.Bounds.Y);
                var intersectionRight = Math.Min(left.Bounds.Right, right.Bounds.Right);
                var intersectionBottom = Math.Min(left.Bounds.Bottom, right.Bounds.Bottom);
                if (intersectionRight <= intersectionLeft || intersectionBottom <= intersectionTop)
                {
                    continue;
                }

                var id = $"J:{left.Id}:{right.Id}";
                result[id] = new CorridorJunction(
                    id,
                    new Rect(
                        intersectionLeft,
                        intersectionTop,
                        intersectionRight - intersectionLeft,
                        intersectionBottom - intersectionTop),
                    new[] { left.Id, right.Id });
            }
        }

        return result;
    }

    private static Segment[] CompleteSegments(LinkLayout link)
    {
        var points = new[] { link.SourcePoint }
            .Concat(link.Points)
            .Concat(new[] { link.TargetPoint })
            .ToArray();
        return Enumerable.Range(0, Math.Max(0, points.Length - 1))
            .Select(index => new Segment(points[index], points[index + 1]))
            .ToArray();
    }

    private static void NormalizeBounds(int coordinate, int spacing, ref int lower, ref int upper)
    {
        if (lower < upper)
        {
            return;
        }

        lower = coordinate - spacing;
        upper = coordinate + spacing;
    }

    private static bool RangesOverlap(int firstStart, int firstEnd, int secondStart, int secondEnd) =>
        firstStart < secondEnd && secondStart < firstEnd;

    private sealed record ObservedSegment(
        string EdgeId,
        int SegmentIndex,
        string CorridorKey,
        CorridorOrientation Orientation,
        Segment Segment);

    private sealed record SpatialCorridorGroup(string Id, IReadOnlyList<ObservedSegment> Segments);
}
