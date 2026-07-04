using StandardIo.ArchitectureDiagram.Core.Graph;
using StandardIo.ArchitectureDiagram.Core.Settings;

namespace StandardIo.ArchitectureDiagram.Core.Renderers;

public interface IDiagramRenderer
{
    string RendererId { get; }

    string DisplayName { get; }

    string FileExtension { get; }

    string FileFilter { get; }

    string Render(DiagramModel diagram, DiagramSettings settings);
}
