// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System;
using StandardIo.ArchitectureDiagram.Core2.Services.Processings.Layout;
using StandardIo.ArchitectureDiagram.Core2.Models;
using StandardIo.ArchitectureDiagram.Core2.Brokers.Rendering;
using StandardIo.ArchitectureDiagram.Core2.Brokers.Files;
using StandardIo.ArchitectureDiagram.Core2.Brokers.ProjectModels;
using StandardIo.ArchitectureDiagram.Core2.Brokers.Roslyn;
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
using StandardIo.ArchitectureDiagram.Core2.Services.Orchestrations.Rendering;
using StandardIo.ArchitectureDiagram.Core2.Services.Processings.Commands;
using StandardIo.ArchitectureDiagram.Core2.Services.Processings.Dependencies;
using StandardIo.ArchitectureDiagram.Core2.Services.Processings.ProjectModels;
using StandardIo.ArchitectureDiagram.Core2.Services.Processings.Projects;
using StandardIo.ArchitectureDiagram.Core2.Services.Processings.Rendering;
using StandardIo.ArchitectureDiagram.Core2.Services.Processings.Types;
using Microsoft.Extensions.DependencyInjection;
using StandardIo.ArchitectureDiagram.Core2.Exposures;

namespace StandardIo.ArchitectureDiagram.Core2;
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddArchitectureDiagram(this IServiceCollection services)
    {
        services.AddTransient<IFileBroker, FileBroker>();
        services.AddTransient<IRenderConfigurationBroker, RenderConfigurationBroker>();
        services.AddTransient<IRenderConfigurationService, RenderConfigurationService>();
        services.AddTransient<IProjectModelBroker, ProjectModelBroker>();
        services.AddTransient<IProjectModelSplitterBroker, ProjectModelSplitterBroker>();
        services.AddTransient<IRoslynBroker, RoslynBroker>();
        services.AddTransient<IDiagramRendererFactory, DiagramRendererFactory>();
        services.AddTransient<IDiagramRequestBroker, DiagramRequestBroker>();
        services.AddTransient<IDiagramRequestService, DiagramRequestService>();
        services.AddTransient<IProjectDependenciesService, ProjectDependenciesService>();
        services.AddTransient<IProjectModelBuilderService, ProjectModelBuilderService>();
        services.AddTransient<IProjectModelTreeService, ProjectModelTreeService>();
        services.AddTransient<IProjectModelService, ProjectModelService>();
        services.AddTransient<IProjectService, ProjectService>();
        services.AddTransient<IDrawIODocumentService, DrawIODocumentService>();
        services.AddTransient<IHtmlDocumentService, HtmlDocumentService>();
        services.AddTransient<ILayoutRuleFactory, LayoutRuleFactory>();
        services.AddTransient<IProjectModelLayoutBroker, ProjectModelLayoutBroker>();
        services.AddTransient<ILayoutRuleProcessingService, DepthLayoutRuleProcessingService>();
        services.AddTransient<ILayoutRuleProcessingService, CategoryRowLayoutRuleProcessingService>();
        services.AddTransient<ILayoutRuleProcessingService, TreeSpacingLayoutRuleProcessingService>();
        services.AddTransient<ILayoutRuleProcessingService, SharedParentCentringLayoutRuleProcessingService>();
        services.AddTransient<ILayoutRuleProcessingService, BranchSpacingLayoutRuleProcessingService>();
        services.AddTransient<ILayoutRuleProcessingService, ParentCentringLayoutRuleProcessingService>();
        services.AddTransient<ILayoutRuleProcessingService, LayoutCleanupRuleProcessingService>();
        services.AddTransient<ILayoutRuleProcessingService, BoundsLayoutRuleProcessingService>();
        services.AddTransient<DepthLayoutRuleProcessingService>();
        services.AddTransient<ParentCentringLayoutRuleProcessingService>();
        services.AddTransient<SharedParentCentringLayoutRuleProcessingService>();
        services.AddTransient<BranchSpacingLayoutRuleProcessingService>();
        services.AddTransient<ILayoutRuleProcessingService, ProjectPositioningLayoutRuleProcessingService>();
        services.AddTransient<ILayoutRuleProcessingService, RoutingLayoutRuleProcessingService>();
        services.AddTransient<ILayoutRuleProcessingService, CrossProjectRoutingLayoutRuleProcessingService>();
        services.AddTransient<IProjectModelLayoutService, ProjectModelLayoutService>();
        services.AddTransient<IProjectModelPresentationService, ProjectModelPresentationService>();
        services.AddTransient<IProjectTypesService, ProjectTypesService>();
        services.AddTransient<IDiagramRenderOrchestrationService, DiagramRenderOrchestrationService>();
        services.AddTransient<IProjectModelOrchestrationService, ProjectModelOrchestrationService>();
        services.AddTransient<ICommandParserProcessingService, CommandParserProcessingService>();
        services.AddTransient<IDiagramRequestProcessingService, DiagramRequestProcessingService>();
        services.AddTransient<IProjectDependenciesProcessingService, ProjectDependenciesProcessingService>();
        services.AddTransient<IProjectModelSplitterProcessingService, ProjectModelSplitterProcessingService>();
        services.AddTransient<IProjectProcessingService, ProjectProcessingService>();
        services.AddTransient<IProjectModelCompositionProcessingService, ProjectModelCompositionProcessingService>();
        services.AddTransient<IRenderModelProcessingService, RenderModelProcessingService>();
        services.AddTransient<ILayoutOrchestrationService, LayoutOrchestrationService>();
        services.AddTransient(implementationFactory: provider => new LayoutModelBuilder(provider.GetRequiredService<ILayoutOrchestrationService>()));
        services.AddTransient<IHtmlModelPreparationBroker, HtmlModelPreparationBroker>();
        services.AddTransient<IDrawIOModelPreparationBroker, DrawIOModelPreparationBroker>();
        services.AddTransient<IHtmlModelPreparationService, HtmlModelPreparationService>();
        services.AddTransient<IDrawIOModelPreparationService, DrawIOModelPreparationService>();
        services.AddTransient<IProjectTypesProcessingService, ProjectTypesProcessingService>();
        services.AddTransient(implementationFactory: provider => new ProjectModelBuilder(provider.GetRequiredService<IProjectModelOrchestrationService>()));
        services.AddTransient(implementationFactory: provider => new ProjectModelSplitter(provider.GetRequiredService<IProjectModelSplitterProcessingService>()));
        services.AddTransient(implementationFactory: provider => new DiagramRenderCommand(provider.GetRequiredService<IDiagramRenderOrchestrationService>()));
        services.AddTransient(implementationFactory: provider => new DrawIODiagramRenderer(provider.GetRequiredService<IDrawIOModelPreparationService>(), provider.GetRequiredService<IDrawIODocumentService>()));

        foreach (DiagramTypes diagramType in Enum.GetValues<DiagramTypes>())
        {
            services.AddKeyedTransient<IDiagramRenderer>(serviceKey: $"{DiagramFormats.DrawIO}_{diagramType}", implementationFactory: (provider, _) => provider.GetRequiredService<DrawIODiagramRenderer>());
            services.AddKeyedTransient<IDiagramRenderer>(serviceKey: $"{DiagramFormats.Html}_{diagramType}", implementationFactory: (provider, _) => provider.GetRequiredService<HtmlDiagramRenderer>());
        }

        services.AddTransient(implementationFactory: provider => new HtmlDiagramRenderer(provider.GetRequiredService<IHtmlModelPreparationService>(), provider.GetRequiredService<IHtmlDocumentService>()));

        services.AddTransient(implementationFactory: provider => new DiagramGenerator(provider.GetRequiredService<IDiagramRendererFactory>()
            .CreateDiagramGenerationOrchestrationService(namedKey: $"{DiagramFormats.DrawIO}_{DiagramTypes.Architecture}")));

        return services;
    }
}
