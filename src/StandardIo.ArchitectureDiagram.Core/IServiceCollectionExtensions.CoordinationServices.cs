using Microsoft.Extensions.DependencyInjection;
using StandardIo.ArchitectureDiagram.Core.Services.Coordinations.Diagrams;

namespace StandardIo.ArchitectureDiagram.Core;

public static partial class IServiceCollectionExtensions
{
    private static void AddArchitectureDiagramCoordinationServices(IServiceCollection services) =>
        services.AddTransient<IDiagramPathGenerationCoordinationService, DiagramPathGenerationCoordinationService>();
}
