using System.Collections.Generic;

namespace StandardIo.ArchitectureDiagram.Core.Graph;

public record DiagramModel(
    IReadOnlyList<ProjectContainer> Projects,
    IReadOnlyList<ExternalDependencyNode> ExternalDependencies,
    IReadOnlyList<DependencyEdge> Edges,
    DiagramMetadata? Metadata = null);

public sealed record ArchitectureGraph(
    IReadOnlyList<ProjectContainer> Projects,
    IReadOnlyList<ExternalDependencyNode> ExternalDependencies,
    IReadOnlyList<DependencyEdge> Edges)
    : DiagramModel(Projects, ExternalDependencies, Edges, new DiagramMetadata());

public sealed record DiagramMetadata(
    int SchemaVersion = 1,
    string GeneratedBy = "StandardIo.ArchitectureDiagram");

public sealed record ProjectContainer(
    string Id,
    string Name,
    IReadOnlyList<TypeNode> Types,
    string UniqueId = "");

public sealed record TypeNode(
    string Id,
    string ProjectId,
    string Name,
    string FullName,
    string Kind,
    string UniqueId = "",
    IReadOnlyList<string>? Interfaces = null,
    IReadOnlyList<TypeProperty>? Properties = null,
    int MethodCount = 0);

public sealed record TypeProperty(
    string Name,
    string TypeName,
    string? TypeFullName = null,
    string? TypeId = null);

public sealed record ExternalDependencyNode(
    string Id,
    string Name,
    string AssemblyName,
    string UniqueId = "",
    string FullName = "",
    string Tag = "");

public sealed record DependencyEdge(
    string Id,
    string SourceId,
    string TargetId,
    string Kind);
