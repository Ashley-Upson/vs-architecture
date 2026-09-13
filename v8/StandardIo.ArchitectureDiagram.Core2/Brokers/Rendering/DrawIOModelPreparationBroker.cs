// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using StandardIo.ArchitectureDiagram.Core2.Models;
using StandardIo.ArchitectureDiagram.Core2.Exposures;
namespace StandardIo.ArchitectureDiagram.Core2.Brokers.Rendering;
internal sealed class DrawIOModelPreparationBroker(IRenderModelBuilderFactory renderModelBuilderFactory) : IDrawIOModelPreparationBroker
{
    public RenderModel PrepareRenderModel(RenderModel renderModel) => renderModelBuilderFactory.CreateRenderModelBuilder(namedKey: renderModel.DiagramType.ToString()).BuildRenderModel(renderModel: renderModel);
}