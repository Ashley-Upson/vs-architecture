// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using StandardIo.ArchitectureDiagram.Core2.Models;
namespace StandardIo.ArchitectureDiagram.Core2.Services.Orchestrations.Rendering;
internal interface ILayoutOrchestrationService
{
    RenderModel BuildRenderModel(RenderModel renderModel);
}