using StandardIo.ArchitectureDiagram.Core.Models;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core.Tests;

public sealed class PlacementPipelineTests
{
    [Fact]
    public void Place_keeps_parent_within_its_children_span()
    {
        var placed = Place(
            Nodes("parent", "left", "right"),
            Edges(("parent", "left"), ("parent", "right")));

        var parent = placed.Nodes["parent"].Rect;
        var left = placed.Nodes["left"].Rect;
        var right = placed.Nodes["right"].Rect;

        Assert.InRange(parent.CenterX, left.CenterX, right.CenterX);
    }

    [Fact]
    public void Place_vertically_aligns_single_child_with_parent()
    {
        var placed = Place(Nodes("parent", "child"), Edges(("parent", "child")));

        Assert.Equal(placed.Nodes["parent"].Rect.CenterX, placed.Nodes["child"].Rect.CenterX);
    }

    [Fact]
    public void Place_is_deterministic_for_multiple_roots()
    {
        var nodes = Nodes("root-a", "child-a", "root-b", "child-b");
        var edges = Edges(("root-a", "child-a"), ("root-b", "child-b"));

        var forward = Place(nodes, edges);
        var repeated = Place(nodes, edges);

        Assert.Equal(forward.Hierarchy.StableNodeOrder, repeated.Hierarchy.StableNodeOrder);
        Assert.Equal(forward.Nodes, repeated.Nodes);
        Assert.Equal(forward.Projects, repeated.Projects);
    }

    [Fact]
    public void Place_records_project_and_external_visual_ownership()
    {
        var diagram = new DiagramModel(
            new[] { Project("project", Nodes("source")[0]) },
            new[] { new ExternalDependencyNode("external", "External", "External", "external", "External", "[External]") },
            Edges(("source", "external")));

        var placed = PlacementPipeline.Place(
            RenderGraph.From(diagram, DiagramSettings.CreateDefault()),
            DiagramSettings.CreateDefault(),
            new LayoutRevision(0));

        Assert.Equal("project", placed.NodeOwnership.ProjectByNode["source"]);
        Assert.Equal("project", placed.NodeOwnership.ProjectByNode["external"]);
        Assert.Equal("project", placed.NodeOwnership.ExternalOwnerProjectByNode["external"]);
        Assert.Contains("external", placed.ProjectPlacement.NodeIdsByProject["project"]);
    }

    [Fact]
    public void Place_materializes_each_final_rectangle_from_base_plus_translation()
    {
        var placed = Place(
            Nodes("parent", "left", "right", "external-source"),
            Edges(("parent", "left"), ("parent", "right"), ("external-source", "right")));

        Assert.All(placed.Nodes, item => Assert.Equal(
            item.Value.Rect,
            placed.Translations.Materialize(item.Key, placed.NodeBasePlacements[item.Key])));
    }

    [Fact]
    public void Revise_increments_all_layout_revision_views_and_recomputes_translations()
    {
        var placed = Place(Nodes("node"), Array.Empty<DependencyEdge>());
        var node = placed.Nodes["node"];
        var moved = new Dictionary<string, NodeLayout>
        {
            ["node"] = node with { Rect = node.Rect.Translate(17, -9) }
        };

        var revised = placed.Revise(moved, placed.Projects);

        Assert.Equal(new LayoutRevision(1), revised.Revision);
        Assert.Equal(revised.Revision, revised.Hierarchy.Revision);
        Assert.Equal(new NodeTranslation(17, -9), revised.Translations.ByNode["node"]);
        Assert.Equal(moved["node"].Rect, revised.Translations.Materialize("node", revised.NodeBasePlacements["node"]));
    }

    [Fact]
    public void Place_honours_cancellation_without_returning_partial_state()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var graph = RenderGraph.From(Diagram(Nodes("node"), Array.Empty<DependencyEdge>()));

        Assert.Throws<OperationCanceledException>(() => PlacementPipeline.Place(
            graph, DiagramSettings.CreateDefault(), new LayoutRevision(0), cancellation.Token));
    }

    private static PlacedGraph Place(TypeNode[] nodes, DependencyEdge[] edges)
    {
        var settings = DiagramSettings.CreateDefault();
        return PlacementPipeline.Place(RenderGraph.From(Diagram(nodes, edges), settings), settings, new LayoutRevision(0));
    }

    private static DiagramModel Diagram(TypeNode[] nodes, DependencyEdge[] edges) =>
        new(new[] { Project("project", nodes) }, Array.Empty<ExternalDependencyNode>(), edges);

    private static ProjectContainer Project(string id, params TypeNode[] nodes) =>
        new(id, id, nodes);

    private static TypeNode[] Nodes(params string[] ids) =>
        ids.Select(id => new TypeNode(id, "project", id, $"Fixture.{id}", "Class")).ToArray();

    private static DependencyEdge[] Edges(params (string Source, string Target)[] edges) =>
        edges.Select((edge, index) => new DependencyEdge($"edge-{index}", edge.Source, edge.Target, "internal")).ToArray();
}
