using Microsoft.Extensions.DependencyInjection;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.Analyses;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.ArchitectureV6;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.Renderers;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.DataModels;

namespace StandardIo.ArchitectureDiagram.Core;

public static partial class IServiceCollectionExtensions
{
    private static void AddArchitectureDiagramFoundationServices(IServiceCollection services)
    {
        services.AddTransient<IRoslynDependencyAnalyzer, RoslynDependencyAnalyzer>();
        services.AddTransient<IArchitectureAnalyser, RoslynDependencyAnalyzer>();
        services.AddTransient<IDataModelAnalyser, RoslynDataModelAnalyser>();
        services.AddTransient<IArchitectureDiagramPlanner, ArchitectureDiagramV6Planner>();
        services.AddTransient<IPlannedArchitectureDiagramValidator, ArchitectureDiagramV6Validator>();
        services.AddTransient<IArchitectureDiagramRenderer<StandardIo.ArchitectureDiagram.Core.Models.Drawios.DrawioPage>, DrawioArchitectureV6Renderer>();
        services.AddTransient<IDataModelRenderer<StandardIo.ArchitectureDiagram.Core.Models.Drawios.DrawioPage>, DrawioDataModelRenderer>();
        services.AddTransient<IDrawioDocumentComposer, DrawioDocumentComposer>();
        AddLegacyDiagramRendererCompatibility(services);
    }

    // The generic renderer registry remains available for non-typed diagram flows. Architecture
    // generation is wired directly to the V6 planner and renderer above.
    private static void AddLegacyDiagramRendererCompatibility(IServiceCollection services)
    {
        services.AddTransient<IDiagramRenderer, DrawioDiagramRenderer>();
        services.AddTransient<IDiagramRenderer, JsonDiagramRenderer>();
        services.AddTransient<IDiagramRendererRegistry, DiagramRendererRegistry>();
    }
}
