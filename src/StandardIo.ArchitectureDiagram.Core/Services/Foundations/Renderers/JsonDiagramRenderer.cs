using StandardIo.ArchitectureDiagram.Core.Models;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Renderers;

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
