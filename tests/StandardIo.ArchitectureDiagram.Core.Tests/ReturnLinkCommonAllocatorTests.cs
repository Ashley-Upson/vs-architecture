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

        var report = ReturnLinkCommonAllocator.Assign(new[] { context }, Placement(nodes), 12, 8);

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

        var forward = ReturnLinkCommonAllocator.Assign(contexts, Placement(nodes), 12, 8);
        var reverse = ReturnLinkCommonAllocator.Assign(contexts.AsEnumerable().Reverse(), Placement(nodes), 12, 8);

        Assert.Equal(forward.VerticalColumns.ColumnsByDemandId.OrderBy(item => item.Key),
            reverse.VerticalColumns.ColumnsByDemandId.OrderBy(item => item.Key));
        var xs = forward.VerticalColumns.ColumnsByDemandId.Values.Select(item => item.X).OrderBy(x => x).ToArray();
        Assert.True(xs[1] - xs[0] >= 12);
    }

    [Theory]
    [InlineData("p0", "p0", 0, 0, 1)]
    [InlineData("p0", "p2", 0, 2, 3)]
    [InlineData("p1", "p2", 1, 2, 2)]
    public void Ownership_is_the_smallest_contiguous_ordered_project_span(
        string sourceProject, string targetProject, int first, int last, int count)
    {
        var fixture = OwnershipFixture(sourceProject, targetProject);

        var ownership = ReturnLinkCommonAllocator.Ownership(fixture.Context, fixture.Placement, 8);

        Assert.Equal(first, ownership.FirstProjectOrder);
        Assert.Equal(last, ownership.LastProjectOrder);
        Assert.Equal(count, ownership.ProjectIds.Count);
        Assert.Equal(fixture.Placement.Revision, ownership.OwnershipRevision);
    }

    [Fact]
    public void Root_owned_endpoint_uses_the_whole_canvas_span()
    {
        var fixture = OwnershipFixture("p1", null);

        var ownership = ReturnLinkCommonAllocator.Ownership(fixture.Context, fixture.Placement, 8);

        Assert.Equal(new[] { "p0", "p1", "p2" }, ownership.ProjectIds);
        Assert.Equal(0, ownership.FirstProjectOrder);
        Assert.Equal(2, ownership.LastProjectOrder);
    }

    private static Dictionary<string, NodeLayout> Nodes(int sourceDepth, int targetDepth) => new(StringComparer.Ordinal)
    {
        ["source"] = new(Node("source", 0), new Rect(100, sourceDepth * 100, 40, 40), sourceDepth, false),
        ["target"] = new(Node("target", 1), new Rect(220, targetDepth * 100, 40, 40), targetDepth, false)
    };

    private static PlacedGraph Placement(IReadOnlyDictionary<string, NodeLayout> nodes)
    {
        var graph = RenderGraph.From(new DiagramModel(
            new[] { new ProjectContainer("project", "Project", nodes.Values.Select(item =>
                new TypeNode(item.Node.Id, "project", item.Node.Name, item.Node.FullName, "Class")).ToArray()) },
            Array.Empty<ExternalDependencyNode>(), new[] { new DependencyEdge("link", "source", "target", "Dependency") }));
        return new PlacedGraph(graph, nodes, new Dictionary<string, ProjectLayout>(), new LayoutRevision(1));
    }

    private static (PlacedGraph Placement, AdjacentDownwardLinkContext Context) OwnershipFixture(
        string sourceProject, string? targetProject)
    {
        var projects = new[] { "p0", "p1", "p2" };
        var graph = RenderGraph.From(new DiagramModel(projects.Select((id, order) =>
                new ProjectContainer(id, id, new[] { new TypeNode($"{id}-node", id, id, id, "Class") })).ToArray(),
            targetProject is null
                ? new[] { new ExternalDependencyNode("root-target", "root-target", "root-target", "External") }
                : Array.Empty<ExternalDependencyNode>(),
            new[] { new DependencyEdge("ownership-link", $"{sourceProject}-node",
                targetProject is null ? "root-target" : $"{targetProject}-node", "Dependency") }));
        var renderNodes = graph.Nodes.ToDictionary(item => item.Id, StringComparer.Ordinal);
        var sourceId = $"{sourceProject}-node";
        var targetId = targetProject is null ? "root-target" : $"{targetProject}-node";
        var nodes = projects.Select((id, order) => new NodeLayout(renderNodes[$"{id}-node"],
                new Rect(order * 200, 100, 80, 40), 1, false))
            .ToDictionary(item => item.Node.Id, StringComparer.Ordinal);
        if (targetProject is null)
            nodes[targetId] = new NodeLayout(new RenderNode(targetId, null, targetId, targetId, "External", true, "", 0,
                Array.Empty<string>(), Array.Empty<TypeProperty>(), 0), new Rect(700, 100, 80, 40), 1, true);
        var layouts = projects.Select((id, order) => new ProjectLayout(
                graph.Projects.Single(item => item.Id == id), new Rect(order * 200 - 20, 20, 120, 180)))
            .ToDictionary(item => item.Project.Id, StringComparer.Ordinal);
        var placement = new PlacedGraph(graph, nodes, layouts, new LayoutRevision(3));
        var sourceNode = nodes[sourceId];
        var targetNode = nodes[targetId];
        var route = new LinkLayout(new RenderLink("ownership-link", sourceId, targetId, "Dependency", 0),
            new Point(sourceNode.Rect.CenterX, sourceNode.Rect.Bottom),
            new Point(targetNode.Rect.CenterX, targetNode.Rect.Y), Array.Empty<Point>(), .5, .5);
        var context = new AdjacentDownwardLinkContext(route, sourceNode, targetNode, placement.Revision,
            new RouteRevision(0), Array.Empty<InterLayerLinkMembership>(), Array.Empty<InterLayerLinkDemand>(),
            new Dictionary<InterLayerId, AxisInterval>(), EmptyCorridors(), EmptyLanes(), null, false);
        return (placement, context);
    }

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
