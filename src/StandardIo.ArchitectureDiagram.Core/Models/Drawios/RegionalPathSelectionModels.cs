using System.Collections.Generic;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal enum RouteInteractionReason
{
    SharedSegment,
    SpacingDeficit,
    ReusedBend,
    Crossing,
    CorridorCapacity
}

internal enum RegionFallbackReason
{
    None,
    RegionTooLarge,
    NoTraceabilityIssue,
    NoAlternativeCandidate,
    NoStrictImprovement,
    WholeDiagramRegression,
    ExposureLocalityViolation,
    CandidateGenerationFailure
}

internal sealed record RouteInteraction(
    string FirstEdgeId,
    string SecondEdgeId,
    RouteInteractionReason Reason,
    Rect Region,
    int Severity);

internal sealed record RouteOptimisationRegion(
    string Id,
    Rect Bounds,
    IReadOnlyList<string> MutableEdgeIds,
    IReadOnlyList<string> FixedContextEdgeIds,
    IReadOnlyList<RouteInteraction> Interactions);

internal sealed record RegionalOptimisationLimits(
    int MaximumMutableEdges = 24,
    int MaximumFixedContextEdges = 48,
    int MaximumCandidatesPerEdge = 8,
    int MaximumPasses = 4,
    int MaximumRegions = 64,
    int InteractionMargin = 12);

internal sealed record RegionOptimisationDecision(
    string RegionId,
    IReadOnlyList<string> MutableEdgeIds,
    IReadOnlyList<string> FixedContextEdgeIds,
    GlobalRouteScore InitialScore,
    GlobalRouteScore FinalScore,
    bool Changed,
    RegionFallbackReason FallbackReason,
    string Reason);

internal sealed record RegionalPathSelectionResult(
    IReadOnlyDictionary<string, CorridorPathCandidate> Initial,
    IReadOnlyDictionary<string, CorridorPathCandidate> Selected,
    IReadOnlyList<RouteInteraction> Interactions,
    IReadOnlyList<RouteOptimisationRegion> Regions,
    IReadOnlyList<RegionOptimisationDecision> Decisions,
    GlobalRouteScore InitialScore,
    GlobalRouteScore FinalScore);
