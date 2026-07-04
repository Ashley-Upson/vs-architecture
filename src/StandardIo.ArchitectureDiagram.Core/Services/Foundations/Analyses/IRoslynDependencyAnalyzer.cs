using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using StandardIo.ArchitectureDiagram.Core.Models;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Analyses;

public interface IRoslynDependencyAnalyzer
{
    Task<DiagramModel> AnalyzeAsync(
        Project selectedProject,
        DiagramSettings settings,
        CancellationToken cancellationToken = default);

    Task<DiagramModel> AnalyzeAsync(
        IEnumerable<Project> selectedProjects,
        DiagramSettings settings,
        CancellationToken cancellationToken = default);
}
