// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System.Threading;
using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.Core2.Models;

namespace StandardIo.ArchitectureDiagram.Core2.Services.Orchestrations.Generation;
internal interface IDiagramGenerationOrchestrationService
{
    Task<byte[]> GenerateAsync(DiagramRenderRequest request, CancellationToken cancellationToken);
}