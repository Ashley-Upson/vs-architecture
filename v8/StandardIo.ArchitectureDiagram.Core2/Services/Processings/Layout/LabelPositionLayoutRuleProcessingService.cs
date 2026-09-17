using System.Linq;
using StandardIo.ArchitectureDiagram.Core2.Models;
namespace StandardIo.ArchitectureDiagram.Core2.Services.Processings.Layout;
internal sealed class LabelPositionLayoutRuleProcessingService : ArchitectureLayoutRuleProcessingService
{
    protected override void ApplyArchitectureRule(RenderModel model)
    {
        foreach (var project in model.Projects)
        for (int index=0;index<project.Nodes.Length;index++)
        {
            var node=project.Nodes[index];
            string[] lines=node.Label.Split('\n');
            project.Nodes[index]=node with { TextLines=lines.Select((line,row)=>new RenderText(line,
                node.X+node.Width/2,node.Y+node.Height/2+(row-(lines.Length-1)/2d)*16,
                row==0,row<node.TextLines.Length?node.TextLines[row].FontSize:12)).ToArray() };
        }
    }
}
