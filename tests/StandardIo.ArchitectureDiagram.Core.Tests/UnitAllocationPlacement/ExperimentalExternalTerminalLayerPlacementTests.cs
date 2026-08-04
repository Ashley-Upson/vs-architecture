using StandardIo.ArchitectureDiagram.Core.Models;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core.Tests;

public sealed class ExperimentalExternalTerminalLayerPlacementTests
{
    [Fact]
    public void Project_externals_share_the_layer_immediately_after_deepest_internal_layer()
    {
        var placed = Place(Graph(
            Nodes(Internal("root", 0), Internal("child", 1), External("external-a", 2), External("external-b", 3)),
            Links(Link("root-child", "root", "child"), Link("child-a", "child", "external-a"),
                Link("root-b", "root", "external-b"))));

        Assert.Equal(1, placed.Nodes["child"].Depth);
        Assert.Equal(2, placed.Nodes["external-a"].Depth);
        Assert.Equal(placed.Nodes["external-a"].Depth, placed.Nodes["external-b"].Depth);
        Assert.Equal(placed.Nodes["external-a"].Rect.Y, placed.Nodes["external-b"].Rect.Y);
    }

    [Fact]
    public void External_links_do_not_change_internal_parent_centring_or_subtree_width()
    {
        var ordinary = Place(Graph(Nodes(Internal("root", 0), Internal("left", 1), Internal("right", 2)),
            Links(Link("root-left", "root", "left"), Link("root-right", "root", "right"))));
        var withExternal = Place(Graph(
            Nodes(Internal("root", 0), Internal("left", 1), Internal("right", 2), External("external", 3)),
            Links(Link("root-left", "root", "left"), Link("root-right", "root", "right"),
                Link("root-external", "root", "external"))));

        Assert.Equal(ordinary.Nodes["root"].Rect, withExternal.Nodes["root"].Rect);
        Assert.Equal(ordinary.Nodes["left"].Rect, withExternal.Nodes["left"].Rect);
        Assert.Equal(ordinary.Nodes["right"].Rect, withExternal.Nodes["right"].Rect);
    }

    [Fact]
    public void External_order_uses_connected_median_then_minimum_then_stable_id()
    {
        var graph = Graph(
            Nodes(Internal("left", 0), Internal("middle", 1), Internal("right", 2),
                External("a-tie", 3), External("b-tie", 4), External("late", 5), External("unconnected", 6)),
            Links(
                Link("left-a", "left", "a-tie"), Link("right-a", "right", "a-tie"),
                Link("middle-b", "middle", "b-tie"),
                Link("right-late", "right", "late")));
        var internals = new Dictionary<string, NodeLayout>(StringComparer.Ordinal)
        {
            ["left"] = new(graph.Nodes.Single(node => node.Id == "left"), new Rect(100, 100, 200, 80), 0, false),
            ["middle"] = new(graph.Nodes.Single(node => node.Id == "middle"), new Rect(380, 100, 200, 80), 0, false),
            ["right"] = new(graph.Nodes.Single(node => node.Id == "right"), new Rect(660, 100, 200, 80), 0, false)
        };
        var placed = ExperimentalExternalTerminalLayerPlacement.Apply(graph, internals, Settings());

        var order = placed.Values.Where(node => node.Node.IsExternal)
            .OrderBy(node => node.Rect.X).Select(node => node.Node.Id).ToArray();
        Assert.Equal(new[] { "a-tie", "b-tie", "late", "unconnected" }, order);
        Assert.Equal(380, placed["a-tie"].Rect.X);
        Assert.All(order.Zip(order.Skip(1), (left, right) => (left, right)), pair =>
            Assert.True(placed[pair.right].Rect.X >= placed[pair.left].Rect.Right + 80));
    }

    [Fact]
    public void Projects_calculate_external_terminal_depth_independently()
    {
        var graph = RenderGraph.Create(
            [new RenderProject("a", "A", 0), new RenderProject("b", "B", 1)],
            [Node("a-root", "a", false, 0), Node("a-ext", "a", true, 1),
             Node("b-root", "b", false, 2), Node("b-child", "b", false, 3), Node("b-ext", "b", true, 4)],
            [Link("a-link", "a-root", "a-ext"), Link("b-child", "b-root", "b-child"),
             Link("b-link", "b-child", "b-ext")]);

        var placed = ProjectRegionPlacement.Place(graph, Settings(), new LayoutRevision(0));

        Assert.Equal(1, placed.Nodes["a-ext"].Depth);
        Assert.Equal(2, placed.Nodes["b-ext"].Depth);
    }

    [Fact]
    public void Project_with_only_externals_uses_depth_zero_fallback()
    {
        var placed = Place(Graph(Nodes(External("a", 0), External("b", 1)), Array.Empty<RenderLink>()));

        Assert.All(placed.Nodes.Values, node => Assert.Equal(0, node.Depth));
    }

    [Fact]
    public void External_to_internal_link_remains_an_upward_route_and_inside_final_bounds()
    {
        var graph = Graph(Nodes(Internal("internal", 0), External("external", 1)),
            Links(Link("edge", "external", "internal")));
        var layout = ProjectRegionLayoutBuilder.Build(graph, Settings());

        Assert.True(layout.Nodes["external"].Depth > layout.Nodes["internal"].Depth);
        var route = Assert.Single(layout.Links).Value;
        var bounds = layout.Projects["project"].Rect;
        Assert.All(route.Points, point => Assert.True(point.X >= bounds.X && point.X <= bounds.Right &&
            point.Y >= bounds.Y && point.Y <= bounds.Bottom));
    }

    [Fact]
    public void Internal_node_connected_only_to_external_is_not_a_routing_standalone()
    {
        var graph = Graph(Nodes(Internal("internal", 0), External("external", 1)),
            Links(Link("edge", "internal", "external")));
        var placed = Place(graph);

        Assert.False(placed.Nodes["internal"].IsStandalone);
        Assert.False(placed.Nodes["external"].IsStandalone);
    }

    [Fact]
    public void Exposure_tree_parent_is_recentered_after_external_child_placement()
    {
        var graph = RenderGraph.Create(
            [new RenderProject("project", "Project", 0)],
            [Node("tree_root", "project", false, 0), Node("tree_child", "project", false, 1),
             Node("tree_external", "project", true, 2)],
            [Link("root-child", "tree_root", "tree_child"), Link("root-external", "tree_root", "tree_external")]);

        var placed = Place(graph);
        var root = placed.Nodes["tree_root"].Rect;
        var children = new[] { placed.Nodes["tree_child"].Rect, placed.Nodes["tree_external"].Rect };
        var childSpanCenter = (children.Min(rect => rect.X) + children.Max(rect => rect.Right)) / 2;

        Assert.Equal(childSpanCenter, root.CenterX);
    }

    [Fact]
    public void Reversed_node_and_link_enumeration_has_identical_placement()
    {
        var graph = Graph(
            Nodes(Internal("left", 0), Internal("right", 1), External("a", 2), External("b", 3)),
            Links(Link("left-a", "left", "a"), Link("right-b", "right", "b")));
        var reversed = RenderGraph.Create(graph.Projects.Reverse().ToArray(), graph.Nodes.Reverse().ToArray(),
            graph.Links.Reverse().ToArray(), graph.PlacementParentByNode);

        Assert.Equal(Signature(Place(graph)), Signature(Place(reversed)));
    }

    private static PlacedGraph Place(RenderGraph graph) =>
        ProjectRegionPlacement.Place(graph, Settings(), new LayoutRevision(0));

    private static RenderGraph Graph(RenderNode[] nodes, RenderLink[] links) =>
        RenderGraph.Create([new RenderProject("project", "Project", 0)], nodes, links);

    private static RenderNode[] Nodes(params RenderNode[] nodes) => nodes;
    private static RenderLink[] Links(params RenderLink[] links) => links;
    private static RenderNode Internal(string id, int order) => Node(id, "project", false, order);
    private static RenderNode External(string id, int order) => Node(id, "project", true, order);
    private static RenderNode Node(string id, string project, bool external, int order) =>
        new(id, project, id, "Fixture." + id, external ? "External" : "Class", external,
            external ? "[External]" : string.Empty, order, Array.Empty<string>(), Array.Empty<TypeProperty>(), 0);
    private static RenderLink Link(string id, string source, string target) => new(id, source, target, "internal", 0);
    private static DiagramSettings Settings() => DiagramSettings.CreateDefault();
    private static string[] Signature(PlacedGraph placed) => placed.Nodes.OrderBy(item => item.Key)
        .Select(item => $"{item.Key}:{item.Value.Depth}:{item.Value.Rect}").ToArray();
}
