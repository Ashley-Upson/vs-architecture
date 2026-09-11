// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core2.Models;
using StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Rendering;
using StandardIo.ArchitectureDiagram.Core2.Services.Processings.Rendering;
namespace StandardIo.ArchitectureDiagram.Core2.Services.Orchestrations.Rendering;
internal sealed class LayoutOrchestrationService(IProjectModelLayoutService layoutService, IRenderModelProcessingService renderModelProcessingService, StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Commands.IRenderConfigurationService configurationService) : ILayoutOrchestrationService
{
    public RenderModel BuildRenderModel(RenderModel renderModel)
    {
        ArgumentNullException.ThrowIfNull(renderModel);
        ArgumentNullException.ThrowIfNull(renderModel.ProjectModels);
        configurationService.Validate(renderModel.Configuration);
        renderModelProcessingService.PrepareRenderModel(renderModel);
        return layoutService.Layout(renderModel);
    }
}
