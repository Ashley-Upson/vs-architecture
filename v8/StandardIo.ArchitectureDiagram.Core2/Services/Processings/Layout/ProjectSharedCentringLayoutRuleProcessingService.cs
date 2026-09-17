using System;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core2.Models;
namespace StandardIo.ArchitectureDiagram.Core2.Services.Processings.Layout;
internal sealed class ProjectSharedCentringLayoutRuleProcessingService : ArchitectureLayoutRuleProcessingService
{
    protected override void ApplyArchitectureRule(RenderModel model) => ProjectLayout.Apply(model,containers=>
    {
        var graph=containers.Projects[0];
        foreach(var group in LayoutGraph.SharedGroups(graph))
        {
            double delta=LayoutGraph.Midpoint(LayoutGraph.Parents(graph,group[0].Id))-LayoutGraph.Midpoint(group);
            var ids=group.Select(n=>n.Id).ToHashSet();
            if(group.Any(n=>graph.Nodes.Any(peer=>!ids.Contains(peer.Id)&&peer.Y<n.Y+n.Height&&peer.Y+peer.Height>n.Y &&
                n.X+delta<peer.X+peer.Width+model.Configuration.Architecture.ProjectSpacing && n.X+n.Width+delta+model.Configuration.Architecture.ProjectSpacing>peer.X))) continue;
            if(Math.Abs(delta)<LayoutGraph.Tolerance) continue;
            foreach(var child in group) LayoutGraph.Move(graph,child.Id,delta);
        }
    });
}
