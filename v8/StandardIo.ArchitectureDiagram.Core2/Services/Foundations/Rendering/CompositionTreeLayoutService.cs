using System;
using System.Linq;
using System.Collections.Generic;
using StandardIo.ArchitectureDiagram.Core2.Models;
namespace StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Rendering;
internal interface ICompositionTreeLayoutService { RenderModel Layout(RenderModel model, CompositionTree[] trees); }
internal sealed class CompositionTreeLayoutService : ICompositionTreeLayoutService
{
    public RenderModel Layout(RenderModel model, CompositionTree[] trees)
    {
        var config = model.Configuration.Composition;
        var projects = new List<RenderProject>();
        foreach (var tree in trees.OrderByDescending(t => t.Nodes.Length).ThenBy(t => t.Title, StringComparer.Ordinal))
        {
            double nextY = 60;
            var nodes = tree.Nodes.Select(n =>
            {
                double x = 40+n.Depth*28, y = nextY;
                string[] labels = new[] { n.Label }.Concat(n.Details ?? []).ToArray();
                double height = n.Details is null ? 30 : labels.Length*18+34;
                nextY += height+14;
                return new RenderNode(n.Id,n.TypeName,string.Join("\n",labels),n.Depth == 0 ? "#075985" : "#334155",
                    x,y,config.NodeWidth,height,labels.Select((label,i)=>new RenderText(label,
                        n.Details is null || i==0 ? x+config.NodeWidth/2 : x+12,n.Details is null ? y+15 : i==0 ? y+17 : y+34+i*18,i==0&&(n.Depth==0||n.Details!=null),12)).ToArray()) { HasHeader=n.Details!=null };
            }).ToArray();
            var byId = nodes.ToDictionary(n=>n.Id);
            var edges = tree.Nodes.Where(n=>n.ParentId!=null).Select(n =>
            {
                var a = byId[n.ParentId!]; var b = byId[n.Id];
                return new RenderConnection("edge-"+n.Id,a.Id,b.Id,a.TypeName,b.TypeName,false,
                    [new(a.X+12,a.Y+a.Height),new(a.X+12,b.Y+b.Height/2),new(b.X,b.Y+b.Height/2)],model.DiagramType==DiagramTypes.CallChain?"#64748b":"#d1d5db") { IsTree=true };
            }).ToArray();
            projects.Add(new("composition-"+projects.Count,tree.Title,0,0,nodes.Max(n=>n.X+n.Width)+40,nodes.Max(n=>n.Y+n.Height)+40,nodes,edges));
        }
        if(model.DiagramType==DiagramTypes.Composition && trees.Any(t=>t.ProjectName!=null))
        {
            var treeDefinitions=trees.ToDictionary(t=>t.Nodes[0].Id);
            var containers=new List<RenderProject>();double top=40;
            foreach(var group in projects.GroupBy(p=>{
                var tree=treeDefinitions[p.Nodes[0].Id];return (Project:tree.ProjectName??tree.Title,Namespace:tree.NamespaceName??"");
            }).OrderBy(g=>g.Key.Project,StringComparer.Ordinal).ThenBy(g=>g.Key.Namespace,StringComparer.Ordinal))
            {
                var nodes=new List<RenderNode>();var connections=new List<RenderConnection>();double left=0;
                foreach(var tree in group)
                {
                    nodes.AddRange(tree.Nodes.Select(n=>n with {X=n.X+left,TextLines=n.TextLines.Select(t=>t with {X=t.X+left}).ToArray()}));
                    connections.AddRange(tree.Connections.Select(e=>e with {Points=e.Points.Select(p=>p with {X=p.X+left}).ToArray()}));
                    left+=tree.Width+config.ProjectSpacing;
                }
                double containerHeight=group.Max(p=>p.Height);
                containers.Add(new("composition-namespace-"+containers.Count,group.Key.Project+"\n"+(group.Key.Namespace.Length==0?"(global namespace)":group.Key.Namespace),
                    40,top,left-config.ProjectSpacing,containerHeight,nodes.ToArray(),connections.ToArray()));
                top+=containerHeight+config.ProjectSpacing;
            }
            model.Projects=containers.ToArray();model.CrossProjectConnections=[];
            model.Width=containers.Max(p=>p.X+p.Width)+40;model.Height=containers.Max(p=>p.Y+p.Height)+40;
            return model;
        }
        double spacing = config.ProjectSpacing;
        double x=40,height=0;
        for(int i=0;i<projects.Count;i++)
        {
            var p=projects[i];
            projects[i]=p with { X=x,Y=40 }; x+=p.Width+spacing; height=Math.Max(height,p.Height);
        }
        model.Projects=projects.ToArray();model.CrossProjectConnections=[];
        var specifications = trees.SelectMany(t => t.Nodes).ToDictionary(n=>n.Id);
        var locations = projects.SelectMany(p=>p.Nodes.Select(n=>(Project:p,Node:n))).ToDictionary(x=>x.Node.Id);
        var roots = trees.ToDictionary(t=>t.Nodes[0].TypeName,t=>t.Nodes[0].Id);
        var methods = trees.SelectMany(t=>t.Nodes.Where(n=>n.MemberId!=null)).GroupBy(n=>(n.TypeName,n.MemberId!)).ToDictionary(g=>g.Key,g=>g.First().Id);
        var links = new List<(string Source,string Target)>();
        foreach(var n in specifications.Values.Where(n=>n.TargetTypeName!=null))
        {
            string? target = null;
            if(n.TargetMemberId!=null) methods.TryGetValue((n.TargetTypeName!,n.TargetMemberId),out target);
            else roots.TryGetValue(n.TargetTypeName!,out target);
            if(target!=null && target!=n.Id) links.Add((n.Id,target));
        }
        // Route reference links through a band above the containers. Tree coordinates
        // remain local; cross-container links use document coordinates in both writers.
        if(model.DiagramType != DiagramTypes.Composition && links.Count>0)
        {
            var targets=links.Select(l=>l.Target).Distinct().ToArray();
            double band=targets.Length*4+40;
            for(int i=0;i<projects.Count;i++) projects[i]=projects[i] with { Y=40+band };
            model.Projects=projects.ToArray();
            locations=projects.SelectMany(p=>p.Nodes.Select(n=>(Project:p,Node:n))).ToDictionary(x=>x.Node.Id);
            model.CrossProjectConnections=links.Select((link,i)=>
            {
                var a=locations[link.Source]; var b=locations[link.Target];
                double ax=a.Project.X+a.Node.X+a.Node.Width, ay=a.Project.Y+a.Node.Y+a.Node.Height/2;
                double bx=b.Project.X+b.Node.X, by=b.Project.Y+b.Node.Y+b.Node.Height/2;
                double right=a.Project.X+a.Project.Width-12, left=b.Project.X+12, lane=20+Array.IndexOf(targets,link.Target)*4;
                return new RenderConnection("reference-"+i,a.Node.Id,b.Node.Id,a.Node.TypeName,b.Node.TypeName,false,
                    [new(ax,ay),new(right,ay),new(right,lane),new(left,lane),new(left,by),new(bx,by)],"#38bdf8");
            }).ToArray();
            height+=band;
        }
        model.Width=projects.Select(p=>p.X+p.Width+40).DefaultIfEmpty(400).Max();model.Height=height+80;
        return model;
    }
}
