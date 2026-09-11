using System;
using System.Collections.Generic;

namespace StandardIo.ArchitectureDiagram.Core.Models.ArchitectureV6;

public enum TerminalDirectionGroupKind
{
    Left,
    Down,
    Right
}

public enum EndpointReservationRole
{
    SourceEndpointEnvelope,
    DestinationEndpointEnvelope,
    SourceTerminalDrop,
    DestinationTerminalApproach,
    EndpointHandoff,
    EndpointTurn
}

public sealed record TerminalDemand(
    string PhysicalNodeId,
    GridSide Side,
    IReadOnlyList<string> PhysicalLinkIds,
    int TerminalCount,
    int MinimumSpacing,
    int Inset,
    int RequiredWidth,
    string Provenance);

public sealed record TerminalDirectionGroup(
    TerminalDirectionGroupKind Direction,
    IReadOnlyList<string> PhysicalLinkIds,
    int LogicalOrder,
    string Provenance);

public sealed record TerminalOrder(
    string PhysicalLinkId,
    string PhysicalNodeId,
    GridSide Side,
    TerminalDirectionGroupKind Direction,
    int DirectionOrder,
    int GlobalOrder,
    string Provenance);

public sealed record LogicalEndpointEnvelope(
    string EnvelopeId,
    string PhysicalNodeId,
    GridSide Side,
    IReadOnlyList<string> PhysicalLinkIds,
    IReadOnlyList<PlanningGridCellId> Cells,
    int RequiredWidth,
    int RequiredHeight,
    IReadOnlyList<TerminalDirectionGroup> DirectionGroups,
    string Provenance);

public sealed record EndpointEnvelopeReservation(
    string ReservationId,
    string PhysicalNodeId,
    GridSide Side,
    IReadOnlyList<string> PhysicalLinkIds,
    IReadOnlyList<PlanningGridCellId> Cells,
    EndpointReservationRole Role,
    int Iteration,
    string Provenance);

public sealed record FinalTerminalSlot(
    string PhysicalLinkId,
    string PhysicalNodeId,
    GridSide Side,
    RelativePoint Point,
    int DirectionOrder,
    string Provenance);

public sealed record EndpointHandoff(
    string PhysicalLinkId,
    GridSide Side,
    IReadOnlyList<PlanningGridCellId> ReservedCells,
    IReadOnlyList<PlanningGridCellId> RouteCells,
    string Provenance,
    RelativePoint? SourceRouteHandoffPoint = null,
    RelativePoint? DestinationRouteHandoffPoint = null);

public sealed record ArchitectureEndpointPlanningResult(
    IReadOnlyList<TerminalDemand> Demands,
    IReadOnlyList<TerminalDirectionGroup> DirectionGroups,
    IReadOnlyList<TerminalOrder> Orders,
    IReadOnlyList<LogicalEndpointEnvelope> Envelopes,
    IReadOnlyList<EndpointEnvelopeReservation> Reservations,
    IReadOnlyList<ArchitecturePlanningDiagnostic> Diagnostics,
    IReadOnlyList<ProjectRoutingGrid> ProjectGrids,
    DiagramRoutingGrid DiagramGrid,
    int Iteration = 0);

public enum ArchitectureConvergenceRequirementKind
{
    NodeFootprintExpansion,
    EndpointEnvelopeExpansion,
    EndpointCorridorConflict,
    TrackCapacityExpansion
}

public sealed record ArchitectureConvergenceRequirement(
    ArchitectureConvergenceRequirementKind Kind,
    string OwnerId,
    int CurrentValue,
    int RequiredValue,
    string Requester,
    string Reason,
    IReadOnlyList<string> AffectedPhysicalLinkIds,
    int Iteration);
