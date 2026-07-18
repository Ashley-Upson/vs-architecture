using System.Collections.Generic;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal enum IntervalContactKind { Disjoint, EndpointContact, PositiveOverlap }
internal enum RoutePointContactKind { None, StraightContinuation, AmbiguousBend, CleanCrossover }
internal enum SpacingConstraintScope { LayerBoundary, SiblingSubtree, SiblingSuffix, ProjectRoot, RootProjectGroup }

internal readonly record struct SpacingConstraintKey(
    int Y,
    int X,
    SpacingConstraintScope Scope,
    string StableIdentity);

internal sealed record MinimumSpacingConstraint(
    SpacingConstraintKey Key,
    int Minimum,
    string GroupId);

internal sealed record BandConflictGroup(
    string Id,
    InterLayerBandId BandId,
    IReadOnlyList<BandRouteDemand> Demands,
    int CurrentLaneCount,
    int RequiredLaneCount,
    int CurrentExtent,
    int RequiredExtent,
    int MissingExtent,
    SpacingConstraintScope MovementScope);

internal sealed record GroupedSpacingTelemetry(
    int GroupsObserved,
    int PairwiseConflictsCollapsed,
    int ConstraintProposals,
    int ConstraintsIncreased,
    long ConflictComparisons);
