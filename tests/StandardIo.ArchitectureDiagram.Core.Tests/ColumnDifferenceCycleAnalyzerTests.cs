using StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core.Tests;

public sealed class ColumnDifferenceCycleAnalyzerTests
{
    [Fact]
    public void Reciprocal_destination_blockers_form_one_stable_cycle()
    {
        var first = Constraint("a", "destination-a", "destination-b");
        var second = Constraint("b", "destination-b", "destination-a");

        var forward = ColumnDifferenceCycleAnalyzer.FindMutualDestinationCycles(new[] { first, second });
        var reverse = ColumnDifferenceCycleAnalyzer.FindMutualDestinationCycles(new[] { second, first });

        var cycle = Assert.Single(forward);
        Assert.Equal("destination-a", cycle.FirstDestinationSubtreeId);
        Assert.Equal("destination-b", cycle.SecondDestinationSubtreeId);
        Assert.Equal(forward, reverse);
    }

    [Fact]
    public void One_way_blocking_is_not_a_cycle()
    {
        Assert.Empty(ColumnDifferenceCycleAnalyzer.FindMutualDestinationCycles(new[]
        {
            Constraint("a", "destination-a", "destination-b"),
            Constraint("b", "destination-b", "other")
        }));
    }

    private static ColumnToEnvelopeDifferenceConstraint Constraint(
        string id,
        string destination,
        string blocker) =>
        new(id, $"link-{id}", $"column-{id}", 100, destination, new Rect(80, 200, 40, 40),
            blocker, new Rect(90, 100, 40, 40), new AxisInterval(100, 200), 12,
            new HorizontalDifferenceAlternative(HorizontalMovementDirection.Left, 77,
                new[] { new MovementScopeIdentity(MovementScopeKind.LayoutSubtree, destination) }),
            new HorizontalDifferenceAlternative(HorizontalMovementDirection.Right, 143,
                new[] { new MovementScopeIdentity(MovementScopeKind.LayoutSubtree, destination) }),
            new LayoutRevision(1), new LayoutRevision(1));
}
