using Microsoft.Extensions.DependencyInjection;
using StandardIo.ArchitectureDiagram.Core.Services.Orchestrations.Diagrams;

namespace StandardIo.ArchitectureDiagram.Core;

public static partial class IServiceCollectionExtensions
{
    private static void AddArchitectureDiagramOrchestrationServices(IServiceCollection services) =>
        services.AddTransient<IDiagramGenerationOrchestrationService, DiagramGenerationOrchestrationService>();
}
