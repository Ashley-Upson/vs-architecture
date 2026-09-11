// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System.IO;

namespace StandardIo.ArchitectureDiagram.Core2.Brokers.Files;
internal sealed class FileBroker : IFileBroker
{
    public string GetFullPath(string path) =>
        Path.GetFullPath(path: path);

    public string GetDirectoryPath(string path) =>
        Path.GetDirectoryName(path: path)!;

    public bool FileExists(string path) =>
        File.Exists(path: path);

    public bool DirectoryExists(string path) =>
        Directory.Exists(path: path);

    public string[] GetProjectFiles(string directory) =>
        Directory.GetFiles(path: directory, searchPattern: "*.csproj", searchOption: SearchOption.TopDirectoryOnly);
}