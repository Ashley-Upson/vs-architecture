using System;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core2.Models;
namespace StandardIo.ArchitectureDiagram.Core2.Services.Processings.Layout;
internal static class LayoutQuality
{
    internal static (int Violations, int Blocked, int Crossings, double Length, double Width) Measure(RenderModel model, RenderProject project)
    {
        var nodes=project.Nodes.ToDictionary(n=>n.Id);
        var edges=project.Connections.Where(e=>nodes[e.TargetId].Y>nodes[e.SourceId].Y).ToArray();
        int crossings=edges.SelectMany((a,i)=>edges.Skip(i+1).Where(b=>a.SourceId!=b.SourceId && a.TargetId!=b.TargetId &&
            nodes[a.SourceId].Y==nodes[b.SourceId].Y && nodes[a.TargetId].Y==nodes[b.TargetId].Y)
            .Select(b=>(LayoutGraph.Centre(nodes[a.SourceId])-LayoutGraph.Centre(nodes[b.SourceId]))*(LayoutGraph.Centre(nodes[a.TargetId])-LayoutGraph.Centre(nodes[b.TargetId]))<0)).Count(x=>x);
        int blocked=model.PassageOffsetParents.Count>0?LayoutPassages.Blocked(model,project).Count:0;
        double spacing=model.IsProjectGraph?model.Configuration.Architecture.ProjectSpacing:model.Configuration.Architecture.NodeSpacing;
        int violations=project.Nodes.GroupBy(n=>n.Y).Sum(row=>
        {
            var ordered=row.OrderBy(n=>n.X).ToArray();
            return ordered.Zip(ordered.Skip(1)).Count(pair=>pair.Second.X-pair.First.X-pair.First.Width<spacing-LayoutGraph.Tolerance);
        });
        violations+=LayoutGraph.BranchGroups(project).Sum(group=>
        {
            var ordered=group.OrderBy(n=>n.X).ToArray();
            return ordered.Zip(ordered.Skip(1)).Count(pair=>LayoutGraph.BranchClearance(project,pair.First.Id,pair.Second.Id,true,respectBranchOrder:false)<spacing-LayoutGraph.Tolerance);
        });
        return (violations,blocked,crossings,Math.Round(edges.Sum(e=>Math.Abs(LayoutGraph.Centre(nodes[e.SourceId])-LayoutGraph.Centre(nodes[e.TargetId]))),4),
            Math.Round(project.Nodes.Select(n=>n.X+n.Width).DefaultIfEmpty(0).Max()-project.Nodes.Select(n=>n.X).DefaultIfEmpty(0).Min(),4));
    }
}
