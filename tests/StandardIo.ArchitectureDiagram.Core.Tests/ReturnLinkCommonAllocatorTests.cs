using StandardIo.ArchitectureDiagram.Core.Models;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core.Tests;

public sealed class ReturnLinkCommonAllocatorTests
{
    [Theory]
    [InlineData(1, 1, "SameLayer")]
    [InlineData(2, 0, "Upward")]
    public void Same_layer_and_upward_links_use_bottom_exit_top_entry_and_shared_return_columns(
        int sourceDepth, int targetDepth, string kind)
    {
        var nodes = Nodes(sourceDepth, targetDepth);
        var context = Context("link", nodes, sourceDepth, targetDepth);

        var report = ReturnLinkCommonAllocator.Assign(new[] { context }, nodes, 12, 8);

        Assert.Equal(kind, Assert.Single(report.Plans).Kind.ToString());
        var route = Assert.Single(report.Assignments);
        Assert.True(route.IsValid);
        Assert.Equal(context.Route.SourcePoint, route.ReconstructedPoints[0]);
        Assert.Equal(context.Route.TargetPoint, route.ReconstructedPoints[^1]);
        Assert.Equal(context.Route.SourcePoint.X, route.ReconstructedPoints[1].X);
        Assert.True(route.ReconstructedPoints[1].Y > context.Route.SourcePoint.Y);
        Assert.Equal(context.Route.TargetPoint.X, route.ReconstructedPoints[^2].X);
        Assert.True(route.ReconstructedPoints[^2].Y < context.Route.TargetPoint.Y);
    }

    [Fact]
    public void Return_column_assignment_is_deterministic_and_separated()
    {
        var nodes = Nodes(1, 1);
        var contexts = new[] { Context("b", nodes, 1, 1), Context("a", nodes, 1, 1) };

        var forward = ReturnLinkCommonAllocator.Assign(contexts, nodes, 12, 8);
        var reverse = ReturnLinkCommonAllocator.Assign(contexts.AsEnumerable().Reverse(), nodes, 12, 8);

        Assert.Equal(forward.VerticalColumns.ColumnsByDemandId.OrderBy(item => item.Key),
            reverse.VerticalColumns.ColumnsByDemandId.OrderBy(item => item.Key));
        var xs = forward.VerticalColumns.ColumnsByDemandId.Values.Select(item => item.X).OrderBy(x => x).ToArray();
        Assert.True(xs[1] - xs[0] >= 12);
    }

    private static Dictionary<string, NodeLayout> Nodes(int sourceDepth, int targetDepth) => new(StringComparer.Ordinal)
    {
        ["source"] = new(Node("source", 0), new Rect(100, sourceDepth * 100, 40, 40), sourceDepth, false),
        ["target"] = new(Node("target", 1), new Rect(220, targetDepth * 100, 40, 40), targetDepth, false)
    };

    private static AdjacentDownwardLinkContext Context(
        string id, IReadOnlyDictionary<string, NodeLayout> nodes, int sourceDepth, int targetDepth)
    {
        var source = nodes["source"];
        var target = nodes["target"];
        var link = new LinkLayout(new RenderLink(id, "source", "target", "Dependency", 0),
            new Point(120, source.Rect.Bottom), new Point(240, target.Rect.Y), Array.Empty<Point>(), 0.5, 0.5);
        return new AdjacentDownwardLinkContext(link, source, target, new LayoutRevision(1), new RouteRevision(0),
            Array.Empty<InterLayerLinkMembership>(), Array.Empty<InterLayerLinkDemand>(),
            new Dictionary<InterLayerId, AxisInterval>(), EmptyCorridors(), EmptyLanes(), null, false);
    }

    private static RenderNode Node(string id, int order) =>
        new(id, "project", id, id, "Class", false, "", order,
            Array.Empty<string>(), Array.Empty<TypeProperty>(), 0);
    private static CorridorObservation EmptyCorridors() => new(
        new Dictionary<string, RoutingCorridor>(), new Dictionary<string, CorridorJunction>(),
        Array.Empty<CorridorSegmentMapping>(), new Dictionary<string, CorridorUsage>());
    private static CorridorLaneAllocation EmptyLanes() => new(
        new Dictionary<string, IReadOnlyDictionary<string, AllocatedCorridorLane>>(), Array.Empty<string>());
}
