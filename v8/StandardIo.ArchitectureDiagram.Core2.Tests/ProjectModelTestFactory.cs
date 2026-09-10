// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System;
using System.IO;
using StandardIo.ArchitectureDiagram.Core2.Brokers.Files;
using StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Projects;
using StandardIo.ArchitectureDiagram.Core2.Services.Processings.Projects;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using StandardIo.ArchitectureDiagram.Core2.Models;
using StandardIo.ArchitectureDiagram.Core2.Brokers.Roslyn;
using StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Types;
using StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Dependencies;
using StandardIo.ArchitectureDiagram.Core2.Services.Processings.Types;
using StandardIo.ArchitectureDiagram.Core2.Services.Processings.Dependencies;
using StandardIo.ArchitectureDiagram.Core2.Services.Orchestrations.Projects;

namespace StandardIo.ArchitectureDiagram.Core2.Tests;
internal static class ProjectModelTestFactory
{
    public static ProjectModelOrchestrationService Create(IRoslynBroker? broker = null, IProjectService? projectService = null)
    {
        broker ??= new RoslynBroker();
        return new ProjectModelOrchestrationService(projectProcessingService: new ProjectProcessingService(projectService ?? new ProjectService(new FileBroker())), projectTypesProcessingService: new ProjectTypesProcessingService(new ProjectTypesService(broker)), projectDependenciesProcessingService: new ProjectDependenciesProcessingService(new ProjectDependenciesService(broker)));
    }

    public static Task<ProjectModel> ExtractAsync(Project project) =>
        Create(broker: new CompilationBroker(project), projectService: new FixtureProjectService())
        .GenerateProjectModelAsync(projectFilePath: project.FilePath!);
    private sealed class FixtureProjectService : IProjectService
    {
        public string GetFullPath(string path) =>
            Path.GetFullPath(path: path);

        public string GetDirectoryPath(string path) =>
            Path.GetDirectoryName(path: path)!;

        public bool FileExists(string path) =>
            true;

        public bool DirectoryExists(string path) =>
            true;

        public string[] GetProjectFiles(string directory) =>
            throw new NotSupportedException();
    }

    private sealed class CompilationBroker(Project project) : RoslynBroker
    {
        public override async Task<Compilation> LoadCompilationAsync(string projectFilePath, CancellationToken cancellationToken) =>
            (await project.GetCompilationAsync(cancellationToken: cancellationToken))!;
    }
}