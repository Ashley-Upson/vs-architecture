using StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core.Tests;

public sealed class LinkConnectionPathCompatibilityTests
{
    [Fact]
    public void Preserves_terminal_and_stub_but_allows_first_avoidance_bend_to_move()
    {
        var accepted = Candidate(new[]
        {
            new Point(100, 0),
            new Point(100, 20),
            new Point(100, 80),
            new Point(300, 80),
            new Point(300, 180),
            new Point(300, 200)
        });
        var alternative = Candidate(new[]
        {
            new Point(100, 0),
            new Point(100, 20),
            new Point(40, 20),
            new Point(40, 180),
            new Point(300, 180),
            new Point(300, 200)
        });

        Assert.True(LinkConnectionPathCompatibility.Preserves(accepted, alternative));
    }

    [Fact]
    public void Rejects_changed_terminal_stub()
    {
        var accepted = Candidate(new[]
        {
            new Point(100, 0),
            new Point(100, 20),
            new Point(300, 180),
            new Point(300, 200)
        });
        var alternative = Candidate(new[]
        {
            new Point(100, 0),
            new Point(120, 20),
            new Point(300, 180),
            new Point(300, 200)
        });

        Assert.False(LinkConnectionPathCompatibility.Preserves(accepted, alternative));
    }

    private static CorridorPathCandidate Candidate(IReadOnlyList<Point> points) =>
        new(
            "edge",
            Array.Empty<string>(),
            Array.Empty<string>(),
            new CorridorPathSignature(string.Join(";", points)),
            new CorridorPathLocalCost(0, 0, 0),
            points,
            FanoutMemberships: new[]
            {
                new LinkConnectionFanoutMembership(
                    "group",
                    FanoutDirection.Source,
                    "source",
                    100,
                    0,
                    300,
                    FanoutSide.Right)
            });
}
