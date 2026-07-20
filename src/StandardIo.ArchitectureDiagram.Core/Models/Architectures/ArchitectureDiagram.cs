using System.Collections.Generic;

namespace StandardIo.ArchitectureDiagram.Core.Models.Architectures;

public sealed record ArchitectureNode(
    string Id,
    string ProjectId,
    string Name,
    string FullName,
    string Kind,
    string UniqueId,
    IReadOnlyList<string> Interfaces);

public sealed record ArchitectureProject(
    string Id,
    string Name,
    IReadOnlyList<ArchitectureNode> Nodes,
    string UniqueId);

public sealed record ArchitectureExternalNode(
    string Id,
    string Name,
    string AssemblyName,
    string UniqueId,
    string FullName,
    string Tag);

public sealed record ArchitectureLink(string Id, string SourceId, string TargetId, string Kind);

public sealed record ArchitectureRoot(
    string SemanticNodeId,
    string MatchedCanonicalValue,
    int PatternIndex,
    int SourceLine,
    string PatternText);

public sealed record ArchitectureSelectionDiagnostic(
    string ScopePolicy,
    IReadOnlyList<ArchitectureRoot> Roots,
    IReadOnlyList<string> SelectedNodeIds,
    IReadOnlyList<string> OmittedNodeIds,
    IReadOnlyList<string> SelectedLinkIds,
    IReadOnlyList<string> OmittedLinkIds,
    IReadOnlyList<int> UnmatchedPatternIndexes);

public sealed record ArchitectureDiagram(
    IReadOnlyList<ArchitectureProject> Projects,
    IReadOnlyList<ArchitectureExternalNode> ExternalNodes,
    IReadOnlyList<ArchitectureLink> Links,
    ArchitectureSelectionDiagnostic? Selection);
