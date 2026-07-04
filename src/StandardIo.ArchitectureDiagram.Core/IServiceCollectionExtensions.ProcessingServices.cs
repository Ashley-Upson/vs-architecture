using Microsoft.Extensions.DependencyInjection;
using StandardIo.ArchitectureDiagram.Core.Services.Processings.Diagrams;

namespace StandardIo.ArchitectureDiagram.Core;

public static partial class IServiceCollectionExtensions
{
    private static void AddArchitectureDiagramProcessingServices(IServiceCollection services)
    {
        services.AddTransient<IDiagramAnalysisProcessingService, DiagramAnalysisProcessingService>();
        services.AddTransient<IDiagramRenderingProcessingService, DiagramRenderingProcessingService>();
    }
}
