using StandardIo.ArchitectureDiagram.Core.Models;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Renderers;

public interface IDiagramRenderer
{
    string RendererId { get; }

    string DisplayName { get; }

    string FileExtension { get; }

    string FileFilter { get; }

    string Render(DiagramModel diagram, DiagramSettings settings);
}
