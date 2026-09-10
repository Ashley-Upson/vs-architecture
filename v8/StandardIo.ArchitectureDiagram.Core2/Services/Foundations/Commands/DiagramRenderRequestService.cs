using System.Threading;
using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.Core2.Factories;
using StandardIo.ArchitectureDiagram.Core2.Models;
using StandardIo.ArchitectureDiagram.Core2.Services.Orchestrations.Generation;
namespace StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Commands;
internal sealed class DiagramRenderRequestService(IDiagramRendererFactory rendererFactory,
    IDiagramGenerationOrchestrationService generationService) : IDiagramRenderRequestService
{
    public Task<byte[]> RenderAsync(DiagramRenderRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IDiagramRenderer renderer = rendererFactory.Create(format: request.Format, outputPath: request.OutputPath);
        return generationService.GenerateAsync(request: new DiagramGenerationRequest
        {
            ProjectPaths = request.ProjectPaths, DiagramType = request.DiagramType
        }, cancellationToken: cancellationToken, renderer: renderer);
    }
}
