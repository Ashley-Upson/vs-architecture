// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using StandardIo.ArchitectureDiagram.Core2.Models;
using StandardIo.ArchitectureDiagram.Core2.Services.Orchestrations.Rendering;
using StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Rendering;
using StandardIo.ArchitectureDiagram.Core2.Services.Processings.Rendering;
namespace StandardIo.ArchitectureDiagram.Core2.Exposures;
public sealed class LayoutModelBuilder : IRenderModelBuilder
{
    private readonly ILayoutOrchestrationService layoutOrchestrationService;
internal LayoutModelBuilder(ILayoutOrchestrationService layoutOrchestrationService) => this.layoutOrchestrationService = layoutOrchestrationService;
    public RenderModel BuildRenderModel(RenderModel renderModel) => this.layoutOrchestrationService.BuildRenderModel(renderModel: renderModel);
}