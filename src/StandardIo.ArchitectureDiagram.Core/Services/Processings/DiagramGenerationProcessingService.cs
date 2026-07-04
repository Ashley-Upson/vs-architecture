using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using StandardIo.ArchitectureDiagram.Core.Settings;

namespace StandardIo.ArchitectureDiagram.Core.Services.Processings;

public sealed class DiagramGenerationProcessingService : IDiagramGenerationProcessingService
{
    private readonly IDiagramAnalysisProcessingService _analysisProcessingService;
    private readonly IDiagramRenderingProcessingService _renderingProcessingService;

    public DiagramGenerationProcessingService()
        : this(new DiagramAnalysisProcessingService(), new DiagramRenderingProcessingService())
    {
    }

    public DiagramGenerationProcessingService(
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
