// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System;
using System.Threading;
using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.Core2.Models;
using StandardIo.ArchitectureDiagram.Core2.Brokers.ProjectModels;
using StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Generation;
using StandardIo.ArchitectureDiagram.Core2.Services.Orchestrations.Generation;

namespace StandardIo.ArchitectureDiagram.Core2.Exposures;
public sealed class DiagramGenerator
{
    private readonly IDiagramGenerationOrchestrationService generationService;

internal DiagramGenerator(IDiagramGenerationOrchestrationService generationService)
    {
        this.generationService = generationService;
    }

    public Task<byte[]> GenerateAsync(DiagramGenerationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(argument: request);
        cancellationToken.ThrowIfCancellationRequested();
        return this.generationService.GenerateAsync(request: new DiagramRenderRequest { ProjectPaths = request.ProjectPaths, RenderConfiguration = request.RenderConfiguration, DiagramType = request.DiagramType, Format = DiagramFormats.DrawIO }, cancellationToken: cancellationToken);
    }
}