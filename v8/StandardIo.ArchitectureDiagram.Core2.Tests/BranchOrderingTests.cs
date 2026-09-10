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
