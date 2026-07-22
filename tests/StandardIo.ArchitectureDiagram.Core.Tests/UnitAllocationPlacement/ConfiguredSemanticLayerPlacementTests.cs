using StandardIo.ArchitectureDiagram.Core.Models;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core.Tests;

public sealed class ConfiguredSemanticLayerPlacementTests
{
    [Fact]
    public void Specific_first_rule_wins_over_generic_service_and_empty_groups_are_compressed()
    {
        var fixture = Fixture(
            [("controller", "ApiController", 5, false), ("coord", "WorkCoordinationService", 0, false),
             ("broker", "DataBroker", 2, false)]);

        var result = Assign(fixture.Placement);

        Assert.Equal(new[] { "Controller", "CoordinationService", "Broker" },
            result.ActiveGroups.Select(group => group.Name));
        Assert.Equal(1, result.FinalDepthByNodeId["coord"]);
        Assert.DoesNotContain(result.ActiveGroups, group => group.Name == "Service");
    }

    [Fact]
    public void Matched_nodes_ignore_original_depth_and_majority_uses_mode_with_shallow_tie()
    {
        var fixture = Fixture(
            [("c1", "FirstController", 4, false), ("c2", "SecondController", 2, false),
             ("c3", "ThirdController", 2, false), ("b1", "FirstBroker", 5, false),
             ("b2", "SecondBroker", 3, false)]);

        var result = Assign(fixture.Placement);
        var controller = result.ActiveGroups.Single(group => group.Name == "Controller");
        var broker = result.ActiveGroups.Single(group => group.Name == "Broker");

        Assert.Equal(2, controller.PreviousMajorityDepth);
        Assert.Equal(3, broker.PreviousMajorityDepth);
        Assert.All(new[] { "c1", "c2", "c3" }, id => Assert.Equal(0, result.FinalDepthByNodeId[id]));
        Assert.All(new[] { "b1", "b2" }, id => Assert.Equal(1, result.FinalDepthByNodeId[id]));
    }

    [Fact]
    public void Unmatched_node_with_processing_children_shares_preceding_orchestration_layer()
    {
        var fixture = Fixture(
            [("controller", "ApiController", 0, false), ("orchestration", "FlowOrchestrationService", 1, false),
             ("thing", "ThisFuckingThing", 5, false), ("p1", "FirstProcessingService", 3, false),
             ("p2", "SecondProcessingService", 4, false)],
            [("thing", "p1"), ("thing", "p2")]);

        var result = Assign(fixture.Placement);
        var diagnostic = result.UnmatchedNodes.Single(node => node.NodeId == "thing");

        Assert.Equal(result.FinalDepthByNodeId["orchestration"], result.FinalDepthByNodeId["thing"]);
        Assert.Equal("ChildDerivedMode", diagnostic.DecisionReason);
    }

    [Fact]
    public void Parents_children_conflicts_and_unmatched_chains_are_resolved_deterministically()
    {
        var fixture = Fixture(
            [("controller", "ApiController", 0, false), ("processing", "FlowProcessingService", 3, false),
             ("broker", "DataBroker", 5, false), ("a", "Alpha", 7, false),
             ("b", "Beta", 6, false), ("conflict", "Conflict", 4, false)],
            [("a", "b"), ("b", "processing"), ("broker", "conflict"), ("conflict", "controller")]);
        var first = Assign(fixture.Placement);
        var reversed = Assign(fixture.ReversedPlacement);

        Assert.Equal(first.FinalDepthByNodeId.OrderBy(item => item.Key),
            reversed.FinalDepthByNodeId.OrderBy(item => item.Key));
        Assert.True(first.UnmatchedNodes.Single(node => node.NodeId == "conflict").ConstraintConflict);
        Assert.True(first.FinalDepthByNodeId["a"] <= first.FinalDepthByNodeId["b"]);
    }

    [Fact]
    public void Disconnected_unmatched_node_uses_nearest_group_majority()
    {
        var fixture = Fixture(
            [("controller", "ApiController", 0, false), ("broker", "DataBroker", 6, false),
             ("unknown", "Unknown", 5, false)]);

        var result = Assign(fixture.Placement);

        Assert.Equal(result.FinalDepthByNodeId["broker"], result.FinalDepthByNodeId["unknown"]);
        Assert.Equal("OriginalDepthNearestGroupMajority",
            result.UnmatchedNodes.Single(node => node.NodeId == "unknown").DecisionReason);
    }

    [Fact]
    public void Externals_use_terminal_depth()
    {
        var fixture = Fixture(
            [("controller", "ApiController", 4, false), ("broker", "DataBroker", 1, false),
             ("external", "ExternalService", 0, true)]);
        var result = Assign(fixture.Placement);

        Assert.Equal(2, result.FinalDepthByNodeId["external"]);
        Assert.DoesNotContain(result.ActiveGroups, group => group.MatchedNodeCount > 0 && group.Name == "Service");
    }

    [Fact]
    public void Empty_rules_disable_depth_and_geometry_changes()
    {
        var fixture = Fixture([("controller", "ApiController", 4, false), ("broker", "DataBroker", 1, false)]);
        var settings = Settings();
        settings.Layout.NodeLayerGroups = [];

        var result = ConfiguredSemanticLayerPlacement.Assign(fixture.Placement.Graph, settings,
            fixture.Placement.Revision, fixture.Placement.Nodes.ToDictionary(item => item.Key, item => item.Value.Depth));

        Assert.False(result.Enabled);
        Assert.Equal(fixture.Placement.Nodes.ToDictionary(item => item.Key, item => item.Value.Depth),
            result.FinalDepthByNodeId);
    }

    [Fact]
    public void Positional_placement_consumes_semantic_depths_without_overlap()
    {
        var fixture = Fixture([("first", "FirstBroker", 1, false), ("second", "SecondBroker", 4, false)]);
        var result = Assign(fixture.Placement);
        var placed = ProjectRegionPlacement.Place(fixture.Placement.Graph, Settings(), new LayoutRevision(0),
            result.FinalDepthByNodeId);
        var layer = placed.Nodes.Values.Where(node => node.Depth == result.FinalDepthByNodeId["first"]).ToArray();

        Assert.Equal(2, layer.Length);
        Assert.True(layer[0].Rect.Right <= layer[1].Rect.X || layer[1].Rect.Right <= layer[0].Rect.X);
    }

    [Fact]
    public void Split_layers_and_reversed_enumeration_produce_deterministic_placement()
    {
        var fixture = Fixture(
            [("controller", "ApiController", 4, false), ("service", "PageRenderContentService", 4, false),
             ("broker", "PageBroker", 4, false)],
            [("controller", "service"), ("service", "broker")]);
        var firstDepths = Assign(fixture.Placement);
        var reversedDepths = Assign(fixture.ReversedPlacement);
        var first = ProjectRegionPlacement.Place(fixture.Placement.Graph, Settings(), new LayoutRevision(0),
            firstDepths.FinalDepthByNodeId);
        var reversed = ProjectRegionPlacement.Place(fixture.ReversedPlacement.Graph, Settings(), new LayoutRevision(0),
            reversedDepths.FinalDepthByNodeId);

        Assert.Equal(3, first.Nodes.Values.Select(node => node.Depth).Distinct().Count());
        Assert.Equal(first.Nodes.OrderBy(item => item.Key).Select(item => (item.Key, item.Value.Rect)),
            reversed.Nodes.OrderBy(item => item.Key).Select(item => (item.Key, item.Value.Rect)));
    }

    [Fact]
    public void Complete_graph_connectivity_keeps_internal_node_linked_only_to_external_out_of_standalone_region()
    {
        var fixture = Fixture(
            [("controller", "ApiController", 0, false), ("params", "PageRenderParams", 2, false),
             ("external", "ExternalPage", 3, true)],
            [("params", "external")]);
        var depths = Assign(fixture.Placement);
        var placed = ProjectRegionPlacement.Place(fixture.Placement.Graph, Settings(), new LayoutRevision(0),
            depths.FinalDepthByNodeId);

        Assert.False(placed.Nodes["params"].IsStandalone);
        Assert.Equal(depths.FinalDepthByNodeId["params"], placed.Nodes["params"].Depth);
    }

    private static DiagramSettings Settings() => DiagramSettings.CreateDefault();

    private static ConfiguredSemanticLayerPlacementResult Assign(PlacedGraph placement) =>
        ConfiguredSemanticLayerPlacement.Assign(placement.Graph, Settings(), placement.Revision,
            placement.Nodes.ToDictionary(item => item.Key, item => item.Value.Depth));

    private static PlacementFixture Fixture(
        IReadOnlyList<(string Id, string Name, int Depth, bool External)> specifications,
        IReadOnlyList<(string Source, string Target)>? links = null)
    {
        var project = new RenderProject("project", "Project", 0);
        var renderNodes = specifications.Select((item, index) => new RenderNode(
            item.Id, "project", item.Name, $"Fixture.{item.Name}", "Class", item.External,
            item.External ? "[External]" : string.Empty, index, Array.Empty<string>(),
            Array.Empty<TypeProperty>(), 0)).ToArray();
        var renderLinks = (links ?? Array.Empty<(string Source, string Target)>()).Select((item, index) =>
            new RenderLink($"route-{index}", item.Source, item.Target, "Dependency", index)).ToArray();
        var graph = RenderGraph.Create([project], renderNodes, renderLinks);
        var layouts = renderNodes.Select((node, index) => new NodeLayout(
                node, new Rect(100 + index * 260, 100 + specifications[index].Depth * 160,
                    180 + index, 80), specifications[index].Depth, false))
            .ToDictionary(node => node.Node.Id, StringComparer.Ordinal);
        var projects = new Dictionary<string, ProjectLayout>(StringComparer.Ordinal)
        {
            [project.Id] = new(project, new Rect(40, 40, 2000, 1600))
        };
        var placement = new PlacedGraph(graph, layouts, projects, new LayoutRevision(0));

        var reversedGraph = RenderGraph.Create([project], Enumerable.Reverse(renderNodes).ToArray(),
            Enumerable.Reverse(renderLinks).ToArray());
        var reversedPlacement = new PlacedGraph(reversedGraph,
            layouts.Reverse().ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal),
            projects, new LayoutRevision(0));
        return new PlacementFixture(placement, reversedPlacement);
    }

    private sealed record PlacementFixture(PlacedGraph Placement, PlacedGraph ReversedPlacement);
}
