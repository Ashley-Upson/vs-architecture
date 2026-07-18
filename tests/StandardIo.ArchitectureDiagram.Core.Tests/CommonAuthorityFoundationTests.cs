using StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core.Tests;

public sealed class CommonAuthorityFoundationTests
{
    [Fact]
    public void Independent_unsupported_route_does_not_suppress_eligible_component()
    {
        var result = CommonAuthorityComponentClassifier.Classify(
            new[] { Route("eligible", true), Route("unsupported", false) },
            System.Array.Empty<CommonAuthorityInteraction>());

        Assert.Equal(CommonAuthorityComponentDisposition.Eligible,
            Assert.Single(result.Components, item => item.Id == "eligible").Disposition);
        Assert.Equal(CommonAuthorityComponentDisposition.Unsupported,
            Assert.Single(result.Components, item => item.Id == "unsupported").Disposition);
    }

    [Fact]
    public void Mixed_closed_component_is_rejected_whole()
    {
        var result = CommonAuthorityComponentClassifier.Classify(
            new[] { Route("eligible", true), Route("unsupported", false) },
            new[] { Interaction("eligible", "unsupported", "ParallelSpacing", true) });

        Assert.Equal(CommonAuthorityComponentDisposition.MixedBoundaryUnsafe,
            Assert.Single(result.Components).Disposition);
    }

    [Fact]
    public void Clean_crossover_does_not_close_components_but_spacing_does()
    {
        var routes = new[] { Route("a", true), Route("b", false) };
        var clean = CommonAuthorityComponentClassifier.Classify(routes,
            new[] { Interaction("a", "b", "CleanPerpendicularCrossover", false) });
        var spacing = CommonAuthorityComponentClassifier.Classify(routes,
            new[] { Interaction("a", "b", "ParallelSpacing", true) });

        Assert.Equal(2, clean.Components.Count);
        Assert.Single(clean.AdvisoryCrossovers);
        Assert.Equal(CommonAuthorityComponentDisposition.MixedBoundaryUnsafe,
            Assert.Single(spacing.Components).Disposition);
    }

    [Fact]
    public void Shared_turns_are_orthogonal_distinct_and_input_order_independent()
    {
        var rails = new[]
        {
            Rail("b", "departure", RailOrientation.Vertical, 40, RailSemanticRole.TerminalDeparture),
            Rail("b", "through", RailOrientation.Horizontal, 120, RailSemanticRole.Through),
            Rail("b", "arrival", RailOrientation.Vertical, 180, RailSemanticRole.TerminalArrival),
            Rail("a", "departure", RailOrientation.Vertical, 20, RailSemanticRole.TerminalDeparture),
            Rail("a", "through", RailOrientation.Horizontal, 100, RailSemanticRole.Through),
            Rail("a", "arrival", RailOrientation.Vertical, 160, RailSemanticRole.TerminalArrival)
        };

        var forward = DeterministicSharedTurnAllocator.Assign(rails);
        var reverse = DeterministicSharedTurnAllocator.Assign(rails.AsEnumerable().Reverse());

        Assert.Empty(forward.RejectedRouteIds);
        Assert.Equal(forward.TransitionsByRouteId, reverse.TransitionsByRouteId);
        var turns = forward.TransitionsByRouteId.Values.SelectMany(item => item).Select(item => item.Turn).ToArray();
        Assert.Equal(turns.Length, turns.Distinct().Count());
        Assert.All(forward.TransitionsByRouteId, pair =>
        {
            var routeRails = rails.Where(item => item.LogicalRouteId == pair.Key).ToArray();
            var reconstructed = AdjacentDownwardRailDemandObserver.Reconstruct(
                new Point(routeRails[0].AxisCoordinate, 0), new Point(routeRails[2].AxisCoordinate, 200),
                routeRails, pair.Value);
            Assert.All(reconstructed.Zip(reconstructed.Skip(1)), pair =>
                Assert.True(pair.First.X == pair.Second.X || pair.First.Y == pair.Second.Y));
        });
    }

    private static CommonAuthorityRouteCapability Route(string id, bool eligible) =>
        new(id, eligible, eligible ? "Eligible" : "UnsupportedTopology");

    private static CommonAuthorityInteraction Interaction(string a, string b, string kind, bool couples) =>
        new(a, b, kind, couples);

    private static AssignedRail Rail(string route, string id, RailOrientation orientation, int axis, RailSemanticRole role) =>
        new($"{route}:{id}", $"{route}:{id}:demand", route, orientation, axis, 0,
            new AxisInterval(0, 200), role, new LayoutRevision(1), new RouteRevision(2));
}
