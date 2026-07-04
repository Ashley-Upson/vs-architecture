using StandardIo.ArchitectureDiagram.Core.Models;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.Renderers;

namespace StandardIo.ArchitectureDiagram.Core.Services.Processings.Diagrams;

public sealed class DiagramRenderingProcessingService : IDiagramRenderingProcessingService
{
    private readonly IDiagramRendererRegistry _rendererRegistry;

    public DiagramRenderingProcessingService()
        : this(new DiagramRendererRegistry())
    {
    }

    public DiagramRenderingProcessingService(IDiagramRendererRegistry rendererRegistry)
    {
        _rendererRegistry = rendererRegistry ?? throw new System.ArgumentNullException(nameof(rendererRegistry));
    }

    public string Render(DiagramModel diagram, DiagramSettings settings)
    {
        settings ??= DiagramSettings.CreateDefault();
        return _rendererRegistry.Resolve(settings.OutputRenderer).Render(diagram, settings);
    }
}
