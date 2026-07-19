using StandardIo.ArchitectureDiagram.Core.Models;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core.Tests;

public sealed class CanonicalTopologyFamilySelectorTests
{
    [Fact]
    public void Selects_every_supported_family_once_and_is_input_order_independent()
    {
        var diagram = Fixture();
        var forward = Select(diagram);
        var reverse = Select(diagram with { Edges = diagram.Edges.Reverse().ToArray() });

        Assert.Empty(forward.Rejections);
        Assert.Equal(diagram.Edges.Count, forward.Plans.Count);
        Assert.Equal(
            forward.Plans.OrderBy(item => item.Key).Select(item => (item.Key, item.Value.Family)),
            reverse.Plans.OrderBy(item => item.Key).Select(item => (item.Key, item.Value.Family)));
        Assert.Equal(Enum.GetValues<CanonicalTopologyFamily>().OrderBy(item => item),
            forward.Plans.Values.Select(item => item.Family).Distinct().OrderBy(item => item));
        Assert.All(forward.Plans.Values, plan =>
        {
            Assert.Equal(CanonicalTerminal.SourceBottom, plan.SourceTerminal);
            Assert.Equal(CanonicalTerminal.TargetTop, plan.TargetTerminal);
            Assert.NotEmpty(plan.Segments);
            Assert.Equal(plan.Segments.Select(item => item.Role), plan.OrderedTransitions);
        });
    }

    [Fact]
    public void Self_loop_is_rejected_without_a_partial_plan()
    {
        var diagram = new DiagramModel(
            new[] { new ProjectContainer("p", "P", new[] { Node("source", "p") }) },
            Array.Empty<ExternalDependencyNode>(),
            new[] { new DependencyEdge("self", "source", "source", "Dependency") });

        var result = Select(diagram);

        Assert.Empty(result.Plans);
        Assert.Equal(new[] { "UnsupportedSelfLoop:self" }, result.Rejections);
    }

    private static CanonicalTopologySelection Select(DiagramModel diagram)
    {
        var graph = RenderGraph.From(diagram, DiagramSettings.CreateDefault());
        var depths = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["source"] = 1, ["adjacent"] = 2, ["long"] = 4, ["same"] = 1,
            ["up"] = 0, ["other"] = 2, ["external"] = 2
        };
        var nodes = graph.Nodes.ToDictionary(node => node.Id, node =>
            new NodeLayout(node, new Rect(node.Order * 100, depths.TryGetValue(node.Name, out var depth) ? depth * 100 : 0, 80, 40),
                depths.TryGetValue(node.Name, out depth) ? depth : 0, node.IsExternal), StringComparer.Ordinal);
        return CanonicalTopologyFamilySelector.Select(graph, nodes, new LayoutRevision(0));
    }

    private static DiagramModel Fixture() => new(
        new[]
        {
            new ProjectContainer("p", "P", new[]
            {
                Node("source", "p"), Node("adjacent", "p"), Node("long", "p"),
                Node("same", "p"), Node("up", "p")
            }),
            new ProjectContainer("q", "Q", new[] { Node("other", "q") })
        },
        new[] { new ExternalDependencyNode("external", "External", "External", "external", "External", "[External]") },
        new[]
        {
            Edge("adjacent", "source", "adjacent"), Edge("long", "source", "long"),
            Edge("same", "source", "same"), Edge("up", "source", "up"),
            Edge("internal-external", "source", "external"), Edge("external-internal", "external", "source"),
            Edge("cross-project", "source", "other")
        });

    private static TypeNode Node(string id, string project) => new(id, project, id, id, "Class");
    private static DependencyEdge Edge(string id, string source, string target) => new(id, source, target, "Dependency");
}
