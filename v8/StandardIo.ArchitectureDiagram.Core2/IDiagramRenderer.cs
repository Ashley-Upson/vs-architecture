using StandardIo.ArchitectureDiagram.Core2.Models;
namespace StandardIo.ArchitectureDiagram.Core2;
public interface IDiagramRenderer
{
    DiagramRendererRule Rule { get; }
    byte[] Render(ProjectModel[] projectModels);
}
