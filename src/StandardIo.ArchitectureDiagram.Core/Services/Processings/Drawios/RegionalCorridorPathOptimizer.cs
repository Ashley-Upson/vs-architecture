using System;
using System.Collections.Generic;
using System.Linq;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal static class RegionalCorridorPathOptimizer
{
    public static RegionalPathSelectionResult Optimise(
        IReadOnlyDictionary<string, IReadOnlyList<CorridorPathCandidate>> candidates,
        IReadOnlyDictionary<string, int> corridorCapacities,
        int minimumSpacing,
        RegionalOptimisationLimits limits)
    {
        var retained = candidates.OrderBy(item => item.Key, StringComparer.Ordinal).ToDictionary(
            item => item.Key,
            item => (IReadOnlyList<CorridorPathCandidate>)item.Value
                .OrderBy(candidate => candidate.IsAcceptedPath ? 0 : 1)
                .ThenBy(candidate => candidate.Signature.Value, StringComparer.Ordinal)
                .Take(limits.MaximumCandidatesPerEdge)
                .ToArray(),
            StringComparer.Ordinal);
        var selected = retained.ToDictionary(
            item => item.Key,
            item => item.Value.FirstOrDefault(candidate => candidate.IsAcceptedPath) ?? item.Value.First(),
            StringComparer.Ordinal);
        var initialScore = GlobalCorridorPathSelector.Score(selected, corridorCapacities, minimumSpacing);
        var interactions = DiscoverInteractions(selected, minimumSpacing);
        var regions = BuildRegions(interactions, retained, limits);
        var decisions = new List<RegionOptimisationDecision>();

        foreach (var region in regions.Take(limits.MaximumRegions))
        {
            var beforeWhole = GlobalCorridorPathSelector.Score(selected, corridorCapacities, minimumSpacing);
            if (region.MutableEdgeIds.Count > limits.MaximumMutableEdges ||
                region.FixedContextEdgeIds.Count > limits.MaximumFixedContextEdges)
            {
                decisions.Add(Decision(region, beforeWhole, beforeWhole, false, RegionFallbackReason.RegionTooLarge,
                    $"Region exceeds limits mutable={region.MutableEdgeIds.Count}/{limits.MaximumMutableEdges}, context={region.FixedContextEdgeIds.Count}/{limits.MaximumFixedContextEdges}."));
                continue;
            }

            if (region.MutableEdgeIds.Count == 0)
            {
                decisions.Add(Decision(region, beforeWhole, beforeWhole, false, RegionFallbackReason.NoAlternativeCandidate,
                    "No interacting edge has a viable alternative candidate."));
                continue;
            }

            var localCandidates = region.MutableEdgeIds.Concat(region.FixedContextEdgeIds)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToDictionary(
                    id => id,
                    id => region.MutableEdgeIds.Contains(id, StringComparer.Ordinal)
                        ? retained[id]
                        : (IReadOnlyList<CorridorPathCandidate>)new[] { selected[id] with { IsAcceptedPath = true } },
                    StringComparer.Ordinal);
            var local = GlobalCorridorPathSelector.Select(
                localCandidates,
                corridorCapacities,
                minimumSpacing,
                limits.MaximumPasses);
            var trial = selected.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
            foreach (var edgeId in region.MutableEdgeIds)
            {
                trial[edgeId] = local.Selected[edgeId];
            }

            var afterWhole = GlobalCorridorPathSelector.Score(trial, corridorCapacities, minimumSpacing);
            if (afterWhole.CompareTo(beforeWhole) < 0)
            {
                selected = trial;
                decisions.Add(Decision(region, beforeWhole, afterWhole, true, RegionFallbackReason.None,
                    $"Accepted strict whole-diagram improvement {beforeWhole} -> {afterWhole}."));
            }
            else
            {
                var reason = local.FinalScore.CompareTo(local.InitialScore) < 0
                    ? RegionFallbackReason.WholeDiagramRegression
                    : RegionFallbackReason.NoStrictImprovement;
                decisions.Add(Decision(region, beforeWhole, beforeWhole, false, reason,
                    reason == RegionFallbackReason.WholeDiagramRegression
                        ? $"Rejected local improvement because whole-diagram score would be {afterWhole}."
                        : "No strict regional lexicographic improvement was found."));
            }
        }

        var finalScore = GlobalCorridorPathSelector.Score(selected, corridorCapacities, minimumSpacing);
        return new RegionalPathSelectionResult(selected, interactions, regions, decisions, initialScore, finalScore);
    }

    internal static IReadOnlyList<RouteInteraction> DiscoverInteractions(
        IReadOnlyDictionary<string, CorridorPathCandidate> selected,
        int minimumSpacing)
    {
        var ordered = selected.OrderBy(item => item.Key, StringComparer.Ordinal).ToArray();
        var interactions = new List<RouteInteraction>();
        for (var leftIndex = 0; leftIndex < ordered.Length; leftIndex++)
        {
            for (var rightIndex = leftIndex + 1; rightIndex < ordered.Length; rightIndex++)
            {
                var left = ordered[leftIndex];
                var right = ordered[rightIndex];
                var pair = new Dictionary<string, CorridorPathCandidate>(StringComparer.Ordinal)
                {
                    [left.Key] = left.Value,
                    [right.Key] = right.Value
                };
                var score = GlobalCorridorPathSelector.Score(pair, new Dictionary<string, int>(), minimumSpacing);
                var reason = score.SharedSegmentLength > 0 ? RouteInteractionReason.SharedSegment
                    : score.SpacingDeficit > 0 ? RouteInteractionReason.SpacingDeficit
                    : score.AmbiguousTransitions > 0 ? RouteInteractionReason.ReusedBend
                    : score.CrossingsAndCongestion > 0 ? RouteInteractionReason.Crossing
                    : (RouteInteractionReason?)null;
                if (reason is null)
                {
                    continue;
                }

                var severity = score.SharedSegmentLength + score.SpacingDeficit +
                    score.AmbiguousTransitions + score.CrossingsAndCongestion;
                interactions.Add(new RouteInteraction(
                    left.Key,
                    right.Key,
                    reason.Value,
                    Union(Bounds(left.Value.Points), Bounds(right.Value.Points)),
                    severity));
            }
        }

        return interactions.OrderBy(interaction => interaction.Reason)
            .ThenByDescending(interaction => interaction.Severity)
            .ThenBy(interaction => interaction.Region.X)
            .ThenBy(interaction => interaction.Region.Y)
            .ThenBy(interaction => interaction.FirstEdgeId, StringComparer.Ordinal)
            .ThenBy(interaction => interaction.SecondEdgeId, StringComparer.Ordinal)
            .ToArray();
    }

    internal static IReadOnlyList<RouteOptimisationRegion> BuildRegions(
        IReadOnlyList<RouteInteraction> interactions,
        IReadOnlyDictionary<string, IReadOnlyList<CorridorPathCandidate>> candidates,
        RegionalOptimisationLimits limits)
    {
        var remaining = new HashSet<string>(interactions.SelectMany(item => new[] { item.FirstEdgeId, item.SecondEdgeId }), StringComparer.Ordinal);
        var regions = new List<RouteOptimisationRegion>();
        while (remaining.Count > 0)
        {
            var seed = remaining.OrderBy(id => id, StringComparer.Ordinal).First();
            var component = new HashSet<string>(StringComparer.Ordinal) { seed };
            var queue = new Queue<string>();
            queue.Enqueue(seed);
            while (queue.Count > 0)
            {
                var edge = queue.Dequeue();
                foreach (var neighbour in interactions.Where(item => item.FirstEdgeId == edge || item.SecondEdgeId == edge)
                    .Select(item => item.FirstEdgeId == edge ? item.SecondEdgeId : item.FirstEdgeId)
                    .OrderBy(id => id, StringComparer.Ordinal))
                {
                    if (component.Add(neighbour))
                    {
                        queue.Enqueue(neighbour);
                    }
                }
            }

            remaining.ExceptWith(component);
            var componentInteractions = interactions.Where(item => component.Contains(item.FirstEdgeId) && component.Contains(item.SecondEdgeId)).ToArray();
            var mutable = component.Where(id => candidates[id].Count > 1).OrderBy(id => id, StringComparer.Ordinal).ToArray();
            var context = component.Except(mutable, StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal).ToArray();
            var bounds = componentInteractions.Select(item => item.Region).Aggregate(Union).Inflate(limits.InteractionMargin);
            var id = $"region_{bounds.X}_{bounds.Y}_{string.Join("_", component.OrderBy(value => value, StringComparer.Ordinal))}";
            regions.Add(new RouteOptimisationRegion(id, bounds, mutable, context, componentInteractions));
        }

        return regions.OrderBy(region => region.Interactions.Min(item => item.Reason))
            .ThenByDescending(region => region.Interactions.Sum(item => item.Severity))
            .ThenBy(region => region.Bounds.X)
            .ThenBy(region => region.Bounds.Y)
            .ThenBy(region => region.Id, StringComparer.Ordinal)
            .ToArray();
    }

    private static RegionOptimisationDecision Decision(
        RouteOptimisationRegion region,
        GlobalRouteScore initial,
        GlobalRouteScore final,
        bool changed,
        RegionFallbackReason fallback,
        string reason) =>
        new(region.Id, region.MutableEdgeIds, region.FixedContextEdgeIds, initial, final, changed, fallback, reason);

    private static Rect Bounds(IReadOnlyList<Point> points)
    {
        var left = points.Min(point => point.X);
        var top = points.Min(point => point.Y);
        return new Rect(left, top, points.Max(point => point.X) - left, points.Max(point => point.Y) - top);
    }

    private static Rect Union(Rect left, Rect right)
    {
        var x = Math.Min(left.X, right.X);
        var y = Math.Min(left.Y, right.Y);
        return new Rect(x, y, Math.Max(left.Right, right.Right) - x, Math.Max(left.Bottom, right.Bottom) - y);
    }
}
