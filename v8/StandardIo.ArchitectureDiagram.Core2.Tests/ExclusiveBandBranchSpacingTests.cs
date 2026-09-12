using System;
using StandardIo.ArchitectureDiagram.Core2.Models;
using StandardIo.ArchitectureDiagram.Core2.Services.Processings.Layout;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core2.Tests;

public sealed class ExclusiveBandBranchSpacingTests
{
    [Fact]
    public void ShouldKeepSiblingBranchBoundsSeparateEvenOnDifferentCategoryRows()
    {
        // Given: exclusive bands can put sibling leaves on distinct rows.
        var nodes = new[] { new RenderNode("p", "Parent", "Parent", "orange", 40, 0, 210, 60, []),
            new RenderNode("a", "First", "First", "purple", 40, 160, 210, 60, []),
            new RenderNode("b", "Second", "Second", "green", 40, 320, 210, 60, []) };
        var project = new RenderProject("p", "Project", 0, 0, 600, 500, nodes,
            [new("pa", "p", "a", "Parent", "First", false, []), new("pb", "p", "b", "Parent", "Second", false, [])]);
        var model = new RenderModel(600, 500, [project]);
        // When
        new BranchSpacingLayoutRuleProcessingService().ApplyRule(model);
        new LayoutCleanupRuleProcessingService().ApplyRule(model);
        // Then: both placement and cleanup preserve the sibling branch constraint.
        Assert.True(Math.Abs(project.Nodes[1].X - project.Nodes[2].X) >= 270);
    }
}
