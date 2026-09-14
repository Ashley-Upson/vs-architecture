using System;
using System.Collections.Generic;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core2.Models;
namespace StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Rendering;
internal interface IDataModelRoutingService { void Route(RenderModel model); }
internal sealed class DataModelRoutingService : IDataModelRoutingService
{
    public void Route(RenderModel model)
    {
        var nodes=model.Projects.SelectMany(p=>p.Nodes.Select(n=>n with {X=n.X+p.X,Y=n.Y+p.Y})).ToArray();
        var byId=nodes.ToDictionary(n=>n.Id);
        var xs=nodes.SelectMany(n=>new[]{n.X-16,n.X+n.Width+16}).Distinct().ToArray();
        var ys=nodes.SelectMany(n=>new[]{n.Y-16,n.Y+n.Height+16}).Distinct().ToArray();
        DrawingPoint[] Ports(RenderNode n)=>[new(n.X,n.Y+n.Height/2),new(n.X+n.Width,n.Y+n.Height/2),new(n.X+n.Width/2,n.Y),new(n.X+n.Width/2,n.Y+n.Height)];
        bool Clear(DrawingPoint[] points)
        {
            for(int i=1;i<points.Length;i++)
            foreach(var n in nodes)
            {
                var a=points[i-1];var b=points[i];
                if(a.X==b.X ? a.X>n.X&&a.X<n.X+n.Width&&Math.Max(a.Y,b.Y)>n.Y&&Math.Min(a.Y,b.Y)<n.Y+n.Height
                    : a.Y>n.Y&&a.Y<n.Y+n.Height&&Math.Max(a.X,b.X)>n.X&&Math.Min(a.X,b.X)<n.X+n.Width)return false;
            }
            return true;
        }
        DrawingPoint[] Simplify(DrawingPoint[] points)
        {
            var result=new List<DrawingPoint>();
            foreach(var p in points)
            {
                if(result.Count>0&&result[^1]==p)continue;
                if(result.Count>1&&(result[^2].X==result[^1].X&&result[^1].X==p.X||result[^2].Y==result[^1].Y&&result[^1].Y==p.Y))result.RemoveAt(result.Count-1);
                result.Add(p);
            }
            return result.ToArray();
        }
        DrawingPoint[] Find(RenderConnection edge)
        {
            var candidates=new List<DrawingPoint[]>();
            foreach(var a in Ports(byId[edge.SourceId]))
            foreach(var b in Ports(byId[edge.TargetId]))
            {
                candidates.Add([a,new(a.X,b.Y),b]);candidates.Add([a,new(b.X,a.Y),b]);
                foreach(double x in xs)candidates.Add([a,new(x,a.Y),new(x,b.Y),b]);
                foreach(double y in ys)candidates.Add([a,new(a.X,y),new(b.X,y),b]);
            }
            double Cost(DrawingPoint[] points)=>points.Zip(points.Skip(1)).Sum(p=>Math.Abs(p.First.X-p.Second.X)+Math.Abs(p.First.Y-p.Second.Y))+(points.Length-2)*12;
            return candidates.Select(Simplify).Where(p=>p.Length>=(edge.SourceId==edge.TargetId?4:2) && p[0]!=p[^1]).OrderBy(Cost).FirstOrDefault(Clear) ?? edge.Points;
        }
        foreach(var project in model.Projects)
        for(int i=0;i<project.Connections.Length;i++)
        {
            var edge=project.Connections[i];
            var global=edge with {Points=edge.Points.Select(p=>new DrawingPoint(p.X+project.X,p.Y+project.Y)).ToArray()};
            project.Connections[i]=edge with {Points=Find(global).Select(p=>new DrawingPoint(p.X-project.X,p.Y-project.Y)).ToArray()};
        }
        for(int i=0;i<model.CrossProjectConnections.Length;i++)
            model.CrossProjectConnections[i]=model.CrossProjectConnections[i] with {Points=Find(model.CrossProjectConnections[i])};
    }
}
