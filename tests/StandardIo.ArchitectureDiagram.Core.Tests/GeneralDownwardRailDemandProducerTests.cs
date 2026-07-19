using StandardIo.ArchitectureDiagram.Core.Models;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core.Tests;

public sealed class GeneralDownwardRailDemandProducerTests
{
    [Fact]
    public void Adjacent_downward_is_the_one_band_case_of_shared_demand_factory()
    {
        var context = Context("route", 1);
        var general = Assert.Single(GeneralDownwardRailDemandProducer.Observe(new[] { context }).Routes);
        var adjacent = Assert.Single(AdjacentDownwardRailDemandObserver.Observe(new[]
        {
            context with
            {
                BandMemberships = new[]
                {
                    new BandRouteMembership("m", "route", new RouteRevision(0), context.BandAxisRanges.Keys.Single(),
                        0, 2, BandMembershipRole.Through)
                },
                BandDemands = new[]
                {
                    new BandRouteDemand("d", "route", new RouteRevision(0), context.BandAxisRanges.Keys.Single(),
                        1, BandMembershipRole.Through, 20, 120, 0, BandRouteDirection.Right, 0)
                }
            }
        }).Routes);

        Assert.Equal(adjacent.Demands.Select(item => item.Id), general.Observation.Demands.Select(item => item.Id));
        Assert.Single(general.Observation.Demands, item => item.Role == RailSemanticRole.Through);
    }

    [Fact]
    public void Multi_band_route_emits_ordered_demands_and_orthogonal_deterministic_transitions()
    {
        var contexts = new[] { Context("b", 3), Context("a", 2) };
        var forward = GeneralDownwardRailDemandProducer.Observe(contexts);
        var reverse = GeneralDownwardRailDemandProducer.Observe(contexts.AsEnumerable().Reverse());

        Assert.Equal(
            forward.Routes.SelectMany(item => item.Observation.Demands).Select(item => item.Id),
            reverse.Routes.SelectMany(item => item.Observation.Demands).Select(item => item.Id));
        var route = Assert.Single(forward.Routes, item => item.Observation.LogicalRouteId == "b");
        Assert.Equal(3, route.Observation.Demands.Count(item => item.Role == RailSemanticRole.Through));
        Assert.Equal(new int?[] { 1, 2, 3 }, route.Observation.Demands.Where(item => item.Role == RailSemanticRole.Through)
            .Select(item => item.TurnOrder));

        var assigned = GeneralDownwardCommonAllocator.Assign(forward, Nodes(), 12, 4);
        var reconstructed = Assert.Single(assigned.Routes, item => item.LogicalRouteId == "b");
        Assert.True(reconstructed.IsValid);
        Assert.All(reconstructed.ReconstructedPoints.Zip(reconstructed.ReconstructedPoints.Skip(1)), pair =>
            Assert.True(pair.First.X == pair.Second.X || pair.First.Y == pair.Second.Y));
        Assert.DoesNotContain(reconstructed.ReconstructedPoints.Zip(reconstructed.ReconstructedPoints.Skip(1)), pair =>
            pair.First == pair.Second);
    }

    [Fact]
    public void Intermediate_obstacle_produces_explicit_bypass_requirement()
    {
        var report = GeneralDownwardRailDemandProducer.Observe(new[] { Context("route", 2) });
        var nodes = Nodes();
        nodes["obstacle"] = new NodeLayout(Node("obstacle", 3), new Rect(60, 100, 20, 80), 1, false);

        var route = Assert.Single(GeneralDownwardCommonAllocator.Assign(report, nodes, 12, 4).Routes);

        Assert.False(route.IsValid);
        Assert.Contains(route.Diagnostics, item => item.StartsWith("ObstacleBypassRequired:", StringComparison.Ordinal));
    }

    private static AdjacentDownwardRouteContext Context(string id, int bands)
    {
        var source = new NodeLayout(Node("source", 0), new Rect(0, 0, 40, 40), 0, false);
        var targetY = bands * 100 + 60;
        var target = new NodeLayout(Node("target", 1), new Rect(100, targetY, 40, 40), bands, false);
        var route = new LinkLayout(new RenderLink(id, "source", "target", "Dependency", 0),
            new Point(20, 40), new Point(120, targetY), new[] { new Point(20, 80), new Point(120, 80) }, .5, .5);
        var ranges = Enumerable.Range(0, bands).ToDictionary(
            depth => new InterLayerBandId(depth, depth + 1, new LayoutRevision(1)),
            depth => new AxisInterval(depth * 100 + 60, depth * 100 + 120));
        return new AdjacentDownwardRouteContext(route, source, target, new LayoutRevision(1), new RouteRevision(0),
            Array.Empty<BandRouteMembership>(), Array.Empty<BandRouteDemand>(), ranges,
            EmptyCorridors(), EmptyLanes(), null, false);
    }

    private static Dictionary<string, NodeLayout> Nodes() => new(StringComparer.Ordinal)
    {
        ["source"] = new NodeLayout(Node("source", 0), new Rect(0, 0, 40, 40), 0, false),
        ["target"] = new NodeLayout(Node("target", 1), new Rect(100, 360, 40, 40), 3, false)
    };

    private static RenderNode Node(string id, int order) =>
        new(id, "project", id, id, "Class", false, "", order,
            Array.Empty<string>(), Array.Empty<TypeProperty>(), 0);
    private static CorridorObservation EmptyCorridors() => new(
        new Dictionary<string, RoutingCorridor>(), new Dictionary<string, CorridorJunction>(),
        Array.Empty<CorridorSegmentMapping>(), new Dictionary<string, CorridorUsage>());
    private static CorridorLaneAllocation EmptyLanes() => new(
        new Dictionary<string, IReadOnlyDictionary<string, AllocatedCorridorLane>>(), Array.Empty<string>());
}
