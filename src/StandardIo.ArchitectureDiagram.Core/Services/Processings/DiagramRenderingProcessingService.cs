using StandardIo.ArchitectureDiagram.Core.Brokers.Drawio;
using StandardIo.ArchitectureDiagram.Core.Graph;
using StandardIo.ArchitectureDiagram.Core.Settings;

namespace StandardIo.ArchitectureDiagram.Core.Services.Processings;

public sealed class DiagramRenderingProcessingService : IDiagramRenderingProcessingService
{
    private readonly IDrawioBroker _drawioBroker;

    public DiagramRenderingProcessingService()
        : this(new DrawioBroker())
    {
    }

    public DiagramRenderingProcessingService(IDrawioBroker drawioBroker)
    {
        _drawioBroker = drawioBroker ?? throw new System.ArgumentNullException(nameof(drawioBroker));
    }

    public string Render(DiagramModel diagram, DiagramSettings settings)
    {
        return _drawioBroker.Export(diagram, settings);
    }
}
