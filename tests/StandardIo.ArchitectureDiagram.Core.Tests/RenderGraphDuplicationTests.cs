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

    internal static TypeNode Node(string id, string name) =>
        new(id, "project", name, $"App.{name}", "Class");
}
