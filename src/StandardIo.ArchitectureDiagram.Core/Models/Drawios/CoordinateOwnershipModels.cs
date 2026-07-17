using System.Collections.Generic;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal enum PhysicalEdgeSegmentRole
{
    Complete,
    Source,
    Middle,
    Target
}

internal sealed record BoundaryAnchor(
    string Id,
    string LogicalEdgeId,
    string OwnerProjectId,
    int TransitionIndex,
    Point AbsolutePoint,
    Point RelativePoint);

internal sealed record PhysicalEdgeSegment(
    string Id,
    string LogicalEdgeId,
    string SemanticSourceId,
    string SemanticTargetId,
    int SegmentIndex,
    PhysicalEdgeSegmentRole Role,
    string ParentId,
    string? OwnerProjectId,
    string SourceCellId,
    string TargetCellId,
    Point AbsoluteStart,
    Point AbsoluteEnd,
    IReadOnlyList<Point> AbsoluteWaypoints,
    IReadOnlyList<Point> RelativeWaypoints,
    bool HasSourceMarker,
    bool HasTargetArrow,
    bool OwnsLabel,
    LinkLayout LogicalLink);

internal sealed record CoordinateOwnershipCompilation(
    IReadOnlyList<BoundaryAnchor> Anchors,
    IReadOnlyList<PhysicalEdgeSegment> Segments);
