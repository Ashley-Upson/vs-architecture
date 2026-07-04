using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using StandardIo.ArchitectureDiagram.Core.Models;

namespace StandardIo.ArchitectureDiagram.Core.Services.Orchestrations.Diagrams;

public interface IDiagramGenerationOrchestrationService
{
    Task<string> GenerateAsync(
        Project selectedProject,
        DiagramSettings settings,
        CancellationToken cancellationToken = default);

    Task<string> GenerateAsync(
        IEnumerable<Project> selectedProjects,
        DiagramSettings settings,
        CancellationToken cancellationToken = default);
}
