using StandardIo.ArchitectureDiagram.Core.Models;
using StandardIo.ArchitectureDiagram.Core.Models.Architectures;
using StandardIo.ArchitectureDiagram.Core.Services.Processings.Architectures;
using Xunit;
using ArchitectureDiagramModel = StandardIo.ArchitectureDiagram.Core.Models.Architectures.ArchitectureDiagram;

namespace StandardIo.ArchitectureDiagram.Core.Tests;

public sealed class ArchitectureTopologyProjectorTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Duplication_modes_do_not_depend_on_configured_roots(bool configuredRoots)
    {
        var diagram = Shared(configuredRoots);
        var enabled = Project(diagram, true);
        var disabled = Project(diagram, false);

        Assert.Equal(2, enabled.Nodes.Count(node => node.SemanticNodeId == "shared"));
        Assert.Equal(2, enabled.Nodes.Count(node => node.SemanticNodeId == "child"));
        Assert.Single(disabled.Nodes, node => node.SemanticNodeId == "shared");
        Assert.Single(disabled.Nodes, node => node.SemanticNodeId == "child");
        Assert.Equal(2, disabled.Links.Count(link => link.TargetSemanticId == "shared"));
    }

    [Fact]
    public void Exception_duplicates_matching_node_and_its_downstream_occurrence()
    {
        var settings = new NodeDuplicationSettings { AllowDuplicateNodes = false };
        settings.DuplicationExceptionPatterns.Add("^Shared$");

        var graph = new ArchitectureTopologyProjector().Project(Shared(false), settings);

        Assert.Equal(2, graph.Nodes.Count(node => node.SemanticNodeId == "shared"));
        Assert.Equal(2, graph.Nodes.Count(node => node.SemanticNodeId == "child"));
        Assert.Contains(graph.Nodes, node => node.SemanticNodeId == "shared" &&
            node.DuplicationReason == ArchitectureDuplicationReason.ExceptionPattern);
    }

    [Fact]
    public void Empty_root_inference_covers_disconnected_nodes_and_cycles()
    {
        var diagram = Diagram(
            [Node("a"), Node("b"), Node("c"), Node("isolated")],
            [Link("ab", "a", "b"), Link("ba", "b", "a")]);

        var graph = Project(diagram, false);

        Assert.Equal(new[] { "a", "c", "isolated" }, graph.TraversalRootSemanticIds);
        Assert.Equal(4, graph.Nodes.Count);
        Assert.Equal(2, graph.Links.Count);
    }

    [Fact]
    public void Projection_preserves_discovery_order_and_uses_ids_only_as_tie_breakers()
    {
        var diagram = Diagram([Node("z"), Node("a")], []);
        var reversed = diagram with
        {
            Projects = diagram.Projects.Reverse().Select(project => project with
            {
                Nodes = project.Nodes.Reverse().ToArray()
            }).ToArray(),
            Links = diagram.Links.Reverse().ToArray()
        };

        var original = Project(diagram, false);
        var reversedGraph = Project(reversed, false);

        Assert.Equal(new[] { "z", "a" }, original.Nodes.Select(node => node.SemanticNodeId));
        Assert.Equal(new[] { "a", "z" }, reversedGraph.Nodes.Select(node => node.SemanticNodeId));

        var linked = Project(Shared(false), false);
        Assert.Equal(new[] { "a_shared", "b_shared", "shared_child" },
            linked.Links.Select(link => link.SemanticLinkId));
    }

    [Fact]
    public void Canonical_first_occurrence_keeps_its_placement_parent()
    {
        var graph = Project(Shared(false), false);
        var shared = Assert.Single(graph.Nodes, node => node.SemanticNodeId == "shared");
        var parent = graph.Nodes.Single(node => node.Id == shared.PlacementParentRenderId);

        Assert.Equal("a", parent.SemanticNodeId);
        Assert.Equal(2, graph.Links.Count(link => link.TargetRenderInstanceId == shared.Id));
    }

    [Fact]
    public void Canonical_mode_creates_exactly_one_physical_instance_per_semantic_node()
    {
        var graph = Project(Shared(false), false);

        Assert.Equal(graph.Nodes.Select(node => node.SemanticNodeId).Distinct(StringComparer.Ordinal).Count(), graph.Nodes.Count);
        Assert.All(graph.RenderInstancesBySemanticNodeId, item => Assert.Single(item.Value));
    }

    [Fact]
    public void Duplicate_enabled_projection_records_source_and_reason_for_every_extra_instance()
    {
        var graph = Project(Shared(false), true);

        foreach (var group in graph.Nodes.GroupBy(node => node.SemanticNodeId, StringComparer.Ordinal))
        {
            Assert.NotEmpty(group);
            Assert.Equal(group.Count(), graph.RenderInstancesBySemanticNodeId[group.Key].Count);
            Assert.All(group.Skip(1), node =>
            {
                Assert.Equal(ArchitectureRenderNodeOccurrence.Duplicated, node.Occurrence);
                Assert.NotEqual(ArchitectureDuplicationReason.None, node.DuplicationReason);
                Assert.Equal(group.Key, node.SemanticNodeId);
            });
        }
    }

    [Fact]
    public void External_projection_preserves_external_tag_and_semantic_identity()
    {
        var graph = Project(new ArchitectureDiagramModel(
            [Project("project", Node("owner"))],
            [new ArchitectureExternalNode("external", "ILogger", "Logging", "", "Logging.ILogger", "[External]")],
            [Link("owner_external", "owner", "external")], null), false);

        var external = Assert.Single(graph.Nodes, node => node.SemanticNodeId == "external");
        Assert.True(external.IsExternal);
        Assert.Equal("[External]", external.ExternalTag);
        Assert.Equal("external", external.SemanticNodeId);
    }

    [Fact]
    public void Internal_node_ownership_always_comes_from_its_source_project()
    {
        var diagram = new ArchitectureDiagramModel(
            [Project("pa", Node("a", "pa")), Project("pb", Node("b", "pb")), Project("pc", Node("shared", "pc"))],
            [], [Link("a_shared", "a", "shared"), Link("b_shared", "b", "shared")], null);

        Assert.All(Project(diagram, true).Nodes.Where(node => node.SemanticNodeId == "shared"),
            node => Assert.Equal("pc", node.ProjectId));
        Assert.Equal("pc", Assert.Single(Project(diagram, false).Nodes,
            node => node.SemanticNodeId == "shared").ProjectId);
    }

    [Fact]
    public void Shared_external_is_root_owned_when_canonical_and_project_local_when_duplicated()
    {
        var diagram = new ArchitectureDiagramModel(
            [Project("pa", Node("a", "pa")), Project("pb", Node("b", "pb"))],
            [new ArchitectureExternalNode("external", "ILogger", "Logging", "", "Logging.ILogger", "[External]")],
            [Link("a_external", "a", "external", "external"), Link("b_external", "b", "external", "external")], null);

        var canonical = Project(diagram, false);
        var duplicated = Project(diagram, true);

        Assert.Null(Assert.Single(canonical.Nodes, node => node.SemanticNodeId == "external").ProjectId);
        Assert.Equal(new[] { "pa", "pb" }, duplicated.Nodes.Where(node => node.SemanticNodeId == "external")
            .Select(node => node.ProjectId).OrderBy(id => id, StringComparer.Ordinal));
    }

    [Fact]
    public void Single_project_external_is_project_owned_in_canonical_mode()
    {
        var diagram = new ArchitectureDiagramModel(
            [Project("pa", Node("a", "pa"))],
            [new ArchitectureExternalNode("external", "ILogger", "Logging", "", "Logging.ILogger", "[External]")],
            [Link("a_external", "a", "external", "external")], null);

        Assert.Equal("pa", Assert.Single(Project(diagram, false).Nodes,
            node => node.SemanticNodeId == "external").ProjectId);
    }

    [Fact]
    public void Invalid_exception_regex_remains_a_settings_error()
    {
        var settings = new NodeDuplicationSettings { AllowDuplicateNodes = false };
        settings.DuplicationExceptionPatterns.Add("[");

        Assert.Throws<InvalidDataException>(() => new ArchitectureTopologyProjector().Project(Shared(false), settings));
    }

    private static ArchitectureRenderGraph Project(ArchitectureDiagramModel diagram, bool allowDuplicates) =>
        new ArchitectureTopologyProjector().Project(diagram,
            new NodeDuplicationSettings { AllowDuplicateNodes = allowDuplicates });

    private static ArchitectureDiagramModel Shared(bool configuredRoots) => new(
        [Project("project", Node("a"), Node("b"), Node("shared"), Node("child"))], [],
        [Link("a_shared", "a", "shared"), Link("b_shared", "b", "shared"), Link("shared_child", "shared", "child")],
        configuredRoots ? new ArchitectureSelectionDiagnostic("ConfiguredRootReachability",
            [new ArchitectureRoot("a", "A", 0, 1, "A"), new ArchitectureRoot("b", "B", 0, 1, "B")],
            ["a", "b", "shared", "child"], [], ["a_shared", "b_shared", "shared_child"], [], []) : null);

    private static ArchitectureDiagramModel Diagram(ArchitectureNode[] nodes, ArchitectureLink[] links) =>
        new([Project("project", nodes)], [], links, null);

    private static ArchitectureProject Project(string id, params ArchitectureNode[] nodes) =>
        new(id, id, nodes, id);

    private static ArchitectureNode Node(string id, string projectId = "project") =>
        new(id, projectId, char.ToUpperInvariant(id[0]) + id.Substring(1), "Fixture." + id,
            "Class", id, []);

    private static ArchitectureLink Link(string id, string source, string target, string kind = "internal") =>
        new(id, source, target, kind);

    private static string[] Signature(ArchitectureRenderGraph graph) => graph.Nodes.OrderBy(node => node.Id)
        .Select(node => $"N|{node.Id}|{node.SemanticNodeId}|{node.ProjectId}|{node.PlacementParentRenderId}")
        .Concat(graph.Links.OrderBy(link => link.Id).Select(link =>
            $"L|{link.Id}|{link.SemanticLinkId}|{link.SourceRenderInstanceId}|{link.TargetRenderInstanceId}"))
        .ToArray();
}
