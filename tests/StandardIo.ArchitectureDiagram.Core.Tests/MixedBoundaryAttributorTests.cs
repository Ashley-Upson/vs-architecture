using StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core.Tests;

public sealed class MixedBoundaryAttributorTests
{
    [Fact]
    public void Multiple_band_rejection_does_not_obscure_semantic_downward_family()
    {
        var contexts = new[] { Context("route", sourceDepth: 0, targetDepth: 2) };
        var observation = AdjacentDownwardLinkSegmentDemandObserver.Observe(contexts);
        var result = MixedBoundaryAttributor.Attribute(contexts, observation,
            Array.Empty<CommonAuthorityInteraction>(), Bands(contexts[0]));

        var route = Assert.Single(result.Routes);
        Assert.Equal(DownwardIntegrationFamily.MultiBandDownward, route.PrimaryFamily);
        Assert.False(route.CurrentlyEligible);
        Assert.Contains("SkippedLayer", route.SecondaryReasons);
    }

    private static AdjacentDownwardRouteContext Context(string id, int sourceDepth, int targetDepth)
    {
        var sourceNode = new RenderNode("source", "project", "Source", "Source", "Class", false, "", 0,
            Array.Empty<string>(), Array.Empty<StandardIo.ArchitectureDiagram.Core.Models.TypeProperty>(), 0);
        var targetNode = sourceNode with { Id = "target", Name = "Target", FullName = "Target", Order = 1 };
        var source = new NodeLayout(sourceNode, new Rect(0, 0, 40, 40), sourceDepth, false);
        var target = new NodeLayout(targetNode, new Rect(100, 300, 40, 40), targetDepth, false);
        var link = new LinkLayout(new RenderLink(id, "source", "target", "Dependency", 0),
            new Point(20, 40), new Point(120, 300), new[] { new Point(20, 140), new Point(120, 140) }, .5, .5);
        var band0 = new InterLayerBandId(0, 1, new LayoutRevision(1));
        var band1 = new InterLayerBandId(1, 2, new LayoutRevision(1));
        return new AdjacentDownwardRouteContext(link, source, target, new LayoutRevision(1), new RouteRevision(0),
            new[]
            {
                new BandRouteMembership("m0", id, new RouteRevision(0), band0, 0, 1, BandMembershipRole.SourceTransition),
                new BandRouteMembership("m1", id, new RouteRevision(0), band1, 1, 2, BandMembershipRole.TargetTransition)
            }, Array.Empty<BandRouteDemand>(),
            new Dictionary<InterLayerBandId, AxisInterval> { [band0] = new(40, 140), [band1] = new(160, 300) },
            EmptyCorridors(), EmptyLanes(), null, false);
    }

    private static InterLayerBandReport Bands(AdjacentDownwardRouteContext context) =>
        new(Array.Empty<InterLayerBandObservation>(), Array.Empty<BandFindingCorrelation>(),
            new InterLayerBandTelemetry(new LayoutRevision(1), new RouteRevision(0), 2, 1, 3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0));

    private static CorridorObservation EmptyCorridors() => new(
        new Dictionary<string, RoutingCorridor>(), new Dictionary<string, CorridorJunction>(),
        Array.Empty<CorridorSegmentMapping>(), new Dictionary<string, CorridorUsage>());

    private static CorridorLaneAllocation EmptyLanes() => new(
        new Dictionary<string, IReadOnlyDictionary<string, AllocatedCorridorLane>>(), Array.Empty<string>());
}
