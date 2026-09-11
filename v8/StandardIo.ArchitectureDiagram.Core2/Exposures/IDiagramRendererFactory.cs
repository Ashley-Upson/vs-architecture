// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using StandardIo.ArchitectureDiagram.Core2.Services.Orchestrations.Generation;

namespace StandardIo.ArchitectureDiagram.Core2.Exposures;
internal interface IDiagramRendererFactory
{
    IDiagramGenerationOrchestrationService CreateDiagramGenerationOrchestrationService(string namedKey);
}