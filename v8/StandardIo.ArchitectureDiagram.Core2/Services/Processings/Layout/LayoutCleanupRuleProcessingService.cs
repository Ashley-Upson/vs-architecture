using System.Linq;
using StandardIo.ArchitectureDiagram.Core2.Models;
namespace StandardIo.ArchitectureDiagram.Core2.Services.Processings.Layout;
internal sealed class LayoutCleanupRuleProcessingService : ArchitectureLayoutRuleProcessingService
{
    protected override void ApplyArchitectureRule(RenderModel model)
    {
        var saved=model.Projects.Select(p=>p.Nodes.ToArray()).ToArray();
        var before=model.Projects.Select(p=>LayoutQuality.Measure(model,p)).ToArray();
        BranchSpacingLayoutRuleProcessingService.ArrangeBranches(model,reclaimSpace:true);
        for(int i=0;i<model.Projects.Length;i++)
            if(model.LayoutInitialized && LayoutQuality.Measure(model,model.Projects[i]).CompareTo(before[i]) >= 0)
                System.Array.Copy(saved[i],model.Projects[i].Nodes,saved[i].Length);
    }
}
