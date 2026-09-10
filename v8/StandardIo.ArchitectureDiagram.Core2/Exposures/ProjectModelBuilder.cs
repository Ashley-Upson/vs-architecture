// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.Core2.Brokers.Files;
using StandardIo.ArchitectureDiagram.Core2.Brokers.Roslyn;
using StandardIo.ArchitectureDiagram.Core2.Models;
using StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Dependencies;
using StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Projects;
using StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Types;
using StandardIo.ArchitectureDiagram.Core2.Services.Orchestrations.Projects;
using StandardIo.ArchitectureDiagram.Core2.Services.Processings.Dependencies;
using StandardIo.ArchitectureDiagram.Core2.Services.Processings.Projects;
using StandardIo.ArchitectureDiagram.Core2.Services.Processings.Types;

namespace StandardIo.ArchitectureDiagram.Core2;

public sealed class ProjectModelBuilder
{
    private readonly IProjectModelOrchestrationService projectModelOrchestrationService;

    internal ProjectModelBuilder(IProjectModelOrchestrationService service) => this.projectModelOrchestrationService = service;

    public ProjectModelBuilder()
    {
        this.projectModelOrchestrationService = new ProjectModelOrchestrationService(
            projectProcessingService: new ProjectProcessingService(
                projectService: new ProjectService(fileBroker: new FileBroker())),
            projectTypesProcessingService: new ProjectTypesProcessingService(
                projectTypesService: new ProjectTypesService(roslynBroker: new RoslynBroker())),
            projectDependenciesProcessingService: new ProjectDependenciesProcessingService(
                projectDependenciesService: new ProjectDependenciesService(roslynBroker: new RoslynBroker())));
    }

    public Task<ProjectModel> BuildAsync(string projectFilePath, CancellationToken cancellationToken = default) =>
        this.projectModelOrchestrationService.GenerateProjectModelAsync(
            projectFilePath: projectFilePath,
            cancellationToken: cancellationToken);
}
