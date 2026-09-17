using System.Linq;
using StandardIo.ArchitectureDiagram.Core2.Models;
namespace StandardIo.ArchitectureDiagram.Core2.Services.Processings.Layout;
internal sealed class CanvasBoundsLayoutRuleProcessingService : ArchitectureLayoutRuleProcessingService
{
    protected override void ApplyArchitectureRule(RenderModel model)
    {
        double shift=40-model.Projects.Select(p=>p.X).DefaultIfEmpty(40).Min();
        for(int i=0;i<model.Projects.Length;i++) model.Projects[i]=model.Projects[i] with {X=model.Projects[i].X+shift};
        model.Width=model.Projects.Select(p=>p.X+p.Width+40).DefaultIfEmpty(300).Max();
        model.Height=model.Projects.Select(p=>p.Y+p.Height+40).DefaultIfEmpty(200).Max();
    }
}
