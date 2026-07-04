using System.Collections.Generic;

namespace StandardIo.ArchitectureDiagram.Core.Models;

public record DiagramModel(
    IReadOnlyList<ProjectContainer> Projects,
    IReadOnlyList<ExternalDependencyNode> ExternalDependencies,
    IReadOnlyList<DependencyEdge> Edges,
    DiagramMetadata? Metadata = null);
