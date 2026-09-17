using System;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core2.Models;
namespace StandardIo.ArchitectureDiagram.Core2.Services.Processings.Layout;
internal static class LayerLinkClassification
{
    internal static (RenderNodeCategory From, RenderNodeCategory To, int A, int B, bool SameProject, int Layers) Describe(RenderModel model, RenderConnection edge)
    {
        var owners=model.Projects.SelectMany(p=>p.Nodes.Select(n=>(n.Id,Node:n,Project:p))).ToDictionary(x=>x.Id);
        var source=owners[edge.SourceId];var target=owners[edge.TargetId];
        var from=source.Node.Category??RenderNodeCategory.Other;var to=target.Node.Category??RenderNodeCategory.Other;
        var order=source.Project.ArchitecturalLayers;
        return (from,to,Array.IndexOf(order,from),Array.IndexOf(order,to),source.Project.Id==target.Project.Id,order.Length);
    }
    internal static void Apply(RenderModel model, Func<RenderConnection,RenderConnection> update)
    {
        foreach(var project in model.Projects)
            for(int i=0;i<project.Connections.Length;i++) project.Connections[i]=update(project.Connections[i]);
        for(int i=0;i<model.CrossProjectConnections.Length;i++) model.CrossProjectConnections[i]=update(model.CrossProjectConnections[i]);
    }
}
