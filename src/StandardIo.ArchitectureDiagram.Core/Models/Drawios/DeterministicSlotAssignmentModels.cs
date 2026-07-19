using System.Collections.Generic;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal readonly record struct LinkSegmentAllocationRegionIdentity(
    LinkSegmentOrientation Orientation,
    AxisInterval AllowedAxisRange,
    string EnvelopeIdentity,
    MovementScopeIdentity? MovementScope,
    LayoutRevision PlacementRevision);

internal sealed record LinkSegmentAssignmentOptions(
    int Separation,
    int Padding,
    bool EndpointContactCreatesComponent = false,
    bool EndpointContactRequiresSeparateSlot = false);

internal sealed record LinkSegmentConflictComponent(
    string Id,
    LinkSegmentAllocationRegionIdentity Region,
    IReadOnlyList<LinkSegmentDemand> Demands,
    IReadOnlyList<AssignedLinkSegment> Segments,
    int RequiredExtent,
    int MissingExtent);

internal sealed record DeterministicSlotAssignment(
    IReadOnlyList<LinkSegmentConflictComponent> Components,
    IReadOnlyDictionary<string, AssignedLinkSegment> SegmentsByDemandId,
    int RequiredExtent,
    long ConflictComparisons,
    long ConflictDiscoveryMicroseconds,
    long ComponentConstructionMicroseconds,
    long SlotAssignmentMicroseconds,
    long ExtentCalculationMicroseconds);
