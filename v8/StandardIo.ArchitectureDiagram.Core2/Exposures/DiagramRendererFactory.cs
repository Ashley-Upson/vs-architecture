// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System;
using Microsoft.Extensions.DependencyInjection;
using StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Generation;
using StandardIo.ArchitectureDiagram.Core2.Services.Orchestrations.Generation;

namespace StandardIo.ArchitectureDiagram.Core2.Exposures;
internal sealed class DiagramRendererFactory(IServiceProvider serviceProvider) : IDiagramRendererFactory
{
    public IDiagramGenerationOrchestrationService CreateDiagramGenerationOrchestrationService(string namedKey)
    {
        var modelBuilder = serviceProvider.GetRequiredService<IProjectModelBuilderService>();
        var treeBuilder = serviceProvider.GetRequiredService<IProjectModelTreeService>();
        var renderer = serviceProvider.GetRequiredKeyedService<IDiagramRenderer>(serviceKey: namedKey);
        return new DiagramGenerationOrchestrationService(builderService: modelBuilder, treeService: treeBuilder, renderer: renderer);
    }
}