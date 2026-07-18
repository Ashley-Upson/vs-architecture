using System.Collections.Generic;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal enum RailOrientation { Horizontal, Vertical }
internal enum RailSemanticRole
{
    TerminalDeparture,
    TerminalArrival,
    Through,
    Return,
    ObstacleBypass,
    TurnTransition
}

internal sealed record RailDemand(
    string Id,
    string LogicalRouteId,
    RailOrientation Orientation,
    AxisInterval OccupiedInterval,
    AxisInterval AllowedAxisRange,
    int? PreferredAxis,
    RailSemanticRole Role,
    int? TerminalOrder,
    int? TurnOrder,
    MovementScopeIdentity? MovementScope,
    LayoutRevision PlacementRevision,
    RouteRevision RouteRevision);

internal sealed record AssignedRail(
    string Id,
    string DemandId,
    string LogicalRouteId,
    RailOrientation Orientation,
    int AxisCoordinate,
    int LaneIndex,
    AxisInterval OccupiedInterval,
    RailSemanticRole Role,
    LayoutRevision PlacementRevision,
    RouteRevision RouteRevision);

internal sealed record RailTransition(
    string Id,
    string LogicalRouteId,
    string FromAssignedRailId,
    string ToAssignedRailId,
    Point Turn,
    int Order,
    LayoutRevision PlacementRevision,
    RouteRevision RouteRevision);

internal enum MovementScopeKind
{
    Node,
    LayoutSubtree,
    OrderedSiblingSuffix,
    LayerAndLowerSuffix,
    ProjectRoot,
    OrderedProjectSuffix
}

internal readonly record struct MovementScopeIdentity(MovementScopeKind Kind, string Id);

internal sealed record MovementScopeDefinition(
    MovementScopeIdentity Identity,
    IReadOnlyList<string> MemberNodeIds,
    string OwnerId);

internal enum GenerationConstraintKind { MinimumX, MinimumY, MinimumWidth, MinimumHeight }

internal readonly record struct GenerationConstraintKey(
    MovementScopeIdentity Scope,
    GenerationConstraintKind Kind);

internal sealed record GenerationConstraint(
    GenerationConstraintKey Key,
    int Minimum,
    string Reason);

internal enum RouteInvalidationCause
{
    EndpointMoved,
    EndpointResized,
    TerminalAllocationChanged,
    CrossedBoundaryMoved,
    AssignedRailChanged,
    ObstacleRelationshipChanged,
    ProjectBoundsChanged,
    SharedTurnAllocationChanged
}

internal sealed record RouteInvalidation(
    string LogicalRouteId,
    RouteInvalidationCause Cause,
    RouteRevision SourceRouteRevision,
    LayoutRevision SourcePlacementRevision,
    LayoutRevision TargetPlacementRevision,
    MovementScopeIdentity? Scope = null,
    string? RailId = null);

internal sealed record SemanticRouteReference(
    string LogicalRouteId,
    string SourceNodeId,
    string TargetNodeId,
    RouteRevision RouteRevision);

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
    RailDemandAlternatives,
    IncreasedExtentDemand,
    RejectTopologyAndRegenerate
}

internal sealed record DefectDemandContract(
    HardGeometryDefectKind Defect,
    DefectResolutionKind Resolution,
    IReadOnlyList<RailSemanticRole> RailRoles,
    bool IsSpacingDemand,
    string Requirement);
