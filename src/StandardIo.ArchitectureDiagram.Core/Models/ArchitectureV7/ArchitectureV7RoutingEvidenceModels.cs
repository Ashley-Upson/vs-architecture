using System.Collections.Generic;

namespace StandardIo.ArchitectureDiagram.Core.Models.ArchitectureV7;

public sealed record ArchitectureV7RoutingCandidateEvidence(
    int CandidateColumn,
    bool IsPlusOrMinusTwo,
    bool Accepted,
    string RejectionReason,
    int TraversedCellCount,
    IReadOnlyList<ArchitectureV7RouteCell> TraversedCells);

public sealed record ArchitectureV7RoutingCandidateSummary(
    int TotalCandidateCount,
    int? FirstCandidateExamined,
    int? LastCandidateExamined,
    int? SelectedCandidate,
    int? NearestCandidateDistance,
    int? FarthestCandidateDistanceExamined,
    IReadOnlyDictionary<string, int> RejectionReasonHistogram,
    IReadOnlyDictionary<int, int> CandidateDistanceHistogram,
    int TotalTraversedCellCount,
    int MaximumTraversedCellCount,
    ArchitectureV7RoutingCandidateEvidence? SelectedCandidateEvidence,
    ArchitectureV7RoutingCandidateEvidence? FinalFailedCandidateEvidence,
    IReadOnlyList<ArchitectureV7RoutingCandidateEvidence> RepresentativeFirstCandidates,
    IReadOnlyList<ArchitectureV7RoutingCandidateEvidence> RepresentativeLastCandidates);

public sealed record ArchitectureV7RelationshipRoutingEvidence(
    string SemanticRelationshipId,
    string PhysicalRelationshipId,
    string SourcePhysicalNodeId,
    string TargetPhysicalNodeId,
    ArchitectureV7RouteCell SourceCentreCell,
    ArchitectureV7RouteCell TargetCentreCell,
    string Scenario,
    IReadOnlyList<ArchitectureV7RouteCell> AttemptedCells,
    ArchitectureV7RouteCell? FailureCell,
    string? AttemptedEntryDirection,
    string? AttemptedExitDirection,
    ArchitectureV7CellCapability? FailureCapabilityMask,
    string? RejectionReason,
    int? CurrentContinuationColumn,
    int? IntendedDestinationColumn,
    ArchitectureV7RoutingCandidateSummary CandidateSummary,
    IReadOnlyList<string> EndpointClassifications,
    string ProjectCommonGridProvenance,
    IReadOnlyList<string> DiagnosticCodes);

public sealed record ArchitectureV7PlacementRoutingEvidence(
    string PhysicalNodeId,
    string? PositionalParentId,
    string TreeRootId,
    string? DetachedUnitId,
    string? ProjectId,
    int TreeLocalCentre,
    int TreeLocalSpan,
    string TreeLocalBounds,
    int ProjectCentre,
    int ProjectSpan,
    string ProjectBounds,
    int CommonGridCentre,
    int CommonGridSpan,
    string CommonGridBounds,
    string AtomicTopLevelUnitId,
    IReadOnlyList<string> ImmediateSiblingUnits,
    string CompositionOffset,
    string Provenance);
