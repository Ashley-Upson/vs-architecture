namespace StandardIo.ArchitectureDiagram.Core.Models;

public sealed record ExternalDependencyNode(
    string Id,
    string Name,
    string AssemblyName,
    string UniqueId = "",
    string FullName = "",
    string Tag = "");
