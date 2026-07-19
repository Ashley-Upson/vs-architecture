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
    MultipleBand,
    UnsupportedTerminalTopology,
    RevisionMismatch
}

internal enum ExistingLaneMappingSource
{
    LegacyCorridor,
    StageBHypothetical,
    StageCGrouped
}

internal enum ObservationalRouteParity
{
    ExactPointParity,
    TopologyParityCoordinateDifference,
    UnableToMap
}

internal sealed record AdjacentDownwardRouteContext(
    LinkLayout Route,
    NodeLayout Source,
    NodeLayout Target,
    LayoutRevision LayoutRevision,
    RouteRevision RouteRevision,
    IReadOnlyList<BandRouteMembership> BandMemberships,
    IReadOnlyList<BandRouteDemand> BandDemands,
    IReadOnlyDictionary<InterLayerBandId, AxisInterval> BandAxisRanges,
    CorridorObservation Corridors,
    CorridorLaneAllocation CorridorLanes,
    GroupedVerticalBandPlan? GroupedPlan,
    bool ExposureTreeSpecific);

internal sealed record ExistingLaneMapping(
    ExistingLaneMappingSource Source,
    AssignedLinkSegment Rail,
    IReadOnlyDictionary<string, string> SpecializedMetadata);

internal sealed record AdjacentDownwardRouteObservation(
    string LogicalRouteId,
    bool Eligible,
    AdjacentDownwardRejectionReason? RejectionReason,
    IReadOnlyList<LinkSegmentDemand> Demands,
    IReadOnlyList<ExistingLaneMapping> ExistingLaneMappings,
    IReadOnlyList<AssignedLinkSegment> SelectedAssignedLinkSegments,
    IReadOnlyList<LinkTransition> Transitions,
    IReadOnlyList<Point> ReconstructedPoints,
    ObservationalRouteParity Parity,
    IReadOnlyList<Point> CanonicalAuthoritativePoints,
    IReadOnlyList<string> Diagnostics);

internal sealed record AdjacentDownwardObservationReport(
    IReadOnlyList<AdjacentDownwardRouteObservation> Routes,
    long DemandProductionMicroseconds,
    long ExistingLaneAdaptationMicroseconds,
    long ReconstructionMicroseconds,
    long ParityComparisonMicroseconds);

internal sealed record AdjacentDownwardComponentProjection(
    IReadOnlyList<ConflictComponent<AdjacentDownwardRouteObservation>> UnassignedComponents,
    IReadOnlyList<ConflictComponent<AdjacentDownwardRouteObservation>> AssignedComponents,
    IReadOnlyList<ConflictEdge> UnassignedEdges,
    IReadOnlyList<ConflictEdge> AssignedEdges,
    long ElapsedMicroseconds);
