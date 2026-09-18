using System;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core2.Models;
namespace StandardIo.ArchitectureDiagram.Core2.Services.Processings.Layout;
internal sealed class ProjectPositioningLayoutRuleProcessingService(DepthLayoutRuleProcessingService depthRule) : ArchitectureLayoutRuleProcessingService
{
    protected override void ApplyArchitectureRule(RenderModel model) => ProjectLayout.Apply(model,containers=>
    {
        depthRule.ApplyRule(containers);
        var graph=containers.Projects[0];
        double top=40;
        foreach(var row in graph.Nodes.GroupBy(n=>n.Y).OrderBy(g=>g.Key).ToArray())
        {
            foreach(var node in row)
            {
                int index=Array.FindIndex(graph.Nodes,n=>n.Id==node.Id);
                graph.Nodes[index]=node with {Y=top};
            }
            top+=row.Max(n=>n.Height)+model.Configuration.Architecture.ProjectSpacing;
        }
    });
}
