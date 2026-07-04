using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.Analyses;
using StandardIo.ArchitectureDiagram.Core.Models;

namespace StandardIo.ArchitectureDiagram.Core.Brokers.Roslyn;

public sealed class RoslynBroker : IRoslynBroker
{
    private readonly IRoslynDependencyAnalyzer _analyzer;

    public RoslynBroker()
        : this(new RoslynDependencyAnalyzer())
    {
    }

    public RoslynBroker(IRoslynDependencyAnalyzer analyzer)
    {
        _analyzer = analyzer ?? throw new System.ArgumentNullException(nameof(analyzer));
    }

    public Task<DiagramModel> AnalyzeAsync(
        IEnumerable<Project> selectedProjects,
        DiagramSettings settings,
        CancellationToken cancellationToken = default)
    {
        return _analyzer.AnalyzeAsync(selectedProjects, settings, cancellationToken);
    }
}
