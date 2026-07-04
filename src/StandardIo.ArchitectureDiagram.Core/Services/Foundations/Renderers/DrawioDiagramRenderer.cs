using StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;
using StandardIo.ArchitectureDiagram.Core.Models;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Renderers;

public sealed class DrawioDiagramRenderer : IDiagramRenderer
{
    private readonly IDeterministicDrawioExporter _exporter;

    public DrawioDiagramRenderer()
        : this(new DeterministicDrawioExporter())
    {
    }

    public DrawioDiagramRenderer(IDeterministicDrawioExporter exporter)
    {
        _exporter = exporter ?? throw new System.ArgumentNullException(nameof(exporter));
    }

    public string RendererId => DiagramRendererIds.Drawio;

    public string DisplayName => "Draw.io diagram";

    public string FileExtension => ".drawio";

    public string FileFilter => "Draw.io diagram (*.drawio)|*.drawio|XML file (*.xml)|*.xml|All files (*.*)|*.*";

    public string Render(DiagramModel diagram, DiagramSettings settings)
    {
        return _exporter.Export(diagram, settings);
    }
}
