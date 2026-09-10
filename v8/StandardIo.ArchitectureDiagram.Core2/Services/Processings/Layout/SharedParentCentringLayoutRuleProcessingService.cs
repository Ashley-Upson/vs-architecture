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
        foreach (var group in LayoutGraph.SharedGroups(project).ToArray())
        {
            var parents = LayoutGraph.Parents(project, group[0].Id);
            double delta = LayoutGraph.Midpoint(group) - LayoutGraph.Midpoint(parents);
            var branches = parents.Select(parent => LayoutGraph.OwnedChainRoot(project, parent)).DistinctBy(root => root.Id);
            foreach (var branch in branches)
                LayoutGraph.MoveSubtree(project, branch.Id, delta / 2);
            foreach (var child in group)
                LayoutGraph.MoveSubtree(project, child.Id, -delta / 2);
        }
    }
}
