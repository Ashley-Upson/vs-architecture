using System;
using System.Collections.Generic;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core2.Models;
namespace StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Rendering;
internal interface ICallChainLayoutService { RenderModel Layout(RenderModel model,ContextualDiagram diagram); }
internal sealed class CallChainLayoutService(ICallChainRegionService regions) : ICallChainLayoutService
{
    private sealed record Branch(CompositionTree Tree,int Depth,List<Step> Steps,double Height);
    private sealed record Step(CompositionTreeNode Method,bool Recursive,List<Branch> Children,double Height);
    public RenderModel Layout(RenderModel model,ContextualDiagram diagram)
    {
        var catalog=diagram.Trees ?? [];
        var methods=catalog.SelectMany(t=>t.Nodes.Where(n=>n.MemberId!=null).Select(n=>(Tree:t,Node:n))).ToDictionary(x=>x.Node.Id);
        var calls=diagram.Links.ToLookup(l=>l.From,l=>l.To);
        var incoming=diagram.Links.Select(l=>l.To).ToHashSet();
        var seen=new HashSet<string>();
        var roots=new List<Branch>();
        double width=model.Configuration.Composition.NodeWidth,gap=Math.Max(60,model.Configuration.Composition.ProjectSpacing);
        string Owner(CompositionTree tree)=>tree.ProjectName??"Project";
        double NodeHeight(CompositionTreeNode n)=>n.Details is null?30:(n.Details.Length+1)*18+34;
        Branch Build(CompositionTree tree,IEnumerable<string> selected,int depth,HashSet<string> path)
        {
            var steps=new List<Step>();
            foreach(string id in selected.Distinct())
            {
                seen.Add(id);bool recursive=path.Contains(id);
                var next=new HashSet<string>(path){id};var children=new List<Branch>();
                if(!recursive)
                foreach(var group in calls[id].GroupBy(target=>methods[target].Tree))
                    children.Add(Build(group.Key,group,Owner(tree)==Owner(group.Key)?depth+1:0,next));
                double height=Math.Max(NodeHeight(methods[id].Node),children.Sum(c=>c.Height)+Math.Max(0,children.Count-1)*30);
                steps.Add(new(methods[id].Node,recursive,children,height));
            }
            return new(tree,depth,steps,54+steps.Sum(m=>m.Height)+Math.Max(0,steps.Count-1)*30);
        }
        foreach(var tree in catalog)
        {
            var entry=tree.Nodes.Where(n=>n.MemberId!=null&&!incoming.Contains(n.Id)).Select(n=>n.Id).ToArray();
            if(entry.Length>0)roots.Add(Build(tree,entry,0,new()));
        }
        foreach(var method in methods.Values)
            if(!seen.Contains(method.Node.Id))roots.Add(Build(method.Tree,[method.Node.Id],0,new()));
        var branches=new List<Branch>();
        void Gather(Branch branch){branches.Add(branch);foreach(var child in branch.Steps.SelectMany(m=>m.Children))Gather(child);}
        foreach(var root in roots)Gather(root);
        var owners=branches.Select(b=>Owner(b.Tree)).Distinct().ToArray();
        var projectLinks=diagram.Links.Select(l=>(From:Owner(methods[l.From].Tree),To:Owner(methods[l.To].Tree))).Where(e=>e.From!=e.To).Distinct().ToArray();
        var ranks=Ranks(owners,projectLinks.ToLookup(e=>e.From,e=>e.To));
        var projectX=new Dictionary<string,double>();var projectWidth=new Dictionary<string,double>();
        double x=40;
        foreach(string owner in owners.OrderBy(o=>ranks[o]).ThenBy(o=>o,StringComparer.Ordinal))
        {
            projectX[owner]=x;
            projectWidth[owner]=80+(branches.Where(b=>Owner(b.Tree)==owner).Max(b=>b.Depth)+1)*(width+28)+branches.Where(b=>Owner(b.Tree)==owner).Max(b=>b.Depth)*gap;
            x+=projectWidth[owner]+gap;
        }
        var nodes=owners.ToDictionary(o=>o,_=>new List<RenderNode>());
        var lines=owners.ToDictionary(o=>o,_=>new List<RenderConnection>());
        var pending=new List<(string FromOwner,RenderNode From,string ToOwner,RenderNode To)>();
        int serial=0;
        RenderNode Node(string owner,CompositionTreeNode definition,double left,double top,bool recursive=false)
        {
            string label=definition.Label+(recursive?" (recursive call)":"");
            var labels=new[]{label}.Concat(definition.Details??[]).ToArray();
            var node=new RenderNode("call-node-"+serial++,definition.TypeName,string.Join("\n",labels),definition.MemberId is null?"#075985":"#334155",
                left,top,width,NodeHeight(definition),labels.Select((text,i)=>new RenderText(text,definition.Details is null||i==0?left+width/2:left+12,
                    definition.Details is null?top+15:i==0?top+17:top+34+i*18,i==0,12)).ToArray()){HasHeader=definition.Details!=null};
            nodes[owner].Add(node);return node;
        }
        Dictionary<string,RenderNode> Place(Branch branch,double top)
        {
            string owner=Owner(branch.Tree);double left=40+branch.Depth*(width+28+gap);
            var root=Node(owner,branch.Tree.Nodes[0],left,top);double cursor=top+54;
            var result=new Dictionary<string,RenderNode>();
            foreach(var step in branch.Steps)
            {
                double childY=cursor;
                var targets=new List<(string Owner,RenderNode Node)>();
                foreach(var child in step.Children)
                {
                    var placed=Place(child,childY);
                    targets.AddRange(placed.Values.Select(n=>(Owner(child.Tree),n)));childY+=child.Height+30;
                }
                double center=targets.Count==0?cursor+step.Height/2:(targets.Min(t=>t.Node.Y)+targets.Max(t=>t.Node.Y+t.Node.Height))/2;
                var method=Node(owner,step.Method,left+28,center-NodeHeight(step.Method)/2,step.Recursive);
                result[step.Method.Id]=method;
                lines[owner].Add(new("tree-edge-"+method.Id,root.Id,method.Id,root.TypeName,method.TypeName,false,
                    [new(root.X+12,root.Y+root.Height),new(root.X+12,method.Y+method.Height/2),new(method.X,method.Y+method.Height/2)],"#64748b"){IsTree=true});
                foreach(var target in targets)pending.Add((owner,method,target.Owner,target.Node));
                cursor+=step.Height+30;
            }
            // The heading follows its first method, whose position is determined
            // by the consumed branches, rather than the reserved branch height.
            double headingY=result.Values.Min(n=>n.Y)-root.Height-24;
            double offset=headingY-root.Y;
            nodes[owner][nodes[owner].FindIndex(n=>n.Id==root.Id)]=root with {
                Y=headingY,TextLines=root.TextLines.Select(t=>t with {Y=t.Y+offset}).ToArray() };
            return result;
        }
        double y=60;
        foreach(var root in roots.OrderByDescending(b=>b.Height)){Place(root,y);y+=root.Height+gap;}
        var cross=new List<RenderConnection>();
        foreach(var link in pending)
        {
            bool local=link.FromOwner==link.ToOwner;
            double ax=link.From.X+link.From.Width+(local?0:projectX[link.FromOwner]),ay=link.From.Y+link.From.Height/2;
            double bx=link.To.X+(local?0:projectX[link.ToOwner]),by=link.To.Y+link.To.Height/2;
            var edge=new RenderConnection("call-edge-"+serial++,link.From.Id,link.To.Id,link.From.TypeName,link.To.TypeName,false,
                ay==by?[new(ax,ay),new(bx,by)]:[new(ax,ay),new(ax+gap/2,ay),new(ax+gap/2,by),new(bx,by)],"#38bdf8");
            if(local)lines[link.FromOwner].Add(edge);else cross.Add(edge with { Points=edge.Points.Select(p=>p with { Y=p.Y+40 }).ToArray() });
        }
        model.Projects=owners.Select((o,i)=>new RenderProject("call-project-"+i,o,projectX[o],40,projectWidth[o],nodes[o].Max(n=>n.Y+n.Height)+40,nodes[o].ToArray(),lines[o].ToArray())).ToArray();
        model.CrossProjectConnections=cross.ToArray();model.Width=x;model.Height=model.Projects.Select(p=>p.Y+p.Height+40).DefaultIfEmpty(200).Max();
        return regions.Compact(model);
    }
    private static Dictionary<string,int> Ranks(string[] ids,ILookup<string,string> outgoing)
    {
        // Condense cycles before assigning longest-path depth, so recursion cannot
        // keep pushing the same containers into ever later columns.
        int next=0,componentCount=0;
        var index=new Dictionary<string,int>();var low=new Dictionary<string,int>();var component=new Dictionary<string,int>();
        var stack=new Stack<string>();var active=new HashSet<string>();
        void Visit(string id)
        {
            index[id]=low[id]=next++;stack.Push(id);active.Add(id);
            foreach(string child in outgoing[id])
            {
                if(!index.ContainsKey(child)){Visit(child);low[id]=Math.Min(low[id],low[child]);}
                else if(active.Contains(child))low[id]=Math.Min(low[id],index[child]);
            }
            if(low[id]!=index[id])return;
            string current;
            do{current=stack.Pop();active.Remove(current);component[current]=componentCount;}while(current!=id);
            componentCount++;
        }
        foreach(string id in ids)if(!index.ContainsKey(id))Visit(id);
        var links=ids.SelectMany(id=>outgoing[id].Select(child=>(From:component[id],To:component[child]))).Where(e=>e.From!=e.To).Distinct().ToArray();
        var children=links.ToLookup(e=>e.From,e=>e.To);var counts=new int[componentCount];var depths=new int[componentCount];
        foreach(var edge in links)counts[edge.To]++;
        var pending=new Queue<int>(Enumerable.Range(0,componentCount).Where(c=>counts[c]==0));
        while(pending.TryDequeue(out int parent))foreach(int child in children[parent])
        {
            depths[child]=Math.Max(depths[child],depths[parent]+1);
            if(--counts[child]==0)pending.Enqueue(child);
        }
        return ids.ToDictionary(id=>id,id=>depths[component[id]]);
    }
}
