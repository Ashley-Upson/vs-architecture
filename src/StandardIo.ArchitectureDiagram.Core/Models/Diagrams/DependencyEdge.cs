namespace StandardIo.ArchitectureDiagram.Core.Models;

public sealed record DependencyEdge(
    string Id,
    string SourceId,
    string TargetId,
    string Kind);
