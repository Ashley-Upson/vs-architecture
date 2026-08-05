using System;
using System.Collections.Generic;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core.Models.ArchitectureV6;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.ArchitectureV6;

internal static class ArchitectureV6RouteBuilder
{
    public static IReadOnlyList<PlannedPhysicalRoute> Build(
        ArchitecturePlanningRequest request,
        IReadOnlyList<PlannedPhysicalLink> links,
        PlannedArchitectureGeometry geometry)
    {
        var nodes = geometry.Nodes.ToDictionary(node => node.PhysicalNodeId, StringComparer.Ordinal);
        var semantic = request.SemanticModel.Projects.SelectMany(project => project.Nodes)
            .ToDictionary(node => node.Id, StringComparer.Ordinal);
        var routes = new List<PlannedPhysicalRoute>();
        foreach (var link in links.OrderBy(item => item.PhysicalLinkId, StringComparer.Ordinal))
        {
            if (!nodes.TryGetValue(link.SourcePhysicalNodeId, out var source) ||
                !nodes.TryGetValue(link.DestinationPhysicalNodeId, out var target))
                continue;
            var sourceRect = source.AbsoluteBounds;
            var targetRect = target.AbsoluteBounds;
            var topology = Classify(link, sourceRect, targetRect);
            var points = ChoosePath(sourceRect, targetRect, nodes.Values, request.RoutePlanning.MinimumPortSpacing,
                request.RoutePlanning.MinimumParallelSpacing, routes.Count, routes.SelectMany(route => route.Segments));
            var segments = ToSegments(link.PhysicalLinkId, source.GridId, topology, points, routes.Count);
            var sourceName = semantic.TryGetValue(link.SemanticLinkId, out var ignoredSource) ? ignoredSource.Id : link.SemanticLinkId;
            routes.Add(new PlannedPhysicalRoute(
                link.PhysicalLinkId,
                link.SemanticLinkId,
                source.SemanticNodeId,
                target.SemanticNodeId,
                source.ProjectionMode.ToString(),
                target.ProjectionMode.ToString(),
                topology,
                segments,
                Math.Max(0, points.Count - 2),
                segments.Sum(SegmentLength),
                HasNodeIntersection(segments, nodes.Values, source.PhysicalNodeId, target.PhysicalNodeId),
                HasSharedSegment(segments, routes.SelectMany(route => route.Segments)),
                false));
        }
        return routes;
    }

    private static RouteTopologyFamily Classify(PlannedPhysicalLink link, AbsoluteRectangle source, AbsoluteRectangle target)
    {
        if (link.SourceProjectId != link.DestinationProjectId) return RouteTopologyFamily.CrossProject;
        if (target.Y < source.Y) return RouteTopologyFamily.Upward;
        if (target.Y == source.Y) return RouteTopologyFamily.SameLayer;
        if (target.Y - source.Y <= source.Height * 2) return RouteTopologyFamily.AdjacentDownward;
        return RouteTopologyFamily.LongDownward;
    }

    private static IReadOnlyList<AbsolutePoint> ChoosePath(
        AbsoluteRectangle source,
        AbsoluteRectangle target,
        IEnumerable<PlannedPhysicalNodeGeometry> allNodes,
        int portGap,
        int laneGap,
        int routeIndex,
        IEnumerable<PlannedPhysicalRouteSegment> previousSegments)
    {
        var clearance = Math.Max(1, portGap);
        var sx = source.X + source.Width / 2;
        var tx = target.X + target.Width / 2;
            var departure = source.Y + source.Height + clearance;
            var arrival = target.Y - clearance;
        if (arrival <= departure)
        {
            var common = Math.Max(source.Y + source.Height, target.Y + target.Height) + clearance;
            departure = common + (routeIndex % 16) * Math.Max(1, laneGap);
            arrival = departure;
        }
        else
        {
            var available = Math.Max(0, arrival - departure - clearance * 2);
            var offset = Math.Min(available, (routeIndex % 8) * Math.Max(1, laneGap));
            departure += offset;
            arrival -= (routeIndex / 8 % 4) * Math.Max(1, laneGap);
            if (arrival < departure) arrival = departure;
        }
        var obstacles = allNodes.Where(node => node.AbsoluteBounds != source && node.AbsoluteBounds != target).ToArray();
        var laneCandidates = new List<int> { sx, tx };
        foreach (var node in obstacles)
        {
            laneCandidates.Add(node.AbsoluteBounds.X - laneGap);
            laneCandidates.Add(node.AbsoluteBounds.X + node.AbsoluteBounds.Width + laneGap);
        }
        laneCandidates = laneCandidates.Distinct().OrderBy(x => Math.Abs(x - sx) + Math.Abs(x - tx)).ThenBy(x => x).ToList();
        var best = default(IReadOnlyList<AbsolutePoint>);
        var bestScore = int.MaxValue;
        foreach (var lane in laneCandidates)
        {
            var points = Simplify(new[]
            {
                new AbsolutePoint(sx, source.Y + source.Height), new AbsolutePoint(sx, departure),
                new AbsolutePoint(lane, departure), new AbsolutePoint(lane, arrival),
                new AbsolutePoint(tx, arrival), new AbsolutePoint(tx, target.Y)
            });
            if (points.Count < 2 || HasNodeIntersection(points, obstacles)) continue;
            var shared = HasSharedSegment(ToSegmentsForComparison(points), previousSegments);
            var score = points.Count * 100 + PathLength(points) + (shared ? 100000 : 0);
            if (score < bestScore) { best = points; bestScore = score; }
        }
        return best ?? Simplify(new[] { new AbsolutePoint(sx, source.Y + source.Height), new AbsolutePoint(sx, departure), new AbsolutePoint(tx, departure), new AbsolutePoint(tx, target.Y) });
    }

    private static IReadOnlyList<PlannedPhysicalRouteSegment> ToSegments(string linkId, PlanningGridId gridId,
        RouteTopologyFamily topology, IReadOnlyList<AbsolutePoint> points, int routeIndex)
    {
        var result = new List<PlannedPhysicalRouteSegment>();
        for (var i = 1; i < points.Count; i++)
        {
            var start = points[i - 1]; var end = points[i];
            if (start == end) continue;
            var axis = start.X == end.X ? RouteAxis.Vertical : RouteAxis.Horizontal;
            var role = i == 1 ? RouteStepRole.SourceExit : i == points.Count - 1 ? RouteStepRole.DestinationEntry : start.X != end.X && i + 1 < points.Count && points[i + 1].X == end.X ? RouteStepRole.Turn : axis == RouteAxis.Horizontal ? RouteStepRole.HorizontalPassThrough : RouteStepRole.VerticalPassThrough;
            result.Add(new PlannedPhysicalRouteSegment(linkId, null, null, start, end, gridId, axis, role, new LaneId($"route:{routeIndex}:{axis}:{i}"), topology));
        }
        return result;
    }

    private static IReadOnlyList<AbsolutePoint> Simplify(IEnumerable<AbsolutePoint> input)
    {
        var points = input.ToList();
        for (var i = points.Count - 2; i >= 1; i--)
            if ((points[i - 1].X == points[i].X && points[i].X == points[i + 1].X) ||
                (points[i - 1].Y == points[i].Y && points[i].Y == points[i + 1].Y)) points.RemoveAt(i);
        return points;
    }

    private static bool HasNodeIntersection(IEnumerable<PlannedPhysicalRouteSegment> segments, IEnumerable<PlannedPhysicalNodeGeometry> nodes, string sourceId, string targetId) =>
        segments.Any(segment => nodes.Any(node => node.PhysicalNodeId != sourceId && node.PhysicalNodeId != targetId && Intersects((segment.Start, segment.End), node.AbsoluteBounds)));

    private static bool HasNodeIntersection(IReadOnlyList<AbsolutePoint> points, IEnumerable<PlannedPhysicalNodeGeometry> nodes) =>
        ToPairs(points).Any(segment => nodes.Any(node => Intersects(segment, node.AbsoluteBounds)));

    private static bool Intersects((AbsolutePoint Start, AbsolutePoint End) segment, AbsoluteRectangle rect)
    {
        if (segment.Start.X == segment.End.X)
            return segment.Start.X > rect.X && segment.Start.X < rect.X + rect.Width && Math.Max(segment.Start.Y, segment.End.Y) > rect.Y && Math.Min(segment.Start.Y, segment.End.Y) < rect.Y + rect.Height;
        return segment.Start.Y > rect.Y && segment.Start.Y < rect.Y + rect.Height && Math.Max(segment.Start.X, segment.End.X) > rect.X && Math.Min(segment.Start.X, segment.End.X) < rect.X + rect.Width;
    }

    private static bool HasSharedSegment(IEnumerable<PlannedPhysicalRouteSegment> current, IEnumerable<PlannedPhysicalRouteSegment> previous) =>
        current.Any(left => previous.Any(right => left.Axis == right.Axis && left.Axis == RouteAxis.Horizontal
            ? left.Start.Y == right.Start.Y && Math.Max(left.Start.X, right.Start.X) < Math.Min(left.End.X, right.End.X)
            : left.Axis == right.Axis && left.Start.X == right.Start.X && Math.Max(left.Start.Y, right.Start.Y) < Math.Min(left.End.Y, right.End.Y)));

    private static bool HasSharedSegment(IEnumerable<AbsolutePoint> points, IEnumerable<PlannedPhysicalRouteSegment> previous) => HasSharedSegment(
        ToSegmentsForComparison(points), previous);

    private static IEnumerable<PlannedPhysicalRouteSegment> ToSegmentsForComparison(IEnumerable<AbsolutePoint> points) =>
        ToPairs(points.ToList()).Select((pair, index) => new PlannedPhysicalRouteSegment("candidate", null, null, pair.Start, pair.End, new PlanningGridId("diagram"), pair.Start.X == pair.End.X ? RouteAxis.Vertical : RouteAxis.Horizontal, RouteStepRole.Turn, new LaneId($"candidate:{index}"), RouteTopologyFamily.SameLayer));

    private static IEnumerable<(AbsolutePoint Start, AbsolutePoint End)> ToPairs(IReadOnlyList<AbsolutePoint> points) => Enumerable.Range(1, Math.Max(0, points.Count - 1)).Select(i => (points[i - 1], points[i]));
    private static int PathLength(IReadOnlyList<AbsolutePoint> points) => ToPairs(points).Sum(pair => Math.Abs(pair.Start.X - pair.End.X) + Math.Abs(pair.Start.Y - pair.End.Y));
    private static int SegmentLength(PlannedPhysicalRouteSegment segment) => Math.Abs(segment.Start.X - segment.End.X) + Math.Abs(segment.Start.Y - segment.End.Y);
}
