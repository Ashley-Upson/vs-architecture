// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------

using System.Collections.Generic;

namespace StandardIo.ArchitectureDiagram.Core2.Brokers.Files;

internal interface IFileBroker
{
    string GetFullPath(string path);
    string GetDirectoryPath(string path);
    bool FileExists(string path);
    bool DirectoryExists(string path);
    string[] GetProjectFiles(string directory);
}
