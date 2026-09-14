using System;
using System.Collections.Generic;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core2.Models;
namespace StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Rendering;
internal interface ICallChainLayoutService { RenderModel Layout(RenderModel model,ContextualDiagram diagram); }
internal sealed class CallChainLayoutService(ICompositionTreeLayoutService containers) : ICallChainLayoutService
{
    public RenderModel Layout(RenderModel model,ContextualDiagram diagram)
    {
        containers.Layout(model,diagram.Trees ?? []);
        var projects=model.Projects.ToDictionary(p=>p.Id);
        var owners=model.Projects.SelectMany(p=>p.Nodes.Select(n=>(n.Id,Owner:p.Id))).ToDictionary(x=>x.Id,x=>x.Owner);
        var edges=diagram.Links.Select(l=>(From:owners[l.From],To:owners[l.To])).Where(e=>e.From!=e.To).Distinct().ToArray();
        var outgoing=edges.ToLookup(e=>e.From,e=>e.To);
        var incoming=edges.ToLookup(e=>e.To,e=>e.From);
        var rank=Ranks(projects.Keys.ToArray(),outgoing);
        double gap=Math.Max(60,model.Configuration.Composition.ProjectSpacing);
        double x=40;
        foreach(var column in projects.Keys.GroupBy(id=>rank[id]).OrderBy(g=>g.Key))
        {
            double Preferred(string id) => incoming[id].Where(p=>rank[p]<rank[id]).Select(p=>projects[p].Y+projects[p].Height/2).DefaultIfEmpty(40).Average();
            double cursor=40;
            foreach(string id in column.OrderBy(Preferred).ThenByDescending(id=>outgoing[id].Count()).ThenBy(id=>projects[id].Name,StringComparer.Ordinal))
            {
                var p=projects[id]; double y=Math.Max(cursor,Preferred(id)-p.Height/2);
                projects[id]=p with { X=x,Y=y };cursor=y+p.Height+gap;
            }
            x+=column.Max(id=>projects[id].Width)+gap;
        }
        var columns=projects.Values.GroupBy(p=>rank[p.Id]).ToDictionary(g=>g.Key,g=>g.ToArray());
        var locations=projects.Values.SelectMany(p=>p.Nodes.Select(n=>(Project:p,Node:n))).ToDictionary(x=>x.Node.Id);
        model.CrossProjectConnections=diagram.Links.Select((link,i)=>
        {
            var a=locations[link.From];var b=locations[link.To];
            double ax=a.Project.X+a.Node.X+a.Node.Width,ay=a.Project.Y+a.Node.Y+a.Node.Height/2;
            double by=b.Project.Y+b.Node.Y+b.Node.Height/2;
            var points=new List<DrawingPoint> {new(ax,ay)};
            double clearance=12+(i%6)*4;
            if(link.From==link.To)
            {
                double side=a.Project.X+a.Project.Width+clearance;
                double returnY=ay+a.Node.Height/4;
                points.Add(new(side,ay));points.Add(new(side,returnY));points.Add(new(ax,returnY));
            }
            else if(rank[b.Project.Id]<=rank[a.Project.Id])
            {
                // Recursion and mutually calling types use the local right gutter.
                double side=Math.Max(a.Project.X+a.Project.Width,b.Project.X+b.Project.Width)+clearance;
                points.Add(new(side,ay));points.Add(new(side,by));points.Add(new(b.Project.X+b.Node.X+b.Node.Width,by));
            }
            else
            {
                double currentX=a.Project.X+a.Project.Width+clearance,currentY=ay;
                points.Add(new(currentX,currentY));
                for(int column=rank[a.Project.Id]+1;column<rank[b.Project.Id];column++)
                {
                    if(!columns.TryGetValue(column,out var obstacles)) continue;
                    bool Clear(double y)=>obstacles.All(p=>y<=p.Y-12||y>=p.Y+p.Height+12);
                    if(!Clear(currentY))
                    {
                        double y=obstacles.SelectMany(p=>new[]{p.Y-12,p.Y+p.Height+12}).Where(y=>y>=40&&Clear(y))
                            .OrderBy(y=>Math.Abs(y-currentY)+Math.Abs(y-by)).First();
                        points.Add(new(currentX,y));currentY=y;
                    }
                    currentX=obstacles.Max(p=>p.X+p.Width)+clearance;points.Add(new(currentX,currentY));
                }
                double targetX=b.Project.X-clearance;
                points.Add(new(targetX,currentY));points.Add(new(targetX,by));points.Add(new(b.Project.X+b.Node.X,by));
            }
            var clean=new List<DrawingPoint>();
            foreach(var point in points)
            {
                if(clean.Count>0&&clean[^1]==point)continue;
                while(clean.Count>1&&((clean[^2].X==clean[^1].X&&clean[^1].X==point.X)||(clean[^2].Y==clean[^1].Y&&clean[^1].Y==point.Y))) clean.RemoveAt(clean.Count-1);
                clean.Add(point);
            }
            return new RenderConnection("call-link-"+i,a.Node.Id,b.Node.Id,a.Node.TypeName,b.Node.TypeName,false,clean.ToArray(),"#38bdf8");
        }).ToArray();
        model.Projects=projects.Values.ToArray();
        model.Width=model.Projects.Select(p=>p.X+p.Width+gap).DefaultIfEmpty(400).Max();
        model.Height=model.Projects.Select(p=>p.Y+p.Height+40).DefaultIfEmpty(200).Max();
        return model;
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
