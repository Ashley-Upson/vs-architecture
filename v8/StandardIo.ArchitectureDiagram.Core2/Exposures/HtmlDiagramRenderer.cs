using StandardIo.ArchitectureDiagram.Core2.Models;
using StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Rendering;
using StandardIo.ArchitectureDiagram.Core2.Services.Processings.Rendering;
namespace StandardIo.ArchitectureDiagram.Core2;
public sealed class HtmlDiagramRenderer : IDiagramRenderer
{
    public DiagramRendererRule Rule { get; } = new("html", ".html", ".htm");
    private readonly IProjectModelRenderingProcessingService processingService;
    private readonly IHtmlDocumentService documentService;
    public HtmlDiagramRenderer() : this(new ProjectModelRenderingProcessingService(new ProjectModelPresentationService(), new ProjectModelLayoutService()), new HtmlDocumentService()) { }
    internal HtmlDiagramRenderer(IProjectModelRenderingProcessingService processingService, IHtmlDocumentService documentService)
    {
        this.processingService = processingService;
        this.documentService = documentService;
    }
    public byte[] Render(ProjectModel[] projectModels) => this.documentService.Render(drawings: this.processingService.Prepare(projectModels: projectModels));
}
