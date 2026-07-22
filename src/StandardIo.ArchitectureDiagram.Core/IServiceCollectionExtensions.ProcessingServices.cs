using Microsoft.Extensions.DependencyInjection;
using StandardIo.ArchitectureDiagram.Core.Services.Processings.Diagrams;
using StandardIo.ArchitectureDiagram.Core.Services.Processings.Drawios;
using StandardIo.ArchitectureDiagram.Core.Services.Processings.Architectures;

namespace StandardIo.ArchitectureDiagram.Core;

public static partial class IServiceCollectionExtensions
{
    private static void AddArchitectureDiagramProcessingServices(IServiceCollection services)
    {
        services.AddTransient<IDiagramAnalysisProcessingService, DiagramAnalysisProcessingService>();
        services.AddTransient<IDiagramRenderingProcessingService, DiagramRenderingProcessingService>();
        services.AddTransient<IArchitectureGeometryAnalyser, ArchitectureGeometryAnalyser>();
        services.AddTransient<IArchitectureTopologyProjector, ArchitectureTopologyProjector>();
    }
}
