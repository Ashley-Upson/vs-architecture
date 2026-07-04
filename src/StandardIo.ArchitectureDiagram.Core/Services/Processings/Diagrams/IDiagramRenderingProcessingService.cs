using StandardIo.ArchitectureDiagram.Core.Models;

namespace StandardIo.ArchitectureDiagram.Core.Services.Processings.Diagrams;

public interface IDiagramRenderingProcessingService
{
    string Render(DiagramModel diagram, DiagramSettings settings);
}
