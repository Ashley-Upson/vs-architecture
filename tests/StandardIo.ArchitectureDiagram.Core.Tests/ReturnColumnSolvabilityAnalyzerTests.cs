using StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core.Tests;

public sealed class ReturnColumnSolvabilityAnalyzerTests
{
    [Fact]
    public void Clear_left_exterior_is_horizontally_solvable() =>
        Assert.Equal(ReturnColumnHorizontalSolvability.LeftExteriorClear,
            ReturnColumnSolvabilityAnalyzer.Analyze(Constraint([], ["right-blocker"])));

    [Fact]
    public void Clear_right_exterior_is_horizontally_solvable() =>
        Assert.Equal(ReturnColumnHorizontalSolvability.RightExteriorClear,
            ReturnColumnSolvabilityAnalyzer.Analyze(Constraint(["left-blocker"], [])));

    [Fact]
    public void Blockers_on_both_exteriors_require_an_ordering_change() =>
        Assert.Equal(ReturnColumnHorizontalSolvability.OrderingInvariantInteriorBlocker,
            ReturnColumnSolvabilityAnalyzer.Analyze(Constraint(["left-blocker"], ["right-blocker"])));

    private static ReturnColumnEnvelopeConstraint Constraint(
        IReadOnlyList<string> leftBlockers,
        IReadOnlyList<string> rightBlockers) =>
        new("return-envelope:link:projects:0-1", "link",
            new ReturnColumnOwnership(0, 1, ["project-a", "project-b"],
                new Rect(0, 0, 400, 300), new LayoutRevision(4)),
            -8, 408, leftBlockers, rightBlockers, new LayoutRevision(4));
}
