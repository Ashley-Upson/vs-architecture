using StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core.Tests;

public sealed class DeterministicSharedTurnAllocatorTests
{
    [Fact]
    public void Shared_turns_are_orthogonal_distinct_and_input_order_independent()
    {
        var rails = new[]
        {
            Rail("b", "departure", LinkSegmentOrientation.Vertical, 40, LinkSegmentRole.ConnectionDeparture),
            Rail("b", "through", LinkSegmentOrientation.Horizontal, 120, LinkSegmentRole.Through),
            Rail("b", "arrival", LinkSegmentOrientation.Vertical, 180, LinkSegmentRole.ConnectionArrival),
            Rail("a", "departure", LinkSegmentOrientation.Vertical, 20, LinkSegmentRole.ConnectionDeparture),
            Rail("a", "through", LinkSegmentOrientation.Horizontal, 100, LinkSegmentRole.Through),
            Rail("a", "arrival", LinkSegmentOrientation.Vertical, 160, LinkSegmentRole.ConnectionArrival)
        };

        var forward = DeterministicSharedTurnAllocator.Assign(rails);
        var reverse = DeterministicSharedTurnAllocator.Assign(rails.AsEnumerable().Reverse());

        Assert.Empty(forward.RejectedRouteIds);
        Assert.Equal(forward.TransitionsByRouteId, reverse.TransitionsByRouteId);
        var turns = forward.TransitionsByRouteId.Values.SelectMany(item => item).Select(item => item.Turn).ToArray();
        Assert.Equal(turns.Length, turns.Distinct().Count());
        Assert.All(forward.TransitionsByRouteId, pair =>
        {
            var routeRails = rails.Where(item => item.LogicalRouteId == pair.Key).ToArray();
            var railsById = routeRails.ToDictionary(item => item.Id);
            Assert.All(pair.Value, transition =>
            {
                Assert.True(OnSegment(transition.Turn, railsById[transition.FromAssignedLinkSegmentId]));
                Assert.True(OnSegment(transition.Turn, railsById[transition.ToAssignedLinkSegmentId]));
            });
        });
    }

    private static bool OnSegment(Point point, AssignedLinkSegment segment) =>
        segment.Orientation == LinkSegmentOrientation.Horizontal
            ? point.Y == segment.AxisCoordinate && segment.OccupiedInterval.ContainsClosed(point.X)
            : point.X == segment.AxisCoordinate && segment.OccupiedInterval.ContainsClosed(point.Y);

    private static AssignedLinkSegment Rail(string route, string id, LinkSegmentOrientation orientation, int axis, LinkSegmentRole role) =>
        new($"{route}:{id}", $"{route}:{id}:demand", route, orientation, axis, 0,
            new AxisInterval(0, 200), role, new LayoutRevision(1), new RouteRevision(2));
}
