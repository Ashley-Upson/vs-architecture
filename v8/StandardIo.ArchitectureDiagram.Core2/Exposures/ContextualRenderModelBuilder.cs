using StandardIo.ArchitectureDiagram.Core2.Models;
using StandardIo.ArchitectureDiagram.Core2.Services.Orchestrations.Rendering;
namespace StandardIo.ArchitectureDiagram.Core2.Exposures;
public sealed class ContextualRenderModelBuilder : IRenderModelBuilder
{
    private readonly IContextualLayoutOrchestrationService service;
    internal ContextualRenderModelBuilder(IContextualLayoutOrchestrationService service) => this.service = service;
    public RenderModel BuildRenderModel(RenderModel model) => service.BuildRenderModel(model);
}
