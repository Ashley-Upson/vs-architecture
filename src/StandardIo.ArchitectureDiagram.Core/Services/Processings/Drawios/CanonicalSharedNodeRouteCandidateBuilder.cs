using System;
using System.Collections.Generic;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core.Models;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal static class CanonicalSharedNodeRouteCandidateBuilder
{
    public static IReadOnlyList<IReadOnlyList<Point>> BuildExteriorRoutes(
        Point sourceExit,
        Point targetEntry,
        IReadOnlyList<Rect> obstacles,
        DiagramSettings settings,
        int laneOffset)
    {
        var clearance = settings.Layout.HorizontalSpacing + Math.Abs(laneOffset);
        var leftX = obstacles
            .Select(obstacle => obstacle.X)
            .DefaultIfEmpty(Math.Min(sourceExit.X, targetEntry.X))
            .Min() - clearance;
        var rightX = obstacles
            .Select(obstacle => obstacle.Right)
            .DefaultIfEmpty(Math.Max(sourceExit.X, targetEntry.X))
            .Max() + clearance;
        var routeClearance = settings.Layout.LinkPadding +
            Math.Abs(laneOffset) +
            settings.Layout.ParallelLaneSpacing * 4;
        var sourceLevels = EscapeLevels(sourceExit.Y, obstacles, routeClearance)
            .Where(y => y >= sourceExit.Y)
            .OrderBy(y => Math.Abs(y - sourceExit.Y))
            .ThenBy(y => y)
            .ToArray();
        var targetLevels = EscapeLevels(targetEntry.Y, obstacles, routeClearance)
            .Where(y => y <= targetEntry.Y)
            .OrderBy(y => Math.Abs(y - targetEntry.Y))
            .ThenByDescending(y => y)
            .ToArray();

        return new[] { leftX, rightX }
            .Distinct()
            .OrderBy(x => Math.Abs(x - sourceExit.X) + Math.Abs(x - targetEntry.X))
            .ThenBy(x => x)
            .SelectMany(sideX => sourceLevels
                .SelectMany(sourceY => targetLevels.Select(targetY =>
                    (IReadOnlyList<Point>)BuildRoute(sourceExit, targetEntry, sideX, sourceY, targetY)))
                .Where(route => !CrossesObstacle(route, obstacles))
                .GroupBy(RouteKey, StringComparer.Ordinal)
                .Select(group => group.First())
                .Take(4))
            .ToArray();
    }

    public static bool HasInvalidGeometry(
        IReadOnlyList<Point> route,
        IReadOnlyList<Rect> obstacles) =>
        CrossesObstacle(route, obstacles);

    private static Point[] BuildRoute(
        Point sourceExit,
        Point targetEntry,
        int sideX,
        int sourceY,
        int targetY) =>
        new[]
        {
            sourceExit,
            new Point(sourceExit.X, sourceY),
            new Point(sideX, sourceY),
            new Point(sideX, targetY),
            new Point(targetEntry.X, targetY),
            targetEntry
        };

    private static IEnumerable<int> EscapeLevels(int terminalY, IEnumerable<Rect> obstacles, int padding) =>
        new[] { terminalY }
            .Concat(obstacles.SelectMany(obstacle => new[]
            {
                obstacle.Y - padding,
                obstacle.Bottom + padding
            }))
            .Distinct();

    private static bool CrossesObstacle(IReadOnlyList<Point> route, IReadOnlyList<Rect> obstacles)
    {
        for (var index = 0; index < route.Count - 1; index++)
        {
            var segment = new Segment(route[index], route[index + 1]);
            if (obstacles.Any(segment.Intersects))
            {
                return true;
            }
        }

        return false;
    }

    private static string RouteKey(IEnumerable<Point> route) =>
        string.Join(";", route.Select(point => $"{point.X},{point.Y}"));
}
