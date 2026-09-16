using System;
using System.Collections.Generic;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core2.Models;
namespace StandardIo.ArchitectureDiagram.Core2.Services.Processings.Layout;
internal sealed class GutterSpacingLayoutRuleProcessingService : ILayoutRuleProcessingService
{
    public void ApplyRule(RenderModel model)
    {
        if (model.IsProjectGraph || model.DiagramType != DiagramTypes.Architecture) return;
        foreach (var project in model.Projects)
        {
            var nodes=project.Nodes.ToDictionary(node=>node.Id);
            var rows=project.Nodes.GroupBy(node=>node.Y).OrderBy(row=>row.Key).ToArray();
            double shift=0;
            for (int index=1;index<rows.Length;index++)
            {
                var upper=rows[index-1];var lower=rows[index];
                var spans=project.Connections.Where(edge=>nodes[edge.SourceId].Y<nodes[edge.TargetId].Y &&
                    (nodes[edge.SourceId].Y==upper.Key && nodes[edge.TargetId].Y>=lower.Key || nodes[edge.TargetId].Y==lower.Key && nodes[edge.SourceId].Y<=upper.Key))
                    .GroupBy(edge=>edge.TargetId).Select(group=>
                    {
                        var xs=group.Select(edge=>LayoutGraph.Centre(nodes[edge.SourceId])).Append(LayoutGraph.Centre(nodes[group.Key])).ToArray();
                        return (Left:xs.Min(),Right:xs.Max());
                    }).Where(span=>span.Right-span.Left>LayoutGraph.Tolerance).OrderBy(span=>span.Left).ToArray();
                var ends=new List<double>();
                foreach(var span in spans)
                {
                    int lane=ends.FindIndex(end=>end<span.Left);
                    if(lane<0) ends.Add(span.Right); else ends[lane]=span.Right;
                }
                double gap=lower.Key-upper.Max(node=>node.Y+node.Height);
                double required=2*model.Configuration.HorizontalOffset*(ends.Count+1);
                shift+=Math.Max(0,required-gap);
                foreach(var node in lower)
                {
                    int position=Array.FindIndex(project.Nodes,current=>current.Id==node.Id);
                    project.Nodes[position]=node with { Y=node.Y+shift };
                }
            }
        }
    }
}
