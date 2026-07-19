using System.Collections.Generic;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal enum PositionalParentSelectionReason
{
    OnlyCandidate,
    DirectDownwardPath,
    LeastHorizontalMovement,
    ShortestConnectionDistance,
    LeftmostParent,
    StableNodeId
}

internal sealed record PositionalParentCandidate(
    string ParentNodeId,
    bool DirectDownwardPath,
    int HorizontalMovement,
    int ConnectionDistance,
    int ParentLeft);

internal sealed record PositionalParentSelection(
    string NodeId,
    string ParentNodeId,
    PositionalParentSelectionReason Reason,
    IReadOnlyList<PositionalParentCandidate> Candidates);

internal sealed record PositionalSubtreeEnvelope(
    string RootNodeId,
    Rect OverallBounds,
    IReadOnlyDictionary<int, Rect> BoundsByLayer,
    IReadOnlyDictionary<int, int> LeftBoundaryByLayer,
    IReadOnlyDictionary<int, int> RightBoundaryByLayer,
    int MinimumLayer,
    int MaximumLayer,
    string? ProjectId,
    LayoutRevision PositionalRevision);

internal sealed record PositionalHierarchy(
    IReadOnlyDictionary<string, string> ParentByNode,
    IReadOnlyDictionary<string, IReadOnlyList<string>> ChildrenByNode,
    IReadOnlyList<string> RootNodeIds,
    IReadOnlyDictionary<string, PositionalParentSelection> ParentSelections,
    IReadOnlyDictionary<string, PositionalSubtreeEnvelope> EnvelopesByRootNode,
    LayoutRevision Revision);

internal sealed record HorizontalMovementIteration(
    PlacedGraph Placement,
    IReadOnlyList<MovementScopeIdentity> AppliedScopes,
    IReadOnlyList<string> MovedNodeIds,
    IReadOnlyList<string> InvalidatedLinkIds,
    int MaximumDelta,
    bool Changed);

internal sealed record HorizontalCompactionMove(
    string SubtreeRootNodeId,
    int DeltaX,
    int GapBefore,
    int GapAfter);

internal sealed record HorizontalCompactionResult(
    PlacedGraph Placement,
    IReadOnlyList<HorizontalCompactionMove> Moves,
    int MaximumUnownedGapBefore,
    int MaximumUnownedGapAfter);
