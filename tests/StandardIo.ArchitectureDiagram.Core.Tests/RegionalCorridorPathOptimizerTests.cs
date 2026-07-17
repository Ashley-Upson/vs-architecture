using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

public sealed class RegionalCorridorPathOptimizerTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Score_accepts_three_route_monotonic_same_side_fanout(bool rightSide)
    {
        var side = rightSide ? FanoutSide.Right : FanoutSide.Left;
        var selection = Enumerable.Range(0, 3).ToDictionary(
            index => $"edge-{index}",
            index => Candidate($"edge-{index}", "accepted", P(0, index * 10, 100, index * 10), accepted: true) with
            {
                FanoutMemberships = new[] { Membership("source:node", index, index, index, side) }
            },
            StringComparer.Ordinal);

        var score = GlobalCorridorPathSelector.Score(selection, Capacities(), 10);

        Assert.Equal(0, score.TerminalFanoutViolations);
    }

    [Fact]
    public void Select_rejects_crossing_reduction_that_reverses_fanout_order()
    {
        var candidates = new Dictionary<string, IReadOnlyList<CorridorPathCandidate>>(StringComparer.Ordinal)
        {
            ["a"] = new[]
            {
                Candidate("a", "accepted", P(0, 0, 100, 0), accepted: true) with
                { FanoutMemberships = new[] { Membership("source:node", 0, 0, 0, FanoutSide.Right) } },
                Candidate("a", "reversed", P(0, 0, 0, 20, 100, 20)) with
                { FanoutMemberships = new[] { Membership("source:node", 2, 2, 0, FanoutSide.Right) } }
            },
            ["b"] = new[] { Candidate("b", "fixed", P(0, 10, 100, 10), accepted: true) with
                { FanoutMemberships = new[] { Membership("source:node", 1, 1, 1, FanoutSide.Right) } } },
            ["c"] = new[] { Candidate("c", "fixed", P(0, 30, 100, 30), accepted: true) with
                { FanoutMemberships = new[] { Membership("source:node", 2, 2, 2, FanoutSide.Right) } } }
        };

        var result = GlobalCorridorPathSelector.Select(candidates, Capacities(), 10, 4);

        Assert.Equal("accepted", result.Selected["a"].Signature.Value);
        Assert.Equal(0, result.FinalScore.TerminalFanoutViolations);
    }
    [Fact]
    public void Optimise_changes_only_local_conflict_in_one_hundred_edge_diagram()
    {
        var candidates = OneHundredEdgeCandidates();
        var accepted = candidates.ToDictionary(item => item.Key, item => item.Value.Single(candidate => candidate.IsAcceptedPath));

        var result = RegionalCorridorPathOptimizer.Optimise(candidates, Capacities(), 10, new RegionalOptimisationLimits());

        Assert.Equal("upper", result.Selected["blocker"].Signature.Value);
        Assert.True(result.FinalScore.CompareTo(result.InitialScore) < 0);
        Assert.Single(result.Decisions, decision => decision.Changed);
        foreach (var edgeId in accepted.Keys.Where(id => id != "blocker"))
        {
            Assert.Equal(accepted[edgeId].Points, result.Selected[edgeId].Points);
        }
    }

    [Fact]
    public void Optimise_processes_two_distant_conflict_regions_independently()
    {
        var candidates = ConflictCandidates("left", 0)
            .Concat(ConflictCandidates("right", 1000))
            .ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);

        var result = RegionalCorridorPathOptimizer.Optimise(candidates, Capacities(), 10, new RegionalOptimisationLimits());

        Assert.Equal(2, result.Regions.Count);
        Assert.Equal(2, result.Decisions.Count(decision => decision.Changed));
        Assert.Equal("upper", result.Selected["left-blocker"].Signature.Value);
        Assert.Equal("upper", result.Selected["right-blocker"].Signature.Value);
    }

    [Fact]
    public void Discover_merges_overlapping_interactions_into_one_region()
    {
        var candidates = ConflictCandidates("overlap", 0).ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        candidates["overlap-third"] = new[]
        {
            Candidate("overlap-third", "fixed", P(40, 0, 90, 0), accepted: true)
        };
        var accepted = candidates.ToDictionary(item => item.Key, item => item.Value.First(candidate => candidate.IsAcceptedPath));

        var interactions = RegionalCorridorPathOptimizer.DiscoverInteractions(accepted, 10);
        var regions = RegionalCorridorPathOptimizer.BuildRegions(interactions, candidates, new RegionalOptimisationLimits());

        Assert.Single(regions);
        Assert.Equal(3, regions[0].MutableEdgeIds.Count + regions[0].FixedContextEdgeIds.Count);
    }

    [Fact]
    public void Optimise_preserves_oversized_region_and_emits_structured_fallback()
    {
        var candidates = ConflictCandidates("large", 0).ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);

        var result = RegionalCorridorPathOptimizer.Optimise(
            candidates,
            Capacities(),
            10,
            new RegionalOptimisationLimits(MaximumMutableEdges: 0));

        var decision = Assert.Single(result.Decisions);
        Assert.False(decision.Changed);
        Assert.Equal(RegionFallbackReason.RegionTooLarge, decision.FallbackReason);
        Assert.All(result.Selected, item => Assert.True(item.Value.IsAcceptedPath));
    }

    [Fact]
    public void Optimise_scores_fixed_context_without_mutating_it()
    {
        var candidates = new Dictionary<string, IReadOnlyList<CorridorPathCandidate>>(StringComparer.Ordinal)
        {
            ["mutable"] = new[]
            {
                Candidate("mutable", "accepted", P(0, 0, 100, 0), accepted: true),
                Candidate("mutable", "overlaps-context", P(0, 20, 100, 20))
            },
            ["fixed"] = new[] { Candidate("fixed", "fixed", P(20, 20, 80, 20), accepted: true) },
            ["trigger"] = new[] { Candidate("trigger", "trigger", P(20, 0, 80, 0), accepted: true) }
        };

        var result = RegionalCorridorPathOptimizer.Optimise(candidates, Capacities(), 10, new RegionalOptimisationLimits());

        Assert.Equal("accepted", result.Selected["mutable"].Signature.Value);
        Assert.Equal("fixed", result.Selected["fixed"].Signature.Value);
    }

    [Fact]
    public void Optimise_is_deterministic_under_reversed_edge_and_candidate_enumeration()
    {
        var forward = OneHundredEdgeCandidates();
        var reverse = forward.Reverse().ToDictionary(
            item => item.Key,
            item => (IReadOnlyList<CorridorPathCandidate>)item.Value.Reverse().ToArray(),
            StringComparer.Ordinal);

        var first = RegionalCorridorPathOptimizer.Optimise(forward, Capacities(), 10, new RegionalOptimisationLimits());
        var second = RegionalCorridorPathOptimizer.Optimise(reverse, Capacities(), 10, new RegionalOptimisationLimits());

        Assert.Equal(
            first.Selected.OrderBy(item => item.Key).Select(item => (item.Key, item.Value.Signature.Value)),
            second.Selected.OrderBy(item => item.Key).Select(item => (item.Key, item.Value.Signature.Value)));
        Assert.Equal(first.Regions.Select(region => region.Id), second.Regions.Select(region => region.Id));
    }

    [Fact]
    public void Optimise_accepts_same_branch_exposure_alternative_for_higher_tier_improvement()
    {
        var candidates = ConflictCandidates("exposure", 0).ToDictionary(item => item.Key, item =>
            (IReadOnlyList<CorridorPathCandidate>)item.Value.Select(candidate => candidate with
            {
                ExposureRootId = "root-a",
                ExposureBranchId = "branch-a"
            }).ToArray(), StringComparer.Ordinal);

        var result = RegionalCorridorPathOptimizer.Optimise(candidates, Capacities(), 10, new RegionalOptimisationLimits());

        Assert.Equal("upper", result.Selected["exposure-blocker"].Signature.Value);
        Assert.Contains(result.Decisions, decision => decision.Changed);
    }

    [Fact]
    public void Optimise_rejects_sibling_branch_escape_without_higher_tier_necessity()
    {
        var candidates = new Dictionary<string, IReadOnlyList<CorridorPathCandidate>>(StringComparer.Ordinal)
        {
            ["mutable"] = new[]
            {
                Candidate("mutable", "accepted", P(0, 0, 100, 0), accepted: true, root: "root-a", branch: "branch-a"),
                Candidate("mutable", "sibling", P(0, 0, 0, -20, 100, -20, 100, 0), root: "root-a", branch: "branch-b")
            },
            ["fixed"] = new[] { Candidate("fixed", "fixed", P(20, 0, 80, 0), accepted: true, root: "root-a", branch: "branch-a") }
        };

        var result = RegionalCorridorPathOptimizer.Optimise(candidates, Capacities(), 10, new RegionalOptimisationLimits());

        Assert.Equal("accepted", result.Selected["mutable"].Signature.Value);
        Assert.Equal(RegionFallbackReason.ExposureLocalityViolation, Assert.Single(result.Decisions).FallbackReason);
    }

    [Fact]
    public void Optimise_reverts_local_change_that_regresses_whole_diagram_score()
    {
        var candidates = new Dictionary<string, IReadOnlyList<CorridorPathCandidate>>(StringComparer.Ordinal)
        {
            ["mutable"] = new[]
            {
                Candidate("mutable", "accepted", P(0, 0, 100, 0), accepted: true),
                Candidate("mutable", "locally-clean", P(0, 20, 100, 20))
            },
            ["local-trigger"] = new[] { Candidate("local-trigger", "fixed", P(20, 0, 80, 0), accepted: true) },
            ["outside-context"] = new[] { Candidate("outside-context", "fixed", P(20, 20, 80, 20), accepted: true) }
        };

        var result = RegionalCorridorPathOptimizer.Optimise(candidates, Capacities(), 10, new RegionalOptimisationLimits());

        Assert.Equal("accepted", result.Selected["mutable"].Signature.Value);
        Assert.Contains(result.Decisions, decision =>
            !decision.Changed && decision.FallbackReason == RegionFallbackReason.WholeDiagramRegression);
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<CorridorPathCandidate>> OneHundredEdgeCandidates()
    {
        var result = ConflictCandidates(string.Empty, 0).ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        for (var index = 0; index < 97; index++)
        {
            var edgeId = $"unaffected-{index:D3}";
            var y = 100 + index * 20;
            result[edgeId] = new[] { Candidate(edgeId, "accepted", P(1000, y, 1100, y), accepted: true) };
        }

        return result;
    }

    private static IEnumerable<KeyValuePair<string, IReadOnlyList<CorridorPathCandidate>>> ConflictCandidates(string prefix, int offset)
    {
        var label = string.IsNullOrEmpty(prefix) ? string.Empty : prefix + "-";
        yield return Pair(label + "blocker",
            Candidate(label + "blocker", "main", P(offset, 0, offset + 100, 0), accepted: true),
            Candidate(label + "blocker", "upper", P(offset, 0, offset, -20, offset + 100, -20, offset + 100, 0), escape: 20));
        yield return Pair(label + "dependent-a",
            Candidate(label + "dependent-a", "main-a", P(offset + 20, 0, offset + 80, 0), accepted: true));
        yield return Pair(label + "dependent-b",
            Candidate(label + "dependent-b", "main-b", P(offset + 20, 10, offset + 80, 10), accepted: true));
    }

    private static KeyValuePair<string, IReadOnlyList<CorridorPathCandidate>> Pair(
        string edgeId,
        params CorridorPathCandidate[] candidates) => new(edgeId, candidates);

    private static CorridorPathCandidate Candidate(
        string edgeId,
        string signature,
        IReadOnlyList<Point> points,
        bool accepted = false,
        int escape = 0,
        string? root = null,
        string? branch = null) =>
        new(edgeId, new[] { signature }, Array.Empty<string>(), new CorridorPathSignature(signature),
            new CorridorPathLocalCost(
                points.Zip(points.Skip(1), (left, right) => Math.Abs(left.X - right.X) + Math.Abs(left.Y - right.Y)).Sum(),
                Math.Max(0, points.Count - 2),
                escape),
            points,
            IsAcceptedPath: accepted,
            ExposureRootId: root,
            ExposureBranchId: branch);

    private static IReadOnlyDictionary<string, int> Capacities() => new Dictionary<string, int>(StringComparer.Ordinal);

    private static TerminalFanoutMembership Membership(
        string groupId, int terminal, int lane, int remote, FanoutSide side) =>
        new(groupId, FanoutDirection.Source, "node", terminal, lane, remote, side);

    private static IReadOnlyList<Point> P(params int[] coordinates) =>
        Enumerable.Range(0, coordinates.Length / 2)
            .Select(index => new Point(coordinates[index * 2], coordinates[index * 2 + 1]))
            .ToArray();
}
