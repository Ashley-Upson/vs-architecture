using StandardIo.ArchitectureDiagram.Core.Graph;
using StandardIo.ArchitectureDiagram.Core.Settings;

namespace StandardIo.ArchitectureDiagram.Core.Brokers.Drawio;

public interface IDrawioBroker
{
    string Export(DiagramModel diagram, DiagramSettings settings);
}
