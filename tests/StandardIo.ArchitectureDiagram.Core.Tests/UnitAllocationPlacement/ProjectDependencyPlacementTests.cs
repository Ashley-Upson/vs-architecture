using StandardIo.ArchitectureDiagram.Core.Models;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core.Tests;

public sealed class ProjectDependencyPlacementTests
{
    [Fact]
    public void Exclusive_one_to_one_child_is_aligned_beneath_parent()
    {
        var fixture = Fixture(["parent", "child"], [("parent", "child")]);
        var placed = ProjectDependencyPlacement.Apply(fixture.Graph, fixture.Nodes, fixture.Settings);

        Assert.Equal(placed["parent"].Rect.CenterX, placed["child"].Rect.CenterX);
        Assert.Equal(NodePlacementAuthority.ExclusiveOneToOneChain, placed["child"].PlacementAuthority);
    }

    [Fact]
    public void Exclusive_siblings_remain_grouped_and_parent_is_centred_over_combined_bounds()
    {
        var fixture = Fixture(["parent", "left", "right"], [("parent", "left"), ("parent", "right")]);
        var placed = ProjectDependencyPlacement.Apply(fixture.Graph, fixture.Nodes, fixture.Settings);
        var childrenLeft = Math.Min(placed["left"].Rect.X, placed["right"].Rect.X);
        var childrenRight = Math.Max(placed["left"].Rect.Right, placed["right"].Rect.Right);

        Assert.Equal((childrenLeft + childrenRight) / 2, placed["parent"].Rect.CenterX);
        Assert.True(Math.Abs(placed["left"].Rect.Right - placed["right"].Rect.X) >=
            fixture.Settings.Layout.HorizontalSpacing);
        Assert.All(new[] { "left", "right" }, id =>
            Assert.Equal(NodePlacementAuthority.ExclusiveSiblingSubtree, placed[id].PlacementAuthority));
    }

    [Fact]
    public void Shared_child_is_excluded_from_exclusive_sibling_subtree()
    {
        var fixture = Fixture(["parent", "other", "exclusive", "shared"],
            [("parent", "exclusive"), ("parent", "shared"), ("other", "shared")]);
        var placed = ProjectDependencyPlacement.Apply(fixture.Graph, fixture.Nodes, fixture.Settings);

        Assert.Equal(NodePlacementAuthority.SharedDependency, placed["shared"].PlacementAuthority);
        Assert.NotEqual(NodePlacementAuthority.ExclusiveSiblingSubtree, placed["shared"].PlacementAuthority);
    }

    [Fact]
    public void True_standalone_is_root_owned_below_project_while_external_linked_node_is_connected()
    {
        var settings = DiagramSettings.CreateDefault();
        var project = new RenderProject("project", "Project", 0);
        var connected = Node("connected", 0);
        var external = new RenderNode("external", "project", "External", "External", "External", true,
            "[External]", 1, Array.Empty<string>(), Array.Empty<TypeProperty>(), 0);
        var standalone = Node("standalone", 2);
        var graph = RenderGraph.Create([project], [connected, external, standalone],
            [new RenderLink("edge", "connected", "external", "Dependency", 0)]);
        var depths = ConfiguredSemanticLayerPlacement.Assign(graph, settings, new LayoutRevision(0));
        var placed = ProjectRegionPlacement.Place(graph, settings, new LayoutRevision(0), depths.FinalDepthByNodeId);

        Assert.False(placed.Nodes["connected"].IsStandalone);
        Assert.True(placed.Nodes["standalone"].IsStandalone);
        Assert.Equal(NodePlacementAuthority.StandaloneExternalRegion,
            placed.Nodes["standalone"].PlacementAuthority);
        Assert.Null(placed.Nodes["standalone"].Node.ProjectId);
        Assert.True(placed.Nodes["standalone"].Rect.Y >= placed.Projects["project"].Rect.Bottom);
        Assert.DoesNotContain(depths.UnmatchedNodes, node => node.NodeId == "standalone");
    }

    private static FixtureData Fixture(IReadOnlyList<string> ids,
        IReadOnlyList<(string Source, string Target)> links)
    {
        var settings = DiagramSettings.CreateDefault();
        var project = new RenderProject("project", "Project", 0);
        var nodes = ids.Select((id, index) => Node(id, index)).ToArray();
        var graph = RenderGraph.Create([project], nodes, links.Select((link, index) =>
            new RenderLink($"edge-{index}", link.Source, link.Target, "Dependency", index)).ToArray());
        var layouts = nodes.Select((node, index) => new NodeLayout(node,
            new Rect(100 + index * 500, 100 + index * 160, 200, 80), index, false))
            .ToDictionary(node => node.Node.Id, StringComparer.Ordinal);
        return new FixtureData(graph, layouts, settings);
    }

    private static RenderNode Node(string id, int order) => new(id, "project", id, id, "Class", false,
        string.Empty, order, Array.Empty<string>(), Array.Empty<TypeProperty>(), 0);

    private sealed record FixtureData(RenderGraph Graph,
        IReadOnlyDictionary<string, NodeLayout> Nodes, DiagramSettings Settings);
}
