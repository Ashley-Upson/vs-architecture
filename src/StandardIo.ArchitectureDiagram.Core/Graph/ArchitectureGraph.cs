using System.Collections.Generic;

namespace StandardIo.ArchitectureDiagram.Core.Graph;

public sealed record ArchitectureGraph(
    IReadOnlyList<ProjectContainer> Projects,
    IReadOnlyList<ExternalDependencyNode> ExternalDependencies,
    IReadOnlyList<DependencyEdge> Edges);

public sealed record ProjectContainer(
    string Id,
    string Name,
    IReadOnlyList<TypeNode> Types);

public sealed record TypeNode(
    string Id,
    string ProjectId,
    string Name,
    string FullName,
    string Kind);

public sealed record ExternalDependencyNode(
    string Id,
    string Name,
    string AssemblyName);

public sealed record DependencyEdge(
    string Id,
    string SourceId,
    string TargetId,
    string Kind);
