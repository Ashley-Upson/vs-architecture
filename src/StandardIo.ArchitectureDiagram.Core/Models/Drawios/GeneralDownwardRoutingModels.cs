using System.Collections.Generic;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal sealed record GeneralDownwardRoutePlan(
    AdjacentDownwardRouteObservation Observation,
    IReadOnlyList<int> TransitionXCoordinates,
    string SourceNodeId,
    string TargetNodeId);

internal sealed record GeneralDownwardObservationReport(
    IReadOnlyList<GeneralDownwardRoutePlan> Routes,
    long DemandProductionMicroseconds);

internal sealed record GeneralDownwardRouteAssignment(
    string LogicalRouteId,
    IReadOnlyList<AssignedLinkSegment> AssignedLinkSegments,
    IReadOnlyList<LinkTransition> Transitions,
    IReadOnlyList<Point> ReconstructedPoints,
    IReadOnlyList<string> Diagnostics,
    bool IsValid);

internal sealed record GeneralDownwardAssignmentReport(
    IReadOnlyList<CommonRailRegionObservation> Regions,
    IReadOnlyList<GeneralDownwardRouteAssignment> Routes,
    long AssignmentMicroseconds,
    long TransitionMicroseconds);
