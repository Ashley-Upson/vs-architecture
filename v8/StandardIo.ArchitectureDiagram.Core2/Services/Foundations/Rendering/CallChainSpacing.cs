using System;
using System.Collections.Generic;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core2.Models;

namespace StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Rendering;

internal static class CallChainSpacing
{
    public static RenderProject[] CompactContainers(RenderProject[] projects,RenderConnection[] edges,Func<string,string> root,RenderConfiguration configuration)
    {
        double gap=Math.Max(60,configuration.Composition.ProjectSpacing),step=configuration.Composition.NodeWidth+28+gap;
        double clearance=Math.Max(10,configuration.HorizontalOffset);
        var components=projects.SelectMany(p=>p.Nodes).ToDictionary(n=>n.Id,n=>n.Id);
        string Component(string id){while(components[id]!=id){components[id]=components[components[id]];id=components[id];}return id;}
        foreach(var edge in edges)components[Component(edge.TargetId)]=Component(edge.SourceId);
        var callers=edges.Where(e=>!e.IsTree).ToLookup(e=>e.TargetId,e=>e.SourceId);
        var compacted=projects.Select(project=>{
            var positioned=new Dictionary<string,RenderNode>();
            double Desired(IEnumerable<RenderNode> unit)
            {
                var heading=unit.Single(n=>root(n.Id)==n.Id);var candidates=new List<double>();double offset=0;
                foreach(var method in unit.Where(n=>n.Id!=heading.Id).OrderBy(n=>n.Y))
                {
                    offset=offset==0?method.Y-heading.Y:offset+30;
                    foreach(string caller in callers[method.Id].Where(positioned.ContainsKey))
                        candidates.Add(positioned[caller].Y+positioned[caller].Height/2-offset-method.Height/2);
                    offset+=method.Height;
                }
                candidates.Sort();return candidates.Count==0?40:Math.Max(40,candidates[candidates.Count/2]);
            }
            foreach(var column in project.Nodes.GroupBy(n=>(int)Math.Round((n.X-20)/step)).OrderBy(g=>g.Key))
            {
                double end=40;string? previous=null;
                foreach(var unit in column.GroupBy(n=>root(n.Id)).OrderBy(Desired).ThenBy(g=>g.Min(n=>n.Y)))
                {
                    var heading=unit.Single(n=>n.Id==unit.Key);string component=Component(heading.Id);
                    double top=Math.Max(Desired(unit),previous is null?40:end+(previous==component?configuration.Composition.NodeSpacing:gap));
                    positioned[heading.Id]=Move(heading,top-heading.Y);end=top+heading.Height;
                    bool first=true;
                    foreach(var method in unit.Where(n=>n.Id!=unit.Key).OrderBy(n=>n.Y))
                    {
                        double y=first?top+method.Y-heading.Y:end+30;
                        positioned[method.Id]=Move(method,y-method.Y);end=y+method.Height;first=false;
                    }
                    previous=component;
                }
            }
            return project with {Nodes=project.Nodes.Select(n=>positioned[n.Id]).ToArray(),Height=positioned.Values.Max(n=>n.Y+n.Height)+20};
        }).ToArray();
        var localModel=new RenderModel(0,0,compacted);
        ClearExits(localModel,edges,root,step,clearance);
        var locations=localModel.Projects.SelectMany(p=>p.Nodes.Select(n=>(n.Id,Project:p,Node:n))).ToDictionary(n=>n.Id);
        var outgoing=edges.Where(e=>!e.IsTree&&locations[e.SourceId].Project.Id!=locations[e.TargetId].Project.Id).ToLookup(e=>locations[e.SourceId].Project.Id);
        var incoming=edges.Where(e=>!e.IsTree&&locations[e.SourceId].Project.Id!=locations[e.TargetId].Project.Id).ToLookup(e=>locations[e.TargetId].Project.Id);
        var placed=new List<RenderProject>();
        var placedById=new Dictionary<string,RenderProject>();
        double PreferredY(RenderProject region)
        {
            var candidates=incoming[region.Id].Where(e=>placedById.ContainsKey(locations[e.SourceId].Project.Id)).Select(e=>{
                var source=locations[e.SourceId];var target=locations[e.TargetId];
                return placedById[source.Project.Id].Y+source.Node.Y+source.Node.Height/2-target.Node.Y-target.Node.Height/2;
            }).OrderBy(y=>y).ToArray();
            return candidates.Length==0?40:Math.Max(40,candidates[candidates.Length/2]);
        }
        var exits=new List<(double Left,double Right,double Y,string Target)>();
        foreach(var column in localModel.Projects.GroupBy(p=>p.X).OrderBy(g=>g.Key))
        foreach(var region in column.OrderBy(PreferredY).ThenBy(p=>p.Y))
        {
            var forbidden=placed.Where(p=>p.X<region.X+region.Width&&region.X<p.X+p.Width)
                .Select(p=>(Start:p.Y-region.Height-20,End:p.Y+p.Height+20)).ToList();
            foreach(var exit in exits.Where(e=>e.Left<region.X+region.Width&&e.Right>region.X))
            foreach(var node in region.Nodes.Where(n=>n.Id!=exit.Target&&n.X+region.X<exit.Right&&exit.Left<n.X+region.X+n.Width))
                forbidden.Add((exit.Y-node.Y-node.Height-clearance,exit.Y-node.Y+clearance));
            double y=PreferredY(region);
            foreach(var interval in forbidden.OrderBy(i=>i.Start))if(y>interval.Start&&y<interval.End)y=interval.End;
            var positioned=region with {Y=y};placed.Add(positioned);placedById[region.Id]=positioned;
            foreach(var edge in outgoing[region.Id])
            {
                var source=locations[edge.SourceId].Node;
                var heading=locations[root(edge.TargetId)];
                exits.Add((region.X+source.X+source.Width,heading.Project.X+heading.Node.X-clearance,y+source.Y+source.Height/2,edge.TargetId));
            }
        }
        return placed.ToArray();
    }

    public static (RenderProject[] Projects,Dictionary<string,double> Gutters) ReserveTracks(
        RenderProject[] projects,RenderConnection[] edges,Func<string,string> root,double gap,double spacing)
    {
        var nodes=projects.SelectMany(p=>p.Nodes.Select(n=>n with {X=n.X+p.X,Y=n.Y+p.Y,
            TextLines=n.TextLines.Select(t=>t with {X=t.X+p.X,Y=t.Y+p.Y}).ToArray()})).ToDictionary(n=>n.Id);
        var columns=new Dictionary<string,int>();
        double columnWidth=nodes.Values.Select(n=>n.Width+28).DefaultIfEmpty(328).Max();
        double left=double.NegativeInfinity;int column=-1;
        foreach(var unit in nodes.Values.GroupBy(n=>root(n.Id)).OrderBy(g=>nodes[g.Key].X))
        {
            if(nodes[unit.Key].X>=left+columnWidth){left=nodes[unit.Key].X;column++;}
            foreach(var node in unit)columns[node.Id]=column;
        }
        var lanes=new Dictionary<string,(int Column,int Lane)>();
        var counts=new int[column+1];
        // One vertical interval per destination. Reuse a lane only when intervals
        // do not overlap, then reserve its width before positioning any columns.
        foreach(var channel in edges.Where(e=>!e.IsTree).GroupBy(e=>columns[e.TargetId]-1))
        {
            var ends=new List<double>();
            var destinations=channel.GroupBy(e=>e.TargetId).Select(group=>new {
                Edges=group.ToArray(),Top=group.Min(e=>Math.Min(nodes[e.SourceId].Y+nodes[e.SourceId].Height/2,nodes[e.TargetId].Y+nodes[e.TargetId].Height/2)),
                Bottom=group.Max(e=>Math.Max(nodes[e.SourceId].Y+nodes[e.SourceId].Height/2,nodes[e.TargetId].Y+nodes[e.TargetId].Height/2))
            }).OrderBy(g=>g.Top).ThenBy(g=>g.Bottom);
            foreach(var destination in destinations)
            {
                int lane=ends.FindIndex(end=>end<=destination.Top);
                if(lane<0){lane=ends.Count;ends.Add(destination.Bottom);}else ends[lane]=destination.Bottom;
                foreach(var edge in destination.Edges)lanes[edge.Id]=(channel.Key,lane);
            }
            counts[channel.Key]=ends.Count;
        }
        var starts=new double[column+1];double x=60;
        for(int i=0;i<starts.Length;i++){starts[i]=x;x+=columnWidth+Math.Max(gap,(counts[i]+1)*spacing+40);}
        var positioned=nodes.Values.ToDictionary(n=>n.Id,n=>{
            double offset=starts[columns[n.Id]]-nodes[root(n.Id)].X;
            return n with {X=n.X+offset,TextLines=n.TextLines.Select(t=>t with {X=t.X+offset}).ToArray()};
        });
        var regions=projects.Select(p=>{
            var contents=p.Nodes.Select(n=>positioned[n.Id]).ToArray();
            double px=contents.Min(n=>n.X)-20,py=contents.Min(n=>n.Y)-40;
            return p with {X=px,Y=py,Width=contents.Max(n=>n.X+n.Width)-px+20,Height=contents.Max(n=>n.Y+n.Height)-py+20,
                Nodes=contents.Select(n=>n with {X=n.X-px,Y=n.Y-py,TextLines=n.TextLines.Select(t=>t with {X=t.X-px,Y=t.Y-py}).ToArray()}).ToArray()};
        }).ToArray();
        return (regions,lanes.ToDictionary(e=>e.Key,e=>starts[e.Value.Column]+columnWidth+20+(e.Value.Lane+1)*spacing));
    }

    public static void CompactTrees(RenderModel model,RenderConnection[] edges,double gap)
    {
        var nodes=model.Projects.SelectMany(p=>p.Nodes.Select(n=>(Node:n,Y:p.Y+n.Y))).ToArray();
        var sets=nodes.ToDictionary(n=>n.Node.Id,n=>n.Node.Id);
        string Root(string id)
        {
            while(sets[id]!=id){sets[id]=sets[sets[id]];id=sets[id];}
            return id;
        }
        foreach(var edge in edges)sets[Root(edge.TargetId)]=Root(edge.SourceId);
        var offsets=new Dictionary<string,double>();
        double cursor=nodes.Select(n=>n.Y).DefaultIfEmpty(0).Min();
        foreach(var tree in nodes.GroupBy(n=>Root(n.Node.Id)).OrderBy(g=>g.Min(n=>n.Y)))
        {
            double top=tree.Min(n=>n.Y),bottom=tree.Max(n=>n.Y+n.Node.Height);
            foreach(var node in tree)offsets[node.Node.Id]=cursor-top;
            cursor+=bottom-top+gap;
        }
        model.Projects=model.Projects.Select(p=>p with {
            Nodes=p.Nodes.Select(n=>Move(n,offsets[n.Id])).ToArray()
        }).ToArray();
    }

    // Reserve caller exit rows before drawing links. Moving a complete type unit
    // preserves its method spacing and tree spine; no per-node route detour is needed.
    public static void ClearExits(RenderModel model,RenderConnection[] edges,Func<string,string> root,double step,double clearance)
    {
        var outgoing=edges.Where(e=>!e.IsTree).ToLookup(e=>e.SourceId);
        var owners=model.Projects.SelectMany(p=>p.Nodes.Select(n=>(n.Id,Project:p.Id))).ToDictionary(n=>n.Id,n=>n.Project);
        var positions=model.Projects.SelectMany(p=>p.Nodes).ToDictionary(n=>n.Id);
        model.Projects=model.Projects.Select(project=>
        {
            var exits=new List<(double Y,double Right,string Target)>();
            foreach(var column in project.Nodes.GroupBy(n=>(int)Math.Round((n.X-40)/step)).OrderBy(g=>g.Key))
            {
                double bottom=double.NegativeInfinity;
                foreach(var unit in column.GroupBy(n=>root(n.Id)).OrderBy(g=>g.Min(n=>n.Y)))
                {
                    var members=unit.ToArray();
                    double shift=Math.Max(0,bottom+clearance-members.Min(n=>n.Y));
                    var forbidden=exits.Where(e=>e.Right>members.Min(n=>n.X))
                        .SelectMany(e=>members.Where(n=>n.Id!=e.Target&&n.X<e.Right)
                            .Select(n=>(Start:e.Y-n.Y-n.Height-clearance,End:e.Y-n.Y+clearance)))
                        .OrderBy(range=>range.Start);
                    foreach(var range in forbidden)
                        if(shift>range.Start&&shift<range.End)shift=range.End;
                    foreach(var node in members)positions[node.Id]=Move(node,shift);
                    bottom=members.Max(n=>n.Y+n.Height)+shift;
                }
                foreach(var node in column.Select(n=>positions[n.Id]))
                foreach(var edge in outgoing[node.Id])
                    exits.Add((node.Y+node.Height/2,owners[edge.TargetId]==project.Id?positions[edge.TargetId].X:double.PositiveInfinity,edge.TargetId));
            }
            var contents=project.Nodes.Select(n=>positions[n.Id]).ToArray();
            return project with {Nodes=contents,Height=Math.Max(project.Height,contents.Select(n=>n.Y+n.Height+20).DefaultIfEmpty(0).Max())};
        }).ToArray();
    }

    private static RenderNode Move(RenderNode node,double offset)=>node with {
        Y=node.Y+offset,TextLines=node.TextLines.Select(t=>t with {Y=t.Y+offset}).ToArray()
    };
}
