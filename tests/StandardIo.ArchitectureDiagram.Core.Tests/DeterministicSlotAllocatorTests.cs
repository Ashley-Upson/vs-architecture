using System;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core.Tests;

public sealed class DeterministicSlotAllocatorTests
{
    [Fact]
    public void Disjoint_intervals_share_a_lane()
    {
        var result = Assign(Demand("a", 0, 10), Demand("b", 30, 40));
        Assert.All(result.SegmentsByDemandId.Values, rail => Assert.Equal(0, rail.SlotIndex));
    }

    [Fact]
    public void Positive_overlap_receives_distinct_lanes()
    {
        var result = Assign(Demand("a", 0, 20), Demand("b", 10, 30));
        Assert.Equal(new[] { 0, 1 }, result.SegmentsByDemandId.Values.Select(item => item.SlotIndex).OrderBy(item => item));
    }

    [Fact]
    public void Spacing_inflation_creates_a_conflict()
    {
        var result = Assign(Demand("a", 0, 10), Demand("b", 15, 25));
        Assert.Single(result.Components);
        Assert.Equal(2, result.Components[0].Segments.Select(item => item.SlotIndex).Distinct().Count());
    }

    [Fact]
    public void Endpoint_contact_is_policy_controlled()
    {
        var shared = Assign(new LinkSegmentAssignmentOptions(0, 4), Demand("a", 0, 10), Demand("b", 10, 20));
        var separated = Assign(new LinkSegmentAssignmentOptions(0, 4, true, true), Demand("a", 0, 10), Demand("b", 10, 20));
        Assert.Equal(2, shared.Components.Count);
        Assert.Single(separated.Components);
        Assert.Equal(2, separated.SegmentsByDemandId.Values.Select(item => item.SlotIndex).Distinct().Count());
    }

    [Fact]
    public void Transitive_overlap_forms_one_complete_component()
    {
        var result = Assign(Demand("a", 0, 10), Demand("b", 8, 18), Demand("c", 16, 26));
        Assert.Single(result.Components);
        Assert.Equal(3, result.Components[0].Demands.Count);
    }

    [Fact]
    public void Reversed_enumeration_and_equal_ties_are_deterministic()
    {
        var demands = new[] { Demand("b", 0, 20), Demand("a", 0, 20), Demand("c", 0, 20) };
        var forward = Assign(demands);
        var reverse = Assign(demands.AsEnumerable().Reverse().ToArray());
        Assert.Equal(forward.SegmentsByDemandId.OrderBy(item => item.Key).Select(item => (item.Key, item.Value.SlotIndex)),
            reverse.SegmentsByDemandId.OrderBy(item => item.Key).Select(item => (item.Key, item.Value.SlotIndex)));
        Assert.Equal(0, forward.SegmentsByDemandId["a"].SlotIndex);
    }

    [Fact]
    public void Required_extent_has_no_lane_count_limit()
    {
        var demands = Enumerable.Range(0, 128).Select(index => Demand(index.ToString("D3"), 0, 100)).ToArray();
        var result = Assign(demands);
        Assert.Equal(128, result.SegmentsByDemandId.Values.Select(item => item.SlotIndex).Distinct().Count());
        Assert.Equal(8 + 128 * 12, result.RequiredExtent);
    }

    [Fact]
    public void Different_regions_are_rejected_instead_of_silently_interacting()
    {
        var demand = Demand("a", 0, 10) with { AllowedAxisRange = new AxisInterval(0, 80) };
        Assert.Throws<ArgumentException>(() => DeterministicSlotAllocator.Assign(
            Region(), new[] { demand }, new LinkSegmentAssignmentOptions(12, 4)));
    }

    private static DeterministicSlotAssignment Assign(params LinkSegmentDemand[] demands) =>
        Assign(new LinkSegmentAssignmentOptions(12, 4), demands);

    private static DeterministicSlotAssignment Assign(LinkSegmentAssignmentOptions options, params LinkSegmentDemand[] demands) =>
        DeterministicSlotAllocator.Assign(Region(), demands, options);

    private static LinkSegmentAllocationRegionIdentity Region() => new(
        LinkSegmentOrientation.Horizontal, new AxisInterval(100, 200), "band:0:1",
        new MovementScopeIdentity(MovementScopeKind.LayerAndLowerSuffix, "depth:1"), new LayoutRevision(1));

    private static LinkSegmentDemand Demand(string id, int start, int end) => new(
        id, id, LinkSegmentOrientation.Horizontal, new AxisInterval(start, end), new AxisInterval(100, 200),
        null, LinkSegmentRole.Through, null, null,
        new MovementScopeIdentity(MovementScopeKind.LayerAndLowerSuffix, "depth:1"),
        new LayoutRevision(1), new RouteRevision(1));
}
