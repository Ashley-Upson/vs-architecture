using StandardIo.ArchitectureDiagram.Core2.Models;
using StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Rendering;
using StandardIo.ArchitectureDiagram.Core2.Services.Processings.Rendering;
namespace StandardIo.ArchitectureDiagram.Core2;
public sealed class DrawIODiagramRenderer : IDiagramRenderer
{
    public DiagramRendererRule Rule { get; } = new("drawio", ".drawio");
    private readonly IProjectModelRenderingProcessingService processingService;
    private readonly IDrawIODocumentService documentService;
    public DrawIODiagramRenderer() : this(new ProjectModelRenderingProcessingService(new ProjectModelPresentationService(), new ProjectModelLayoutService()), new DrawIODocumentService()) { }
    internal DrawIODiagramRenderer(IProjectModelRenderingProcessingService processingService, IDrawIODocumentService documentService)
    {
        this.processingService = processingService;
        this.documentService = documentService;
    }
    public byte[] Render(ProjectModel[] projectModels) => this.documentService.Render(drawings: this.processingService.Prepare(projectModels: projectModels));
}
