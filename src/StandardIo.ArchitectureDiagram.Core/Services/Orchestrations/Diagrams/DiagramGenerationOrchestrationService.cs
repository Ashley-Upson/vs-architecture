using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using StandardIo.ArchitectureDiagram.Core.Models;
using StandardIo.ArchitectureDiagram.Core.Services.Processings.Diagrams;

namespace StandardIo.ArchitectureDiagram.Core.Services.Orchestrations.Diagrams;

public sealed class DiagramGenerationOrchestrationService : IDiagramGenerationOrchestrationService
{
    private readonly IDiagramAnalysisProcessingService _analysisProcessingService;
    private readonly IDiagramRenderingProcessingService _renderingProcessingService;

    public DiagramGenerationOrchestrationService()
        : this(new DiagramAnalysisProcessingService(), new DiagramRenderingProcessingService())
    {
    }

    public DiagramGenerationOrchestrationService(
        IDiagramAnalysisProcessingService analysisProcessingService,
        IDiagramRenderingProcessingService renderingProcessingService)
    {
        _analysisProcessingService = analysisProcessingService ?? throw new System.ArgumentNullException(nameof(analysisProcessingService));
        _renderingProcessingService = renderingProcessingService ?? throw new System.ArgumentNullException(nameof(renderingProcessingService));
    }

    public Task<string> GenerateAsync(
        Project selectedProject,
        DiagramSettings settings,
        CancellationToken cancellationToken = default)
    {
        return GenerateAsync(new[] { selectedProject }, settings, cancellationToken);
    }

    public async Task<string> GenerateAsync(
        IEnumerable<Project> selectedProjects,
        DiagramSettings settings,
        CancellationToken cancellationToken = default)
    {
        var graph = await _analysisProcessingService
            .AnalyzeAsync(selectedProjects, settings, cancellationToken)
            .ConfigureAwait(false);

        return _renderingProcessingService.Render(graph, settings);
    }
}
