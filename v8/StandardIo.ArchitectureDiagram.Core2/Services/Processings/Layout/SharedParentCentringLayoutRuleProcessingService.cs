// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core2.Models;
namespace StandardIo.ArchitectureDiagram.Core2.Services.Processings.Layout;
internal sealed class SharedParentCentringLayoutRuleProcessingService : ILayoutRuleProcessingService
{
    public void ApplyRule(RenderModel renderModel)
    {
        foreach (var project in renderModel.Projects)
        foreach (var groupIds in LayoutGraph.SharedGroups(project).Select(group => group.Select(node => node.Id).ToArray()).ToArray())
        {
            var group = groupIds.Select(id => project.Nodes.Single(node => node.Id == id)).ToArray();
            var parents = LayoutGraph.Parents(project, group[0].Id);
            double delta = LayoutGraph.Midpoint(group) - LayoutGraph.Midpoint(parents);
            var branches = parents.Select(parent => LayoutGraph.OwnedChainRoot(project, parent)).DistinctBy(root => root.Id).ToArray();
            // Translate each group together, preserving its internal spacing. Distribute the
            // correction by branch count so its total squared movement is minimised.
            double parentShare = (double)group.Length / (branches.Length + group.Length);
            foreach (var branch in branches)
                LayoutGraph.MoveSubtree(project, branch.Id, delta * parentShare);
            foreach (var child in group)
                LayoutGraph.MoveSubtree(project, child.Id, -delta * (1 - parentShare));
        }
    }
}
