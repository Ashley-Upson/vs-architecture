using Microsoft.Extensions.DependencyInjection;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.Analyses;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.Renderers;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.DataModels;

namespace StandardIo.ArchitectureDiagram.Core;

public static partial class IServiceCollectionExtensions
{
    private static void AddArchitectureDiagramFoundationServices(IServiceCollection services)
    {
        services.AddTransient<IDeterministicDrawioExporter, DeterministicDrawioExporter>();
        services.AddTransient<IRoslynDependencyAnalyzer, RoslynDependencyAnalyzer>();
        services.AddTransient<IArchitectureAnalyser, RoslynDependencyAnalyzer>();
        services.AddTransient<IDataModelAnalyser, RoslynDataModelAnalyser>();
        services.AddTransient<IArchitectureRenderer<StandardIo.ArchitectureDiagram.Core.Models.Drawios.DrawioPage>, DrawioArchitectureRenderer>();
        services.AddTransient<IArchitectureDiagnosticRenderer, DrawioArchitectureRenderer>();
        services.AddTransient<IDataModelRenderer<StandardIo.ArchitectureDiagram.Core.Models.Drawios.DrawioPage>, DrawioDataModelRenderer>();
        services.AddTransient<IDrawioDocumentComposer, DrawioDocumentComposer>();
        services.AddTransient<IDiagramRenderer, DrawioDiagramRenderer>();
        services.AddTransient<IDiagramRenderer, JsonDiagramRenderer>();
        services.AddTransient<IDiagramRendererRegistry, DiagramRendererRegistry>();
    }
}
