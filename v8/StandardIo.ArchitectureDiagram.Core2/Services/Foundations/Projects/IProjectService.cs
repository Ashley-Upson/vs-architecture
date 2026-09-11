// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
namespace StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Projects;
internal interface IProjectService
{
    string GetFullPath(string path);

    string GetDirectoryPath(string path);

    bool FileExists(string path);

    bool DirectoryExists(string path);

    string[] GetProjectFiles(string directory);
}