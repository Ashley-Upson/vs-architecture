using System;
using System.Collections.Generic;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core.Models;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal static class CorridorObserver
{
    public static CorridorObservation Observe(
        IReadOnlyDictionary<string, NodeLayout> nodes,
        IReadOnlyDictionary<string, LinkLayout> links,
        int laneSpacing,
        int clearance)
    {
        PerformanceAudit.Increment("corridor observations built");
        laneSpacing = Math.Max(1, laneSpacing);
        clearance = Math.Max(0, clearance);
        var sourceCounts = links.Values.GroupBy(link => link.Link.SourceId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var targetCounts = links.Values.GroupBy(link => link.Link.TargetId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var observed = links.Values
            .OrderBy(link => link.Link.Order)
            .ThenBy(link => link.Link.Id, StringComparer.Ordinal)
            .SelectMany(link =>
            {
                var segments = CompleteSegments(link);
                return segments.Select((segment, index) => Describe(
                    link,
                    segment,
                    index,
                    segments.Length,
                    sourceCounts[link.Link.SourceId],
                    targetCounts[link.Link.TargetId],
                    nodes,
                    laneSpacing));
            })
            .Where(segment => segment is not null)
            .Select(segment => segment!)
            .ToArray();
        PerformanceAudit.Increment("corridor segment observations", observed.Length);
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
                segment.Segment,
                segment.RouteRevision))
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
        var transitions = BuildTerminalTransitions(nodes, links, mappings, corridors, laneSpacing);

        return new CorridorObservation(corridors, junctions, mappings, usage, transitions);
    }

    private static IReadOnlyList<TerminalTransition> BuildTerminalTransitions(
        IReadOnlyDictionary<string, NodeLayout> nodes,
        IReadOnlyDictionary<string, LinkLayout> links,
        IReadOnlyList<CorridorSegmentMapping> mappings,
        IReadOnlyDictionary<string, RoutingCorridor> corridors,
        int laneSpacing)
    {
        var result = new List<TerminalTransition>();
        Add(FanoutDirection.Source);
        Add(FanoutDirection.Target);
        return result.OrderBy(item => item.Id, StringComparer.Ordinal).ToArray();

        void Add(FanoutDirection direction)
        {
            var groups = links.Values
                .Where(link => nodes.ContainsKey(direction == FanoutDirection.Source ? link.Link.SourceId : link.Link.TargetId))
                .GroupBy(link => direction == FanoutDirection.Source ? link.Link.SourceId : link.Link.TargetId, StringComparer.Ordinal);
            foreach (var group in groups.OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                var terminal = nodes[group.Key].Rect;
                foreach (var side in group.GroupBy(link =>
                {
                    var remoteId = direction == FanoutDirection.Source ? link.Link.TargetId : link.Link.SourceId;
                    return nodes.TryGetValue(remoteId, out var remote) && remote.Rect.CenterX < terminal.CenterX
                        ? FanoutSide.Left
                        : FanoutSide.Right;
                }))
                {
                    var ordered = side.OrderBy(link =>
                    {
                        var remoteId = direction == FanoutDirection.Source ? link.Link.TargetId : link.Link.SourceId;
                        return nodes.TryGetValue(remoteId, out var remote)
                            ? Math.Abs(remote.Rect.CenterX - terminal.CenterX)
                            : int.MaxValue;
                    }).ThenBy(link => link.Link.Id, StringComparer.Ordinal).ToArray();
                    for (var index = 0; index < ordered.Length; index++)
                    {
                        var link = ordered[index];
                        var points = new[] { link.SourcePoint }.Concat(link.Points).Concat(new[] { link.TargetPoint }).ToArray();
                        var stub = direction == FanoutDirection.Source
                            ? new Segment(points[0], points[Math.Min(1, points.Length - 1)])
                            : new Segment(points[points.Length - 1], points[Math.Max(0, points.Length - 2)]);
                        var ordinary = mappings
                            .Where(mapping => mapping.EdgeId == link.Link.Id &&
                                corridors[mapping.CorridorId].Role == CorridorRole.Ordinary)
                            .OrderBy(mapping => direction == FanoutDirection.Source ? mapping.SegmentIndex : -mapping.SegmentIndex)
                            .FirstOrDefault();
                        result.Add(new TerminalTransition(
                            $"terminal:{direction}:{group.Key}:{side.Key}:{index}",
                            link.Link.Id,
                            link.RouteState.Revision,
                            direction,
                            group.Key,
                            side.Key,
                            direction == FanoutDirection.Source ? link.SourcePoint.X : link.TargetPoint.X,
                            stub,
                            ordinary?.CorridorId,
                            Math.Max(stub.Length, laneSpacing),
                            Math.Max(0, ordered.Length - 1) * laneSpacing,
                            index));
                    }
                }
            }
        }
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
        int segmentCount,
        int sourceRouteCount,
        int targetRouteCount,
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
            var role = Role(segmentIndex, segmentCount, sourceRouteCount, targetRouteCount);
            var region = Region(link, nodes, role);
            return new ObservedSegment(link.Link.Id, link.RouteState.Revision, segmentIndex,
                $"H:{top}:{bottom}:{role}:{region}", CorridorOrientation.Horizontal, role, region, $"{top}:{bottom}", segment);
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
        var verticalRole = Role(segmentIndex, segmentCount, sourceRouteCount, targetRouteCount);
        var verticalRegion = Region(link, nodes, verticalRole);
        return new ObservedSegment(link.Link.Id, link.RouteState.Revision, segmentIndex,
            $"V:{corridorLeft}:{corridorRight}:{verticalRole}:{verticalRegion}", CorridorOrientation.Vertical,
            verticalRole, verticalRegion, $"{corridorLeft}:{corridorRight}", segment);
    }

    private static CorridorRole Role(
        int segmentIndex,
        int segmentCount,
        int sourceRouteCount,
        int targetRouteCount) =>
        segmentCount <= 1
            ? CorridorRole.Ordinary
            : segmentIndex == 0 || segmentIndex == 1 && sourceRouteCount > 1
            ? CorridorRole.SourceTransition
            : segmentIndex == segmentCount - 1 || segmentIndex == segmentCount - 2 && targetRouteCount > 1
                ? CorridorRole.TargetTransition
                : CorridorRole.Ordinary;

    private static string Region(
        LinkLayout link,
        IReadOnlyDictionary<string, NodeLayout> nodes,
        CorridorRole role)
    {
        if (role == CorridorRole.SourceTransition)
        {
            return $"terminal-source:{link.Link.SourceId}";
        }

        if (role == CorridorRole.TargetTransition)
        {
            return $"terminal-target:{link.Link.TargetId}";
        }

        if (!nodes.TryGetValue(link.Link.SourceId, out var source) ||
            !nodes.TryGetValue(link.Link.TargetId, out var target))
        {
            return "unknown";
        }

        var distance = Math.Abs(source.Rect.CenterX - target.Rect.CenterX);
        var localThreshold = Math.Max(source.Rect.Width, target.Rect.Width) * 2;
        return distance > localThreshold
            ? $"cross:{Math.Min(source.Depth, target.Depth)}:{Math.Max(source.Depth, target.Depth)}"
            : $"local:{Math.Min(source.Depth, target.Depth)}:{Math.Max(source.Depth, target.Depth)}:" +
                $"band:{Math.Min(source.Rect.CenterX, target.Rect.CenterX) / Math.Max(1, localThreshold)}";
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
        return new RoutingCorridor(
            id,
            orientation,
            bounds,
            laneSpacing,
            capacity,
            items[0].Role,
            items[0].RegionKey,
            items[0].ObstacleBoundaryKey,
            clearance);
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
        int RouteRevision,
        int SegmentIndex,
        string CorridorKey,
        CorridorOrientation Orientation,
        CorridorRole Role,
        string RegionKey,
        string ObstacleBoundaryKey,
        Segment Segment);

    private sealed record SpatialCorridorGroup(string Id, IReadOnlyList<ObservedSegment> Segments);
}
