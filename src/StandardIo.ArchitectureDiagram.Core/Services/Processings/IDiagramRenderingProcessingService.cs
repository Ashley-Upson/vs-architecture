using StandardIo.ArchitectureDiagram.Core.Graph;
using StandardIo.ArchitectureDiagram.Core.Settings;

namespace StandardIo.ArchitectureDiagram.Core.Services.Processings;

public interface IDiagramRenderingProcessingService
{
    string Render(DiagramModel diagram, DiagramSettings settings);
}
