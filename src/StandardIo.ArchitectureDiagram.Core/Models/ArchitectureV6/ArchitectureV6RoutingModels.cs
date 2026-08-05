using System;
using System.Collections.Generic;

namespace StandardIo.ArchitectureDiagram.Core.Models.ArchitectureV6;

public enum RouteTopologyFamily
{
    AdjacentDownward,
    LongDownward,
    SameLayer,
    Upward,
    OwnershipLocalReturn,
    External,
    CrossProject
}

public enum RouteStepRole
{
    HorizontalPassThrough,
    VerticalPassThrough,
    Turn,
    SourceExit,
    DestinationEntry,
    ProjectTransition,
    CleanCrossing,
    ProjectExit,
    DiagramGridPassage,
    ProjectEntry
}

public enum GridSide
{
    Top,
    Bottom,
    Left,
    Right
}

public sealed record NodeEndpoint(
    string PhysicalNodeId,
    GridSide Side,
    string TerminalId,
    int Order,
    PlanningGridId? GridId = null,
    PlanningGridColumnId? PreferredTrack = null,
    int MinimumStraightDistance = 1,
    string OrderingKey = "");

public sealed record PlannedGridRouteStep(
    PlanningGridId GridId,
    PlanningGridCellId CellId,
    GridSide EntrySide,
    GridSide ExitSide,
    RouteStepRole Role,
    int Order = 0,
    string TopologyProvenance = "",
    string? EndpointRelationship = null,
    LaneId? AllocatedLane = null,
    string? StraightRunId = null);

public sealed record GridTransition(
    PlanningGridId SourceGridId,
    PlanningGridCellId SourceBoundaryCellId,
    PlanningGridId DestinationGridId,
    PlanningGridCellId DestinationBoundaryCellId,
    string OwnershipTransition,
    string SemanticLinkId,
    string Direction = "",
    string? SourceProjectId = null,
    string? DestinationProjectId = null,
    bool ProjectLabelsPermit = true,
    bool ProjectContainersVisible = true);

public sealed record PlannedGridRoute(
    string PhysicalLinkId,
    NodeEndpoint Source,
    IReadOnlyList<PlannedGridRouteStep> Steps,
    IReadOnlyList<GridTransition> Transitions,
    NodeEndpoint Destination,
    RouteTopologyFamily TopologyFamily,
    string? SourceProjectId,
    string? DestinationProjectId,
    string Provenance = "",
    string? DestinationApproachReservationId = null,
    bool IsStructurallySupported = true,
    string? UnsupportedReason = null);

public enum RouteAxis
{
    Horizontal,
    Vertical
}

public readonly record struct LaneId
{
    public LaneId(string value)
    {
        Value = value?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(Value)) throw new ArgumentException("Lane id is required.", nameof(value));
    }
    public string Value { get; }
    public override string ToString() => Value;
}

public sealed record PlannedStraightRun(
    string RouteId,
    PlanningGridId GridId,
    RouteAxis Axis,
    IReadOnlyList<PlanningGridCellId> Cells,
    string StartTransition,
    string EndTransition,
    LaneId Lane);

public sealed record TurnDemand(string RouteId, string CellId, LaneId HorizontalLane, LaneId VerticalLane);
public sealed record CrossingDemand(string RouteId, string OtherRouteId, string CellId);
public sealed record NodeEndpointDemand(
    string PhysicalLinkId,
    NodeEndpoint Endpoint,
    int MinimumStraightSpacing,
    string? DestinationApproachReservationId);

public sealed record DestinationApproachReservation(
    string ReservationId,
    string PhysicalNodeId,
    PlanningGridId GridId,
    IReadOnlyList<PlanningGridCellId> Cells,
    IReadOnlyList<string>? PhysicalLinkIds = null,
    string ApproachMode = "direct");
