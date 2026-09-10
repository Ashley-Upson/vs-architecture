// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using StandardIo.ArchitectureDiagram.Core2.Models;
using StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Rendering;
using StandardIo.ArchitectureDiagram.Core2.Brokers.Rendering;

namespace StandardIo.ArchitectureDiagram.Core2.Exposures;
public sealed class HtmlDiagramRenderer : IDiagramRenderer
{

    private readonly IHtmlModelPreparationService preparationService;
    private readonly IHtmlDocumentService documentService;
internal HtmlDiagramRenderer(IHtmlModelPreparationService preparationService, IHtmlDocumentService documentService)
    {
        this.preparationService = preparationService;
        this.documentService = documentService;
    }

    public byte[] Render(RenderModel renderModel) => this.documentService.Render(renderModel: this.preparationService.PrepareRenderModel(renderModel: renderModel));
}