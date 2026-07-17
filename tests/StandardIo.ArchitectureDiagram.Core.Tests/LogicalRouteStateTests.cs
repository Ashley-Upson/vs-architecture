using StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core.Tests;

public sealed class LogicalRouteStateTests
{
    [Fact]
    public void AcceptedRevisionReplacesAuthorityAndRetainsImmutableHistory()
    {
        var selected = LogicalRouteState.Selected(
            "edge",
            "candidate-a",
            new[] { new Point(0, 0), new Point(0, 20), new Point(40, 20) });

        var allocated = selected.Accept(
            LogicalRouteStage.Allocated,
            "CorridorLaneGeometryCompiler",
            new[] { new Point(0, 0), new Point(0, 24), new Point(40, 24) });

        Assert.Equal(1, allocated.Revision);
        Assert.Equal(LogicalRouteStage.Allocated, allocated.Stage);
        Assert.Equal(new[] { new Point(0, 0), new Point(0, 24), new Point(40, 24) }, allocated.AuthoritativePoints);
        Assert.Single(allocated.History);
        Assert.Equal(selected.AuthoritativePoints, allocated.History[0].Points);
    }

    [Fact]
    public void RejectedRevisionKeepsCurrentAuthority()
    {
        var selected = LogicalRouteState.Selected(
            "edge",
            "candidate-a",
            new[] { new Point(0, 0), new Point(0, 20), new Point(40, 20) });

        var rejected = selected.Reject("EdgeTraversalCompiler", new[] { "Unsupported junction." });

        Assert.Equal(selected.Revision, rejected.Revision);
        Assert.Equal(selected.Stage, rejected.Stage);
        Assert.Equal(selected.AuthoritativePoints, rejected.AuthoritativePoints);
        Assert.Equal(LogicalRouteCompilationStatus.Rejected, rejected.CompilationStatus);
        Assert.Contains("Unsupported junction.", rejected.Diagnostics);
    }

    [Fact]
    public void RouteCannotMoveBackwardsToEarlierStage()
    {
        var compiled = LogicalRouteState.Selected(
                "edge",
                "candidate-a",
                new[] { new Point(0, 0), new Point(0, 20) })
            .Accept(LogicalRouteStage.Compiled, "Compiler", new[] { new Point(0, 0), new Point(0, 20) });

        Assert.Throws<InvalidOperationException>(() => compiled.Accept(
            LogicalRouteStage.Allocated,
            "Allocator",
            compiled.AuthoritativePoints));
    }
}
