using StandardIo.ArchitectureDiagram.Core.Graph;
using StandardIo.ArchitectureDiagram.Core.Settings;

namespace StandardIo.ArchitectureDiagram.Core.Renderers;

public sealed class JsonDiagramRenderer : IDiagramRenderer
{
    public string RendererId => DiagramRendererIds.Json;

    public string DisplayName => "Diagram JSON";

    public string FileExtension => ".json";

    public string FileFilter => "JSON file (*.json)|*.json|All files (*.*)|*.*";

    public string Render(DiagramModel diagram, DiagramSettings settings)
    {
        return DiagramModelSerializer.Export(diagram);
    }
}
