using System;
using System.Collections.Generic;
using System.Linq;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal static class GlobalCorridorPathSelector
{
    public static CorridorPathSelectionResult Select(
        IReadOnlyDictionary<string, IReadOnlyList<CorridorPathCandidate>> candidates,
        IReadOnlyDictionary<string, int> corridorCapacities,
        int minimumSpacing,
        int maximumPasses)
    {
        var retained = candidates.OrderBy(item => item.Key, StringComparer.Ordinal).ToDictionary(
            item => item.Key,
            item => (IReadOnlyList<CorridorPathCandidate>)item.Value
                .OrderBy(candidate => candidate.LocalCost.PathLength)
                .ThenBy(candidate => candidate.LocalCost.BendCount)
                .ThenBy(candidate => candidate.Signature.Value, StringComparer.Ordinal)
                .ToArray(),
            StringComparer.Ordinal);
        var selected = retained.ToDictionary(
            item => item.Key,
            item => item.Value.FirstOrDefault(candidate => candidate.IsAcceptedPath) ?? item.Value.First(),
            StringComparer.Ordinal);
        var initial = Score(selected, corridorCapacities, minimumSpacing);
        var decisions = selected.ToDictionary(
            item => item.Key,
            item => new CorridorPathDecision(item.Key, item.Value.Signature.Value, item.Value.Signature.Value,
                "Initial locally cheapest candidate retained."),
            StringComparer.Ordinal);
        var completedPasses = 0;

        for (var pass = 0; pass < maximumPasses; pass++)
        {
            var changed = false;
            completedPasses++;
            foreach (var edgeId in retained.Keys.OrderBy(id => id, StringComparer.Ordinal))
            {
                var current = selected[edgeId];
                var currentScore = Score(selected, corridorCapacities, minimumSpacing);
                var best = current;
                var bestScore = currentScore;
                foreach (var alternative in retained[edgeId]
                    .Where(candidate => candidate.Signature.Value != current.Signature.Value)
                    .OrderBy(candidate => candidate.Signature.Value, StringComparer.Ordinal))
                {
                    selected[edgeId] = alternative;
                    var score = Score(selected, corridorCapacities, minimumSpacing);
                    if (score.CompareTo(bestScore) < 0)
                    {
                        best = alternative;
                        bestScore = score;
                    }
                }

                selected[edgeId] = best;
                if (best.Signature.Value != current.Signature.Value)
                {
                    changed = true;
                    decisions[edgeId] = decisions[edgeId] with
                    {
                        FinalSignature = best.Signature.Value,
                        Reason = $"Accepted strict lexicographic improvement {currentScore} -> {bestScore}."
                    };
                }
            }

            if (!changed)
            {
                break;
            }
        }

        var final = Score(selected, corridorCapacities, minimumSpacing);
        return new CorridorPathSelectionResult(
            selected,
            initial,
            final,
            decisions.Values.OrderBy(item => item.EdgeId, StringComparer.Ordinal).ToArray(),
            completedPasses);
    }

    internal static GlobalRouteScore Score(
        IReadOnlyDictionary<string, CorridorPathCandidate> selection,
        IReadOnlyDictionary<string, int> corridorCapacities,
        int minimumSpacing)
    {
        var routes = selection.OrderBy(item => item.Key, StringComparer.Ordinal).ToArray();
        var invalid = routes.Count(route => route.Value.HasInvalidGeometry);
        var shared = 0;
        var spacing = 0;
        var crossings = 0;
        var reusedBends = 0;
        for (var leftIndex = 0; leftIndex < routes.Length; leftIndex++)
        {
            var left = Segments(routes[leftIndex].Value.Points).ToArray();
            var leftBends = routes[leftIndex].Value.Points.Skip(1).Take(Math.Max(0, routes[leftIndex].Value.Points.Count - 2));
            for (var rightIndex = leftIndex + 1; rightIndex < routes.Length; rightIndex++)
            {
                var right = Segments(routes[rightIndex].Value.Points).ToArray();
                var rightBends = routes[rightIndex].Value.Points.Skip(1).Take(Math.Max(0, routes[rightIndex].Value.Points.Count - 2));
                reusedBends += leftBends.Intersect(rightBends).Count();
                foreach (var leftSegment in left)
                {
                    foreach (var rightSegment in right)
                    {
                        shared += leftSegment.OverlapLength(rightSegment);
                        spacing += SpacingDeficit(leftSegment, rightSegment, minimumSpacing);
                        if (CrossesInside(leftSegment, rightSegment))
                        {
                            crossings++;
                        }
                    }
                }
            }
        }

        var usage = routes.SelectMany(route => route.Value.CorridorIds)
            .GroupBy(id => id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var capacityFailure = usage.Sum(item => Math.Max(0,
            item.Value - (corridorCapacities.TryGetValue(item.Key, out var capacity) ? capacity : int.MaxValue)));
        return new GlobalRouteScore(
            invalid,
            shared,
            spacing,
            routes.Sum(route => route.Value.AmbiguousTransitions) + reusedBends,
            capacityFailure,
            crossings,
            routes.Sum(route => route.Value.LocalCost.CanvasEscape),
            routes.Sum(route => route.Value.LocalCost.PathLength + route.Value.LocalCost.BendCount));
    }

    private static IEnumerable<Segment> Segments(IReadOnlyList<Point> points) =>
        points.Zip(points.Skip(1), (start, end) => new Segment(start, end));

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

    private static bool Overlaps(int a, int b, int c, int d) =>
        Math.Min(a, b) < Math.Max(c, d) && Math.Min(c, d) < Math.Max(a, b);

    private static bool BetweenExclusive(int value, int start, int end) =>
        value > Math.Min(start, end) && value < Math.Max(start, end);
}
