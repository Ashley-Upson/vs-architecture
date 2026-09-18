using System;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core2.Models;
namespace StandardIo.ArchitectureDiagram.Core2.Services.Processings.Layout;
internal sealed class ContextualCanvasLayoutRuleProcessingService : ILayoutRuleProcessingService
{
    public void ApplyRule(RenderModel model)
    {
        if(model.DiagramType==DiagramTypes.Architecture) return;
        model.Width=Math.Max(model.Width,Math.Max(400,model.Projects.Select(p=>p.X+p.Width+40).DefaultIfEmpty(400).Max()));
        model.Height=Math.Max(model.Height,Math.Max(200,model.Projects.Select(p=>p.Y+p.Height+40).DefaultIfEmpty(200).Max()));
    }
}
