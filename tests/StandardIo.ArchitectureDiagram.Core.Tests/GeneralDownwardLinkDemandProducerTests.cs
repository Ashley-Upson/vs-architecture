using StandardIo.ArchitectureDiagram.Core.Models;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core.Tests;

public sealed class GeneralDownwardLinkSegmentDemandProducerTests
{
    [Fact]
    public void Adjacent_downward_is_the_one_band_case_of_shared_demand_factory()
    {
        var context = Context("route", 1);
        var general = Assert.Single(GeneralDownwardLinkSegmentDemandProducer.Observe(new[] { context }).Routes);
        var adjacent = DownwardLinkSegmentDemandFactory.Create(
            context, new[] { context.InterLayerAxisRanges.Keys.Single() });

        Assert.Equal(adjacent.SegmentDemands.Select(item => item.Id), general.Demands.Select(item => item.Id));
        Assert.Single(general.Demands, item => item.Role == LinkSegmentRole.Through);
    }

    [Fact]
    public void Multi_band_route_emits_one_departure_band_and_one_full_depth_vertical_column()
    {
        var contexts = new[] { Context("b", 3), Context("a", 2) };
        var forward = GeneralDownwardLinkSegmentDemandProducer.Observe(contexts);
        var reverse = GeneralDownwardLinkSegmentDemandProducer.Observe(contexts.AsEnumerable().Reverse());

        Assert.Equal(
            forward.Routes.SelectMany(item => item.Demands).Select(item => item.Id),
            reverse.Routes.SelectMany(item => item.Demands).Select(item => item.Id));
        var route = Assert.Single(forward.Routes, item => item.LogicalRouteId == "b");
        Assert.Single(route.Demands, item => item.Role == LinkSegmentRole.Through);
        var column = Assert.Single(route.VerticalColumnDemands);
        Assert.Equal(120, column.PreferredX);
        Assert.Equal(new AxisInterval(120, 120), column.AllowedXInterval);
        Assert.Equal(0, column.SourceLayer);
        Assert.Equal(3, column.DestinationLayer);

        var assigned = GeneralDownwardCommonAllocator.Assign(
            GeneralDownwardLinkSegmentDemandProducer.Observe(new[] { Context("b", 3) }), Nodes(), 12, 4);
        var reconstructed = Assert.Single(assigned.Routes);
        Assert.True(reconstructed.IsValid);
        Assert.All(reconstructed.ReconstructedPoints.Zip(reconstructed.ReconstructedPoints.Skip(1)), pair =>
            Assert.True(pair.First.X == pair.Second.X || pair.First.Y == pair.Second.Y));
        Assert.DoesNotContain(reconstructed.ReconstructedPoints.Zip(reconstructed.ReconstructedPoints.Skip(1)), pair =>
            pair.First == pair.Second);
        Assert.Equal(4, reconstructed.ReconstructedPoints.Count);
        Assert.Equal(reconstructed.ReconstructedPoints[2].X, reconstructed.ReconstructedPoints[3].X);
    }

    [Fact]
    public void Intermediate_obstacle_produces_explicit_bypass_requirement()
    {
        var report = GeneralDownwardLinkSegmentDemandProducer.Observe(new[] { Context("route", 2) });
        var nodes = Nodes();
        nodes["obstacle"] = new NodeLayout(Node("obstacle", 3), new Rect(110, 100, 20, 80), 1, false);

        var route = Assert.Single(GeneralDownwardCommonAllocator.Assign(report, nodes, 12, 4).Routes);

        Assert.False(route.IsValid);
        Assert.Contains(route.Diagnostics, item => item.StartsWith("VerticalColumnBlocked:", StringComparison.Ordinal));
    }

    private static AdjacentDownwardLinkContext Context(string id, int bands)
    {
        var source = new NodeLayout(Node("source", 0), new Rect(0, 0, 40, 40), 0, false);
        var targetY = bands * 100 + 60;
        var target = new NodeLayout(Node("target", 1), new Rect(100, targetY, 40, 40), bands, false);
        var route = new LinkLayout(new RenderLink(id, "source", "target", "Dependency", 0),
            new Point(20, 40), new Point(120, targetY), new[] { new Point(20, 80), new Point(120, 80) }, .5, .5);
        var ranges = Enumerable.Range(0, bands).ToDictionary(
            depth => new InterLayerId(depth, depth + 1, new LayoutRevision(1)),
            depth => new AxisInterval(depth * 100 + 60, depth * 100 + 120));
        return new AdjacentDownwardLinkContext(route, source, target, new LayoutRevision(1), new RouteRevision(0),
            Array.Empty<InterLayerLinkDemand>(), ranges, false);
    }

    private static Dictionary<string, NodeLayout> Nodes() => new(StringComparer.Ordinal)
    {
        ["source"] = new NodeLayout(Node("source", 0), new Rect(0, 0, 40, 40), 0, false),
        ["target"] = new NodeLayout(Node("target", 1), new Rect(100, 360, 40, 40), 3, false)
    };

    private static RenderNode Node(string id, int order) =>
        new(id, "project", id, id, "Class", false, "", order,
            Array.Empty<string>(), Array.Empty<TypeProperty>(), 0);
}
