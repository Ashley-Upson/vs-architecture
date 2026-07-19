using StandardIo.ArchitectureDiagram.Core.Models;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core.Tests;

public sealed class PositionalHierarchyAnalyzerTests
{
    [Fact]
    public void Direct_downward_parent_wins_over_leftmost_parent()
    {
        var placement = Place(
            new[]
            {
                Node("left", 0, 0), Node("direct", 100, 0), Node("shared", 100, 100)
            },
            ("left", "shared"), ("direct", "shared"));

        var result = PositionalHierarchyAnalyzer.Analyze(placement);

        Assert.Equal("direct", result.ParentByNode["shared"]);
        Assert.Equal(PositionalParentSelectionReason.DirectDownwardPath, result.ParentSelections["shared"].Reason);
    }

    [Fact]
    public void Least_horizontal_movement_wins_when_no_parent_is_directly_aligned()
    {
        var placement = Place(
            new[]
            {
                Node("far", 0, 0), Node("near", 80, 0), Node("shared", 120, 100)
            },
            ("far", "shared"), ("near", "shared"));

        var result = PositionalHierarchyAnalyzer.Analyze(placement);

        Assert.Equal("near", result.ParentByNode["shared"]);
        Assert.Equal(PositionalParentSelectionReason.LeastHorizontalMovement, result.ParentSelections["shared"].Reason);
    }

    [Fact]
    public void Stable_parent_choice_does_not_depend_on_link_enumeration()
    {
        var nodes = new[] { Node("a", 0, 0), Node("b", 0, 0), Node("shared", 100, 100) };
        var forward = PositionalHierarchyAnalyzer.Analyze(Place(nodes, ("b", "shared"), ("a", "shared")));
        var reverse = PositionalHierarchyAnalyzer.Analyze(Place(nodes, ("a", "shared"), ("b", "shared")));

        Assert.Equal("a", forward.ParentByNode["shared"]);
        Assert.Equal(forward.ParentByNode, reverse.ParentByNode);
        Assert.Equal(PositionalParentSelectionReason.StableNodeId, forward.ParentSelections["shared"].Reason);
    }

    [Fact]
    public void Envelope_records_deeper_per_layer_expansion()
    {
        var placement = Place(
            new[]
            {
                Node("root", 100, 0, depth: 0),
                Node("child", 100, 100, depth: 1),
                Node("left", 0, 200, depth: 2),
                Node("right", 220, 200, depth: 2)
            },
            ("root", "child"), ("child", "left"), ("child", "right"));

        var envelope = PositionalHierarchyAnalyzer.Analyze(placement).EnvelopesByRootNode["root"];

        Assert.Equal(100, envelope.BoundsByLayer[0].Width);
        Assert.Equal(320, envelope.BoundsByLayer[2].Width);
        Assert.Equal(0, envelope.LeftBoundaryByLayer[2]);
        Assert.Equal(320, envelope.RightBoundaryByLayer[2]);
        Assert.Equal(new LayoutRevision(0), envelope.PositionalRevision);
    }

    [Fact]
    public void Non_leaf_subtree_envelope_contains_every_descendant()
    {
        var placement = Place(
            new[] { Node("root", 50, 0, 0), Node("child", 100, 100, 1), Node("leaf", 180, 200, 2) },
            ("root", "child"), ("child", "leaf"));

        var result = PositionalHierarchyAnalyzer.Analyze(placement);
        var root = result.EnvelopesByRootNode["root"];
        var child = result.EnvelopesByRootNode["child"];

        Assert.Equal(new Rect(50, 0, 230, 240), root.OverallBounds);
        Assert.Equal(new Rect(100, 100, 180, 140), child.OverallBounds);
        Assert.Equal(new[] { "child" }, result.ChildrenByNode["root"]);
        Assert.Equal(new[] { "leaf" }, result.ChildrenByNode["child"]);
    }

    [Fact]
    public void Cross_project_link_does_not_create_a_positional_parent()
    {
        var model = new DiagramModel(
            new[]
            {
                new ProjectContainer("one", "One", new[] { Type("source", "one") }),
                new ProjectContainer("two", "Two", new[] { Type("target", "two") })
            },
            Array.Empty<ExternalDependencyNode>(),
            new[] { new DependencyEdge("edge", "source", "target", "internal") });
        var graph = RenderGraph.From(model);
        var layouts = new Dictionary<string, NodeLayout>
        {
            ["source"] = new(graph.Nodes.Single(node => node.Id == "source"), new Rect(0, 0, 100, 40), 0, false),
            ["target"] = new(graph.Nodes.Single(node => node.Id == "target"), new Rect(0, 100, 100, 40), 1, false)
        };
        var projects = graph.Projects.ToDictionary(
            project => project.Id,
            project => new ProjectLayout(project, new Rect(0, 0, 200, 200)));

        var result = PositionalHierarchyAnalyzer.Analyze(new PlacedGraph(graph, layouts, projects, new LayoutRevision(0)));

        Assert.DoesNotContain("target", result.ParentByNode);
    }

    private static PlacedGraph Place(
        IReadOnlyList<(string Id, Rect Rect, int Depth)> specifications,
        params (string Source, string Target)[] links)
    {
        var model = new DiagramModel(
            new[] { new ProjectContainer("project", "Project", specifications.Select(item => Type(item.Id, "project")).ToArray()) },
            Array.Empty<ExternalDependencyNode>(),
            links.Select((link, index) => new DependencyEdge($"edge-{index}", link.Source, link.Target, "internal")).ToArray());
        var graph = RenderGraph.From(model);
        var layouts = specifications.ToDictionary(
            item => item.Id,
            item => new NodeLayout(graph.Nodes.Single(node => node.Id == item.Id), item.Rect, item.Depth, false),
            StringComparer.Ordinal);
        var project = graph.Projects.Single();
        return new PlacedGraph(
            graph,
            layouts,
            new Dictionary<string, ProjectLayout> { [project.Id] = new(project, new Rect(-20, -20, 400, 400)) },
            new LayoutRevision(0));
    }

    private static (string Id, Rect Rect, int Depth) Node(string id, int x, int y, int depth = 0) =>
        (id, new Rect(x, y, 100, 40), depth);

    private static TypeNode Type(string id, string projectId) =>
        new(id, projectId, id, $"Fixture.{id}", "Class");
}
