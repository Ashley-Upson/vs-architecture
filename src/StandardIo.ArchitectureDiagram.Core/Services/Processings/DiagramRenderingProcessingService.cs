using StandardIo.ArchitectureDiagram.Core.Graph;
using StandardIo.ArchitectureDiagram.Core.Renderers;
using StandardIo.ArchitectureDiagram.Core.Settings;

namespace StandardIo.ArchitectureDiagram.Core.Services.Processings;

public sealed class DiagramRenderingProcessingService : IDiagramRenderingProcessingService
{
    private readonly DiagramRendererRegistry _rendererRegistry;

    public DiagramRenderingProcessingService()
        : this(new DiagramRendererRegistry())
    {
    }

    public DiagramRenderingProcessingService(DiagramRendererRegistry rendererRegistry)
    {
        _rendererRegistry = rendererRegistry ?? throw new System.ArgumentNullException(nameof(rendererRegistry));
    }

    public string Render(DiagramModel diagram, DiagramSettings settings)
    {
        settings ??= DiagramSettings.CreateDefault();
        return _rendererRegistry.Resolve(settings.OutputRenderer).Render(diagram, settings);
    }
}
