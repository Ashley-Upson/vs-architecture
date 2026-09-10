// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core2.Models;
namespace StandardIo.ArchitectureDiagram.Core2.Services.Processings.Layout;
internal sealed class CrossProjectClearanceLayoutRuleProcessingService : ILayoutRuleProcessingService
{
    public void ApplyRule(RenderModel renderModel)
    {
        foreach (var project in renderModel.Projects)
        foreach (var sourceId in renderModel.CrossProjectConnections.Select(edge => edge.SourceId).Distinct())
        {
            var source = project.Nodes.FirstOrDefault(node => node.Id == sourceId);
            if (source is null) continue;
            int destinations = renderModel.CrossProjectConnections.Where(edge => edge.SourceId == sourceId).Select(edge => edge.TargetId).Distinct().Count();
            double clearance = 10 + Math.Min(source.Width / 2 - 5, destinations * renderModel.Configuration.HorizontalOffset);
            double centre = LayoutGraph.Centre(source);
            var obstacles = project.Nodes.Where(node => node.Y >= source.Y + source.Height).ToArray();
            if (!obstacles.Any(node => centre > node.X - clearance && centre < node.X + node.Width + clearance)) continue;
            double position = obstacles.SelectMany(node => new[] { node.X - clearance, node.X + node.Width + clearance })
                .Where(x => obstacles.All(node => x <= node.X - clearance || x >= node.X + node.Width + clearance))
                .OrderBy(x => Math.Abs(x - centre)).ThenBy(x => x).First();
            LayoutGraph.MoveSubtree(project, LayoutGraph.OwnedChainRoot(project, source).Id, position - centre);
        }
    }
}
