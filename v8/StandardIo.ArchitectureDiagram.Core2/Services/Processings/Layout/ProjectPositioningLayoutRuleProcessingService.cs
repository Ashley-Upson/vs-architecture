// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core2.Models;
namespace StandardIo.ArchitectureDiagram.Core2.Services.Processings.Layout;
internal sealed class ProjectPositioningLayoutRuleProcessingService(
    DepthLayoutRuleProcessingService depthRule,
    ParentCentringLayoutRuleProcessingService parentRule,
    SharedParentCentringLayoutRuleProcessingService sharedParentRule,
    BranchSpacingLayoutRuleProcessingService spacingRule) : ILayoutRuleProcessingService
{
    public void ApplyRule(RenderModel renderModel)
    {
        if (renderModel.CrossProjectConnections.Length == 0) return;
        var graph = LayoutGraph.ProjectGraph(renderModel);
        var model = new RenderModel(graph.Width, graph.Height, new[] { graph }) { Configuration = renderModel.Configuration, IsProjectGraph = true, SharedParentLayoutInitialized = renderModel.ProjectLayoutInitialized };
        depthRule.ApplyRule(model);
        // Project rows need their actual heights, rather than the fixed node height.
        double top = 40;
        foreach (var row in graph.Nodes.GroupBy(node => node.Y).OrderBy(group => group.Key).ToArray())
        {
            double left = 40;
            foreach (var node in row)
            {
                int index = Array.FindIndex(graph.Nodes, candidate => candidate.Id == node.Id);
                graph.Nodes[index] = node with { Y = top, X = renderModel.ProjectLayoutInitialized ? node.X : left };
                left += node.Width + renderModel.Configuration.Architecture.ProjectSpacing;
            }
            top += row.Max(node => node.Height) + renderModel.Configuration.Architecture.ProjectSpacing;
        }
        renderModel.ProjectLayoutInitialized = true;
        sharedParentRule.ApplyRule(model);
        spacingRule.ApplyRule(model);
        parentRule.ApplyRule(model);
        double shift = 40 - graph.Nodes.Min(node => node.X);
        for (int index = 0; index < renderModel.Projects.Length; index++)
        {
            var node = graph.Nodes.Single(node => node.Id == renderModel.Projects[index].Id);
            renderModel.Projects[index] = renderModel.Projects[index] with { X = node.X + shift, Y = node.Y };
        }
        renderModel.Width = renderModel.Projects.Max(project => project.X + project.Width + 40);
        renderModel.Height = renderModel.Projects.Max(project => project.Y + project.Height + 40);
    }
}
