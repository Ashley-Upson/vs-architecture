using StandardIo.ArchitectureDiagram.Core.Models;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core.Tests;

public sealed class CanonicalSharedNodeRouteCandidateBuilderTests
{
    [Fact]
    public void Long_incoming_route_has_deterministic_exterior_candidates_on_both_sides()
    {
        var settings = DiagramSettings.CreateDefault();
        var obstacles = new[]
        {
            new Rect(100, 100, 120, 60),
            new Rect(500, 300, 120, 60),
            new Rect(900, 500, 120, 60)
        };

        var routes = CanonicalSharedNodeRouteCandidateBuilder.BuildExteriorRoutes(
            new Point(560, 180),
            new Point(160, 480),
            obstacles,
            settings,
            0);

        Assert.NotEmpty(routes);
        Assert.Contains(routes, route => route.Any(point => point.X < obstacles.Min(obstacle => obstacle.X)));
        Assert.Contains(routes, route => route.Any(point => point.X > obstacles.Max(obstacle => obstacle.Right)));
        Assert.All(routes, route => Assert.DoesNotContain(
            Segments(route),
            segment => obstacles.Any(segment.Intersects)));
        var reversed = CanonicalSharedNodeRouteCandidateBuilder.BuildExteriorRoutes(
            new Point(560, 180),
            new Point(160, 480),
            Enumerable.Reverse(obstacles).ToArray(),
            settings,
            0);
        Assert.Equal(routes.Select(RouteKey), reversed.Select(RouteKey));
    }

    [Fact]
    public void Accepted_long_route_crossing_a_node_is_classified_as_invalid()
    {
        var route = new[]
        {
            new Point(300, 500),
            new Point(1000, 500),
            new Point(1000, 100),
            new Point(0, 100)
        };
        var obstacles = new[] { new Rect(400, 450, 100, 100) };

        Assert.True(CanonicalSharedNodeRouteCandidateBuilder.HasInvalidGeometry(route, obstacles));
    }

    private static string RouteKey(IEnumerable<Point> route) =>
        string.Join(";", route.Select(point => $"{point.X},{point.Y}"));

    private static IEnumerable<Segment> Segments(IReadOnlyList<Point> route)
    {
        for (var index = 0; index < route.Count - 1; index++)
        {
            yield return new Segment(route[index], route[index + 1]);
        }
    }
}
