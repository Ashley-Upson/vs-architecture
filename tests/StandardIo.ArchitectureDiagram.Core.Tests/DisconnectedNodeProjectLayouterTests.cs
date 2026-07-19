using StandardIo.ArchitectureDiagram.Core.Models;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core.Tests;

public sealed class DisconnectedNodeProjectLayouterTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 2)]
    [InlineData(4, 2)]
    [InlineData(5, 3)]
    [InlineData(8, 3)]
    [InlineData(10, 4)]
    [InlineData(16, 4)]
    public void Uses_ceiling_square_root_nodes_per_layer(int count, int expected)
    {
        var (graph, nodes) = Fixture(count);

        var result = DisconnectedNodeProjectLayouter.Create(graph, nodes, Settings());

        if (count == 0) Assert.Null(result);
        else Assert.Equal(expected, Assert.IsType<DisconnectedNodeProjectLayout>(result).NodesPerLayer);
    }

    [Fact]
    public void Centres_incomplete_final_layer_and_preserves_natural_widths()
    {
        var (graph, nodes) = Fixture(5);
        nodes["detached-4"] = nodes["detached-4"] with { Rect = new Rect(0, 0, 90, 40) };

        var result = Assert.IsType<DisconnectedNodeProjectLayout>(
            DisconnectedNodeProjectLayouter.Create(graph, nodes, Settings()));

        Assert.Equal(90, result.Nodes["detached-4"].Rect.Width);
        Assert.True(result.Nodes["detached-3"].Rect.X > result.Nodes["detached-0"].Rect.X);
        Assert.Equal(DisconnectedNodeProjectLayouter.ProjectId, result.Nodes["detached-4"].Node.ProjectId);
        Assert.Equal("Disconnected Nodes", result.Project.Name);
    }

    [Fact]
    public void Linked_detached_component_is_not_classified_as_disconnected()
    {
        var types = new[] { Type("a"), Type("b"), Type("alone") };
        var graph = RenderGraph.From(new DiagramModel(
            new[] { new ProjectContainer("project", "Project", types) }, Array.Empty<ExternalDependencyNode>(),
            new[] { new DependencyEdge("ab", "a", "b", "Dependency") }));
        var nodes = graph.Nodes.ToDictionary(node => node.Id,
            node => new NodeLayout(node, new Rect(node.Order * 60, 0, 40, 40), 0, false), StringComparer.Ordinal);

        var result = Assert.IsType<DisconnectedNodeProjectLayout>(
            DisconnectedNodeProjectLayouter.Create(graph, nodes, Settings()));

        Assert.Equal(new[] { "alone" }, result.NodeIds);
    }

    private static (RenderGraph Graph, Dictionary<string, NodeLayout> Nodes) Fixture(int count)
    {
        var types = Enumerable.Range(0, count).Select(index => Type($"detached-{index}")).ToArray();
        var graph = RenderGraph.From(new DiagramModel(
            new[] { new ProjectContainer("project", "Project", types) },
            Array.Empty<ExternalDependencyNode>(), Array.Empty<DependencyEdge>()));
        return (graph, graph.Nodes.ToDictionary(node => node.Id,
            node => new NodeLayout(node, new Rect(0, 0, 40 + node.Order, 40), 0, true), StringComparer.Ordinal));
    }

    private static DiagramSettings Settings() => DiagramSettings.CreateDefault();
    private static TypeNode Type(string id) => new(id, "project", id, id, "Class");
}
