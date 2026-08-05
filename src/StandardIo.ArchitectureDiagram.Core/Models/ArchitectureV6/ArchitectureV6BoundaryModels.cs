using System;
using System.Collections.Generic;
using System.Linq;

namespace StandardIo.ArchitectureDiagram.Core.Models.ArchitectureV6;

public sealed record GridBoundaryIdentity(
    PlanningGridId GridId,
    PlanningGridCellId CellId,
    GridSide Side,
    LaneId? Lane,
    string OwnershipScope,
    string AuthorityId)
{
    public override string ToString() =>
        $"{GridId.Value}:{CellId.RowId.Value}/{CellId.ColumnId.Value}:{Side}:{Lane?.Value ?? "none"}:{AuthorityId}:{OwnershipScope}";
}

public enum PlannedRouteComponentKind
{
    SourceTerminal,
    SourceDeparture,
    HorizontalStraightRun,
    VerticalStraightRun,
    Turn,
    OwnershipTransition,
    ProjectTransition,
    DestinationApproach,
    DestinationTerminal
}

public sealed record PlannedRouteComponentContract(
    string ComponentId,
    string PhysicalLinkId,
    PlannedRouteComponentKind Kind,
    int Order,
    IReadOnlyList<PlanningGridCellId> Cells,
    GridBoundaryIdentity? EntryBoundary,
    GridBoundaryIdentity? ExitBoundary,
    GridSide? EntrySide,
    GridSide? ExitSide,
    LaneId? Lane,
    string? RunId,
    string? TurnId,
    string OwnershipScope,
    string? PrecedingComponentId,
    string? FollowingComponentId,
    string Provenance);

public sealed record RouteBoundaryContractFinding(
    string Code,
    string PhysicalLinkId,
    string ComponentId,
    string? OtherComponentId,
    string Message,
    string? ExpectedBoundary,
    string? ActualBoundary);

public sealed record PlannedRouteBoundaryContract(
    string PhysicalLinkId,
    RouteTopologyFamily TopologyFamily,
    IReadOnlyList<PlannedRouteComponentContract> Components,
    IReadOnlyList<RouteBoundaryContractFinding> Findings,
    bool IsValid,
    string Provenance);

public sealed record ArchitectureRouteBoundaryValidationResult(
    IReadOnlyList<PlannedRouteBoundaryContract> Routes,
    IReadOnlyList<RouteBoundaryContractFinding> Findings)
{
    public int ValidRouteCount => Routes.Count(route => route.IsValid);
    public int InvalidRouteCount => Routes.Count(route => !route.IsValid);
}
