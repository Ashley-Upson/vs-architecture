using StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core.Tests;

public sealed class ChangedIntervalSlotReassignmentTests
{
    [Theory]
    [InlineData(0, 20, 30, 50, 0, 40, 30, 50, true, false)]
    [InlineData(0, 50, 30, 40, 0, 20, 30, 40, true, false)]
    [InlineData(0, 20, 30, 50, 0, 45, 10, 50, true, false)]
    [InlineData(0, 20, 30, 50, 0, 50, 20, 70, true, false)]
    [InlineData(0, 60, 30, 50, 0, 30, 40, 50, true, false)]
    [InlineData(0, 20, 30, 50, 0, 50, 10, 60, true, false)]
    [InlineData(0, 50, 20, 30, 0, 40, 20, 30, true, false)]
    [InlineData(0, 20, 30, 50, 0, 50, 10, 60, true, true)]
    public void Movement_is_followed_by_exactly_one_deterministic_changed_interval_pass(
        int firstStart, int firstEnd, int secondStart, int secondEnd,
        int movedFirstStart, int movedFirstEnd, int movedSecondStart, int movedSecondEnd,
        bool intervalsChanged, bool narrowRegion)
    {
        var allowed = new AxisInterval(0, narrowRegion ? 8 : 100);
        var region = new LinkSegmentAllocationRegionIdentity(LinkSegmentOrientation.Horizontal,
            allowed, "test", null, new LayoutRevision(1));
        var initial = new[] { Demand("a", firstStart, firstEnd, allowed), Demand("b", secondStart, secondEnd, allowed) };
        var changed = new[] { Demand("a", movedFirstStart, movedFirstEnd, allowed), Demand("b", movedSecondStart, movedSecondEnd, allowed) };

        var result = ChangedIntervalSlotReassignment.ReassignOnce(
            region, initial, changed, new LinkSegmentAssignmentOptions(12, 4));

        Assert.Equal(1, result.PassCount);
        Assert.Equal(narrowRegion, result.FurtherMovementRequested);
        Assert.Equal(intervalsChanged, !initial.Select(item => item.OccupiedInterval)
            .SequenceEqual(changed.Select(item => item.OccupiedInterval)));
        var repeated = ChangedIntervalSlotReassignment.ReassignOnce(
            region, initial, changed, new LinkSegmentAssignmentOptions(12, 4));
        Assert.Equal(result.FinalAssignment.SegmentsByDemandId.OrderBy(item => item.Key),
            repeated.FinalAssignment.SegmentsByDemandId.OrderBy(item => item.Key));
    }

    private static LinkSegmentDemand Demand(string id, int start, int end, AxisInterval allowed) => new(
        id, id, LinkSegmentOrientation.Horizontal, new AxisInterval(start, end), allowed, null,
        LinkSegmentRole.Through, 0, 0, null, new LayoutRevision(1), new RouteRevision(1));
}
