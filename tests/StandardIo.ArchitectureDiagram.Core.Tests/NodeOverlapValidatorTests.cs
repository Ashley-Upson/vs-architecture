using StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core.Tests;

public sealed class NodeOverlapValidatorTests
{
    [Fact]
    public void Interior_node_overlap_is_hard_but_exact_boundary_touch_is_allowed()
    {
        var nodes = new Dictionary<string, NodeLayout>
        {
            ["a"] = Node("a", new Rect(0, 0, 100, 50)),
            ["b"] = Node("b", new Rect(90, 10, 100, 50)),
            ["touch"] = Node("touch", new Rect(190, 10, 100, 50))
        };

        var findings = NodeOverlapValidator.Validate(nodes, new Dictionary<string, ProjectLabelGeometry>());

        var finding = Assert.Single(findings);
        Assert.Equal("NodeOverlap", finding.Category);
        Assert.True(finding.IsStrictlyEnforced);
        Assert.Equal("a", finding.LogicalRouteId);
        Assert.Equal("b", finding.OtherNodeId);
    }

    [Fact]
    public void External_nodes_and_disconnected_nodes_use_the_same_overlap_rule()
    {
        var nodes = new Dictionary<string, NodeLayout>
        {
            ["internal"] = Node("internal", new Rect(20, 20, 80, 40)),
            ["external"] = Node("external", new Rect(40, 30, 80, 40), external: true)
        };

        Assert.Single(NodeOverlapValidator.Validate(nodes, new Dictionary<string, ProjectLabelGeometry>()));
    }

    [Fact]
    public void Protected_project_label_overlap_is_hard_without_treating_container_membership_as_overlap()
    {
        var nodes = new Dictionary<string, NodeLayout>
        {
            ["inside"] = Node("inside", new Rect(100, 100, 80, 40))
        };
        var labels = new Dictionary<string, ProjectLabelGeometry>
        {
            ["project"] = new("project", new Rect(0, 0, 500, 500),
                new Rect(90, 90, 200, 30), new Rect(80, 80, 220, 50))
        };

        var finding = Assert.Single(NodeOverlapValidator.Validate(nodes, labels));

        Assert.Contains("project label", finding.Description);
        Assert.Equal("project", finding.OtherNodeId);
    }

    private static NodeLayout Node(string id, Rect rect, bool external = false) => new(
        new RenderNode(id, "project", id, "Fixture." + id, "Class", external, "", 0, [], [], 0),
        rect, 0, true);
}
