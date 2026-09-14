using System;
using System.Collections.Generic;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core2.Models;
namespace StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Rendering;
internal interface ICallChainRegionService { RenderModel Compact(RenderModel model); }
internal sealed class CallChainRegionService : ICallChainRegionService
{
    public RenderModel Compact(RenderModel model)
    {
        double proximity=model.Configuration.Composition.NodeSpacing,gap=Math.Max(60,model.Configuration.Composition.ProjectSpacing);
        double step=model.Configuration.Composition.NodeWidth+28+gap;
        var edges=model.Projects.SelectMany(p=>p.Connections).Concat(model.CrossProjectConnections).ToArray();
        var regions=new List<RenderProject>();
        foreach(var project in model.Projects)
        foreach(var column in project.Nodes.GroupBy(n=>(int)Math.Round((n.X-40)/step)))
        {
            var groups=new List<List<RenderNode>>();double bottom=double.NegativeInfinity;
            foreach(var node in column.OrderBy(n=>n.Y))
            {
                if(groups.Count==0||node.Y-bottom>proximity)groups.Add([]);
                groups[^1].Add(node);bottom=Math.Max(bottom,node.Y+node.Height);
            }
            foreach(var group in groups)
            {
                double left=group.Min(n=>n.X)-20,top=group.Min(n=>n.Y)-40;
                var nodes=group.Select(n=>n with { X=n.X-left,Y=n.Y-top,TextLines=n.TextLines.Select(t=>t with { X=t.X-left,Y=t.Y-top }).ToArray() }).ToArray();
                regions.Add(new("call-region-"+regions.Count,project.Name,project.X+left,project.Y+top,
                    group.Max(n=>n.X+n.Width)-left+20,group.Max(n=>n.Y+n.Height)-top+20,nodes,[]));
            }
        }
        var nodeRegions=regions.SelectMany(p=>p.Nodes.Select(n=>(n.Id,Region:p.Id))).ToDictionary(x=>x.Id,x=>x.Region);
        var parents=edges.Where(e=>!e.IsTree&&nodeRegions[e.SourceId]!=nodeRegions[e.TargetId]).ToLookup(e=>nodeRegions[e.TargetId],e=>nodeRegions[e.SourceId]);
        var placed=new Dictionary<string,RenderProject>();var bands=new Dictionary<int,List<RenderProject>>();
        IEnumerable<int> Bands(RenderProject p)=>Enumerable.Range((int)(p.Y/1024),(int)((p.Y+p.Height)/1024)-(int)(p.Y/1024)+1);
        foreach(var region in regions.OrderBy(p=>p.X).ThenBy(p=>p.Y))
        {
            double x=parents[region.Id].Where(placed.ContainsKey).Select(id=>placed[id].X+placed[id].Width+gap).DefaultIfEmpty(40).Max();
            var obstacles=Bands(region).Where(bands.ContainsKey).SelectMany(b=>bands[b]).DistinctBy(p=>p.Id)
                .Where(p=>p.Y<region.Y+region.Height&&region.Y<p.Y+p.Height).OrderBy(p=>p.X);
            foreach(var obstacle in obstacles)
                if(x<obstacle.X+obstacle.Width+20&&x+region.Width+20>obstacle.X)x=obstacle.X+obstacle.Width+gap;
            var positioned=region with { X=x };placed[region.Id]=positioned;
            foreach(int band in Bands(positioned)){if(!bands.ContainsKey(band))bands[band]=[];bands[band].Add(positioned);}
        }
        var locations=placed.Values.SelectMany(p=>p.Nodes.Select(n=>(Project:p,Node:n))).ToDictionary(x=>x.Node.Id);
        var local=placed.Keys.ToDictionary(id=>id,_=>new List<RenderConnection>());var cross=new List<RenderConnection>();
        foreach(var edge in edges)
        {
            var a=locations[edge.SourceId];var b=locations[edge.TargetId];bool same=a.Project.Id==b.Project.Id;
            double ax=a.Project.X+a.Node.X+(edge.IsTree?12:a.Node.Width),ay=a.Project.Y+a.Node.Y+(edge.IsTree?a.Node.Height:a.Node.Height/2);
            double bx=b.Project.X+b.Node.X,by=b.Project.Y+b.Node.Y+b.Node.Height/2;
            double gutter=edge.IsTree?ax:a.Project.X+a.Project.Width+gap/2;
            DrawingPoint[] points=ay==by?[new(ax,ay),new(bx,by)]:[new(ax,ay),new(gutter,ay),new(gutter,by),new(bx,by)];
            if(same)local[a.Project.Id].Add(edge with {Points=points.Select(p=>p with { X=p.X-a.Project.X,Y=p.Y-a.Project.Y }).ToArray()});
            else cross.Add(edge with {Points=points});
        }
        model.Projects=placed.Values.Select(p=>p with {Connections=local[p.Id].ToArray()}).ToArray();model.CrossProjectConnections=cross.ToArray();
        model.Width=model.Projects.Select(p=>p.X+p.Width+40).DefaultIfEmpty(400).Max();
        model.Height=model.Projects.Select(p=>p.Y+p.Height+40).DefaultIfEmpty(200).Max();return model;
    }
}
