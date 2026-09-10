using System.Threading;
using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.Core2.Models;
using StandardIo.ArchitectureDiagram.Core2.Brokers.ProjectModels;
using StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Generation;
using StandardIo.ArchitectureDiagram.Core2.Services.Orchestrations.Generation;
namespace StandardIo.ArchitectureDiagram.Core2;
public sealed class DiagramGenerator
{
    private readonly IDiagramGenerationOrchestrationService generationService;
    private readonly IDiagramRenderer renderer;
    public DiagramGenerator() : this(new DiagramGenerationOrchestrationService(
        new ProjectModelBuilderService(new ProjectModelBroker(new ProjectModelBuilder())),
        new ProjectModelTreeService(new ProjectModelSplitterBroker(new ProjectModelSplitter()))), new DrawIODiagramRenderer()) { }
    internal DiagramGenerator(IDiagramGenerationOrchestrationService generationService, DrawIODiagramRenderer renderer)
    {
        this.generationService = generationService;
        this.renderer = renderer;
    }
    public Task<byte[]> GenerateAsync(DiagramGenerationRequest request, CancellationToken cancellationToken = default) =>
        this.generationService.GenerateAsync(request: request, cancellationToken: cancellationToken, renderer: this.renderer);
}
