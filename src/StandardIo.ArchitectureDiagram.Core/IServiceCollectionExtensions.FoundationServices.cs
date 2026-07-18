using Microsoft.Extensions.DependencyInjection;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.Analyses;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.Renderers;

namespace StandardIo.ArchitectureDiagram.Core;

public static partial class IServiceCollectionExtensions
{
    private static void AddArchitectureDiagramFoundationServices(IServiceCollection services)
    {
        services.AddTransient<IDeterministicDrawioExporter, DeterministicDrawioExporter>();
        services.AddTransient<IRoslynDependencyAnalyzer, RoslynDependencyAnalyzer>();
        services.AddTransient<IDiagramRenderer, DrawioDiagramRenderer>();
        services.AddTransient<IDiagramRenderer, JsonDiagramRenderer>();
        services.AddTransient<IDiagramRendererRegistry, DiagramRendererRegistry>();
    }
}
