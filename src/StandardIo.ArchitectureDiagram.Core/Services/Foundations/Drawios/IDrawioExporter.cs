using StandardIo.ArchitectureDiagram.Core.Models;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

public interface IDrawioExporter
{
    string Export(DiagramModel graph, DiagramSettings settings);
}
