using System.Collections.Generic;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal sealed record GeneralDownwardLinkPlan(
    AdjacentDownwardLinkObservation Observation,
    IReadOnlyList<VerticalLinkColumnDemand> VerticalColumnDemands,
    string SourceNodeId,
    string TargetNodeId);

internal sealed record GeneralDownwardObservationReport(
    IReadOnlyList<GeneralDownwardLinkPlan> Routes,
    long DemandProductionMicroseconds);

internal sealed record GeneralDownwardLinkAssignment(
    string LogicalRouteId,
    IReadOnlyList<AssignedLinkSegment> AssignedLinkSegments,
    IReadOnlyList<LinkTransition> Transitions,
    IReadOnlyList<Point> ReconstructedPoints,
    IReadOnlyList<string> Diagnostics,
    bool IsValid);

internal sealed record GeneralDownwardAssignmentReport(
    IReadOnlyList<CommonAuthorityRegionObservation> Regions,
    VerticalLinkColumnAssignment VerticalColumns,
    IReadOnlyList<GeneralDownwardLinkAssignment> Routes,
    long AssignmentMicroseconds,
    long TransitionMicroseconds);
