using System.Collections.Generic;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal enum AdjacentDownwardRejectionReason
{
    SameLayer,
    SkippedLayer,
    UpwardOrReturn,
    CrossProject,
    ExposureTreeSpecific,
    NonOrthogonal,
    MultipleInterLayer,
    UnsupportedConnectionTopology,
    RevisionMismatch
}

internal enum ExistingSegmentMappingSource
{
    LegacyCorridor,
    InterLayerObservation,
    InterLayerSpacingConstraint
}

internal enum ObservationalLinkPathParity
{
    ExactPointParity,
    TopologyParityCoordinateDifference,
    UnableToMap
}

internal sealed record AdjacentDownwardLinkContext(
    LinkLayout Route,
    NodeLayout Source,
    NodeLayout Target,
    LayoutRevision LayoutRevision,
    RouteRevision RouteRevision,
    IReadOnlyList<InterLayerLinkMembership> BandMemberships,
    IReadOnlyList<InterLayerLinkDemand> BandDemands,
    IReadOnlyDictionary<InterLayerId, AxisInterval> BandAxisRanges,
    CorridorObservation Corridors,
    CorridorLaneAllocation CorridorLanes,
    InterLayerSpacingConstraintPlan? GroupedPlan,
    bool ExposureTreeSpecific);

internal sealed record ExistingSegmentMapping(
    ExistingSegmentMappingSource Source,
    AssignedLinkSegment Rail,
    IReadOnlyDictionary<string, string> SpecializedMetadata);

internal sealed record AdjacentDownwardLinkObservation(
    string LogicalRouteId,
    bool Eligible,
    AdjacentDownwardRejectionReason? RejectionReason,
    IReadOnlyList<LinkSegmentDemand> Demands,
    IReadOnlyList<ExistingSegmentMapping> ExistingSegmentMappings,
    IReadOnlyList<AssignedLinkSegment> SelectedAssignedLinkSegments,
    IReadOnlyList<LinkTransition> Transitions,
    IReadOnlyList<Point> ReconstructedPoints,
    ObservationalLinkPathParity Parity,
    IReadOnlyList<Point> CanonicalAuthoritativePoints,
    IReadOnlyList<string> Diagnostics);

internal sealed record AdjacentDownwardObservationReport(
    IReadOnlyList<AdjacentDownwardLinkObservation> Routes,
    long DemandProductionMicroseconds,
    long ExistingLaneAdaptationMicroseconds,
    long ReconstructionMicroseconds,
    long ParityComparisonMicroseconds);

internal sealed record AdjacentDownwardComponentProjection(
    IReadOnlyList<ConflictComponent<AdjacentDownwardLinkObservation>> UnassignedComponents,
    IReadOnlyList<ConflictComponent<AdjacentDownwardLinkObservation>> AssignedComponents,
    IReadOnlyList<ConflictEdge> UnassignedEdges,
    IReadOnlyList<ConflictEdge> AssignedEdges,
    long ElapsedMicroseconds);
