using System.Collections.Generic;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal enum LinkSegmentOrientation { Horizontal, Vertical }
internal enum LinkSegmentRole
{
    ConnectionDeparture,
    ConnectionArrival,
    Through,
    Return,
    ReturnDeparture,
    ReturnArrival,
    ObstacleBypass,
    TurnTransition,
    AdjacentDeparture,
    LongDeparture,
    BoundaryHorizontal
}

internal sealed record LinkSegmentDemand(
    string Id,
    string LogicalRouteId,
    LinkSegmentOrientation Orientation,
    AxisInterval OccupiedInterval,
    AxisInterval AllowedAxisRange,
    int? PreferredAxis,
    LinkSegmentRole Role,
    int? ConnectionOrder,
    int? TurnOrder,
    MovementScopeIdentity? MovementScope,
    LayoutRevision PlacementRevision,
    RouteRevision RouteRevision,
    string? OwnershipEnvelopeId = null);

internal sealed record AssignedLinkSegment(
    string Id,
    string DemandId,
    string LogicalRouteId,
    LinkSegmentOrientation Orientation,
    int AxisCoordinate,
    int SlotIndex,
    AxisInterval OccupiedInterval,
    LinkSegmentRole Role,
    LayoutRevision PlacementRevision,
    RouteRevision RouteRevision);

internal sealed record LinkTransition(
    string Id,
    string LogicalRouteId,
    string FromAssignedLinkSegmentId,
    string ToAssignedLinkSegmentId,
    Point Turn,
    int Order,
    LayoutRevision PlacementRevision,
    RouteRevision RouteRevision);

internal sealed record SharedTurnAllocation(
    IReadOnlyDictionary<string, IReadOnlyList<LinkTransition>> TransitionsByRouteId,
    IReadOnlyList<string> RejectedRouteIds);

internal sealed record LayerSuffixConstraintIteration(
    PlacedGraph Placement,
    IReadOnlyList<int> LayersMoved,
    IReadOnlyList<string> NodesMoved,
    IReadOnlyList<string> InvalidatedRouteIds,
    int MaximumDelta,
    bool Changed);

internal enum MovementScopeKind
{
    Node,
    LayoutSubtree,
    OrderedSiblingPrefix,
    OrderedSiblingSuffix,
    LayerAndLowerSuffix,
    ProjectRoot,
    OrderedProjectPrefix,
    OrderedProjectSuffix
}

internal readonly record struct MovementScopeIdentity(MovementScopeKind Kind, string Id);

internal enum GenerationConstraintKind { MinimumX, MaximumX, MinimumY, MinimumWidth, MinimumHeight }

internal readonly record struct GenerationConstraintKey(
    MovementScopeIdentity Scope,
    GenerationConstraintKind Kind);

internal sealed record GenerationConstraint(
    GenerationConstraintKey Key,
    int Minimum,
    string Reason);

internal enum HardGeometryDefectKind
{
    NodeCollision,
    SharedSegment,
    ReusedBend,
    SpacingDeficit,
    NonOrthogonalSegment,
    ImmediateReversal
}

internal enum DefectResolutionKind
{
    LinkSegmentDemandAlternatives,
    IncreasedExtentDemand,
    RejectTopologyAndRegenerate
}

internal sealed record DefectDemandContract(
    HardGeometryDefectKind Defect,
    DefectResolutionKind Resolution,
    IReadOnlyList<LinkSegmentRole> LinkSegmentRoles,
    bool IsSpacingDemand,
    string Requirement);
