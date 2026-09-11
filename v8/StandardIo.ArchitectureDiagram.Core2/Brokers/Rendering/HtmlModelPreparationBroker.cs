// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using StandardIo.ArchitectureDiagram.Core2.Models;
using StandardIo.ArchitectureDiagram.Core2.Exposures;
namespace StandardIo.ArchitectureDiagram.Core2.Brokers.Rendering;
internal sealed class HtmlModelPreparationBroker(LayoutModelBuilder layoutModelBuilder) : IHtmlModelPreparationBroker
{
    public RenderModel PrepareRenderModel(RenderModel renderModel) => layoutModelBuilder.BuildRenderModel(renderModel: renderModel);
}