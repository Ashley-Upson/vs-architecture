using StandardIo.ArchitectureDiagram.Core.Models;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core.Tests;

public sealed class HorizontalMovementConstraintMaterializerTests
{
    [Fact]
    public void Leaf_node_scope_moves_only_the_leaf_and_invalidates_incident_link()
    {
        var basis = Placement();
        var result = Move(basis, MovementScopeKind.Node, "leaf", 300);

        Assert.Equal(300, result.Placement.Nodes["leaf"].Rect.X);
        Assert.Equal(0, result.Placement.Nodes["root"].Rect.X);
        Assert.Equal(100, result.Placement.Nodes["child"].Rect.X);
        Assert.Equal(new[] { "child-leaf" }, result.InvalidatedLinkIds);
    }

    [Fact]
    public void Non_leaf_node_scope_is_rejected()
    {
        var basis = Placement();

        Assert.Throws<InvalidOperationException>(() => Move(basis, MovementScopeKind.Node, "child", 300));
    }

    [Fact]
    public void Positional_subtree_scope_moves_every_descendant_coherently()
    {
        var basis = Placement();
        var result = Move(basis, MovementScopeKind.LayoutSubtree, "child", 300);

        Assert.Equal(300, result.Placement.Nodes["child"].Rect.X);
        Assert.Equal(320, result.Placement.Nodes["leaf"].Rect.X);
        Assert.Equal(0, result.Placement.Nodes["root"].Rect.X);
        Assert.Equal(new[] { "child", "leaf" }, result.MovedNodeIds);
    }

    [Fact]
    public void Ordered_sibling_suffix_moves_complete_sibling_subtrees_without_interleaving()
    {
        var basis = Placement();
        var result = Move(basis, MovementScopeKind.OrderedSiblingSuffix, "child", 300);

        Assert.Equal(300, result.Placement.Nodes["child"].Rect.X);
        Assert.Equal(320, result.Placement.Nodes["leaf"].Rect.X);
        Assert.Equal(400, result.Placement.Nodes["sibling"].Rect.X);
        Assert.Equal(420, result.Placement.Nodes["sibling-leaf"].Rect.X);
    }

    [Fact]
    public void Repeated_materialization_uses_the_immutable_base()
    {
        var basis = Placement();
        var first = Move(basis, MovementScopeKind.LayoutSubtree, "child", 300);
        var second = Move(basis, MovementScopeKind.LayoutSubtree, "child", 300);

        Assert.Equal(first.Placement.Nodes.Select(item => item.Value.Rect),
            second.Placement.Nodes.Select(item => item.Value.Rect));
        Assert.Equal(basis.Revision.Next(), second.Placement.Revision);
    }

    private static HorizontalMovementIteration Move(
        PlacedGraph basis,
        MovementScopeKind kind,
        string id,
        int minimumX)
    {
        var scope = new MovementScopeIdentity(kind, id);
        return HorizontalMovementConstraintMaterializer.Materialize(
            basis,
            new[] { new GenerationConstraint(new GenerationConstraintKey(scope, GenerationConstraintKind.MinimumX), minimumX, "fixture") },
            Settings(),
            Routes(basis));
    }

    private static PlacedGraph Placement()
    {
        var ids = new[] { "root", "child", "leaf", "sibling", "sibling-leaf" };
        var links = new[]
        {
            Edge("root-child", "root", "child"), Edge("child-leaf", "child", "leaf"),
            Edge("root-sibling", "root", "sibling"), Edge("sibling-leaf", "sibling", "sibling-leaf")
        };
        var graph = RenderGraph.From(new DiagramModel(
            new[] { new ProjectContainer("project", "Project", ids.Select(Type).ToArray()) },
            Array.Empty<ExternalDependencyNode>(), links));
        var renderNodes = graph.Nodes.ToDictionary(node => node.Id, StringComparer.Ordinal);
        var layouts = new Dictionary<string, NodeLayout>
        {
            ["root"] = Layout(renderNodes["root"], 0, 0, 0),
            ["child"] = Layout(renderNodes["child"], 100, 100, 1),
            ["leaf"] = Layout(renderNodes["leaf"], 120, 200, 2),
            ["sibling"] = Layout(renderNodes["sibling"], 200, 100, 1),
            ["sibling-leaf"] = Layout(renderNodes["sibling-leaf"], 220, 200, 2)
        };
        return new PlacedGraph(graph, layouts, new Dictionary<string, ProjectLayout>(), new LayoutRevision(0));
    }

    private static IReadOnlyDictionary<string, LinkLayout> Routes(PlacedGraph placement) =>
        placement.Graph.Links.ToDictionary(link => link.Id, link => new LinkLayout(
            link,
            new Point(placement.Nodes[link.SourceId].Rect.X, placement.Nodes[link.SourceId].Rect.Bottom),
            new Point(placement.Nodes[link.TargetId].Rect.X, placement.Nodes[link.TargetId].Rect.Y),
            Array.Empty<Point>(), .5, .5), StringComparer.Ordinal);

    private static DiagramSettings Settings()
    {
        var settings = DiagramSettings.CreateDefault();
        settings.ShowProjectContainers = false;
        return settings;
    }

    private static TypeNode Type(string id) => new(id, "project", id, id, "Class");
    private static DependencyEdge Edge(string id, string source, string target) => new(id, source, target, "Dependency");
    private static NodeLayout Layout(RenderNode node, int x, int y, int depth) =>
        new(node, new Rect(x, y, 40, 40), depth, false);
}
