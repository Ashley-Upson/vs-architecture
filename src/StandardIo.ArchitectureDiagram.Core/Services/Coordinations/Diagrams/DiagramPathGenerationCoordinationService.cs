using System.IO;
using System.Threading;
using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.Core.Brokers.Workspaces;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.Renderers;
using StandardIo.ArchitectureDiagram.Core.Services.Orchestrations.Diagrams;
using StandardIo.ArchitectureDiagram.Core.Models;

namespace StandardIo.ArchitectureDiagram.Core.Services.Coordinations.Diagrams;

public sealed class DiagramPathGenerationCoordinationService : IDiagramPathGenerationCoordinationService
{
    private readonly IWorkspacePathBroker _workspacePathBroker;
    private readonly IDiagramGenerationOrchestrationService _diagramGenerationOrchestrationService;
    private readonly IDiagramRendererRegistry _rendererRegistry;

    public DiagramPathGenerationCoordinationService()
        : this(
            new WorkspacePathBroker(),
            new DiagramGenerationOrchestrationService(),
            new DiagramRendererRegistry())
    {
    }

    public DiagramPathGenerationCoordinationService(
        IWorkspacePathBroker workspacePathBroker,
        IDiagramGenerationOrchestrationService diagramGenerationOrchestrationService,
        IDiagramRendererRegistry rendererRegistry)
    {
        _workspacePathBroker = workspacePathBroker ?? throw new System.ArgumentNullException(nameof(workspacePathBroker));
        _diagramGenerationOrchestrationService = diagramGenerationOrchestrationService ?? throw new System.ArgumentNullException(nameof(diagramGenerationOrchestrationService));
        _rendererRegistry = rendererRegistry ?? throw new System.ArgumentNullException(nameof(rendererRegistry));
    }

    public async Task<DiagramPathGenerationResult> GenerateAsync(
        string inputPath,
        DiagramSettings settings,
        string? outputPath = null,
        string? projectFilter = null,
        CancellationToken cancellationToken = default)
    {
        settings ??= DiagramSettings.CreateDefault();
        var target = await _workspacePathBroker
            .LoadAsync(inputPath, new WorkspacePathLoadOptions { ProjectFilter = projectFilter }, cancellationToken)
            .ConfigureAwait(false);
        var renderer = _rendererRegistry.Resolve(settings.OutputRenderer);
        var resolvedOutputPath = string.IsNullOrWhiteSpace(outputPath)
            ? Path.Combine(Directory.GetCurrentDirectory(), $"{target.Name}.architecture{renderer.FileExtension}")
            : Path.GetFullPath(outputPath);
        var output = await _diagramGenerationOrchestrationService
            .GenerateAsync(target.Projects, settings, cancellationToken)
            .ConfigureAwait(false);

        return new DiagramPathGenerationResult(target.Name, resolvedOutputPath, renderer.RendererId, output);
    }
}
