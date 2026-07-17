using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

public sealed class GlobalCorridorPathSelectorTests
{
    [Fact]
    public void Reducer_retains_invalid_accepted_baseline_for_safe_candidate_comparison()
    {
        var accepted = Candidate("edge", "accepted", 100, "accepted", P(0, 0, 100, 0)) with
        {
            HasInvalidGeometry = true,
            IsAcceptedPath = true
        };
        var safe = Candidate("edge", "safe", 120, "safe", P(0, 0, 0, 20, 100, 20, 100, 0));

        var retained = CorridorPathCandidateReducer.Retain(new[] { accepted, safe }, 8, 100);

        Assert.Contains(accepted, retained);
        Assert.Contains(safe, retained);
    }

    [Fact]
    public void Reducer_measures_detour_from_valid_route_when_accepted_baseline_is_invalid()
    {
        var accepted = Candidate("edge", "invalid", 100, "invalid", P(0, 0, 100, 0)) with
        {
            HasInvalidGeometry = true,
            IsAcceptedPath = true
        };
        var longSafe = Candidate("edge", "long-safe", 1000, "outside", P(0, 0, 0, 500, 100, 500, 100, 0));

        var retained = CorridorPathCandidateReducer.Retain(new[] { accepted, longSafe }, 8, 200);

        Assert.Contains(longSafe, retained);
    }

    [Fact]
    public void Select_moves_locally_shortest_blocker_to_longer_clean_corridor()
    {
        var candidates = EarlyCommitCandidates();

        var result = GlobalCorridorPathSelector.Select(candidates, Capacities(("main", 3), ("upper", 1)), 10, 4);

        Assert.Equal("upper", result.Selected["blocker"].Signature.Value);
        Assert.True(result.FinalScore.CompareTo(result.InitialScore) < 0);
        Assert.Equal(0, result.FinalScore.SharedSegmentLength);
        Assert.Contains(result.Evaluations, evaluation =>
            evaluation.EdgeId == "blocker" && evaluation.Signature == "upper" && evaluation.IsSelected);
        Assert.Contains(result.Evaluations, evaluation =>
            evaluation.EdgeId == "blocker" && evaluation.Signature == "main" && !evaluation.IsSelected &&
            evaluation.Reason.StartsWith("Rejected because score", StringComparison.Ordinal));
    }

    [Fact]
    public void Reducer_preserves_structurally_distinct_paths_before_lane_variants()
    {
        var candidates = new[]
        {
            Candidate("edge", "left", 100, "left", P(0, 0, 0, 20, 100, 20)),
            Candidate("edge", "left", 101, "left", P(0, 0, 0, 21, 100, 21)),
            Candidate("edge", "right", 110, "right", P(0, 0, 100, 0, 100, 20))
        };

        var retained = CorridorPathCandidateReducer.Retain(candidates, 2, 100);

        Assert.Equal(new[] { "left", "right" }, retained.Select(candidate => candidate.Signature.Value).OrderBy(x => x));
    }

    [Fact]
    public void Reducer_rejects_invalid_and_excessive_detour_candidates()
    {
        var retained = CorridorPathCandidateReducer.Retain(new[]
        {
            Candidate("edge", "direct", 100, "direct", P(0, 0, 100, 0)),
            Candidate("edge", "invalid", 90, "invalid", P(0, 0, 90, 0), invalid: true),
            Candidate("edge", "outside", 400, "outside", P(0, 0, 0, -150, 100, -150, 100, 0))
        }, 5, 100);

        Assert.Single(retained);
        Assert.Equal("direct", retained[0].Signature.Value);
    }

    [Fact]
    public void Select_retains_traceable_high_utilisation_corridor_when_escape_is_only_lower_priority_gain()
    {
        var candidates = new Dictionary<string, IReadOnlyList<CorridorPathCandidate>>(StringComparer.Ordinal)
        {
            ["a"] = new[] { Candidate("a", "main-a", 100, "main", P(0, 0, 100, 0)), Candidate("a", "escape-a", 220, "escape", P(0, 0, 0, -60, 100, -60), escape: 60) },
            ["b"] = new[] { Candidate("b", "main-b", 100, "main", P(0, 10, 100, 10)) }
        };

        var result = GlobalCorridorPathSelector.Select(candidates, Capacities(("main", 2), ("escape", 2)), 10, 4);

        Assert.Equal("main-a", result.Selected["a"].Signature.Value);
    }

    [Fact]
    public void Select_moves_one_edge_when_corridor_is_over_capacity()
    {
        var result = GlobalCorridorPathSelector.Select(EarlyCommitCandidates(), Capacities(("main", 2), ("upper", 1)), 10, 4);

        Assert.Equal(0, result.FinalScore.CapacityFailure);
        Assert.Equal("upper", result.Selected["blocker"].Signature.Value);
    }

    [Fact]
    public void Select_never_trades_shared_segments_for_crossing_reduction()
    {
        var candidates = new Dictionary<string, IReadOnlyList<CorridorPathCandidate>>(StringComparer.Ordinal)
        {
            ["a"] = new[]
            {
                Candidate("a", "crossing", 100, "a", P(0, 0, 100, 0)),
                Candidate("a", "shared", 90, "b", P(50, -50, 50, 50))
            },
            ["b"] = new[] { Candidate("b", "fixed", 100, "b", P(0, 10, 100, 10)) }
        };

        var result = GlobalCorridorPathSelector.Select(candidates, Capacities(("a", 2), ("b", 2)), 10, 4);

        Assert.Equal("crossing", result.Selected["a"].Signature.Value);
        Assert.Equal(0, result.FinalScore.SharedSegmentLength);
    }

    [Fact]
    public void Select_prefers_compact_perpendicular_crossings_over_disproportionate_exterior_detour()
    {
        var candidates = new Dictionary<string, IReadOnlyList<CorridorPathCandidate>>(StringComparer.Ordinal)
        {
            ["mutable"] = new[]
            {
                Candidate("mutable", "compact", 140, "compact", P(0, 0, 140, 0)),
                Candidate("mutable", "exterior", 5000, "exterior", P(0, 0, 0, -2450, 100, -2450, 100, 0), escape: 2450)
            },
            ["crossing-a"] = new[] { Candidate("crossing-a", "fixed-a", 40, "fixed-a", P(40, -20, 40, 20)) },
            ["crossing-b"] = new[] { Candidate("crossing-b", "fixed-b", 40, "fixed-b", P(80, -20, 80, 20)) }
        };

        var result = GlobalCorridorPathSelector.Select(
            candidates,
            Capacities(("compact", 1), ("exterior", 1), ("fixed-a", 1), ("fixed-b", 1)),
            10,
            4);

        Assert.Equal("compact", result.Selected["mutable"].Signature.Value);
        Assert.Equal(2, result.FinalScore.CrossingsAndCongestion);
    }

    [Fact]
    public void Select_longer_path_wins_when_short_path_has_higher_tier_identity_failure()
    {
        var result = GlobalCorridorPathSelector.Select(EarlyCommitCandidates(), Capacities(("main", 3), ("upper", 1)), 10, 4);

        Assert.True(result.Selected["blocker"].LocalCost.PathLength > 100);
        Assert.Equal(0, result.FinalScore.SharedSegmentLength);
    }

    [Fact]
    public void Select_is_independent_of_edge_and_candidate_enumeration_order()
    {
        var forwardCandidates = EarlyCommitCandidates();
        var reverseCandidates = forwardCandidates.Reverse().ToDictionary(
            item => item.Key,
            item => (IReadOnlyList<CorridorPathCandidate>)item.Value.Reverse().ToArray(),
            StringComparer.Ordinal);

        var forward = GlobalCorridorPathSelector.Select(forwardCandidates, Capacities(("main", 3), ("upper", 1)), 10, 4);
        var reverse = GlobalCorridorPathSelector.Select(reverseCandidates, Capacities(("main", 3), ("upper", 1)), 10, 4);

        Assert.Equal(
            forward.Selected.OrderBy(item => item.Key).Select(item => (item.Key, item.Value.Signature.Value)),
            reverse.Selected.OrderBy(item => item.Key).Select(item => (item.Key, item.Value.Signature.Value)));
    }

    [Fact]
    public void Select_stops_deterministically_at_stability_or_pass_limit()
    {
        var stable = GlobalCorridorPathSelector.Select(EarlyCommitCandidates(), Capacities(("main", 3), ("upper", 1)), 10, 10);
        var bounded = GlobalCorridorPathSelector.Select(EarlyCommitCandidates(), Capacities(("main", 3), ("upper", 1)), 10, 1);

        Assert.InRange(stable.CompletedPasses, 1, 10);
        Assert.Equal(1, bounded.CompletedPasses);
        Assert.Equal(stable.Selected["blocker"].Signature.Value, bounded.Selected["blocker"].Signature.Value);
    }

    [Fact]
    public void Score_is_lexicographic_and_does_not_blend_lower_tiers()
    {
        var higherTierFailure = new GlobalRouteScore(0, 1, 0, 0, 0, 0, 0, 0);
        var manyLowerTierFailures = new GlobalRouteScore(0, 0, 999, 999, 999, 999, 999, 999);

        Assert.True(manyLowerTierFailures.CompareTo(higherTierFailure) < 0);
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<CorridorPathCandidate>> EarlyCommitCandidates() =>
        new Dictionary<string, IReadOnlyList<CorridorPathCandidate>>(StringComparer.Ordinal)
        {
            ["blocker"] = new[]
            {
                Candidate("blocker", "main", 100, "main", P(0, 0, 100, 0)),
                Candidate("blocker", "upper", 140, "upper", P(0, 0, 0, -20, 100, -20, 100, 0), escape: 20)
            },
            ["dependent-a"] = new[] { Candidate("dependent-a", "main-a", 60, "main", P(20, 0, 80, 0)) },
            ["dependent-b"] = new[] { Candidate("dependent-b", "main-b", 60, "main", P(20, 10, 80, 10)) }
        };

    private static CorridorPathCandidate Candidate(
        string edgeId,
        string signature,
        int length,
        string corridor,
        IReadOnlyList<Point> points,
        bool invalid = false,
        int escape = 0) =>
        new(edgeId, new[] { corridor }, Array.Empty<string>(), new CorridorPathSignature(signature),
            new CorridorPathLocalCost(length, Math.Max(0, points.Count - 2), escape), points, invalid);

    private static IReadOnlyDictionary<string, int> Capacities(params (string Id, int Capacity)[] capacities) =>
        capacities.ToDictionary(item => item.Id, item => item.Capacity, StringComparer.Ordinal);

    private static IReadOnlyList<Point> P(params int[] coordinates) =>
        Enumerable.Range(0, coordinates.Length / 2)
            .Select(index => new Point(coordinates[index * 2], coordinates[index * 2 + 1]))
            .ToArray();
}
