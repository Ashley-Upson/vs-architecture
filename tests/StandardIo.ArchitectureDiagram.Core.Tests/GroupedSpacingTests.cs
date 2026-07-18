using System.Linq;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core.Tests;

public sealed class GroupedSpacingTests
{
    [Fact]
    public void Inclusive_intervals_distinguish_disjoint_contact_and_overlap()
    {
        Assert.Equal(IntervalContactKind.Disjoint, BandConflictGrouper.Contact(0, 10, 20, 30));
        Assert.Equal(IntervalContactKind.EndpointContact, BandConflictGrouper.Contact(0, 10, 10, 20));
        Assert.Equal(IntervalContactKind.PositiveOverlap, BandConflictGrouper.Contact(0, 15, 10, 20));
    }

    [Fact]
    public void Endpoint_contact_with_a_bend_is_ambiguous()
    {
        var first = new Segment(new Point(0, 10), new Point(10, 10));
        var turn = new Segment(new Point(10, 10), new Point(10, 20));
        var second = new Segment(new Point(10, 10), new Point(20, 10));

        Assert.Equal(RoutePointContactKind.AmbiguousBend,
            BandConflictGrouper.ClassifyContact(first, turn, second, null));
    }

    [Fact]
    public void Strict_interior_perpendicular_intersection_is_clean_crossover()
    {
        var horizontal = new Segment(new Point(0, 10), new Point(20, 10));
        var vertical = new Segment(new Point(10, 0), new Point(10, 20));

        Assert.Equal(RoutePointContactKind.CleanCrossover,
            BandConflictGrouper.ClassifyContact(horizontal, null, vertical, null));
    }

    [Fact]
    public void Perpendicular_intersection_with_turn_is_not_clean_crossover()
    {
        var horizontal = new Segment(new Point(0, 10), new Point(20, 10));
        var vertical = new Segment(new Point(10, 0), new Point(10, 20));
        var turn = new Segment(new Point(20, 10), new Point(20, 20));

        Assert.Equal(RoutePointContactKind.AmbiguousBend,
            BandConflictGrouper.ClassifyContact(horizontal, turn, vertical, null));
    }

    [Fact]
    public void Transitive_overlap_forms_one_group()
    {
        var band = Band(
            Demand("a", 0, 20), Demand("b", 15, 35), Demand("c", 30, 50));

        var groups = BandConflictGrouper.Group(band, 1, 5, out _);

        Assert.Single(groups);
        Assert.Equal(new[] { "a", "b", "c" }, groups[0].Demands.Select(item => item.Id));
        Assert.Equal(2, groups[0].RequiredLaneCount);
    }

    [Fact]
    public void Non_overlapping_intervals_reuse_one_lane()
    {
        var band = Band(Demand("a", 0, 10), Demand("b", 20, 30));

        var groups = BandConflictGrouper.Group(band, 5, 5, out _);

        Assert.Equal(2, groups.Count);
        Assert.All(groups, group => Assert.Equal(1, group.RequiredLaneCount));
    }

    [Fact]
    public void Minimum_constraint_only_increases()
    {
        var store = new MonotonicSpacingConstraintStore();
        var key = new SpacingConstraintKey(1, 0, SpacingConstraintScope.LayerBoundary, "0-1");

        Assert.True(store.Merge(new MinimumSpacingConstraint(key, 156, "group-a")));
        Assert.False(store.Merge(new MinimumSpacingConstraint(key, 72, "group-b")));
        Assert.Equal(156, store.Minimum(key));
    }

    [Fact]
    public void Conflicting_proposals_merge_deterministically_by_maximum()
    {
        var key = new SpacingConstraintKey(1, 0, SpacingConstraintScope.LayerBoundary, "0-1");
        var forward = new MonotonicSpacingConstraintStore();
        var reverse = new MonotonicSpacingConstraintStore();
        var proposals = new[]
        {
            new MinimumSpacingConstraint(key, 72, "b"),
            new MinimumSpacingConstraint(key, 156, "a")
        };
        foreach (var proposal in proposals) forward.Merge(proposal);
        foreach (var proposal in proposals.AsEnumerable().Reverse()) reverse.Merge(proposal);

        Assert.Equal(forward.Snapshot(), reverse.Snapshot());
        Assert.Equal(156, forward.Minimum(key));
    }

    private static InterLayerBandObservation Band(params BandRouteDemand[] demands) => new(
        new InterLayerBandId(0, 1, new LayoutRevision(1)), 20, 100, 80, 80, 0,
        new BandRouteMembership[0], demands, 0, 0, 0, 0,
        new BandReturnRegionObservation[0], new string[0]);

    private static BandRouteDemand Demand(string id, int start, int end) => new(
        id, id, new RouteRevision(1), new InterLayerBandId(0, 1, new LayoutRevision(1)),
        1, BandMembershipRole.SourceTransition, start, end, 0, BandRouteDirection.Right, 0);
}
