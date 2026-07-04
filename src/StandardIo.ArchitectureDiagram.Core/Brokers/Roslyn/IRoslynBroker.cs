using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using StandardIo.ArchitectureDiagram.Core.Models;

namespace StandardIo.ArchitectureDiagram.Core.Brokers.Roslyn;

public interface IRoslynBroker
{
    Task<DiagramModel> AnalyzeAsync(
        IEnumerable<Project> selectedProjects,
        DiagramSettings settings,
        CancellationToken cancellationToken = default);
}
