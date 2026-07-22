using System;
using System.Collections.Generic;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal enum InterLayerBandRole { Legacy, ProjectInternal, RootTransition }

internal readonly record struct InterLayerId(
    int UpperLayer,
    int LowerLayer,
    LayoutRevision LayoutRevision,
    string? ProjectId = null,
    InterLayerBandRole BandRole = InterLayerBandRole.Legacy)
{
    public override string ToString() => ProjectId is null
        ? $"inter-layer:{BandRole}:{UpperLayer}:{LowerLayer}:layout-{LayoutRevision.Value}"
        : $"inter-layer:{BandRole}:project-{ProjectId}:{UpperLayer}:{LowerLayer}:layout-{LayoutRevision.Value}";
}

internal enum InterLayerMembershipRole { SourceTransition, TargetTransition, Through, Return }
internal enum InterLayerLinkDirection { Left, Right, Down, Up }

internal sealed record InterLayerLinkMembership(
    string Id,
    string LogicalEdgeIdentity,
    RouteRevision RouteRevision,
    InterLayerId InterLayerId,
    int FirstSegmentIndex,
    int LastSegmentIndex,
    InterLayerMembershipRole Role);

internal sealed record InterLayerLinkDemand(
    string Id,
    string LogicalEdgeIdentity,
    RouteRevision RouteRevision,
    InterLayerId InterLayerId,
    int SegmentIndex,
    InterLayerMembershipRole Role,
    int XStart,
    int XEnd,
    int ConnectionOrder,
    InterLayerLinkDirection Direction,
    int SlotIndex);

internal sealed record InterLayerObservation(
    InterLayerId Id,
    int UpperBoundary,
    int LowerBoundary,
    int CurrentExtent,
    int RequiredExtent,
    int MissingExtent,
    IReadOnlyList<InterLayerLinkMembership> Memberships,
    IReadOnlyList<InterLayerLinkDemand> Demands,
    int OverlapGroupCount,
    int MaximumSimultaneousOverlap,
    int HypotheticalSlotCount,
    int ReturnSlotCount,
    IReadOnlyList<InterLayerReturnRegionObservation> ReturnRegions,
    IReadOnlyList<string> UnsupportedShapes);

internal sealed record InterLayerReturnRegionObservation(
    string DemandId,
    string LogicalEdgeIdentity,
    string SideChoice,
    int XStart,
    int XEnd,
    bool ConflictsWithDownwardTraffic);

internal sealed record InterLayerFindingCorrelation(
    TraceabilityViolationCode Code,
    string EdgeId,
    string? OtherEdgeId,
    IReadOnlyList<InterLayerId> InterLayerIds,
    IReadOnlyList<string> DemandIds,
    bool? PlausiblyBandResolvable,
    string Reason);

internal sealed record InterLayerTelemetry(
    LayoutRevision LayoutRevision,
    RouteRevision RouteRevision,
    int NodeCount,
    int RouteCount,
    int SegmentCount,
    int InterLayerCount,
    int MembershipCount,
    int MaximumBandsCrossed,
    int HorizontalDemandCount,
    int OverlapGroupCount,
    int MaximumSimultaneousOverlap,
    int ReturnDemandCount,
    int UnsupportedShapeCount,
    long IntervalComparisons,
    long ElapsedMicroseconds);

internal sealed record InterLayerReport(
    IReadOnlyList<InterLayerObservation> InterLayers,
    IReadOnlyList<InterLayerFindingCorrelation> FindingCorrelations,
    InterLayerTelemetry Telemetry);

internal sealed record PhysicalSegmentDiagnostic(
    string Id,
    int SegmentIndex,
    PhysicalEdgeSegmentRole Role,
    string? OwnerProjectId,
    IReadOnlyList<Point> AbsolutePoints);

internal sealed record LogicalRouteHistoryDiagnostic(
    int Revision,
    LogicalRouteStage Stage,
    string Producer,
    LogicalRouteCompilationStatus CompilationStatus,
    IReadOnlyList<Point> Points,
    bool ContainsNonOrthogonalSegment);

internal sealed record NonOrthogonalSegmentDiagnostic(
    string LogicalEdgeId,
    string SourceId,
    string SourceName,
    string TargetId,
    string TargetName,
    int RouteRevision,
    int SegmentIndex,
    Point Start,
    Point End,
    int DeltaX,
    int DeltaY,
    IReadOnlyList<string> InterLayerMemberships,
    string RouteProducer,
    LogicalRouteStage RouteStage,
    bool TraversalFallback,
    IReadOnlyList<string> TraversalDiagnostics,
    bool ConnectionRegion,
    bool OwnershipBoundary,
    string Classification,
    IReadOnlyList<TraceabilityViolationCode> AssociatedFindings,
    IReadOnlyList<LogicalRouteHistoryDiagnostic> RouteHistory,
    IReadOnlyList<Point> CompleteLogicalPoints,
    IReadOnlyList<PhysicalSegmentDiagnostic> PhysicalSegments,
    IReadOnlyList<Point> ReconstructedAbsoluteXmlPoints,
    bool XmlContainsDiagonal);
