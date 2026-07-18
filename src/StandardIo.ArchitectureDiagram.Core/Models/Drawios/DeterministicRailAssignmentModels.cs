using System.Collections.Generic;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal readonly record struct RailAllocationRegionIdentity(
    RailOrientation Orientation,
    AxisInterval AllowedAxisRange,
    string EnvelopeIdentity,
    MovementScopeIdentity? MovementScope,
    LayoutRevision PlacementRevision);

internal sealed record RailAssignmentOptions(
    int Separation,
    int Padding,
    bool EndpointContactCreatesComponent = false,
    bool EndpointContactRequiresSeparateLane = false);

internal sealed record RailAssignmentComponent(
    string Id,
    RailAllocationRegionIdentity Region,
    IReadOnlyList<RailDemand> Demands,
    IReadOnlyList<AssignedRail> Rails,
    int RequiredExtent,
    int MissingExtent);

internal sealed record DeterministicRailAssignment(
    IReadOnlyList<RailAssignmentComponent> Components,
    IReadOnlyDictionary<string, AssignedRail> RailsByDemandId,
    int RequiredExtent,
    long ConflictComparisons,
    long ConflictDiscoveryMicroseconds,
    long ComponentConstructionMicroseconds,
    long LaneAssignmentMicroseconds,
    long ExtentCalculationMicroseconds);
