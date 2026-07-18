using System.Collections.Generic;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal enum CommonAssignmentParity
{
    ExactLaneAndCoordinate,
    EquivalentValidDifferentOrdering,
    DifferentRequiredExtent,
    DifferentConflictComponent,
    UnableToMap
}

internal enum CommonRouteReconstructionParity
{
    ExactGeometry,
    ValidDifferentGeometry,
    HardInvariantRegression,
    UnableToReconstruct
}

internal sealed record CommonRailRouteComparison(
    string LogicalRouteId,
    AssignedRail? CommonThroughRail,
    IReadOnlyDictionary<ExistingLaneMappingSource, CommonAssignmentParity> ExistingParity,
    IReadOnlyList<Point> ReconstructedPoints,
    CommonRouteReconstructionParity ReconstructionParity,
    IReadOnlyList<string> Diagnostics);

internal sealed record CommonRailRegionObservation(
    RailAllocationRegionIdentity Region,
    DeterministicRailAssignment Assignment,
    GenerationConstraint? ConstraintProposal);

internal sealed record CommonRailParityReport(
    IReadOnlyList<CommonRailRegionObservation> Regions,
    IReadOnlyList<CommonRailRouteComparison> Routes,
    long AssignmentMicroseconds,
    long ConstraintProjectionMicroseconds,
    long ReconstructionMicroseconds,
    long ParityComparisonMicroseconds);
