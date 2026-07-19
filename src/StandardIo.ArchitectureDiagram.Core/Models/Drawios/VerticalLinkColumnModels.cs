using System.Collections.Generic;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal sealed record DownwardLinkTopologyDemand(
    IReadOnlyList<LinkSegmentDemand> SegmentDemands,
    IReadOnlyList<VerticalLinkColumnDemand> VerticalColumnDemands);

internal sealed record VerticalLinkColumnDemand(
    string Id,
    string LinkId,
    int PreferredX,
    AxisInterval AllowedXInterval,
    int SourceLayer,
    int DestinationLayer,
    AxisInterval VerticalInterval,
    int RequiredClearance,
    string SourceSubtreeId,
    string DestinationSubtreeId,
    string? ProjectId,
    MovementScopeIdentity? MovementScope,
    LayoutRevision PlacementRevision,
    RouteRevision LinkRevision,
    IReadOnlyList<AxisInterval>? ForbiddenXIntervals = null);

internal sealed record AssignedVerticalLinkColumn(
    string DemandId,
    string LinkId,
    int X,
    int SourceLayer,
    int DestinationLayer,
    AxisInterval VerticalInterval,
    int ColumnIndex,
    LayoutRevision PlacementRevision,
    RouteRevision LinkRevision);

internal sealed record VerticalLinkColumnAssignment(
    IReadOnlyDictionary<string, AssignedVerticalLinkColumn> ColumnsByDemandId,
    int ConflictComponentCount,
    long ConflictComparisons);
