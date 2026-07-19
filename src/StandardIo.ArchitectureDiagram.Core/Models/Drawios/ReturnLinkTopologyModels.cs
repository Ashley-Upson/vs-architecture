using System.Collections.Generic;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal enum ReturnLinkTopologyKind { SameLayer, Upward }

internal sealed record ReturnLinkPlan(
    string LogicalRouteId,
    ReturnLinkTopologyKind Kind,
    string SourceNodeId,
    string TargetNodeId,
    VerticalLinkColumnDemand ColumnDemand,
    int DepartureY,
    int ArrivalY);

internal sealed record ReturnLinkAssignmentReport(
    IReadOnlyList<ReturnLinkPlan> Plans,
    VerticalLinkColumnAssignment VerticalColumns,
    IReadOnlyList<GeneralDownwardLinkAssignment> Assignments);
