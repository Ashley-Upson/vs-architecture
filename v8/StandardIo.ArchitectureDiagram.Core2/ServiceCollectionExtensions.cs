using StandardIo.ArchitectureDiagram.Core2.Brokers.Files;
using StandardIo.ArchitectureDiagram.Core2.Brokers.ProjectModels;
using StandardIo.ArchitectureDiagram.Core2.Brokers.Roslyn;
using StandardIo.ArchitectureDiagram.Core2.Factories;
using StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Commands;
using StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Dependencies;
using StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Generation;
using StandardIo.ArchitectureDiagram.Core2.Services.Foundations.ProjectModels;
using StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Projects;
using StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Rendering;
using StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Types;
using StandardIo.ArchitectureDiagram.Core2.Services.Orchestrations.Commands;
using StandardIo.ArchitectureDiagram.Core2.Services.Orchestrations.Generation;
using StandardIo.ArchitectureDiagram.Core2.Services.Orchestrations.Projects;
using StandardIo.ArchitectureDiagram.Core2.Services.Processings.Commands;
using StandardIo.ArchitectureDiagram.Core2.Services.Processings.Dependencies;
using StandardIo.ArchitectureDiagram.Core2.Services.Processings.ProjectModels;
using StandardIo.ArchitectureDiagram.Core2.Services.Processings.Projects;
using StandardIo.ArchitectureDiagram.Core2.Services.Processings.Rendering;
using StandardIo.ArchitectureDiagram.Core2.Services.Processings.Types;
using Microsoft.Extensions.DependencyInjection;
namespace StandardIo.ArchitectureDiagram.Core2;
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddArchitectureDiagram(this IServiceCollection services)
    {
        services.AddTransient<IFileBroker, FileBroker>();
        services.AddTransient<IProjectModelBroker, ProjectModelBroker>();
        services.AddTransient<IProjectModelSplitterBroker, ProjectModelSplitterBroker>();
        services.AddTransient<IRoslynBroker, RoslynBroker>();
        services.AddTransient<IDiagramRendererFactory, DiagramRendererFactory>();
        services.AddTransient<IDiagramRenderRequestService, DiagramRenderRequestService>();
        services.AddTransient<IProjectDependenciesService, ProjectDependenciesService>();
        services.AddTransient<IProjectModelBuilderService, ProjectModelBuilderService>();
        services.AddTransient<IProjectModelTreeService, ProjectModelTreeService>();
        services.AddTransient<IProjectModelService, ProjectModelService>();
        services.AddTransient<IProjectService, ProjectService>();
        services.AddTransient<IDrawIODocumentService, DrawIODocumentService>();
        services.AddTransient<IHtmlDocumentService, HtmlDocumentService>();
        services.AddTransient<IProjectModelLayoutService, ProjectModelLayoutService>();
        services.AddTransient<IProjectModelPresentationService, ProjectModelPresentationService>();
        services.AddTransient<IProjectTypesService, ProjectTypesService>();
        services.AddTransient<IDiagramRenderOrchestrationService, DiagramRenderOrchestrationService>();
        services.AddTransient<IDiagramGenerationOrchestrationService, DiagramGenerationOrchestrationService>();
        services.AddTransient<IProjectModelOrchestrationService, ProjectModelOrchestrationService>();
        services.AddTransient<ICommandParserProcessingService, CommandParserProcessingService>();
        services.AddTransient<IProjectDependenciesProcessingService, ProjectDependenciesProcessingService>();
        services.AddTransient<IProjectModelSplitterProcessingService, ProjectModelSplitterProcessingService>();
        services.AddTransient<IProjectProcessingService, ProjectProcessingService>();
        services.AddTransient<IProjectModelRenderingProcessingService, ProjectModelRenderingProcessingService>();
        services.AddTransient<IProjectTypesProcessingService, ProjectTypesProcessingService>();
        services.AddTransient(provider => new ProjectModelBuilder(provider.GetRequiredService<IProjectModelOrchestrationService>()));
        services.AddTransient(provider => new ProjectModelSplitter(provider.GetRequiredService<IProjectModelSplitterProcessingService>()));
        services.AddTransient(provider => new DiagramRenderCommand(provider.GetRequiredService<IDiagramRenderOrchestrationService>()));
        services.AddTransient(provider => new DrawIODiagramRenderer(provider.GetRequiredService<IProjectModelRenderingProcessingService>(), provider.GetRequiredService<IDrawIODocumentService>()));
        services.AddTransient<IDiagramRenderer>(provider => provider.GetRequiredService<DrawIODiagramRenderer>());
        services.AddTransient(provider => new HtmlDiagramRenderer(provider.GetRequiredService<IProjectModelRenderingProcessingService>(), provider.GetRequiredService<IHtmlDocumentService>()));
        services.AddTransient<IDiagramRenderer>(provider => provider.GetRequiredService<HtmlDiagramRenderer>());
        services.AddTransient(provider => new DiagramGenerator(provider.GetRequiredService<IDiagramGenerationOrchestrationService>(), provider.GetRequiredService<DrawIODiagramRenderer>()));
        return services;
    }
}
