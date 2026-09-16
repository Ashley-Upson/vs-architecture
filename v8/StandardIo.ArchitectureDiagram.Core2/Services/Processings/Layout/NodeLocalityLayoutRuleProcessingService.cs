using System;
using System.Collections.Generic;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core2.Models;
namespace StandardIo.ArchitectureDiagram.Core2.Services.Processings.Layout;
internal sealed class NodeLocalityLayoutRuleProcessingService : ILayoutRuleProcessingService
{
    public void ApplyRule(RenderModel model)
    {
        if (model.IsProjectGraph || model.DiagramType != DiagramTypes.Architecture) return;
        foreach (var project in model.Projects) Improve(project, model);
    }

    private static void Improve(RenderProject project, RenderModel model)
    {
        bool sharedNodes = LayoutGraph.HasSharedNodes(project);
        double spacing = model.Configuration.Architecture.NodeSpacing;
        var indices = project.Nodes.Select((node,index)=>(node.Id,index)).ToDictionary(p=>p.Id,p=>p.index);
        RenderNode Node(string id) => project.Nodes[indices[id]];
        var groups = LayoutGraph.BranchGroups(project).Select(g=>g.Select(n=>n.Id).ToArray()).ToArray();
        var siblings = project.Nodes.Select(n=>LayoutGraph.OwnedChildren(project,n.Id).Select(c=>c.Id).ToArray())
            .Where(group=>group.Length>1).ToArray();
        var edges = project.Connections.Where(e=>Node(e.TargetId).Y>Node(e.SourceId).Y).ToArray();
        var passages=edges.Concat(model.CrossProjectConnections.Where(e=>indices.ContainsKey(e.SourceId)))
            .GroupBy(e=>e.SourceId).Select(g=>(Source:g.Key,
            Bottom:g.Max(e=>indices.ContainsKey(e.TargetId)?Node(e.TargetId).Y:double.PositiveInfinity),
            Radius:Math.Min(Node(g.Key).Width/2-5,g.Count()*model.Configuration.HorizontalOffset))).ToArray();
        HashSet<(string Source,string Obstacle)> BlockedPassages() => passages.SelectMany(p=>
            project.Nodes.Where(n=>n.Y>Node(p.Source).Y+Node(p.Source).Height && n.Y<p.Bottom &&
                n.X<LayoutGraph.Centre(Node(p.Source))+p.Radius && n.X+n.Width>LayoutGraph.Centre(Node(p.Source))-p.Radius)
                .Select(n=>(p.Source,n.Id))).ToHashSet();
        var pairs = edges.SelectMany((a,i)=>edges.Skip(i+1)
            .Where(b=>a.SourceId!=b.SourceId && a.TargetId!=b.TargetId &&
                Node(a.SourceId).Y==Node(b.SourceId).Y && Node(a.TargetId).Y==Node(b.TargetId).Y)
            .Select(b=>(a,b))).ToArray();
        (int Crossings,double Length) Score() =>
            (pairs.Count(p=>(LayoutGraph.Centre(Node(p.a.SourceId))-LayoutGraph.Centre(Node(p.b.SourceId)))*
                (LayoutGraph.Centre(Node(p.a.TargetId))-LayoutGraph.Centre(Node(p.b.TargetId)))<0),
             edges.Sum(e=>Math.Abs(LayoutGraph.Centre(Node(e.SourceId))-LayoutGraph.Centre(Node(e.TargetId)))));
        bool Valid()
        {
            foreach (var row in project.Nodes.GroupBy(n=>n.Y))
            {
                var nodes=row.OrderBy(n=>n.X).ToArray();
                for(int i=1;i<nodes.Length;i++)
                    if(nodes[i].X-nodes[i-1].X-nodes[i-1].Width<spacing-LayoutGraph.Tolerance) return false;
            }
            foreach(var group in groups)
            {
                var roots=group.OrderBy(id=>Node(id).X).ThenBy(id=>id,StringComparer.Ordinal).ToArray();
                for(int i=1;i<roots.Length;i++)
                    if(LayoutGraph.BranchClearance(project,roots[i-1],roots[i],sharedNodes,respectBranchOrder:false)<spacing-LayoutGraph.Tolerance) return false;
            }
            return project.Nodes.All(n=>double.IsFinite(n.X));
        }
        // A proposal moves a branch as a unit and recursively makes room in occupied
        // rows. The transaction is discarded if a shared branch prevents the move.
        bool Move(string id,double delta,HashSet<string> moving)
        {
            if(Math.Abs(delta)<LayoutGraph.Tolerance) return true;
            var branch=LayoutGraph.OwnedBranch(project,id).Select(n=>n.Id).ToHashSet();
            if(branch.Overlaps(moving)) return false;
            moving.UnionWith(branch);
            foreach(string member in branch) LayoutGraph.Move(project,member,delta);
            foreach(string member in branch)
            {
                var node=Node(member);
                foreach(string peerId in project.Nodes.Where(n=>!branch.Contains(n.Id) && n.Y==node.Y)
                    .OrderBy(n=>delta>0?n.X:-n.X).Select(n=>n.Id).ToArray())
                {
                    var peer=Node(peerId);
                    if(node.X+node.Width+spacing<=peer.X+LayoutGraph.Tolerance || peer.X+peer.Width+spacing<=node.X+LayoutGraph.Tolerance) continue;
                    var consumers=LayoutGraph.Parents(project,peerId);
                    double preferred=consumers.Length>0?LayoutGraph.Midpoint(consumers):LayoutGraph.Centre(peer);
                    double push=preferred>=LayoutGraph.Centre(node)?node.X+node.Width+spacing-peer.X:node.X-spacing-peer.Width-peer.X;
                    if(!Move(peerId,push,moving)) return false;
                }
            }
            moving.ExceptWith(branch);
            return true;
        }
        bool TryMove(string id,double preferred)
        {
            var before=project.Nodes.ToArray();
            var score=Score();
            var blocked= model.PassageOffsetParents.Count>0 ? BlockedPassages() : null;
            double width=before.Max(n=>n.X+n.Width)-before.Min(n=>n.X);
            bool moved=Move(id,preferred-Node(id).X,[]);
            // Re-evaluate ancestors after moving children, deepest first. This preserves
            // centring over the child set's outside edges rather than its mean centre.
            foreach(string parentId in project.Nodes.OrderByDescending(n=>n.Y).Select(n=>n.Id).ToArray())
            {
                if(!moved) break;
                var children=LayoutGraph.OwnedChildren(project,parentId);
                if(children.Length>0 && !model.PassageOffsetParents.Contains(parentId))
                    LayoutGraph.Move(project,parentId,LayoutGraph.Midpoint(children)-LayoutGraph.Centre(Node(parentId)));
            }
            var candidate=Score();
            bool better=candidate.Crossings<score.Crossings || candidate.Crossings==score.Crossings && candidate.Length<score.Length-LayoutGraph.Tolerance;
            // Moving an already untangled sibling set around only to shorten its
            // spokes disrupts stable tree ordering. Reorder it only to remove crossings.
            bool reordered = !sharedNodes && candidate.Crossings >= score.Crossings && siblings.Any(group=>
                !group.OrderBy(child=>before[indices[child]].X).ThenBy(child=>child,StringComparer.Ordinal)
                    .SequenceEqual(group.OrderBy(child=>Node(child).X).ThenBy(child=>child,StringComparer.Ordinal)));
            if(!moved || !better || reordered || !Valid() || project.Nodes.Max(n=>n.X+n.Width)-project.Nodes.Min(n=>n.X)>width+LayoutGraph.Tolerance ||
                blocked is not null && !BlockedPassages().IsSubsetOf(blocked))
            {
                Array.Copy(before,project.Nodes,before.Length);
                return false;
            }
            return true;
        }
        // Parent-first recursion is bounded by the visited set; bottom-up sweeps let
        // the next proposal use the positions accepted for the previous branch.
        for(int pass=0;pass<3;pass++)
        {
            var before=Score();
            var visited=new HashSet<string>();
            void Visit(string id)
            {
                if(!visited.Add(id)) return;
                var parents=LayoutGraph.Parents(project,id).Where(n=>n.Y<Node(id).Y).ToArray();
                foreach(var parent in parents) Visit(parent.Id);
                var children=LayoutGraph.Children(project,id);
                if(parents.Length>0 && children.Any(child=>LayoutGraph.Parents(project,child.Id).Length>1))
                {
                    // A broker can be near its parent but on the wrong side of a
                    // neighbouring branch for its shared dependencies. Consider the
                    // nearest available row slots before asking neighbours to move.
                    var node=Node(id);
                    double preferred=LayoutGraph.Midpoint(children)-node.Width/2;
                    var peers=project.Nodes.Where(n=>n.Id!=id && n.Y==node.Y).ToArray();
                    var slots=peers.SelectMany(n=>new[]{n.X-spacing-node.Width,n.X+n.Width+spacing})
                        .Append(preferred).Distinct()
                        .Where(x=>peers.All(n=>x+node.Width+spacing<=n.X+LayoutGraph.Tolerance || x>=n.X+n.Width+spacing-LayoutGraph.Tolerance))
                        .OrderBy(x=>Math.Abs(x-preferred)).ThenBy(x=>x).ToArray();
                    foreach(double slot in slots)
                        if(TryMove(id,slot)) break;
                }
                if(parents.Length>0)
                    TryMove(id,LayoutGraph.Midpoint(parents.Select(n=>Node(n.Id)))-Node(id).Width/2);
            }
            foreach(string id in project.Nodes.OrderByDescending(n=>n.Y).Select(n=>n.Id).ToArray()) Visit(id);
            if(Score()==before) break;
        }
    }
}
