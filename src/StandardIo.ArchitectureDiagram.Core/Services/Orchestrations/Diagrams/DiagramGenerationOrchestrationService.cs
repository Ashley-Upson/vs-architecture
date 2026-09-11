using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using StandardIo.ArchitectureDiagram.Core.Models;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.Settings;
using StandardIo.ArchitectureDiagram.Core.Services.Processings.Diagrams;

namespace StandardIo.ArchitectureDiagram.Core.Services.Orchestrations.Diagrams;

public sealed class DiagramGenerationOrchestrationService : IDiagramGenerationOrchestrationService
{
    private readonly IDiagramAnalysisProcessingService _analysisProcessingService;
    private readonly IDiagramRenderingProcessingService _renderingProcessingService;
    private readonly ITypedDiagramGenerationOrchestrator _typedOrchestrator;

    public DiagramGenerationOrchestrationService()
        : this(new DiagramAnalysisProcessingService(), new DiagramRenderingProcessingService(),
            new TypedDiagramGenerationOrchestrator())
    {
    }

    public DiagramGenerationOrchestrationService(
        IDiagramAnalysisProcessingService analysisProcessingService,
        IDiagramRenderingProcessingService renderingProcessingService)
        : this(analysisProcessingService, renderingProcessingService, new TypedDiagramGenerationOrchestrator())
    {
    }

    public DiagramGenerationOrchestrationService(
        IDiagramAnalysisProcessingService analysisProcessingService,
        IDiagramRenderingProcessingService renderingProcessingService,
        ITypedDiagramGenerationOrchestrator typedOrchestrator)
    {
        _analysisProcessingService = analysisProcessingService ?? throw new System.ArgumentNullException(nameof(analysisProcessingService));
        _renderingProcessingService = renderingProcessingService ?? throw new System.ArgumentNullException(nameof(renderingProcessingService));
        _typedOrchestrator = typedOrchestrator ?? throw new System.ArgumentNullException(nameof(typedOrchestrator));
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
        if (string.Equals(settings.OutputRenderer, DiagramRendererIds.Drawio, System.StringComparison.OrdinalIgnoreCase))
        {
            var typed = await _typedOrchestrator.GenerateAsync(
                selectedProjects, LegacyDiagramSettingsAdapter.CombinedRequest(settings), cancellationToken)
                .ConfigureAwait(false);
            return typed.Document.Content;
        }

        var graph = await _analysisProcessingService
            .AnalyzeAsync(selectedProjects, settings, cancellationToken)
            .ConfigureAwait(false);

        return _renderingProcessingService.Render(graph, settings);
    }
}
