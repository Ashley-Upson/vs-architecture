using System.Collections.Generic;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal sealed record SemanticLayerGroupDiagnostic(
    string ProjectId,
    int ConfiguredOrder,
    string Name,
    string Pattern,
    int MatchedNodeCount,
    IReadOnlyDictionary<int, int> PreviousDepthDistribution,
    int PreviousMajorityDepth,
    int FinalSemanticDepth);

internal sealed record UnmatchedSemanticLayerDiagnostic(
    string ProjectId,
    string NodeId,
    string NodeName,
    int OriginalDepth,
    IReadOnlyList<string> PlacedParentEvidence,
    IReadOnlyList<string> PlacedChildEvidence,
    int SelectedSemanticDepth,
    string DecisionReason,
    bool ConstraintConflict);

internal sealed record ConfiguredSemanticLayerPlacementResult(
    bool Enabled,
    IReadOnlyList<SemanticLayerGroupDiagnostic> ActiveGroups,
    IReadOnlyList<UnmatchedSemanticLayerDiagnostic> UnmatchedNodes,
    IReadOnlyDictionary<string, int> OriginalDepthByNodeId,
    IReadOnlyDictionary<string, int> FinalDepthByNodeId,
    int ExternalNodeCount,
    IReadOnlyDictionary<string, int> ExternalDepthByProject);
