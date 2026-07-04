using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using StandardIo.ArchitectureDiagram.Core.Settings;

namespace StandardIo.ArchitectureDiagram.Core.Services.Processings;

public interface IDiagramGenerationProcessingService
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
