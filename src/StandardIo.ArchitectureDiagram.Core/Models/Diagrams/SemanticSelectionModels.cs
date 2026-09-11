using System.Collections.Generic;

namespace StandardIo.ArchitectureDiagram.Core.Models;

public sealed record RootDiscoveryPatternDefinition(int PatternIndex, int SourceLine, string PatternText);

public sealed record SemanticRootMatch(
    string SemanticNodeId,
    string MatchedCanonicalValue,
    int PatternIndex,
    int SourceLine,
    string PatternText);

public sealed record SemanticSelectionReport(
    string ScopePolicy,
    IReadOnlyList<RootDiscoveryPatternDefinition> Patterns,
    IReadOnlyList<SemanticRootMatch> Roots,
    IReadOnlyList<string> SelectedNodeIds,
    IReadOnlyList<string> OmittedNodeIds,
    IReadOnlyList<string> SelectedLinkIds,
    IReadOnlyList<string> OmittedLinkIds,
    IReadOnlyList<int> UnmatchedPatternIndexes);
