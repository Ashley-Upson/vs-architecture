using Microsoft.Extensions.DependencyInjection;
using StandardIo.ArchitectureDiagram.Core.Services.Orchestrations.Diagrams;

namespace StandardIo.ArchitectureDiagram.Core;

public static partial class IServiceCollectionExtensions
{
    private static void AddArchitectureDiagramOrchestrationServices(IServiceCollection services) =>
        services
            .AddTransient<IArchitectureGenerationService, ArchitectureV7ProductionGenerationService>()
            .AddTransient<ITypedDiagramGenerationOrchestrator, TypedDiagramGenerationOrchestrator>()
            .AddTransient<IDiagramGenerationOrchestrationService, DiagramGenerationOrchestrationService>();
}
