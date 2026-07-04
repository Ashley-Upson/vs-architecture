using System.Collections.Generic;

namespace StandardIo.ArchitectureDiagram.Core.Models;

public sealed record ArchitectureGraph(
    IReadOnlyList<ProjectContainer> Projects,
    IReadOnlyList<ExternalDependencyNode> ExternalDependencies,
    IReadOnlyList<DependencyEdge> Edges)
    : DiagramModel(Projects, ExternalDependencies, Edges, new DiagramMetadata());
