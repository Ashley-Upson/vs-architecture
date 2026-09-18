using System;
using System.Collections.Generic;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core2.Models;
namespace StandardIo.ArchitectureDiagram.Core2.Services.Processings.Layout;
internal sealed class NodeLocalityLayoutRuleProcessingService : ArchitectureLayoutRuleProcessingService
{
    protected override void ApplyArchitectureRule(RenderModel model)
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
        var parents = project.Nodes.ToDictionary(node=>node.Id,node=>LayoutGraph.Parents(project,node.Id).Select(parent=>parent.Id).ToArray());
        var children = project.Nodes.ToDictionary(node=>node.Id,node=>LayoutGraph.Children(project,node.Id).Select(child=>child.Id).ToArray());
        var ownedChildren = project.Nodes.ToDictionary(node=>node.Id,
            node=>children[node.Id].Where(childId=>parents[childId].Length==1).ToArray());
        var branches = project.Nodes.ToDictionary(node=>node.Id,
            node=>LayoutGraph.OwnedBranch(project,node.Id).Select(branchNode=>branchNode.Id).ToArray());
        var rows = project.Nodes.GroupBy(node=>node.Y).ToDictionary(group=>group.Key,group=>group.Select(node=>node.Id).ToArray());
        var groups = LayoutGraph.BranchGroups(project).Select(g=>g.Select(n=>n.Id).ToArray()).ToArray();
        var siblings = project.Nodes.Select(n=>ownedChildren[n.Id])
            .Where(group=>group.Length>1).ToArray();
        var edges = project.Connections.Where(e=>Node(e.TargetId).Y>Node(e.SourceId).Y).ToArray();
        HashSet<(string Source,string Obstacle)> BlockedPassages(IReadOnlySet<string>? affectedNodeIds = null) =>
            LayoutPassages.Blocked(model,project,affectedNodeIds);
        var pairs = edges.SelectMany((a,i)=>edges.Skip(i+1)
            .Where(b=>a.SourceId!=b.SourceId && a.TargetId!=b.TargetId &&
                Node(a.SourceId).Y==Node(b.SourceId).Y && Node(a.TargetId).Y==Node(b.TargetId).Y)
            .Select(b=>(a,b))).ToArray();
        (int Crossings,double Length) Score() =>
            (pairs.Count(p=>(LayoutGraph.Centre(Node(p.a.SourceId))-LayoutGraph.Centre(Node(p.b.SourceId)))*
                 (LayoutGraph.Centre(Node(p.a.TargetId))-LayoutGraph.Centre(Node(p.b.TargetId)))<0),
             edges.Sum(e=>Math.Abs(LayoutGraph.Centre(Node(e.SourceId))-LayoutGraph.Centre(Node(e.TargetId)))));
        var pairIndices = project.Nodes.ToDictionary(node=>node.Id,_=>new List<int>());
        for (int pairIndex=0; pairIndex<pairs.Length; pairIndex++)
            foreach (string id in new[]{pairs[pairIndex].a.SourceId,pairs[pairIndex].a.TargetId,pairs[pairIndex].b.SourceId,pairs[pairIndex].b.TargetId}.Distinct())
                pairIndices[id].Add(pairIndex);
        var edgeIndices = project.Nodes.ToDictionary(node=>node.Id,_=>new List<int>());
        for (int edgeIndex=0; edgeIndex<edges.Length; edgeIndex++)
            foreach (string id in new[]{edges[edgeIndex].SourceId,edges[edgeIndex].TargetId}.Distinct()) edgeIndices[id].Add(edgeIndex);
        bool Crosses((RenderConnection a,RenderConnection b) pair,Func<string,RenderNode> getNode) =>
            (LayoutGraph.Centre(getNode(pair.a.SourceId))-LayoutGraph.Centre(getNode(pair.b.SourceId)))*
            (LayoutGraph.Centre(getNode(pair.a.TargetId))-LayoutGraph.Centre(getNode(pair.b.TargetId)))<0;
        double Length(RenderConnection edge,Func<string,RenderNode> getNode) =>
            Math.Abs(LayoutGraph.Centre(getNode(edge.SourceId))-LayoutGraph.Centre(getNode(edge.TargetId)));
        (int Crossings,double Length) CandidateScore(RenderNode[] before,HashSet<string> affected,
            (int Crossings,double Length) current)
        {
            RenderNode Before(string id) => before[indices[id]];
            var affectedPairs=affected.SelectMany(id=>pairIndices[id]).ToHashSet();
            var affectedEdges=affected.SelectMany(id=>edgeIndices[id]).ToHashSet();
            int crossings=current.Crossings;
            double length=current.Length;
            foreach(int pairIndex in affectedPairs)
            {
                if(Crosses(pairs[pairIndex],Before)) crossings--;
                if(Crosses(pairs[pairIndex],Node)) crossings++;
            }
            foreach(int edgeIndex in affectedEdges)
            {
                length-=Length(edges[edgeIndex],Before);
                length+=Length(edges[edgeIndex],Node);
            }
            return (crossings,length);
        }
        bool Valid()
        {
            foreach (var row in rows.Values)
            {
                var nodes=row.Select(Node).OrderBy(n=>n.X).ToArray();
                for(int i=1;i<nodes.Length;i++)
                    if(nodes[i].X-nodes[i-1].X-nodes[i-1].Width<spacing-LayoutGraph.Tolerance) return false;
            }
            foreach(var group in groups)
            {
                var roots=group.OrderBy(id=>Node(id).X).ThenBy(id=>id,StringComparer.Ordinal).ToArray();
                for(int i=1;i<roots.Length;i++)
                    if(LayoutGraph.BranchClearance(project,roots[i-1],roots[i],branches[roots[i-1]].Select(Node).ToArray(),
                        branches[roots[i]].Select(Node).ToArray(),
                        sharedNodes,respectBranchOrder:false)<spacing-LayoutGraph.Tolerance) return false;
            }
            return project.Nodes.All(n=>double.IsFinite(n.X));
        }
        // A proposal moves a branch as a unit and recursively makes room in occupied
        // rows. The transaction is discarded if a shared branch prevents the move.
        bool Move(string id,double delta,HashSet<string> moving)
        {
            if(Math.Abs(delta)<LayoutGraph.Tolerance) return true;
            var branch=branches[id].ToHashSet();
            if(branch.Overlaps(moving)) return false;
            moving.UnionWith(branch);
            foreach(string member in branch) LayoutGraph.Move(project,member,delta);
            foreach(string member in branch)
            {
                var node=Node(member);
                foreach(string peerId in rows[node.Y].Where(peerId=>!branch.Contains(peerId))
                    .OrderBy(peerId=>delta>0?Node(peerId).X:-Node(peerId).X).ToArray())
                {
                    var peer=Node(peerId);
                    if(node.X+node.Width+spacing<=peer.X+LayoutGraph.Tolerance || peer.X+peer.Width+spacing<=node.X+LayoutGraph.Tolerance) continue;
                    var consumers=parents[peerId].Select(Node).ToArray();
                    double preferred=consumers.Length>0?LayoutGraph.Midpoint(consumers):LayoutGraph.Centre(peer);
                    double push=preferred>=LayoutGraph.Centre(node)?node.X+node.Width+spacing-peer.X:node.X-spacing-peer.Width-peer.X;
                    if(!Move(peerId,push,moving)) return false;
                }
            }
            moving.ExceptWith(branch);
            return true;
        }
        var currentScore=Score();
        var currentBlocked=model.PassageOffsetParents.Count>0?BlockedPassages():null;
        bool TryMove(string id,double preferred)
        {
            var before=project.Nodes.ToArray();
            double width=before.Max(n=>n.X+n.Width)-before.Min(n=>n.X);
            bool moved=Move(id,preferred-Node(id).X,[]);
            // Re-evaluate ancestors after moving children, deepest first. This preserves
            // centring over the child set's outside edges rather than its mean centre.
            foreach(string parentId in project.Nodes.OrderByDescending(n=>n.Y).Select(n=>n.Id).ToArray())
            {
                if(!moved) break;
                var owned=ownedChildren[parentId];
                if(owned.Length>0 && !model.PassageOffsetParents.Contains(parentId))
                    LayoutGraph.Move(project,parentId,LayoutGraph.Midpoint(owned.Select(Node))-LayoutGraph.Centre(Node(parentId)));
            }
            var affected=before.Where((node,index)=>Math.Abs(node.X-project.Nodes[index].X)>=LayoutGraph.Tolerance)
                .Select(node=>node.Id).ToHashSet();
            var candidate=CandidateScore(before,affected,currentScore);
            bool better=candidate.Crossings<currentScore.Crossings || candidate.Crossings==currentScore.Crossings && candidate.Length<currentScore.Length-LayoutGraph.Tolerance;
            // Moving an already untangled sibling set around only to shorten its
            // spokes disrupts stable tree ordering. Reorder it only to remove crossings.
            bool reordered = !sharedNodes && candidate.Crossings >= currentScore.Crossings && siblings.Any(group=>
                !group.OrderBy(child=>before[indices[child]].X).ThenBy(child=>child,StringComparer.Ordinal)
                    .SequenceEqual(group.OrderBy(child=>Node(child).X).ThenBy(child=>child,StringComparer.Ordinal)));
            HashSet<(string Source,string Obstacle)>? candidateBlocked=null;
            bool invalid=!moved || !better || reordered || !Valid() ||
                project.Nodes.Max(n=>n.X+n.Width)-project.Nodes.Min(n=>n.X)>width+LayoutGraph.Tolerance;
            if(!invalid && currentBlocked is not null)
            {
                candidateBlocked=new HashSet<(string Source,string Obstacle)>(currentBlocked);
                candidateBlocked.RemoveWhere(pair=>affected.Contains(pair.Source)||affected.Contains(pair.Obstacle));
                candidateBlocked.UnionWith(BlockedPassages(affected));
                invalid=!candidateBlocked.IsSubsetOf(currentBlocked);
            }
            if(invalid)
            {
                Array.Copy(before,project.Nodes,before.Length);
                return false;
            }
            currentScore=candidate;
            if(candidateBlocked is not null) currentBlocked=candidateBlocked;
            return true;
        }
        // Parent-first recursion is bounded by the visited set; bottom-up sweeps let
        // the next proposal use the positions accepted for the previous branch.
        for(int pass=0;pass<3;pass++)
        {
            var before=currentScore;
            var visited=new HashSet<string>();
            void Visit(string id)
            {
                if(!visited.Add(id)) return;
                var nodeParents=parents[id].Select(Node).Where(n=>n.Y<Node(id).Y).ToArray();
                foreach(var parent in nodeParents) Visit(parent.Id);
                var nodeChildren=children[id];
                if(nodeParents.Length>0 && nodeChildren.Any(childId=>parents[childId].Length>1))
                {
                    // A broker can be near its parent but on the wrong side of a
                    // neighbouring branch for its shared dependencies. Consider the
                    // nearest available row slots before asking neighbours to move.
                    var node=Node(id);
                    double preferred=LayoutGraph.Midpoint(nodeChildren.Select(Node))-node.Width/2;
                    var peers=rows[node.Y].Where(peerId=>peerId!=id).Select(Node).ToArray();
                    var slots=peers.SelectMany(n=>new[]{n.X-spacing-node.Width,n.X+n.Width+spacing})
                        .Append(preferred).Distinct()
                        .Where(x=>peers.All(n=>x+node.Width+spacing<=n.X+LayoutGraph.Tolerance || x>=n.X+n.Width+spacing-LayoutGraph.Tolerance))
                        .OrderBy(x=>Math.Abs(x-preferred)).ThenBy(x=>x).ToArray();
                    foreach(double slot in slots)
                        if(TryMove(id,slot)) break;
                }
                if(nodeParents.Length>0)
                    TryMove(id,LayoutGraph.Midpoint(nodeParents.Select(n=>Node(n.Id)))-Node(id).Width/2);
            }
            foreach(string id in project.Nodes.OrderByDescending(n=>n.Y).Select(n=>n.Id).ToArray()) Visit(id);
            if(currentScore==before) break;
        }
    }
}
