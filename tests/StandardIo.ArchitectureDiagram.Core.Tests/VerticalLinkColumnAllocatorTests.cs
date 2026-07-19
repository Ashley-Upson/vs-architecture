using StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core.Tests;

public sealed class VerticalLinkColumnAllocatorTests
{
    [Fact]
    public void Disjoint_vertical_ranges_reuse_the_preferred_coordinate()
    {
        var result = VerticalLinkColumnAllocator.Assign(new[]
        {
            Demand("a", 100, 0, 2), Demand("b", 100, 2, 4)
        }, 20);

        Assert.All(result.ColumnsByDemandId.Values, column => Assert.Equal(100, column.X));
    }

    [Fact]
    public void Overlapping_ranges_receive_separated_deterministic_columns()
    {
        var forward = VerticalLinkColumnAllocator.Assign(new[]
        {
            Demand("b", 100, 1, 4), Demand("a", 100, 0, 3), Demand("c", 100, 2, 5)
        }, 20);
        var reverse = VerticalLinkColumnAllocator.Assign(new[]
        {
            Demand("c", 100, 2, 5), Demand("a", 100, 0, 3), Demand("b", 100, 1, 4)
        }, 20);

        Assert.Equal(new[] { 80, 100, 120 }, forward.ColumnsByDemandId.Values.Select(item => item.X).OrderBy(x => x));
        Assert.Equal(forward.ColumnsByDemandId.OrderBy(item => item.Key).Select(item => item.Value.X),
            reverse.ColumnsByDemandId.OrderBy(item => item.Key).Select(item => item.Value.X));
    }

    [Fact]
    public void Allocation_fails_when_no_allowed_column_preserves_spacing()
    {
        Assert.Throws<InvalidOperationException>(() => VerticalLinkColumnAllocator.Assign(new[]
        {
            Demand("a", 100, 0, 3, 100, 100), Demand("b", 100, 1, 4, 100, 100)
        }, 20));
    }

    [Fact]
    public void Endpoint_only_vertical_contact_does_not_create_collinear_sharing()
    {
        var result = VerticalLinkColumnAllocator.Assign(new[]
        {
            Demand("a", 100, 0, 2), Demand("b", 100, 2, 4)
        }, 20);

        Assert.Equal(0, result.ConflictComponentCount);
        Assert.Equal(1, result.ConflictComparisons);
    }

    private static VerticalLinkColumnDemand Demand(
        string id,
        int preferredX,
        int sourceLayer,
        int destinationLayer,
        int minimumX = 0,
        int maximumX = 200) =>
        new(id, id, preferredX, new AxisInterval(minimumX, maximumX), sourceLayer, destinationLayer,
            new AxisInterval(sourceLayer * 100, destinationLayer * 100), 10, $"source-{id}", $"target-{id}",
            "project", null, new LayoutRevision(0), new RouteRevision(0));
}
