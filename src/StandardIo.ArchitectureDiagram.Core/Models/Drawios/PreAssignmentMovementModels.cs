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
