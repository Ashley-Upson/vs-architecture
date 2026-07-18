using System;
using System.Collections.Generic;
using System.Linq;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal static class LogicalRouteNormalizer
{
    public static IReadOnlyDictionary<string, LinkLayout> Normalize(
        IReadOnlyDictionary<string, NodeLayout> nodes,
        IReadOnlyDictionary<string, LinkLayout> links,
        int obstaclePadding)
    {
        return links.Values
            .OrderBy(link => link.Link.Order)
            .ThenBy(link => link.Link.Id, StringComparer.Ordinal)
            .ToDictionary(
                link => link.Link.Id,
                link => Normalize(nodes, link, obstaclePadding),
                StringComparer.Ordinal);
    }

    private static LinkLayout Normalize(
        IReadOnlyDictionary<string, NodeLayout> nodes,
        LinkLayout link,
        int obstaclePadding)
    {
        var points = new[] { link.SourcePoint }
            .Concat(link.Points)
            .Concat(new[] { link.TargetPoint })
            .ToList();
        RemoveConsecutiveDuplicates(points);
        var obstacles = nodes
            .Where(item => !string.Equals(item.Key, link.Link.SourceId, StringComparison.Ordinal) &&
                !string.Equals(item.Key, link.Link.TargetId, StringComparison.Ordinal))
            .Select(item => item.Value.Rect.Inflate(Math.Max(0, obstaclePadding)))
            .ToArray();

        CollapseSafeDirectRoute(points, obstacles);

        var changed = true;
        while (changed)
        {
            changed = RemoveSafeCollinearPoint(points, obstacles) ||
                RemoveSafeZeroAreaHook(points, obstacles);
        }

        return link.AcceptGeometry(
            points,
            LogicalRouteStage.Normalized,
            nameof(LogicalRouteNormalizer));
    }

    private static void CollapseSafeDirectRoute(IList<Point> points, IReadOnlyList<Rect> obstacles)
    {
        if (points.Count <= 2)
        {
            return;
        }

        var direct = new Segment(points[0], points[points.Count - 1]);
        var oneAxis = points.All(point => point.X == points[0].X) ||
            points.All(point => point.Y == points[0].Y);
        if (!oneAxis || obstacles.Any(direct.Intersects))
        {
            return;
        }

        while (points.Count > 2)
        {
            points.RemoveAt(1);
        }
    }

    private static void RemoveConsecutiveDuplicates(IList<Point> points)
    {
        for (var index = points.Count - 1; index > 0; index--)
        {
            if (points[index] == points[index - 1])
            {
                points.RemoveAt(index);
            }
        }
    }

    private static bool RemoveSafeCollinearPoint(IList<Point> points, IReadOnlyList<Rect> obstacles)
    {
        // Adjacent-to-terminal points encode the terminal side and protected stub.
        for (var index = 2; index < points.Count - 2; index++)
        {
            var direct = new Segment(points[index - 1], points[index + 1]);
            if ((!direct.IsHorizontal && !direct.IsVertical) ||
                obstacles.Any(direct.Intersects))
            {
                continue;
            }

            var incoming = new Segment(points[index - 1], points[index]);
            var outgoing = new Segment(points[index], points[index + 1]);
            if (incoming.IsHorizontal && outgoing.IsHorizontal ||
                incoming.IsVertical && outgoing.IsVertical)
            {
                points.RemoveAt(index);
                return true;
            }
        }

        return false;
    }

    private static bool RemoveSafeZeroAreaHook(IList<Point> points, IReadOnlyList<Rect> obstacles)
    {
        for (var index = 1; index < points.Count - 3; index++)
        {
            var first = new Segment(points[index], points[index + 1]);
            var middle = new Segment(points[index + 1], points[index + 2]);
            var third = new Segment(points[index + 2], points[index + 3]);
            var direct = new Segment(points[index], points[index + 3]);
            if ((!direct.IsHorizontal && !direct.IsVertical) ||
                obstacles.Any(direct.Intersects) ||
                !(first.IsHorizontal && middle.IsVertical && third.IsHorizontal ||
                  first.IsVertical && middle.IsHorizontal && third.IsVertical))
            {
                continue;
            }

            points.RemoveAt(index + 2);
            points.RemoveAt(index + 1);
            return true;
        }

        return false;
    }
}
