// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using StandardIo.ArchitectureDiagram.Core2.Brokers.Files;

namespace StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Projects;
internal sealed class ProjectService : IProjectService
{
    private readonly IFileBroker fileBroker;
    public ProjectService(IFileBroker fileBroker) => this.fileBroker = fileBroker;
    public string GetFullPath(string path) =>
        fileBroker.GetFullPath(path: path);

    public string GetDirectoryPath(string path) =>
        fileBroker.GetDirectoryPath(path: path);

    public bool FileExists(string path) =>
        fileBroker.FileExists(path: path);

    public bool DirectoryExists(string path) =>
        fileBroker.DirectoryExists(path: path);

    public string[] GetProjectFiles(string directory) =>
        fileBroker.GetProjectFiles(directory: directory);
}