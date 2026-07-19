using System.Collections.Generic;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal sealed record GeneralDownwardLinkPlan(
    string LogicalRouteId,
    bool Eligible,
    AdjacentDownwardRejectionReason? RejectionReason,
    IReadOnlyList<LinkSegmentDemand> Demands,
    IReadOnlyList<VerticalLinkColumnDemand> VerticalColumnDemands,
    string SourceNodeId,
    string TargetNodeId,
    IReadOnlyList<Point> CanonicalAuthoritativePoints,
    IReadOnlyList<string> Diagnostics);

internal sealed record GeneralDownwardObservationReport(
    IReadOnlyList<GeneralDownwardLinkPlan> Routes,
    long DemandProductionMicroseconds);

internal sealed record GeneralDownwardLinkAssignment(
    string LogicalRouteId,
    IReadOnlyList<AssignedLinkSegment> AssignedLinkSegments,
    IReadOnlyList<LinkTransition> Transitions,
    IReadOnlyList<Point> ReconstructedPoints,
    IReadOnlyList<string> Diagnostics,
    bool IsValid);

internal sealed record GeneralDownwardAssignmentReport(
    IReadOnlyList<SlotRegionAssignment> Regions,
    VerticalLinkColumnAssignment VerticalColumns,
    IReadOnlyList<GeneralDownwardLinkAssignment> Routes,
    long AssignmentMicroseconds,
    long TransitionMicroseconds);

internal sealed record SlotRegionAssignment(
    LinkSegmentAllocationRegionIdentity Region,
    DeterministicSlotAssignment Assignment,
    GenerationConstraint? ConstraintProposal);
