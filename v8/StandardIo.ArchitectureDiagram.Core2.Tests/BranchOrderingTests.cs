// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System.Linq;
using StandardIo.ArchitectureDiagram.Core2.Models;
using StandardIo.ArchitectureDiagram.Core2.Services.Processings.Layout;
using Xunit;
namespace StandardIo.ArchitectureDiagram.Core2.Tests;
public sealed class BranchOrderingTests
{
    [Fact]
    public void ShouldFinishDescendantSpacingBeforePlacingNeighbouringTrees()
    {
        // Given: many consumers each own a deep branch and share another dependency.
        var nodes = Enumerable.Range(0, 40).SelectMany(index => new[]
        {
            new RenderNode($"parent-{index}", $"Parent{index}", "", "#123456", 0, 0, 180, 60, []),
            new RenderNode($"child-{index}", $"Child{index}", "", "#123456", 0, 160, 180, 60, []),
            new RenderNode($"shared-{index}", $"Shared{index}", "", "#123456", 0, 160, 180, 60, [])
        }).ToArray();
        var edges = Enumerable.Range(0, 40).SelectMany(index => new[]
        {
            ($"parent-{index}", $"child-{index}"),
            ($"parent-{index}", $"shared-{index}"),
            ($"parent-{(index + 1) % 40}", $"shared-{index}")
        }).Select((pair, index) => new RenderConnection($"edge-{index}", pair.Item1, pair.Item2, nodes.Single(node => node.Id == pair.Item1).TypeName, nodes.Single(node => node.Id == pair.Item2).TypeName, false, [])).ToArray();
        var model = new RenderModel(0, 0, [new RenderProject("project", "Project", 0, 0, 100, 100, nodes, edges)]);
        model.Configuration.MaxLayoutIterations = 5;

        // When: use the complete rule pipeline with a small bounded pass budget.
        TestServices.Get<StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Rendering.IProjectModelLayoutService>().Layout(model);

        // Then: each owned child remains centred and every row has the configured clearance.
        foreach (var parent in model.Projects[0].Nodes.Where(node => node.Id.StartsWith("parent-")))
        {
            var child = model.Projects[0].Nodes.Single(node => node.Id == parent.Id.Replace("parent-", "child-"));
            Assert.Equal(parent.X, child.X);
        }
        foreach (var row in model.Projects[0].Nodes.GroupBy(node => node.Y))
        {
            var ordered = row.OrderBy(node => node.X).ToArray();
            for (int index = 1; index < ordered.Length; index++)
                Assert.True(ordered[index].X - ordered[index - 1].X - ordered[index - 1].Width >= 60 - 0.01);
        }
        Assert.Equal(120, model.Projects[0].Nodes.Length);
        Assert.Equal(120, model.Projects[0].Connections.Length);
    }

    [Fact]
    public void ShouldOrderAnInternalSharedBranchWithItsOwningTree()
    {
        // Given: A owns a diamond ending at Shared; B owns an independent chain.
        // Shared initially sits beyond B, but still belongs to A's reserved space.
        RenderNode Node(string id, double x, double y) => new(id, id, id, "#123456", x, y, 180, 60, []);
        RenderConnection Edge(string source, string target) => new(source + target, source, target, source, target, false, []);
        var project = new RenderProject("project", "Project", 0, 0, 2000, 600,
            [Node("A", 0, 0), Node("P", 0, 160), Node("Q", 240, 160), Node("Shared", 1000, 320),
             Node("B", 600, 0), Node("T", 600, 160), Node("R", 600, 320)],
            [Edge("A", "P"), Edge("A", "Q"), Edge("P", "Shared"), Edge("Q", "Shared"), Edge("B", "T"), Edge("T", "R")]);

        // When: space all overlapping branch groups in one rule pass.
        new BranchSpacingLayoutRuleProcessingService().ApplyRule(new RenderModel(2000, 600, [project]));

        // Then: ordering the internal shared branch cannot reverse its owner's order against B.
        var left = LayoutGraph.OwnedBranch(project, "A");
        var right = LayoutGraph.OwnedBranch(project, "B");
        Assert.True(right.Min(node => node.X) - left.Max(node => node.X + node.Width) >= 60 - 0.01);
    }
}
