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
    public void Projection_is_stable_when_nodes_and_links_are_reversed()
    {
        var diagram = Shared(false);
        var reversed = diagram with
        {
            Projects = diagram.Projects.Reverse().Select(project => project with
            {
                Nodes = project.Nodes.Reverse().ToArray()
            }).ToArray(),
            Links = diagram.Links.Reverse().ToArray()
        };

        Assert.Equal(Signature(Project(diagram, true)), Signature(Project(reversed, true)));
        Assert.Equal(Signature(Project(diagram, false)), Signature(Project(reversed, false)));
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
