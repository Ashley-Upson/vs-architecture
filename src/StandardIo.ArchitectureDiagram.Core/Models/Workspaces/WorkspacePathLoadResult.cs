using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace StandardIo.ArchitectureDiagram.Core.Brokers.Workspaces;

public sealed class WorkspacePathLoadResult
{
    public WorkspacePathLoadResult(string name, IReadOnlyList<Project> projects)
    {
        Name = name;
        Projects = projects;
    }

    public string Name { get; }

    public IReadOnlyList<Project> Projects { get; }
}
