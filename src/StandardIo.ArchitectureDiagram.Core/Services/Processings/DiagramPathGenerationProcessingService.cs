using System.IO;
using System.Threading;
using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.Core.Brokers.Workspaces;
using StandardIo.ArchitectureDiagram.Core.Renderers;
using StandardIo.ArchitectureDiagram.Core.Settings;

namespace StandardIo.ArchitectureDiagram.Core.Services.Processings;

public sealed class DiagramPathGenerationProcessingService
{
    private readonly IWorkspacePathBroker _workspacePathBroker;
    private readonly IDiagramGenerationProcessingService _diagramGenerationProcessingService;
    private readonly DiagramRendererRegistry _rendererRegistry;

    public DiagramPathGenerationProcessingService()
        : this(
            new WorkspacePathBroker(),
            new DiagramGenerationProcessingService(),
            new DiagramRendererRegistry())
    {
    }

    public DiagramPathGenerationProcessingService(
        IWorkspacePathBroker workspacePathBroker,
        IDiagramGenerationProcessingService diagramGenerationProcessingService,
        DiagramRendererRegistry rendererRegistry)
    {
        _workspacePathBroker = workspacePathBroker ?? throw new System.ArgumentNullException(nameof(workspacePathBroker));
        _diagramGenerationProcessingService = diagramGenerationProcessingService ?? throw new System.ArgumentNullException(nameof(diagramGenerationProcessingService));
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
        var output = await _diagramGenerationProcessingService
            .GenerateAsync(target.Projects, settings, cancellationToken)
            .ConfigureAwait(false);

        return new DiagramPathGenerationResult(target.Name, resolvedOutputPath, renderer.RendererId, output);
    }
}

public sealed class DiagramPathGenerationResult
{
    public DiagramPathGenerationResult(string targetName, string outputPath, string rendererId, string content)
    {
        TargetName = targetName;
        OutputPath = outputPath;
        RendererId = rendererId;
        Content = content;
    }

    public string TargetName { get; }

    public string OutputPath { get; }

    public string RendererId { get; }

    public string Content { get; }
}
