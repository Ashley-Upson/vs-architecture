using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using StandardIo.ArchitectureDiagram.Core.Models;
using StandardIo.ArchitectureDiagram.Core.Services.Coordinations.Diagrams;
using StandardIo.ArchitectureDiagram.Core.Services.Orchestrations.Diagrams;

namespace StandardIo.ArchitectureDiagram.Core.Exposures.Diagrams;

public sealed class DiagramGenerationExposure : IDiagramGenerationExposure
{
    private readonly IDiagramPathGenerationCoordinationService _pathGenerationCoordinationService;
    private readonly IDiagramGenerationOrchestrationService _generationOrchestrationService;

    public DiagramGenerationExposure(
        IDiagramPathGenerationCoordinationService pathGenerationCoordinationService,
        IDiagramGenerationOrchestrationService generationOrchestrationService)
    {
        _pathGenerationCoordinationService = pathGenerationCoordinationService;
        _generationOrchestrationService = generationOrchestrationService;
    }

    public Task<string> GenerateAsync(
        IEnumerable<Project> selectedProjects,
        DiagramSettings settings,
        CancellationToken cancellationToken = default) =>
        _generationOrchestrationService.GenerateAsync(selectedProjects, settings, cancellationToken);

    public Task<DiagramPathGenerationResult> GenerateAsync(
        string inputPath,
        DiagramSettings settings,
        string? outputPath = null,
        string? projectFilter = null,
        CancellationToken cancellationToken = default) =>
        _pathGenerationCoordinationService.GenerateAsync(inputPath, settings, outputPath, projectFilter, cancellationToken);
}
