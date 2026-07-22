using StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core.Tests;

public sealed class LogicalRouteNormalizerTests
{
    [Fact]
    public void Normalize_makes_safe_single_axis_route_authoritative_before_serialization()
    {
        var link = Link(new[]
        {
            new Point(50, 20),
            new Point(50, 120),
            new Point(50, 40),
            new Point(50, 180)
        });

        var result = LogicalRouteNormalizer.Normalize(
            new Dictionary<string, NodeLayout>(),
            new Dictionary<string, LinkLayout> { [link.Link.Id] = link },
            obstaclePadding: 0)[link.Link.Id];

        Assert.Empty(result.Points);
        Assert.Equal(LogicalRouteStage.Normalized, result.RouteState.Stage);
    }

    [Fact]
    public void Normalize_removes_safe_same_axis_reversal_before_validation()
    {
        var link = Link(new[]
        {
            new Point(20, 20),
            new Point(20, 40),
            new Point(80, 40),
            new Point(50, 40),
            new Point(50, 100),
            new Point(80, 100),
            new Point(80, 120)
        });

        var result = LogicalRouteNormalizer.Normalize(
            new Dictionary<string, NodeLayout>(),
            new Dictionary<string, LinkLayout> { ["edge"] = link },
            0)["edge"];

        Assert.Equal(
            new[]
            {
                new Point(20, 20),
                new Point(20, 40),
                new Point(50, 40),
                new Point(50, 100),
                new Point(80, 100),
                new Point(80, 120)
            },
            CompletePoints(result));
        Assert.Equal(LogicalRouteStage.Normalized, result.RouteState.Stage);
        Assert.DoesNotContain(
            TraceabilityValidator.Validate(
                new Dictionary<string, NodeLayout>(),
                new Dictionary<string, LinkLayout> { ["edge"] = result },
                12).Violations,
            violation => violation.Code == TraceabilityViolationCode.ImmediateReversal);
    }

    [Fact]
    public void Normalize_preserves_detour_when_direct_segment_crosses_obstacle()
    {
        var link = Link(new[]
        {
            new Point(20, 60),
            new Point(20, 40),
            new Point(20, 20),
            new Point(80, 20),
            new Point(80, 40),
            new Point(80, 100)
        });
        var obstacle = new NodeLayout(
            new RenderNode("obstacle", "project", "Obstacle", "Fixture.Obstacle", "Class", false, string.Empty, 0,
                Array.Empty<string>(), Array.Empty<StandardIo.ArchitectureDiagram.Core.Models.TypeProperty>(), 0),
            new Rect(45, 30, 15, 20),
            0,
            false);

        var result = LogicalRouteNormalizer.Normalize(
            new Dictionary<string, NodeLayout> { ["obstacle"] = obstacle },
            new Dictionary<string, LinkLayout> { ["edge"] = link },
            0)["edge"];

        Assert.Contains(new Point(20, 20), CompletePoints(result));
        Assert.Contains(new Point(80, 20), CompletePoints(result));
    }

    private static LinkLayout Link(IReadOnlyList<Point> points) =>
        new(
            new RenderLink("edge", "source", "target", "internal", 0),
            points[0],
            points[^1],
            points.Skip(1).Take(points.Count - 2),
            0.5,
            0.5);

    private static Point[] CompletePoints(LinkLayout link) =>
        new[] { link.SourcePoint }.Concat(link.Points).Concat(new[] { link.TargetPoint }).ToArray();
}
