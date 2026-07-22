using System;
using System.Collections.Generic;
using System.Linq;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal sealed record JunctionGeometryMetrics(
    int SharedBends,
    int OverlapLength,
    int SpacingDeficits,
    int Crossings,
    int LaneInversions,
    int PathLength,
    int BendCount)
{
    public static JunctionGeometryMetrics Measure(
        IReadOnlyDictionary<string, IReadOnlyList<Point>> routes,
        int minimumSpacing,
        int laneInversions = 0)
    {
        var ordered = routes.OrderBy(item => item.Key, StringComparer.Ordinal).ToArray();
        var sharedBends = 0;
        var overlapLength = 0;
        var spacingDeficits = 0;
        var crossings = 0;

        for (var leftIndex = 0; leftIndex < ordered.Length; leftIndex++)
        {
            var leftSegments = Segments(ordered[leftIndex].Value).ToArray();
            var leftBends = InteriorBends(ordered[leftIndex].Value);
            for (var rightIndex = leftIndex + 1; rightIndex < ordered.Length; rightIndex++)
            {
                var rightSegments = Segments(ordered[rightIndex].Value).ToArray();
                sharedBends += leftBends.Intersect(InteriorBends(ordered[rightIndex].Value)).Count();
                foreach (var left in leftSegments)
                {
                    foreach (var right in rightSegments)
                    {
                        overlapLength += left.OverlapLength(right);
                        spacingDeficits += SpacingDeficit(left, right, minimumSpacing);
                        if (CrossesInside(left, right))
                        {
                            crossings++;
                        }
                    }
                }
            }
        }

        return new JunctionGeometryMetrics(
            sharedBends,
            overlapLength,
            spacingDeficits,
            crossings,
            laneInversions,
            ordered.Sum(route => Segments(route.Value).Sum(segment => segment.Length)),
            ordered.Sum(route => InteriorBends(route.Value).Count));
    }

    private static IEnumerable<Segment> Segments(IReadOnlyList<Point> points) =>
        points.Zip(points.Skip(1), (start, end) => new Segment(start, end));

    private static IReadOnlyCollection<Point> InteriorBends(IReadOnlyList<Point> points) =>
        points.Skip(1).Take(Math.Max(0, points.Count - 2)).ToArray();

    private static int SpacingDeficit(Segment left, Segment right, int required)
    {
        if (left.IsHorizontal && right.IsHorizontal && Overlaps(left.Start.X, left.End.X, right.Start.X, right.End.X))
        {
            var distance = Math.Abs(left.Start.Y - right.Start.Y);
            return distance > 0 && distance < required ? required - distance : 0;
        }

        if (left.IsVertical && right.IsVertical && Overlaps(left.Start.Y, left.End.Y, right.Start.Y, right.End.Y))
        {
            var distance = Math.Abs(left.Start.X - right.Start.X);
            return distance > 0 && distance < required ? required - distance : 0;
        }

        return 0;
    }

    private static bool CrossesInside(Segment left, Segment right)
    {
        if (left.IsHorizontal == right.IsHorizontal || left.IsVertical == right.IsVertical)
        {
            return false;
        }

        var horizontal = left.IsHorizontal ? left : right;
        var vertical = left.IsVertical ? left : right;
        return BetweenExclusive(vertical.Start.X, horizontal.Start.X, horizontal.End.X) &&
            BetweenExclusive(horizontal.Start.Y, vertical.Start.Y, vertical.End.Y);
    }

    private static bool Overlaps(int leftStart, int leftEnd, int rightStart, int rightEnd) =>
        Math.Min(leftStart, leftEnd) < Math.Max(rightStart, rightEnd) &&
        Math.Min(rightStart, rightEnd) < Math.Max(leftStart, leftEnd);

    private static bool BetweenExclusive(int value, int start, int end) =>
        value > Math.Min(start, end) && value < Math.Max(start, end);
}
