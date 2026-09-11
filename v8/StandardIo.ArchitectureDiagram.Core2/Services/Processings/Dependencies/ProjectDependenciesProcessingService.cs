// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.Core2.Models;
using StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Dependencies;

namespace StandardIo.ArchitectureDiagram.Core2.Services.Processings.Dependencies;
internal sealed class ProjectDependenciesProcessingService : IProjectDependenciesProcessingService
{
    private readonly IProjectDependenciesService projectDependenciesService;
    public ProjectDependenciesProcessingService(IProjectDependenciesService projectDependenciesService) => this.projectDependenciesService = projectDependenciesService;
    public async Task PopulateDependenciesAsync(ProjectModel project, CancellationToken cancellationToken)
    {
        await projectDependenciesService.PopulateDependenciesAsync(project: project, cancellationToken: cancellationToken);

        project.Dependencies = project.Dependencies!.DistinctBy(keySelector: link => (link.DependencyType, link.FromType, link.ToType, link.FromMethod, link.ToMethod, link.ExecutorType, link.RegistrationType, link.IsInjected))
            .OrderBy(keySelector: link => link.FromType, comparer: StringComparer.Ordinal)
            .ThenBy(keySelector: link => link.ToType, comparer: StringComparer.Ordinal)
            .ThenBy(keySelector: link => link.DependencyType)
            .ThenBy(keySelector: link => link.FromMethod, comparer: StringComparer.Ordinal)
            .ThenBy(keySelector: link => link.ToMethod, comparer: StringComparer.Ordinal)
            .ToArray();
    }
}