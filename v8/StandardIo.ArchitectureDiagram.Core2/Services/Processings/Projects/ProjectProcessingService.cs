// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System;
using System.IO;
using StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Projects;

namespace StandardIo.ArchitectureDiagram.Core2.Services.Processings.Projects;
internal sealed class ProjectProcessingService : IProjectProcessingService
{
    private readonly IProjectService projectService;
    public ProjectProcessingService(IProjectService projectService) => this.projectService = projectService;
    public string ResolveProjectFilePath(string suppliedPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(argument: suppliedPath);
        string path = projectService.GetFullPath(path: suppliedPath);
        bool isFile = projectService.FileExists(path: path);

        if (isFile && path.EndsWith(value: ".csproj", comparisonType: StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }

        string directory = isFile ? projectService.GetDirectoryPath(path: path) : path;

        if (!projectService.DirectoryExists(path: directory))
        {
            throw new DirectoryNotFoundException(message: $"Project path '{path}' does not exist.");
        }

        string[] projects = projectService.GetProjectFiles(directory: directory);

        return projects.Length switch
        {
            1 => projectService.GetFullPath(path: projects[0]),
            0 => throw new FileNotFoundException(message: "The containing folder has no .csproj file.", fileName: directory),
            _ => throw new InvalidOperationException(message: "The containing folder has multiple projects; supply a specific .csproj path.")};
    }
}