using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using StandardIo.ArchitectureDiagram.Core.Analysis;
using StandardIo.ArchitectureDiagram.Core.Graph;
using StandardIo.ArchitectureDiagram.Core.Settings;

namespace StandardIo.ArchitectureDiagram.Core.Brokers.Roslyn;

public sealed class RoslynBroker : IRoslynBroker
{
    private readonly RoslynDependencyAnalyzer _analyzer;

    public RoslynBroker()
        : this(new RoslynDependencyAnalyzer())
    {
    }

    public RoslynBroker(RoslynDependencyAnalyzer analyzer)
    {
        _analyzer = analyzer ?? throw new System.ArgumentNullException(nameof(analyzer));
    }

    public Task<ArchitectureGraph> AnalyzeAsync(
        IEnumerable<Project> selectedProjects,
        DiagramSettings settings,
        CancellationToken cancellationToken = default)
    {
        return _analyzer.AnalyzeAsync(selectedProjects, settings, cancellationToken);
    }
}
