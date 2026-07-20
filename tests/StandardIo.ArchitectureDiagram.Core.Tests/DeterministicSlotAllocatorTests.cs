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
    public void Endpoint_contact_orders_right_departure_above_left_arrival()
    {
        var result = Assign(
            Demand("left-arrival", 0, 10) with { MaximumEndpointRole = LinkSegmentEndpointRole.Arrival },
            Demand("right-departure", 10, 20) with { MinimumEndpointRole = LinkSegmentEndpointRole.Departure });

        Assert.Equal(0, result.SegmentsByDemandId["right-departure"].SlotIndex);
        Assert.Equal(1, result.SegmentsByDemandId["left-arrival"].SlotIndex);
    }

    [Fact]
    public void Endpoint_contact_orders_left_departure_above_right_arrival()
    {
        var result = Assign(
            Demand("left-departure", 0, 10) with { MaximumEndpointRole = LinkSegmentEndpointRole.Departure },
            Demand("right-arrival", 10, 20) with { MinimumEndpointRole = LinkSegmentEndpointRole.Arrival });

        Assert.Equal(0, result.SegmentsByDemandId["left-departure"].SlotIndex);
        Assert.Equal(1, result.SegmentsByDemandId["right-arrival"].SlotIndex);
    }

    [Fact]
    public void Near_endpoint_overlap_orders_departure_above_arrival()
    {
        var result = Assign(
            Demand("long-arrival", 0, 104) with { MaximumEndpointRole = LinkSegmentEndpointRole.Arrival },
            Demand("short-departure", 98, 130) with { MinimumEndpointRole = LinkSegmentEndpointRole.Departure });

        Assert.Equal(0, result.SegmentsByDemandId["short-departure"].SlotIndex);
        Assert.Equal(1, result.SegmentsByDemandId["long-arrival"].SlotIndex);
    }

    [Fact]
    public void Arrival_does_not_reuse_a_lane_above_its_departure_predecessor()
    {
        var result = Assign(
            Demand("blocker-a", 0, 8),
            Demand("blocker-b", 0, 8),
            Demand("departure", 0, 20) with { MaximumEndpointRole = LinkSegmentEndpointRole.Departure },
            Demand("arrival", 20, 30) with { MinimumEndpointRole = LinkSegmentEndpointRole.Arrival });

        Assert.True(
            result.SegmentsByDemandId["departure"].SlotIndex <
            result.SegmentsByDemandId["arrival"].SlotIndex);
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
