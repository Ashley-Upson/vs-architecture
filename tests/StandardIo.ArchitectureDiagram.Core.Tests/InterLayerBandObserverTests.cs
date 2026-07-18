using StandardIo.ArchitectureDiagram.Core.Models;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

public sealed class InterLayerBandObserverTests
{
    [Fact]
    public void Adjacent_downward_route_belongs_to_one_band()
    {
        var fixture = Fixture(new[] { Node("source", 0), Node("target", 1) },
            Link("edge", "source", "target", 0, (10, 20), (10, 50), (80, 50), (80, 100)));

        var report = Observe(fixture);

        var membership = Assert.Single(Assert.Single(report.Bands).Memberships);
        Assert.Equal(BandMembershipRole.SourceTransition, membership.Role);
    }

    [Fact]
    public void Skipped_layer_route_belongs_to_every_crossed_band()
    {
        var fixture = Fixture(new[] { Node("source", 0), Node("middle", 1), Node("target", 2) },
            Link("edge", "source", "target", 0, (10, 20), (10, 50), (80, 50), (80, 150), (40, 150), (40, 200)));

        var report = Observe(fixture);

        Assert.Equal(new[] { 0, 1 }, report.Bands.Where(b => b.Memberships.Count > 0).Select(b => b.Id.UpperLayer));
        Assert.Equal(2, report.Telemetry.MaximumBandsCrossed);
    }

    [Fact]
    public void Upward_route_receives_return_classification()
    {
        var fixture = Fixture(new[] { Node("target", 0), Node("source", 1) },
            Link("edge", "source", "target", 0, (80, 100), (80, 50), (10, 50), (10, 20)));

        var membership = Assert.Single(Assert.Single(Observe(fixture).Bands).Memberships);

        Assert.Equal(BandMembershipRole.Return, membership.Role);
    }

    [Fact]
    public void Multiple_horizontal_segments_in_one_band_remain_distinguishable()
    {
        var fixture = Fixture(new[] { Node("source", 0), Node("target", 1) },
            Link("edge", "source", "target", 0, (10, 20), (10, 40), (60, 40), (60, 70), (90, 70), (90, 100)));

        var demands = Assert.Single(Observe(fixture).Bands).Demands;

        Assert.Equal(2, demands.Count);
        Assert.Equal(2, demands.Select(demand => demand.SegmentIndex).Distinct().Count());
    }

    [Fact]
    public void Positive_overlap_and_clearance_endpoint_contact_require_distinct_lanes()
    {
        var fixture = Fixture(new[] { Node("a", 0), Node("b", 0, 1), Node("x", 1), Node("y", 1, 1) },
            Link("one", "a", "x", 0, (0, 20), (0, 50), (50, 50), (50, 100)),
            Link("two", "b", "y", 1, (50, 20), (50, 50), (90, 50), (90, 100)));

        var band = Assert.Single(Observe(fixture).Bands);

        Assert.Equal(2, band.HypotheticalLaneCount);
        Assert.Equal(2, band.MaximumSimultaneousOverlap);
    }

    [Fact]
    public void Separated_intervals_reuse_lowest_lane()
    {
        var fixture = Fixture(new[] { Node("a", 0), Node("b", 0, 1), Node("x", 1), Node("y", 1, 1) },
            Link("one", "a", "x", 0, (0, 20), (0, 50), (40, 50), (40, 100)),
            Link("two", "b", "y", 1, (60, 20), (60, 50), (100, 50), (100, 100)));

        Assert.All(Assert.Single(Observe(fixture).Bands).Demands, demand => Assert.Equal(0, demand.LaneIndex));
    }

    [Fact]
    public void Three_simultaneous_overlaps_require_three_lanes_and_terminal_order()
    {
        var fixture = Fixture(new[] { Node("a", 0), Node("b", 0, 1), Node("c", 0, 2), Node("x", 1), Node("y", 1, 1), Node("z", 1, 2) },
            Link("one", "a", "x", 0, (0, 20), (0, 50), (100, 50), (100, 100)),
            Link("two", "b", "y", 1, (10, 20), (10, 50), (90, 50), (90, 100)),
            Link("three", "c", "z", 2, (20, 20), (20, 50), (80, 50), (80, 100)));

        var band = Assert.Single(Observe(fixture).Bands);

        Assert.Equal(3, band.HypotheticalLaneCount);
        Assert.Equal(new[] { 0, 1, 2 }, band.Demands.OrderBy(d => d.TerminalOrder).Select(d => d.LaneIndex));
    }

    [Fact]
    public void Clean_perpendicular_crossing_creates_no_horizontal_demand()
    {
        var fixture = Fixture(new[] { Node("source", 0), Node("target", 1) },
            Link("edge", "source", "target", 0, (10, 20), (10, 100)));

        Assert.Empty(Assert.Single(Observe(fixture).Bands).Demands);
    }

    [Fact]
    public void Reversed_enumeration_is_deterministic()
    {
        var nodes = new[] { Node("a", 0), Node("b", 0, 1), Node("x", 1), Node("y", 1, 1) };
        var links = new[] {
            Link("one", "a", "x", 0, (0, 20), (0, 50), (80, 50), (80, 100)),
            Link("two", "b", "y", 1, (10, 20), (10, 60), (90, 60), (90, 100)) };

        var forward = Observe(Fixture(nodes, links));
        var reverse = Observe(Fixture(nodes.AsEnumerable().Reverse(), links.AsEnumerable().Reverse().ToArray()));

        Assert.Equal(forward.Bands.SelectMany(b => b.Demands), reverse.Bands.SelectMany(b => b.Demands));
        Assert.Equal(forward.Telemetry with { ElapsedMicroseconds = 0 }, reverse.Telemetry with { ElapsedMicroseconds = 0 });
    }

    [Fact]
    public void Revision_mismatches_are_rejected()
    {
        var fixture = Fixture(new[] { Node("source", 0), Node("target", 1) },
            Link("edge", "source", "target", 0, (10, 20), (10, 100)));
        var moved = fixture.Placement.Revise(fixture.Placement.Nodes, fixture.Placement.Projects);

        Assert.Throws<InvalidOperationException>(() => InterLayerBandObserver.Observe(
            moved, fixture.Routes, DiagramSettings.CreateDefault()));
        Assert.Throws<InvalidOperationException>(() => InterLayerBandObserver.Observe(
            fixture.Placement, fixture.Routes, DiagramSettings.CreateDefault(), expectedRouteRevision: new RouteRevision(4)));
    }

    [Fact]
    public void Observation_does_not_mutate_geometry_and_cancellation_returns_no_result()
    {
        var fixture = Fixture(new[] { Node("source", 0), Node("target", 1) },
            Link("edge", "source", "target", 0, (10, 20), (10, 50), (80, 50), (80, 100)));
        var before = fixture.Routes.Links["edge"].RouteState.AuthoritativePoints.ToArray();

        Observe(fixture);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Equal(before, fixture.Routes.Links["edge"].RouteState.AuthoritativePoints);
        Assert.Throws<OperationCanceledException>(() => InterLayerBandObserver.Observe(
            fixture.Placement, fixture.Routes, DiagramSettings.CreateDefault(), cancellationToken: cancellation.Token));
    }

    private static InterLayerBandReport Observe(FixtureData fixture) =>
        InterLayerBandObserver.Observe(fixture.Placement, fixture.Routes, DiagramSettings.CreateDefault());

    private static FixtureData Fixture(IEnumerable<NodeLayout> nodes, params LinkLayout[] links)
    {
        var nodeMap = nodes.ToDictionary(node => node.Node.Id, StringComparer.Ordinal);
        var graph = RenderGraph.From(new DiagramModel(Array.Empty<ProjectContainer>(), Array.Empty<ExternalDependencyNode>(), Array.Empty<DependencyEdge>()));
        var placement = new PlacedGraph(graph, nodeMap, new Dictionary<string, ProjectLayout>(), new LayoutRevision(2));
        return new FixtureData(placement, new GeneratedLogicalRoutes(
            placement, links.ToDictionary(link => link.Link.Id, StringComparer.Ordinal), new RouteRevision(3)));
    }

    private static NodeLayout Node(string id, int depth, int order = 0) => new(
        new RenderNode(id, null, id, id, "Class", false, string.Empty, order,
            Array.Empty<string>(), Array.Empty<TypeProperty>(), 0),
        new Rect(order * 120, depth * 100, 20, 20), depth, false);

    private static LinkLayout Link(string id, string source, string target, int order, params (int X, int Y)[] points) =>
        new(new RenderLink(id, source, target, "internal", order),
            new Point(points[0].X, points[0].Y), new Point(points[^1].X, points[^1].Y),
            points.Skip(1).Take(points.Length - 2).Select(point => new Point(point.X, point.Y)), 0.5, 0.5);

    private sealed record FixtureData(PlacedGraph Placement, GeneratedLogicalRoutes Routes);
}
