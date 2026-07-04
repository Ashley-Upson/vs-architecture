using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using StandardIo.ArchitectureDiagram.Core.Models;

namespace StandardIo.ArchitectureDiagram.Core.Exposures.Diagrams;

public interface IDiagramGenerationExposure
{
    Task<string> GenerateAsync(
        IEnumerable<Project> selectedProjects,
        DiagramSettings settings,
        CancellationToken cancellationToken = default);

    Task<DiagramPathGenerationResult> GenerateAsync(
        string inputPath,
        DiagramSettings settings,
        string? outputPath = null,
        string? projectFilter = null,
        CancellationToken cancellationToken = default);
}
