using StandardIo.ArchitectureDiagram.Core.Drawio;
using StandardIo.ArchitectureDiagram.Core.Graph;
using StandardIo.ArchitectureDiagram.Core.Settings;

namespace StandardIo.ArchitectureDiagram.Core.Renderers;

public sealed class DrawioDiagramRenderer : IDiagramRenderer
{
    private readonly DrawioExporter _exporter;

    public DrawioDiagramRenderer()
        : this(new DrawioExporter())
    {
    }

    public DrawioDiagramRenderer(DrawioExporter exporter)
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
