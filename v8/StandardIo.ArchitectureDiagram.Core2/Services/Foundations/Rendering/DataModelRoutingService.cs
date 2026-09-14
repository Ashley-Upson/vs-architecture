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
        DrawingPoint[] Ports(RenderNode n,RenderNode other)=>[new(n.X,n.Y+n.Height/2),new(n.X+n.Width,n.Y+n.Height/2),new(n.X+n.Width/2,n.Y),new(n.X+n.Width/2,n.Y+n.Height),
            new(n.X,Math.Clamp(other.Y+other.Height/2,n.Y+12,n.Y+n.Height-12)),new(n.X+n.Width,Math.Clamp(other.Y+other.Height/2,n.Y+12,n.Y+n.Height-12)),
            new(Math.Clamp(other.X+other.Width/2,n.X+12,n.X+n.Width-12),n.Y),new(Math.Clamp(other.X+other.Width/2,n.X+12,n.X+n.Width-12),n.Y+n.Height)];
        DrawingPoint Stub(RenderNode n,DrawingPoint p)=>p.X==n.X?new(p.X-16,p.Y):p.X==n.X+n.Width?new(p.X+16,p.Y):p.Y==n.Y?new(p.X,p.Y-16):new(p.X,p.Y+16);
        bool Outside(RenderNode n,DrawingPoint port,DrawingPoint outside)=>
            port.X==n.X && outside.X<port.X && outside.Y==port.Y ||
            port.X==n.X+n.Width && outside.X>port.X && outside.Y==port.Y ||
            port.Y==n.Y && outside.Y<port.Y && outside.X==port.X ||
            port.Y==n.Y+n.Height && outside.Y>port.Y && outside.X==port.X;
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
        var routed=new List<(RenderConnection Edge,DrawingPoint[] Points)>();
        DrawingPoint[] Find(RenderConnection edge)
        {
            routed.RemoveAll(r=>r.Edge.Id==edge.Id);
            var candidates=new List<DrawingPoint[]>();
            foreach(var a in Ports(byId[edge.SourceId],byId[edge.TargetId]).Distinct())
            foreach(var b in Ports(byId[edge.TargetId],byId[edge.SourceId]).Distinct())
            {
                var start=Stub(byId[edge.SourceId],a);var end=Stub(byId[edge.TargetId],b);
                candidates.Add([a,start,new(start.X,end.Y),end,b]);candidates.Add([a,start,new(end.X,start.Y),end,b]);
                foreach(double x in xs)candidates.Add([a,start,new(x,start.Y),new(x,end.Y),end,b]);
                foreach(double y in ys)candidates.Add([a,start,new(start.X,y),new(end.X,y),end,b]);
            }
            double DistanceCost(DrawingPoint[] points)=>points.Zip(points.Skip(1)).Sum(p=>Math.Abs(p.First.X-p.Second.X)+Math.Abs(p.First.Y-p.Second.Y))+(points.Length-2)*80;
            double Conflicts(DrawingPoint[] points)
            {
                double cost=0;
                foreach(var previous in routed)
                {
                    if(previous.Edge.SourceId==edge.SourceId&&previous.Edge.TargetId==edge.TargetId)continue;
                    for(int i=1;i<points.Length;i++)for(int j=1;j<previous.Points.Length;j++)
                    {
                        var a=points[i-1];var b=points[i];var c=previous.Points[j-1];var d=previous.Points[j];
                        bool horizontal=a.Y==b.Y,otherHorizontal=c.Y==d.Y;
                        if(horizontal!=otherHorizontal)
                        {
                            var h1=horizontal?a:c;var h2=horizontal?b:d;var v1=horizontal?c:a;var v2=horizontal?d:b;
                            if(v1.X>Math.Min(h1.X,h2.X)&&v1.X<Math.Max(h1.X,h2.X)&&h1.Y>Math.Min(v1.Y,v2.Y)&&h1.Y<Math.Max(v1.Y,v2.Y))cost+=2400;
                        }
                        else if(horizontal&&a.Y==c.Y)cost+=2*Math.Max(0,Math.Min(Math.Max(a.X,b.X),Math.Max(c.X,d.X))-Math.Max(Math.Min(a.X,b.X),Math.Min(c.X,d.X)));
                        else if(!horizontal&&a.X==c.X)cost+=2*Math.Max(0,Math.Min(Math.Max(a.Y,b.Y),Math.Max(c.Y,d.Y))-Math.Max(Math.Min(a.Y,b.Y),Math.Min(c.Y,d.Y)));
                    }
                }
                return cost;
            }
            var result=candidates.Select(Simplify).Where(p=>p.Length>=(edge.SourceId==edge.TargetId?4:2) && p[0]!=p[^1]).Where(p=>Outside(byId[edge.SourceId],p[0],p[1])&&Outside(byId[edge.TargetId],p[^1],p[^2])).DistinctBy(p=>string.Join(";",p.Select(q=>(q.X,q.Y)))).OrderBy(DistanceCost).Where(Clear).Take(128).OrderBy(p=>DistanceCost(p)+Conflicts(p)).FirstOrDefault() ?? edge.Points;
            routed.Add((edge,result));return result;
        }
        for(int pass=0;pass<2;pass++)
        {
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
}
