// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.Core2.Models;
using StandardIo.ArchitectureDiagram.Core2.Services.Processings.Types;
using StandardIo.ArchitectureDiagram.Core2.Services.Processings.Dependencies;
using StandardIo.ArchitectureDiagram.Core2.Services.Processings.Projects;

namespace StandardIo.ArchitectureDiagram.Core2.Services.Orchestrations.Projects;
internal sealed class ProjectModelOrchestrationService : IProjectModelOrchestrationService
{
    private readonly IProjectTypesProcessingService projectTypesProcessingService;
    private readonly IProjectDependenciesProcessingService projectDependenciesProcessingService;
    private readonly IProjectProcessingService projectProcessingService;
    public ProjectModelOrchestrationService(IProjectProcessingService projectProcessingService, IProjectTypesProcessingService projectTypesProcessingService, IProjectDependenciesProcessingService projectDependenciesProcessingService)
    {
        this.projectProcessingService = projectProcessingService;
        this.projectTypesProcessingService = projectTypesProcessingService;
        this.projectDependenciesProcessingService = projectDependenciesProcessingService;
    }

    public async Task<ProjectModel> GenerateProjectModelAsync(string projectFilePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string resolvedPath = projectProcessingService.ResolveProjectFilePath(suppliedPath: projectFilePath);

        var project = new ProjectModel
        {
            Name = Path.GetFileNameWithoutExtension(path: resolvedPath),
            Path = resolvedPath,
            Types = Array.Empty<DefinedType>(),
            Dependencies = Array.Empty<TypeRelationship>()
        };

        await projectTypesProcessingService.PopulateTypesAsync(project: project, cancellationToken: cancellationToken);
        await projectDependenciesProcessingService.PopulateDependenciesAsync(project: project, cancellationToken: cancellationToken);
        return project;
    }
}