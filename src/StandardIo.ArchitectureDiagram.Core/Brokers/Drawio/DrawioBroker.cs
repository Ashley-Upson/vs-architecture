using StandardIo.ArchitectureDiagram.Core.Drawio;
using StandardIo.ArchitectureDiagram.Core.Graph;
using StandardIo.ArchitectureDiagram.Core.Settings;

namespace StandardIo.ArchitectureDiagram.Core.Brokers.Drawio;

public sealed class DrawioBroker : IDrawioBroker
{
    private readonly DrawioExporter _exporter;

    public DrawioBroker()
        : this(new DrawioExporter())
    {
    }

    public DrawioBroker(DrawioExporter exporter)
    {
        _exporter = exporter ?? throw new System.ArgumentNullException(nameof(exporter));
    }

    public string Export(DiagramModel diagram, DiagramSettings settings)
    {
        return _exporter.Export(diagram, settings);
    }
}
