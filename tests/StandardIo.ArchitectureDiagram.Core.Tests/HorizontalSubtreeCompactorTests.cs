using StandardIo.ArchitectureDiagram.Core.Models;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core.Tests;

public sealed class HorizontalSubtreeCompactorTests
{
    [Fact]
    public void Compaction_removes_unowned_gap_and_moves_the_complete_subtree()
    {
        var placement = Placement(
            ("left", 0, 0, 0), ("left-child", 20, 100, 1),
            ("right", 400, 0, 0), ("right-child", 430, 100, 1));
        var settings = Settings(40);

        var result = HorizontalSubtreeCompactor.Compact(placement, settings);

        Assert.Equal(40, result.MaximumUnownedGapAfter);
        var move = Assert.Single(result.Moves);
        Assert.Equal("right", move.SubtreeRootNodeId);
        Assert.Equal(-290, move.DeltaX);
        Assert.Equal(110, result.Placement.Nodes["right"].Rect.X);
        Assert.Equal(140, result.Placement.Nodes["right-child"].Rect.X);
    }

    [Fact]
    public void Closest_per_layer_envelope_preserves_deeper_expansion_spacing()
    {
        var placement = Placement(
            ("left", 100, 0, 0), ("left-child", 180, 100, 1),
            ("right", 500, 0, 0), ("right-child", 400, 100, 1));

        var result = HorizontalSubtreeCompactor.Compact(placement, Settings(40));

        Assert.Equal(40, result.MaximumUnownedGapAfter);
        Assert.Equal(290, result.Placement.Nodes["right-child"].Rect.X);
        Assert.Equal(390, result.Placement.Nodes["right"].Rect.X);
    }

    [Fact]
    public void Compaction_is_deterministic()
    {
        var placement = Placement(("a", 0, 0, 0), ("b", 400, 0, 0));
        var first = HorizontalSubtreeCompactor.Compact(placement, Settings(40));
        var second = HorizontalSubtreeCompactor.Compact(placement, Settings(40));

        Assert.Equal(first.Moves, second.Moves);
        Assert.Equal(first.Placement.Nodes.Select(item => item.Value.Rect),
            second.Placement.Nodes.Select(item => item.Value.Rect));
    }

    private static PlacedGraph Placement(params (string Id, int X, int Y, int Depth)[] specifications)
    {
        var ids = specifications.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        var links = new List<DependencyEdge>();
        if (ids.Contains("left-child")) links.Add(new("left-edge", "left", "left-child", "Dependency"));
        if (ids.Contains("right-child")) links.Add(new("right-edge", "right", "right-child", "Dependency"));
        var graph = RenderGraph.From(new DiagramModel(
            new[] { new ProjectContainer("project", "Project", specifications.Select(item => Type(item.Id)).ToArray()) },
            Array.Empty<ExternalDependencyNode>(), links));
        var byId = graph.Nodes.ToDictionary(node => node.Id, StringComparer.Ordinal);
        var layouts = specifications.ToDictionary(
            item => item.Id,
            item => new NodeLayout(byId[item.Id], new Rect(item.X, item.Y, 70, 40), item.Depth, false),
            StringComparer.Ordinal);
        return new PlacedGraph(graph, layouts, new Dictionary<string, ProjectLayout>(), new LayoutRevision(0));
    }

    private static DiagramSettings Settings(int spacing)
    {
        var settings = DiagramSettings.CreateDefault();
        settings.ShowProjectContainers = false;
        settings.Layout.HorizontalSpacing = spacing;
        return settings;
    }

    private static TypeNode Type(string id) => new(id, "project", id, id, "Class");
}
