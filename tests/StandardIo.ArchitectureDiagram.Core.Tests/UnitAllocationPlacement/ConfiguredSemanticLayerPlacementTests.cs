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

        var result = ConfiguredSemanticLayerPlacement.Apply(fixture.Placement, Settings());

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

        var result = ConfiguredSemanticLayerPlacement.Apply(fixture.Placement, Settings());
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

        var result = ConfiguredSemanticLayerPlacement.Apply(fixture.Placement, Settings());
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
        var first = ConfiguredSemanticLayerPlacement.Apply(fixture.Placement, Settings());
        var reversed = ConfiguredSemanticLayerPlacement.Apply(fixture.ReversedPlacement, Settings());

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

        var result = ConfiguredSemanticLayerPlacement.Apply(fixture.Placement, Settings());

        Assert.Equal(result.FinalDepthByNodeId["broker"], result.FinalDepthByNodeId["unknown"]);
        Assert.Equal("OriginalDepthNearestGroupMajority",
            result.UnmatchedNodes.Single(node => node.NodeId == "unknown").DecisionReason);
    }

    [Fact]
    public void Externals_use_terminal_depth_and_horizontal_geometry_is_immutable()
    {
        var fixture = Fixture(
            [("controller", "ApiController", 4, false), ("broker", "DataBroker", 1, false),
             ("external", "ExternalService", 0, true)]);
        var baseline = fixture.Placement.Nodes.ToDictionary(item => item.Key,
            item => (item.Value.Rect.X, item.Value.Rect.Width));

        var result = ConfiguredSemanticLayerPlacement.Apply(fixture.Placement, Settings());

        Assert.Equal(2, result.FinalDepthByNodeId["external"]);
        Assert.DoesNotContain(result.ActiveGroups, group => group.MatchedNodeCount > 0 && group.Name == "Service");
        Assert.Equal(0, result.ChangedXCount);
        Assert.Equal(0, result.ChangedWidthCount);
        Assert.All(result.Placement.Nodes, item => Assert.Equal(baseline[item.Key],
            (item.Value.Rect.X, item.Value.Rect.Width)));
    }

    [Fact]
    public void Empty_rules_disable_depth_and_geometry_changes()
    {
        var fixture = Fixture([("controller", "ApiController", 4, false), ("broker", "DataBroker", 1, false)]);
        var settings = Settings();
        settings.Layout.NodeLayerGroups = [];

        var result = ConfiguredSemanticLayerPlacement.Apply(fixture.Placement, settings);

        Assert.False(result.Enabled);
        Assert.Equal(fixture.Placement.Nodes.ToDictionary(item => item.Key, item => item.Value.Depth),
            result.FinalDepthByNodeId);
    }

    [Fact]
    public void Same_layer_horizontal_overlap_is_reported_without_moving_nodes()
    {
        var fixture = Fixture([("first", "FirstBroker", 1, false), ("second", "SecondBroker", 4, false)]);
        var second = fixture.Placement.Nodes["second"];
        var overlapping = fixture.Placement.Nodes.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        overlapping["second"] = second with { Rect = second.Rect with { X = 150 } };
        var placement = new PlacedGraph(fixture.Placement.Graph, overlapping, fixture.Placement.Projects,
            fixture.Placement.Revision);

        var result = ConfiguredSemanticLayerPlacement.Apply(placement, Settings());

        Assert.Single(result.HorizontalOverlaps);
        Assert.Equal(0, result.ChangedXCount);
        Assert.Equal(150, result.Placement.Nodes["second"].Rect.X);
    }

    private static DiagramSettings Settings() => DiagramSettings.CreateDefault();

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
