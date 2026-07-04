using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using StandardIo.ArchitectureDiagram.Core.Brokers.Roslyn;
using StandardIo.ArchitectureDiagram.Core.Graph;
using StandardIo.ArchitectureDiagram.Core.Settings;

namespace StandardIo.ArchitectureDiagram.Core.Services.Processings;

public sealed class DiagramAnalysisProcessingService : IDiagramAnalysisProcessingService
{
    private readonly IRoslynBroker _roslynBroker;

    public DiagramAnalysisProcessingService()
        : this(new RoslynBroker())
    {
    }

    public DiagramAnalysisProcessingService(IRoslynBroker roslynBroker)
    {
        _roslynBroker = roslynBroker ?? throw new System.ArgumentNullException(nameof(roslynBroker));
    }

    public Task<DiagramModel> AnalyzeAsync(
        IEnumerable<Project> selectedProjects,
        DiagramSettings settings,
        CancellationToken cancellationToken = default)
    {
        return _roslynBroker.AnalyzeAsync(selectedProjects, settings, cancellationToken);
    }
}
