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

    public string Render(ArchitectureGraph graph, DiagramSettings settings)
    {
        return _drawioBroker.Export(graph, settings);
    }
}
