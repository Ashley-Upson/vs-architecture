using System.Collections.Generic;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal enum PositionalConstraintReason
{
    SubtreeSeparation,
    VerticalColumnClearance,
    DestinationColumnSeparation,
    ReturnStubClearance,
    ProjectSeparation
}

internal enum HorizontalMovementDirection { Left, Right }

internal enum HorizontalDifferenceConstraintKind
{
    ColumnToEnvelope,
    ColumnToColumn,
    ReturnColumnToOwnershipEnvelope,
    ProjectEnvelopeToProjectEnvelope
}

internal sealed record HorizontalDifferenceAlternative(
    HorizontalMovementDirection Direction,
    int RequiredCoordinate,
    IReadOnlyList<MovementScopeIdentity> MovementScopes);

internal sealed record ColumnToEnvelopeDifferenceConstraint(
    string Id,
    string LinkId,
    string ColumnDemandId,
    string DestinationSubtreeId,
    string BlockingSubtreeId,
    Rect BlockingEnvelope,
    AxisInterval VerticalLayerInterval,
    int RequiredClearance,
    HorizontalDifferenceAlternative LeftClearance,
    HorizontalDifferenceAlternative RightClearance,
    LayoutRevision PlacementRevision,
    LayoutRevision EnvelopeRevision);

internal sealed record ColumnToColumnDifferenceConstraint(
    string Id,
    string FirstLinkId,
    string SecondLinkId,
    string FirstDestinationSubtreeId,
    string SecondDestinationSubtreeId,
    AxisInterval SharedVerticalLayerInterval,
    int RequiredSeparation,
    IReadOnlyList<MovementScopeIdentity> CandidateMovementScopes,
    LayoutRevision PlacementRevision);

internal sealed record ReturnColumnEnvelopeConstraint(
    string Id,
    string LinkId,
    ReturnColumnOwnership Ownership,
    int LeftCandidateX,
    int RightCandidateX,
    IReadOnlyList<string> LeftBlockingSubtreeIds,
    IReadOnlyList<string> RightBlockingSubtreeIds,
    LayoutRevision PlacementRevision);

internal sealed record PositionalConstraintDemand(
    string Id,
    PositionalConstraintReason Reason,
    HorizontalMovementDirection Direction,
    int MinimumSeparation,
    AxisInterval AffectedLayerInterval,
    string LeftStructureId,
    string RightStructureId,
    IReadOnlyList<MovementScopeIdentity> CandidateMovementScopes,
    IReadOnlyList<string> SourceLinkIds,
    LayoutRevision PlacementRevision,
    RouteRevision TopologyDemandRevision);

internal sealed record PositionalConstraintComponent(
    string Id,
    IReadOnlyList<PositionalConstraintDemand> Demands,
    IReadOnlyList<string> NodeIds,
    IReadOnlyList<string> LinkIds);

internal sealed record PositionalMovementCandidate(
    string DemandId,
    MovementScopeIdentity Scope,
    HorizontalMovementDirection Direction,
    int Delta,
    IReadOnlyList<string> NodeIds,
    int ProjectWidthExpansion,
    int ParentUmbrellaDisruption,
    int InvalidatedLinkCount,
    int ResultingDepartureLength,
    bool IsValid,
    string RejectionReason);

internal sealed record PositionalConstraintSolution(
    PositionalConstraintComponent Component,
    IReadOnlyList<GenerationConstraint> Constraints,
    IReadOnlyList<PositionalMovementCandidate> EvaluatedCandidates,
    IReadOnlyList<PositionalMovementCandidate> AcceptedCandidates,
    bool IsValid,
    string Reason);

internal sealed record PreAssignmentMovementResult(
    PlacedGraph Placement,
    IReadOnlyList<PositionalConstraintComponent> Components,
    IReadOnlyList<PositionalConstraintSolution> Solutions,
    IReadOnlyList<GenerationConstraint> PersistentConstraints,
    IReadOnlyList<string> InvalidatedLinkIds,
    int Iterations);
