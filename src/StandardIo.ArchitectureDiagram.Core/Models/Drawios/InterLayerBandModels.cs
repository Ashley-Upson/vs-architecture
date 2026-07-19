using System;
using System.Collections.Generic;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal readonly record struct InterLayerBandId(int UpperLayer, int LowerLayer, LayoutRevision LayoutRevision)
{
    public override string ToString() => $"band:{UpperLayer}:{LowerLayer}:layout-{LayoutRevision.Value}";
}

internal enum BandMembershipRole { SourceTransition, TargetTransition, Through, Return }
internal enum BandRouteDirection { Left, Right, Down, Up }

internal sealed record BandRouteMembership(
    string Id,
    string LogicalEdgeIdentity,
    RouteRevision RouteRevision,
    InterLayerBandId BandId,
    int FirstSegmentIndex,
    int LastSegmentIndex,
    BandMembershipRole Role);

internal sealed record BandRouteDemand(
    string Id,
    string LogicalEdgeIdentity,
    RouteRevision RouteRevision,
    InterLayerBandId BandId,
    int SegmentIndex,
    BandMembershipRole Role,
    int XStart,
    int XEnd,
    int ConnectionOrder,
    BandRouteDirection Direction,
    int SlotIndex);

internal sealed record InterLayerBandObservation(
    InterLayerBandId Id,
    int UpperBoundary,
    int LowerBoundary,
    int CurrentExtent,
    int RequiredExtent,
    int MissingExtent,
    IReadOnlyList<BandRouteMembership> Memberships,
    IReadOnlyList<BandRouteDemand> Demands,
    int OverlapGroupCount,
    int MaximumSimultaneousOverlap,
    int HypotheticalLaneCount,
    int ReturnLaneCount,
    IReadOnlyList<BandReturnRegionObservation> ReturnRegions,
    IReadOnlyList<string> UnsupportedShapes);

internal sealed record BandReturnRegionObservation(
    string DemandId,
    string LogicalEdgeIdentity,
    string SideChoice,
    int XStart,
    int XEnd,
    bool ConflictsWithDownwardTraffic);

internal sealed record BandFindingCorrelation(
    TraceabilityViolationCode Code,
    string EdgeId,
    string? OtherEdgeId,
    IReadOnlyList<InterLayerBandId> BandIds,
    IReadOnlyList<string> DemandIds,
    bool? PlausiblyBandResolvable,
    string Reason);

internal sealed record InterLayerBandTelemetry(
    LayoutRevision LayoutRevision,
    RouteRevision RouteRevision,
    int NodeCount,
    int RouteCount,
    int SegmentCount,
    int BandCount,
    int MembershipCount,
    int MaximumBandsCrossed,
    int HorizontalDemandCount,
    int OverlapGroupCount,
    int MaximumSimultaneousOverlap,
    int ReturnDemandCount,
    int UnsupportedShapeCount,
    long IntervalComparisons,
    long ElapsedMicroseconds);

internal sealed record InterLayerBandReport(
    IReadOnlyList<InterLayerBandObservation> Bands,
    IReadOnlyList<BandFindingCorrelation> FindingCorrelations,
    InterLayerBandTelemetry Telemetry);

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
    IReadOnlyList<string> BandMemberships,
    string RouteProducer,
    LogicalRouteStage RouteStage,
    bool TraversalFallback,
    IReadOnlyList<string> TraversalDiagnostics,
    bool TerminalRegion,
    bool OwnershipBoundary,
    string Classification,
    IReadOnlyList<TraceabilityViolationCode> AssociatedFindings,
    IReadOnlyList<LogicalRouteHistoryDiagnostic> RouteHistory,
    IReadOnlyList<Point> CompleteLogicalPoints,
    IReadOnlyList<PhysicalSegmentDiagnostic> PhysicalSegments,
    IReadOnlyList<Point> ReconstructedAbsoluteXmlPoints,
    bool XmlContainsDiagonal);
