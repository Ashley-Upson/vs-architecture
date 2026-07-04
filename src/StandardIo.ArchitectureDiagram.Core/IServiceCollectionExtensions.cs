using Microsoft.Extensions.DependencyInjection;

namespace StandardIo.ArchitectureDiagram.Core;

public static partial class IServiceCollectionExtensions
{
    public static IServiceCollection AddArchitectureDiagram(
        this IServiceCollection services)
    {
        AddArchitectureDiagramBrokers(services);
        AddArchitectureDiagramFoundationServices(services);
        AddArchitectureDiagramProcessingServices(services);
        AddArchitectureDiagramOrchestrationServices(services);
        AddArchitectureDiagramCoordinationServices(services);
        AddArchitectureDiagramExposures(services);

        return services;
    }
}
