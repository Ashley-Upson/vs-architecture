using System;
using System.Collections.Generic;

namespace StandardIo.ArchitectureDiagram.Core.Models.ArchitectureV6;

public sealed record PlannedLaneAllocation(
    string RunId,
    string RouteId,
    PlanningGridId GridId,
    RouteAxis Axis,
    string DomainId,
    int Ordinal,
    LaneId Lane,
    IReadOnlyList<PlanningGridCellId> Cells,
    int IntervalStart,
    int IntervalEnd,
    RouteTopologyFamily TopologyFamily,
    string OwnershipRegion,
    string Provenance);

public sealed record PlannedEndpointAllocation(
    string PhysicalLinkId,
    string PhysicalNodeId,
    GridSide Side,
    NodeEndpoint Endpoint,
    int LogicalOrder,
    int TrackOffset,
    string DomainId,
    string Provenance,
    LaneId? TerminalLane = null,
    PlanningGridColumnId? TerminalColumnId = null,
    LaneId? HandoffHorizontalLane = null,
    LaneId? HandoffVerticalLane = null);

public sealed record PlannedDestinationApproachAllocation(
    string ReservationId,
    string PhysicalNodeId,
    string PhysicalLinkId,
    PlanningGridId GridId,
    int LogicalOrder,
    int TrackOffset,
    string DomainId,
    string Provenance);

public sealed record PlannedTurnAllocation(
    string RouteId,
    string CellId,
    GridSide EntrySide,
    GridSide ExitSide,
    string? HorizontalRunId,
    string? VerticalRunId,
    string BendIdentity,
    int ClearanceOrdinal,
    string Provenance);

public sealed record PlannedCleanCrossing(
    string CellId,
    string HorizontalRunId,
    string VerticalRunId,
    string Provenance,
    string? HorizontalPhysicalLinkId = null,
    string? VerticalPhysicalLinkId = null);

public sealed record PlannedProjectTransitionAllocation(
    string PhysicalLinkId,
    PlanningGridId SourceGridId,
    PlanningGridId DestinationGridId,
    string BoundaryDomainId,
    int Ordinal,
    LaneId Lane,
    string Provenance);

public enum LaneAllocationConflictKind
{
    InsufficientEndpointSpan,
    InsufficientHorizontalLaneCapacity,
    InsufficientVerticalLaneCapacity,
    TurnCongestion,
    IncompatibleLaneOrdering,
    BlockedDestinationApproach,
    ProjectTransitionCongestion,
    OwnershipLocalReturnCapacity
}

public sealed record LaneAllocationConflict(
    LaneAllocationConflictKind Kind,
    string OwnerId,
    int RequiredCapacity,
    string Message,
    IReadOnlyList<string> RouteIds);

public sealed record NodeFootprintExpansionRequirement(
    string PhysicalNodeId,
    int CurrentSpan,
    int RequiredOddSpan,
    int RequiredEndpointSlots,
    string Reason,
    IReadOnlyList<string> PhysicalLinkIds);

public sealed record ArchitectureLaneAllocationResult(
    IReadOnlyList<PlannedGridRoute> Routes,
    IReadOnlyList<PlannedStraightRun> StraightRuns,
    IReadOnlyList<PlannedLaneAllocation> HorizontalLanes,
    IReadOnlyList<PlannedLaneAllocation> VerticalLanes,
    IReadOnlyList<PlannedEndpointAllocation> Endpoints,
    IReadOnlyList<PlannedDestinationApproachAllocation> DestinationApproaches,
    IReadOnlyList<PlannedTurnAllocation> Turns,
    IReadOnlyList<PlannedCleanCrossing> CleanCrossings,
    IReadOnlyList<PlannedProjectTransitionAllocation> ProjectTransitions,
    IReadOnlyList<LaneAllocationConflict> Conflicts,
    IReadOnlyList<NodeFootprintExpansionRequirement> FootprintExpansionRequirements,
    GridTrackSizingPlan Sizing,
    IReadOnlyList<ArchitecturePlanningDiagnostic> Diagnostics,
    LaneAllocationPerformance? Performance = null,
    ArchitectureRouteBoundaryValidationResult? BoundaryValidation = null,
    IReadOnlyList<FinalTerminalSlot>? FinalTerminalSlots = null,
    IReadOnlyList<EndpointHandoff>? EndpointHandoffs = null,
    IReadOnlyList<ArchitectureConvergenceRequirement>? ConvergenceRequirements = null);

public sealed record LaneAllocationPerformance(
    long ElapsedMilliseconds,
    int DomainCount,
    int OrderingVertexCount,
    int OrderingEdgeCount,
    int OrderingCycleCount,
    int TurnCellCount,
    int MaximumTurnsInCell,
    long IntervalComparisons,
    int CapacityRequirementCount);
