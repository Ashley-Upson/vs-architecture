using System.Collections.Generic;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal enum ReturnLinkTopologyKind { SameLayer, Upward }

internal sealed record ReturnColumnOwnership(
    int FirstProjectOrder,
    int LastProjectOrder,
    IReadOnlyList<string> ProjectIds,
    Rect OwnershipBounds,
    LayoutRevision OwnershipRevision)
{
    public string Id => $"projects:{FirstProjectOrder}-{LastProjectOrder}:layout-{OwnershipRevision.Value}";
}

internal sealed record AssignedReturnLinkColumn(
    AssignedVerticalLinkColumn Column,
    ReturnColumnOwnership Ownership);

internal sealed record ReturnLinkPlan(
    string LogicalRouteId,
    ReturnLinkTopologyKind Kind,
    string SourceNodeId,
    string TargetNodeId,
    ReturnColumnOwnership Ownership,
    VerticalLinkColumnDemand ColumnDemand,
    InterLayerId DepartureInterLayer,
    InterLayerId ArrivalInterLayer,
    LinkSegmentDemand DepartureDemand,
    LinkSegmentDemand ArrivalDemand);

internal sealed record ReturnLinkAssignmentReport(
    IReadOnlyList<ReturnLinkPlan> Plans,
    VerticalLinkColumnAssignment VerticalColumns,
    IReadOnlyList<AssignedReturnLinkColumn> OwnedColumns,
    IReadOnlyList<CommonAuthorityRegionObservation> SlotRegions,
    IReadOnlyList<GeneralDownwardLinkAssignment> Assignments);
