using Microsoft.Extensions.DependencyInjection;
using StandardIo.ArchitectureDiagram.Core.Exposures.Diagrams;

namespace StandardIo.ArchitectureDiagram.Core;

public static partial class IServiceCollectionExtensions
{
    private static void AddArchitectureDiagramExposures(IServiceCollection services) =>
        services.AddTransient<IDiagramGenerationExposure, DiagramGenerationExposure>();
}
