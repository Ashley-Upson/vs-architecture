using StandardIo.ArchitectureDiagram.Core.Models;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core.Tests;

public sealed class ProjectTerminalAllocatorTests
{
    [Fact]
    public void Long_arrival_and_unrelated_adjacent_departure_share_a_boundary_without_parallel_collision()
    {
        var diagram = new DiagramModel(
            new[] { new ProjectContainer("project", "Project", new[]
            {
                Node("long-source"), Node("adjacent-source"), Node("long-target"), Node("adjacent-target")
            }) },
            Array.Empty<ExternalDependencyNode>(),
            new[]
            {
                Edge("long", "long-source", "long-target"),
                Edge("adjacent", "adjacent-source", "adjacent-target")
            });
        var graph = RenderGraph.From(diagram);
        var nodes = new Dictionary<string, NodeLayout>(StringComparer.Ordinal)
        {
            ["long-source"] = Layout(graph, "long-source", new Rect(500, 0, 120, 60), 0),
            ["adjacent-source"] = Layout(graph, "adjacent-source", new Rect(100, 200, 120, 60), 1),
            ["long-target"] = Layout(graph, "long-target", new Rect(102, 400, 120, 60), 2),
            ["adjacent-target"] = Layout(graph, "adjacent-target", new Rect(400, 400, 120, 60), 2)
        };
        var settings = DiagramSettings.CreateDefault();

        var forward = ProjectTerminalAllocator.Allocate(graph, nodes, settings);
        var reverseGraph = RenderGraph.From(new DiagramModel(
            diagram.Projects, diagram.ExternalDependencies, diagram.Edges.Reverse().ToArray(), diagram.Metadata));
        var reverseNodes = nodes.ToDictionary(item => item.Key,
            item => item.Value with { Node = reverseGraph.Nodes.Single(node => node.Id == item.Key) }, StringComparer.Ordinal);
        var reverse = ProjectTerminalAllocator.Allocate(reverseGraph, reverseNodes, settings);

        Assert.True(Math.Abs(forward["long"].TargetPoint.X - forward["adjacent"].SourcePoint.X) >=
            settings.Layout.ParallelLaneSpacing);
        Assert.Equal(nodes["adjacent-source"].Rect.Bottom, forward["adjacent"].SourcePoint.Y);
        Assert.Equal(nodes["long-target"].Rect.Y, forward["long"].TargetPoint.Y);
        Assert.Equal(forward.OrderBy(item => item.Key).Select(item => (item.Key, item.Value.SourcePoint, item.Value.TargetPoint)),
            reverse.OrderBy(item => item.Key).Select(item => (item.Key, item.Value.SourcePoint, item.Value.TargetPoint)));
    }

    [Fact]
    public void Same_layer_return_arrival_is_separated_from_unrelated_adjacent_departure()
    {
        var diagram = new DiagramModel(
            new[] { new ProjectContainer("project", "Project", new[]
            {
                Node("adjacent-source"), Node("adjacent-target"), Node("return-source"), Node("return-target")
            }) },
            Array.Empty<ExternalDependencyNode>(),
            new[]
            {
                Edge("adjacent", "adjacent-source", "adjacent-target"),
                Edge("return", "return-source", "return-target")
            });
        var graph = RenderGraph.From(diagram);
        var nodes = new Dictionary<string, NodeLayout>(StringComparer.Ordinal)
        {
            ["adjacent-source"] = Layout(graph, "adjacent-source", new Rect(100, 0, 120, 60), 0),
            ["adjacent-target"] = Layout(graph, "adjacent-target", new Rect(400, 200, 120, 60), 1),
            ["return-source"] = Layout(graph, "return-source", new Rect(500, 200, 120, 60), 1),
            ["return-target"] = Layout(graph, "return-target", new Rect(102, 200, 120, 60), 1)
        };
        var settings = DiagramSettings.CreateDefault();

        var allocated = ProjectTerminalAllocator.Allocate(graph, nodes, settings);

        Assert.True(Math.Abs(allocated["return"].TargetPoint.X - allocated["adjacent"].SourcePoint.X) >=
            settings.Layout.ParallelLaneSpacing);
        Assert.Equal(nodes["adjacent-source"].Rect.Bottom, allocated["adjacent"].SourcePoint.Y);
        Assert.Equal(nodes["return-target"].Rect.Y, allocated["return"].TargetPoint.Y);
    }

    private static TypeNode Node(string id) => new(id, "project", id, $"Fixture.{id}", "Class");
    private static DependencyEdge Edge(string id, string source, string target) => new(id, source, target, "Dependency");
    private static NodeLayout Layout(RenderGraph graph, string id, Rect rect, int depth) =>
        new(graph.Nodes.Single(node => node.Id == id), rect, depth, false);
}
