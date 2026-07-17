using StandardIo.ArchitectureDiagram.Core.Models;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core.Tests;

public sealed class RenderGraphDuplicationTests
{
    [Fact]
    public void Default_exposure_rendering_duplicates_shared_dependency_per_branch()
    {
        var settings = DiagramSettings.CreateDefault();
        settings.Layout.ExposureTreeLayoutThreshold = 1;

        var graph = RenderGraph.From(SharedServiceDiagram(), settings);

        Assert.Equal(2, graph.Nodes.Count(node => node.FullName == "App.SharedService"));
        Assert.Equal(2, graph.Links.Count(link => link.SemanticTargetId == "shared"));
    }

    [Fact]
    public void Default_external_rendering_duplicates_shared_dependency_per_incoming_edge()
    {
        var settings = DiagramSettings.CreateDefault();
        settings.Layout.ExposureTreeLayoutThreshold = int.MaxValue;

        var graph = RenderGraph.From(SharedExternalDiagram(), settings);

        Assert.Equal(2, graph.Nodes.Count(node => node.IsExternal && node.FullName == "Microsoft.Extensions.Logging.ILogger"));
        Assert.Equal(2, graph.Links.Select(link => link.TargetId).Distinct(StringComparer.Ordinal).Count());
        Assert.All(graph.Nodes.Where(node => node.IsExternal), node => Assert.Equal("project", node.ProjectId));
    }

    [Fact]
    public void Disabled_exposure_rendering_reuses_one_canonical_shared_dependency()
    {
        var settings = DisabledDuplicationSettings();

        var graph = RenderGraph.From(SharedServiceDiagram(), settings);

        Assert.Single(graph.Nodes, node => node.FullName == "App.SharedService");
        Assert.Equal(2, graph.Links.Count(link => link.SemanticTargetId == "shared"));
        Assert.Single(graph.Links.Where(link => link.SemanticTargetId == "shared").Select(link => link.TargetId).Distinct(StringComparer.Ordinal));
    }

    [Fact]
    public void Disabled_external_rendering_reuses_one_project_local_physical_node()
    {
        var settings = DisabledDuplicationSettings();
        settings.Layout.ExposureTreeLayoutThreshold = int.MaxValue;

        var graph = RenderGraph.From(SharedExternalDiagram(), settings);

        var external = Assert.Single(graph.Nodes, node => node.IsExternal);
        Assert.Equal("project", external.ProjectId);
        Assert.All(graph.Links, link => Assert.Equal(external.Id, link.TargetId));
    }

    [Theory]
    [InlineData("Microsoft\\.Extensions\\.Logging\\.ILogger$")]
    [InlineData("^ILogger$")]
    public void Matching_full_or_short_name_exception_preserves_external_duplication(string pattern)
    {
        var settings = DisabledDuplicationSettings();
        settings.Layout.ExposureTreeLayoutThreshold = int.MaxValue;
        settings.NodeDuplication.DuplicationExceptionPatterns.Add(pattern);

        var graph = RenderGraph.From(SharedExternalDiagram(), settings);

        Assert.Equal(2, graph.Nodes.Count(node => node.IsExternal));
        Assert.Equal(2, graph.Links.Select(link => link.TargetId).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Disabled_exposure_identity_is_deterministic_when_edges_are_reversed()
    {
        var settings = DisabledDuplicationSettings();
        var diagram = SharedServiceDiagram();
        var reversed = diagram with { Edges = diagram.Edges.Reverse().ToArray() };

        var first = RenderGraph.From(diagram, settings);
        var second = RenderGraph.From(reversed, settings);

        Assert.Equal(
            first.Nodes.OrderBy(node => node.Id).Select(node => (node.Id, node.FullName)),
            second.Nodes.OrderBy(node => node.Id).Select(node => (node.Id, node.FullName)));
        Assert.Equal(
            first.Links.OrderBy(link => link.Id).Select(link => (link.Id, link.SourceId, link.TargetId)),
            second.Links.OrderBy(link => link.Id).Select(link => (link.Id, link.SourceId, link.TargetId)));
    }

    [Fact]
    public void Matching_exposure_exception_preserves_shared_dependency_clones()
    {
        var settings = DisabledDuplicationSettings();
        settings.NodeDuplication.DuplicationExceptionPatterns.Add("^SharedService$");

        var graph = RenderGraph.From(SharedServiceDiagram(), settings);

        Assert.Equal(2, graph.Nodes.Count(node => node.FullName == "App.SharedService"));
        Assert.Equal(2, graph.Links.Where(link => link.SemanticTargetId == "shared").Select(link => link.TargetId).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Disabled_external_rendering_keeps_one_physical_copy_per_owning_project()
    {
        var settings = DisabledDuplicationSettings();
        settings.Layout.ExposureTreeLayoutThreshold = int.MaxValue;
        var diagram = SharedExternalDiagramAcrossProjects();

        var graph = RenderGraph.From(diagram, settings);

        var externalNodes = graph.Nodes.Where(node => node.IsExternal).OrderBy(node => node.ProjectId).ToArray();
        Assert.Equal(2, externalNodes.Length);
        Assert.Equal(new[] { "project_a", "project_b" }, externalNodes.Select(node => node.ProjectId));
        Assert.Equal(2, graph.Links.Select(link => link.TargetId).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Disabled_exposure_rendering_terminates_cycles_and_reuses_the_canonical_node()
    {
        var settings = DisabledDuplicationSettings();
        var diagram = new DiagramModel(
            new[]
            {
                new ProjectContainer("project", "Project", new[]
                {
                    Node("controller", "RootController"),
                    Node("service", "SharedService")
                })
            },
            Array.Empty<ExternalDependencyNode>(),
            new[]
            {
                new DependencyEdge("to_service", "controller", "service", "internal"),
                new DependencyEdge("to_root", "service", "controller", "internal")
            });

        var graph = RenderGraph.From(diagram, settings);

        Assert.Equal(2, graph.Nodes.Count);
        Assert.Equal(2, graph.Links.Count);
        Assert.Equal(graph.Links.Single(link => link.SemanticSourceId == "service").TargetId, graph.Links.Single(link => link.SemanticSourceId == "controller").SourceId);
    }

    internal static DiagramModel SharedServiceDiagram() => new(
        new[]
        {
            new ProjectContainer("project", "Project", new[]
            {
                Node("parent_a", "ParentAController"),
                Node("parent_b", "ParentBController"),
                Node("shared", "SharedService")
            })
        },
        Array.Empty<ExternalDependencyNode>(),
        new[]
        {
            new DependencyEdge("edge_a", "parent_a", "shared", "internal"),
            new DependencyEdge("edge_b", "parent_b", "shared", "internal")
        });

    internal static DiagramModel SharedExternalDiagram() => new(
        new[]
        {
            new ProjectContainer("project", "Project", new[]
            {
                Node("parent_a", "ParentAController"),
                Node("parent_b", "ParentBController")
            })
        },
        new[]
        {
            new ExternalDependencyNode(
                "logger",
                "ILogger",
                "Microsoft.Extensions.Logging",
                "logger-guid",
                "Microsoft.Extensions.Logging.ILogger",
                "[External]")
        },
        new[]
        {
            new DependencyEdge("edge_a", "parent_a", "logger", "external"),
            new DependencyEdge("edge_b", "parent_b", "logger", "external")
        });

    internal static DiagramModel SharedExternalDiagramAcrossProjects() => new(
        new[]
        {
            new ProjectContainer("project_a", "Project A", new[]
            {
                new TypeNode("parent_a", "project_a", "ParentAController", "App.ParentAController", "Class")
            }),
            new ProjectContainer("project_b", "Project B", new[]
            {
                new TypeNode("parent_b", "project_b", "ParentBController", "App.ParentBController", "Class")
            })
        },
        new[]
        {
            new ExternalDependencyNode(
                "logger",
                "ILogger",
                "Microsoft.Extensions.Logging",
                "logger-guid",
                "Microsoft.Extensions.Logging.ILogger",
                "[External]")
        },
        new[]
        {
            new DependencyEdge("edge_a", "parent_a", "logger", "external"),
            new DependencyEdge("edge_b", "parent_b", "logger", "external")
        });

    internal static TypeNode Node(string id, string name) =>
        new(id, "project", name, $"App.{name}", "Class");

    private static DiagramSettings DisabledDuplicationSettings()
    {
        var settings = DiagramSettings.CreateDefault();
        settings.Layout.ExposureTreeLayoutThreshold = 1;
        settings.NodeDuplication.AllowDuplicateNodes = false;
        return settings;
    }
}
