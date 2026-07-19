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

internal enum CommonLinkPathReconstructionParity
{
    ExactGeometry,
    ValidDifferentGeometry,
    HardInvariantRegression,
    UnableToReconstruct
}

internal sealed record CommonAuthorityLinkPathComparison(
    string LogicalRouteId,
    AssignedLinkSegment? CommonThroughRail,
    IReadOnlyDictionary<ExistingSegmentMappingSource, CommonAssignmentParity> ExistingParity,
    IReadOnlyList<Point> ReconstructedPoints,
    CommonLinkPathReconstructionParity ReconstructionParity,
    IReadOnlyList<string> Diagnostics);

internal sealed record CommonAuthorityRegionObservation(
    LinkSegmentAllocationRegionIdentity Region,
    DeterministicSlotAssignment Assignment,
    GenerationConstraint? ConstraintProposal);

internal sealed record CommonAuthorityParityReport(
    IReadOnlyList<CommonAuthorityRegionObservation> Regions,
    IReadOnlyList<CommonAuthorityLinkPathComparison> Routes,
    long AssignmentMicroseconds,
    long ConstraintProjectionMicroseconds,
    long ReconstructionMicroseconds,
    long ParityComparisonMicroseconds);
