// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using StandardIo.ArchitectureDiagram.Core2.Models;
using StandardIo.ArchitectureDiagram.Core2.Brokers.Rendering;
namespace StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Rendering;
internal sealed class DrawIOModelPreparationService(IDrawIOModelPreparationBroker modelPreparationBroker) : IDrawIOModelPreparationService
{
    public RenderModel PrepareRenderModel(RenderModel renderModel) => modelPreparationBroker.PrepareRenderModel(renderModel: renderModel);
}