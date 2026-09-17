using System;
using System.Collections.Generic;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core2.Models;
namespace StandardIo.ArchitectureDiagram.Core2.Services.Processings.Layout;
internal static class LayoutPassages
{
    internal static HashSet<(string Source,string Obstacle)> Blocked(RenderModel model,RenderProject project)
    {
        var nodes=project.Nodes.ToDictionary(n=>n.Id);
        var outgoing=project.Connections.Concat(model.CrossProjectConnections.Where(e=>nodes.ContainsKey(e.SourceId))).GroupBy(e=>e.SourceId).ToArray();
        var result=new HashSet<(string Source,string Obstacle)>();
        double clearance=model.Configuration.HorizontalOffset;
        foreach(var node in project.Nodes) foreach(var group in outgoing)
        {
            var source=nodes[group.Key];
            if(source.Y+source.Height>=node.Y) continue;
            int count=group.Count(e=>!nodes.ContainsKey(e.TargetId)||nodes[e.TargetId].Y>node.Y);
            if(count==0) continue;
            double radius=group.Count()==1?0:Math.Min(source.Width/2-5,count*clearance);
            if(node.X<LayoutGraph.Centre(source)+radius+clearance-LayoutGraph.Tolerance &&
                node.X+node.Width>LayoutGraph.Centre(source)-radius-clearance+LayoutGraph.Tolerance)
                result.Add((source.Id,node.Id));
        }
        return result;
    }
}
