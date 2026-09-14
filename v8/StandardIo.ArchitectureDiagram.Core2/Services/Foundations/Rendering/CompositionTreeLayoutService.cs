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
            var nodes = tree.Nodes.Select((n,i) => new RenderNode(n.Id,n.TypeName,n.Label,n.Depth == 0 ? "#075985" : "#334155",
                40+n.Depth*28,60+i*44,config.NodeWidth,30,
                [new(n.Label,40+n.Depth*28+config.NodeWidth/2,75+i*44,n.Depth==0,12)])).ToArray();
            var byId = nodes.ToDictionary(n=>n.Id);
            var edges = tree.Nodes.Where(n=>n.ParentId!=null).Select(n =>
            {
                var a = byId[n.ParentId!]; var b = byId[n.Id];
                return new RenderConnection("edge-"+n.Id,a.Id,b.Id,a.TypeName,b.TypeName,false,
                    [new(a.X+12,a.Y+a.Height),new(a.X+12,b.Y+b.Height/2),new(b.X,b.Y+b.Height/2)]) { IsTree=true };
            }).ToArray();
            projects.Add(new("composition-"+projects.Count,tree.Title,0,0,nodes.Max(n=>n.X+n.Width)+40,nodes.Max(n=>n.Y+n.Height)+40,nodes,edges));
        }
        double spacing = config.ProjectSpacing;
        double x=40,height=0;
        for(int i=0;i<projects.Count;i++)
        {
            var p=projects[i];
            projects[i]=p with { X=x,Y=40 }; x+=p.Width+spacing; height=Math.Max(height,p.Height);
        }
        model.Projects=projects.ToArray();model.CrossProjectConnections=[];
        model.Width=projects.Select(p=>p.X+p.Width+40).DefaultIfEmpty(400).Max();model.Height=height+80;
        return model;
    }
}
