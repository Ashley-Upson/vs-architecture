using System;
using System.Collections.Generic;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core2.Models;

namespace StandardIo.ArchitectureDiagram.Core2.Services.Processings.Layout;

internal sealed class VerticalPassageLayoutRuleProcessingService : ArchitectureLayoutRuleProcessingService
{
    protected override void ApplyArchitectureRule(RenderModel model)
    {
        if (model.IsProjectGraph || model.DiagramType != DiagramTypes.Architecture) return;
        double clearance=model.Configuration.HorizontalOffset;
        double spacing=model.Configuration.Architecture.NodeSpacing;
        foreach(var project in model.Projects)
        {
            var nodes=project.Nodes.ToDictionary(n=>n.Id);
            var outgoing=project.Connections.Concat(model.CrossProjectConnections.Where(e=>nodes.ContainsKey(e.SourceId)))
                .GroupBy(e=>e.SourceId).ToArray();
            var branchIds=project.Nodes.ToDictionary(n=>n.Id,n=>LayoutGraph.OwnedBranch(project,n.Id).Select(child=>child.Id).ToHashSet());
            var siblingGroups=project.Nodes.Select(n=>LayoutGraph.OwnedChildren(project,n.Id).Select(c=>c.Id).ToArray()).Where(g=>g.Length>1).ToArray();
            RenderNode Current(string id)=>project.Nodes[Array.FindIndex(project.Nodes,n=>n.Id==id)];
            foreach(string id in project.Nodes.OrderBy(n=>n.Y).ThenBy(n=>n.X).Select(n=>n.Id).ToArray())
            {
                var node=Current(id);
                // Reserve the central exit band for deeper dependencies. Near-layer
                // links use the remaining exits, outside these vertical passages.
                var passages=outgoing.Where(g=>Current(g.Key).Y+Current(g.Key).Height<node.Y)
                    .Select(g=>
                    {
                        var source=Current(g.Key);
                        int count=g.Count(e=>!nodes.ContainsKey(e.TargetId)||Current(e.TargetId).Y>node.Y);
                        double radius=g.Count()==1?0:Math.Min(source.Width/2-5,count*clearance);
                        return (Source:source.Id,Left:LayoutGraph.Centre(source)-radius-clearance,
                            Right:LayoutGraph.Centre(source)+radius+clearance,Count:count);
                    }).Where(p=>p.Count>0).ToArray();
                if(!passages.Any(p=>node.X<p.Right && node.X+node.Width>p.Left)) continue;
                var moving=branchIds[id];
                var branch=moving.Select(Current).ToArray();
                var forbidden=new List<(double Left,double Right)>();
                foreach(var member in branch)
                {
                    foreach(var passage in passages.Where(p=>!moving.Contains(p.Source)))
                        forbidden.Add((passage.Left-member.X-member.Width,passage.Right-member.X));
                    foreach(var peer in project.Nodes.Where(n=>!moving.Contains(n.Id)&&n.Y<member.Y+member.Height&&n.Y+n.Height>member.Y))
                        forbidden.Add((peer.X-spacing-member.X-member.Width,peer.X+peer.Width+spacing-member.X));
                }
                bool KeepsSiblingOrder(double candidate)
                {
                    foreach(string member in moving) LayoutGraph.Move(project,member,candidate);
                    // A translation cannot change spacing inside wholly moved groups,
                    // or repair unrelated groups that a later rule will arrange.
                    bool valid=siblingGroups.Where(group=>group.Any(moving.Contains) && !group.All(moving.Contains)).All(group=>
                    {
                        var ordered=group.OrderBy(child=>Current(child).X).ThenBy(child=>child,StringComparer.Ordinal).ToArray();
                        return ordered.Zip(ordered.Skip(1)).Where(pair=>moving.Contains(pair.First) || moving.Contains(pair.Second)).All(pair=>LayoutGraph.BranchClearance(project,pair.First,pair.Second,true)>=spacing-LayoutGraph.Tolerance);
                    });
                    foreach(string member in moving) LayoutGraph.Move(project,member,-candidate);
                    return valid;
                }
                double delta=forbidden.SelectMany(f=>new[]{f.Left,f.Right}).Append(0)
                    .Where(x=>forbidden.All(f=>x<=f.Left+0.001||x>=f.Right-0.001))
                    .OrderBy(Math.Abs).ThenBy(x=>x).First(KeepsSiblingOrder);
                foreach(string child in moving)
                {
                    foreach(var parent in LayoutGraph.Parents(project,child).Where(p=>!moving.Contains(p.Id)))
                        model.PassageOffsetParents.Add(parent.Id);
                    LayoutGraph.Move(project,child,delta);
                }
            }
        }
    }
}
