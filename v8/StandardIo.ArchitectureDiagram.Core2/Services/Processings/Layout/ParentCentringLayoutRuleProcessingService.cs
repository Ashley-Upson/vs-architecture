// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core2.Models;
namespace StandardIo.ArchitectureDiagram.Core2.Services.Processings.Layout;
internal sealed class ParentCentringLayoutRuleProcessingService : ILayoutRuleProcessingService
{
    public void ApplyRule(RenderModel renderModel)
    {
        foreach (var project in renderModel.Projects)
        foreach (var parent in project.Nodes.OrderByDescending(node => node.Y).ToArray())
        {
            var children = LayoutGraph.OwnedChildren(project, parent.Id);
            if (children.Length == 0) continue;
            LayoutGraph.Move(project, parent.Id, LayoutGraph.Midpoint(children) - LayoutGraph.Centre(parent));
        }
    }
}
