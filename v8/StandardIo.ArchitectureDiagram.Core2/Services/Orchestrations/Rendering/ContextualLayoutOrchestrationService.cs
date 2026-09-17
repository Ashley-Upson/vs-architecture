using StandardIo.ArchitectureDiagram.Core2.Models;
using StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Rendering;
namespace StandardIo.ArchitectureDiagram.Core2.Services.Orchestrations.Rendering;
internal interface IContextualLayoutOrchestrationService { RenderModel BuildRenderModel(RenderModel model); }
internal sealed class ContextualLayoutOrchestrationService(IContextualModelService projection, IContextualLayoutService layout, IProjectModelLayoutService rules) : IContextualLayoutOrchestrationService
{
    public RenderModel BuildRenderModel(RenderModel model)
    {
        layout.Layout(model, projection.Prepare(model));
        model.LayoutInitialized=true;
        return rules.Layout(model);
    }
}
