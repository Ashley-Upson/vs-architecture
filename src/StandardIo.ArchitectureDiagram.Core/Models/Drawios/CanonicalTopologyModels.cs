using System.Collections.Generic;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal enum CanonicalTopologyFamily
{
    AdjacentDownward,
    LongDownward,
    SameLayerReturn,
    UpwardReturn,
    InternalToExternal,
    ExternalToInternal,
    CrossProjectBoundaryTransition
}

internal enum CanonicalTerminal { SourceBottom, TargetTop }

internal enum CanonicalTopologySegmentRole
{
    Departure,
    Through,
    Arrival,
    DestinationColumn,
    ReturnColumn,
    BoundaryTransition
}

internal sealed record CanonicalTopologySegmentRequirement(
    CanonicalTopologySegmentRole Role,
    LinkSegmentOrientation Orientation,
    bool Mandatory,
    bool Allocatable);

internal sealed record CanonicalTopologyPlan(
    string LogicalRouteId,
    CanonicalTopologyFamily Family,
    CanonicalTerminal SourceTerminal,
    CanonicalTerminal TargetTerminal,
    IReadOnlyList<CanonicalTopologySegmentRole> OrderedTransitions,
    IReadOnlyList<CanonicalTopologySegmentRequirement> Segments,
    InterLayerId? DepartureInterLayer,
    InterLayerId? ArrivalInterLayer,
    bool RequiresDestinationColumn,
    bool RequiresReturnColumn,
    IReadOnlyList<string> OwnershipLocalProjectIds,
    int PreferredBendCount);

internal sealed record CanonicalTopologySelection(
    IReadOnlyDictionary<string, CanonicalTopologyPlan> Plans,
    IReadOnlyList<string> Rejections);
