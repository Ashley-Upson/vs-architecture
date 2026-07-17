using System.Collections.Generic;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal enum TraversalDirection
{
    Left,
    Right,
    Up,
    Down
}

internal sealed record TerminalAccess(Point Terminal, Point CorridorBoundary);

internal sealed record CorridorTraversal(
    int SegmentIndex,
    string CorridorId,
    TraversalDirection Direction,
    AllocatedCorridorLane Lane,
    Point Start,
    Point End);

internal sealed record JunctionTraversal(
    string JunctionId,
    string IncomingCorridorId,
    int IncomingLaneIndex,
    string OutgoingCorridorId,
    int OutgoingLaneIndex,
    Point TransitionPoint,
    bool IsStraightContinuation);

internal sealed record TraversalDiagnostic(
    string EdgeId,
    string Code,
    string Message,
    int? SegmentIndex = null,
    string? JunctionId = null);

internal sealed record EdgeTraversal(
    RenderLink Link,
    TerminalAccess SourceAccess,
    IReadOnlyList<CorridorTraversal> Corridors,
    IReadOnlyList<JunctionTraversal> Junctions,
    TerminalAccess TargetAccess,
    IReadOnlyList<Point> AcceptedFallbackPoints,
    IReadOnlyList<TraversalDiagnostic> Diagnostics)
{
    public bool UsesFallback => Diagnostics.Count > 0;
}

internal sealed record CompiledEdgeGeometry(string EdgeId, IReadOnlyList<Point> Points, bool UsedFallback);

internal sealed record EdgeTraversalCompilation(
    IReadOnlyDictionary<string, EdgeTraversal> Traversals,
    IReadOnlyDictionary<string, CompiledEdgeGeometry> Geometry,
    IReadOnlyList<TraversalDiagnostic> Diagnostics);

internal sealed record JunctionAllocationResult(
    IReadOnlyDictionary<string, EdgeTraversal> Traversals,
    IReadOnlyList<TraversalDiagnostic> Diagnostics,
    IReadOnlyCollection<string> AllocatedEdgeIds);
