using System;
using System.Collections.Generic;
using System.Linq;

namespace StandardIo.ArchitectureDiagram.Core.Models.ArchitectureV7;

public sealed record ArchitectureV7AcceptanceFinding(
    string Code,
    string Stage,
    string Message,
    string? SubjectId,
    IReadOnlyList<ArchitectureV7RouteCell> LogicalCells,
    IReadOnlyList<ArchitectureV7PhysicalPoint> PhysicalPoints,
    IReadOnlyList<string> Provenance);

public sealed record ArchitectureV7AcceptanceValidationMetrics(
    int TotalPhysicalSegments,
    long PhysicalGeometryCandidateSegmentPairs,
    long SegmentPairPredicateEvaluations,
    long NodeBoundCandidates,
    long SegmentNodePredicateEvaluations,
    long CrossingCandidates,
    long CrossingPredicateEvaluations,
    long AccountingLookups,
    int GlobalCollectionScansRemaining);

public sealed class ArchitectureV7AcceptanceReport
{
    public ArchitectureV7AcceptanceReport(
        IReadOnlyList<ArchitectureV7AcceptanceFinding> findings,
        IReadOnlyDictionary<string, int> counts,
        string projectionFingerprint,
        string ownershipFingerprint,
        string sizingFingerprint,
        string reservationFingerprint,
        string placementFingerprint,
        string routeFingerprint,
        string allocationFingerprint,
        string sceneFingerprint,
        bool isNormalEligible,
        ArchitectureV7AcceptanceValidationMetrics? metrics = null)
    {
        Findings = Array.AsReadOnly((findings ?? Array.Empty<ArchitectureV7AcceptanceFinding>()).ToArray());
        Counts = new Dictionary<string, int>((counts ?? new Dictionary<string, int>()).ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal), StringComparer.Ordinal);
        ProjectionFingerprint = projectionFingerprint; OwnershipFingerprint = ownershipFingerprint; SizingFingerprint = sizingFingerprint;
        ReservationFingerprint = reservationFingerprint; PlacementFingerprint = placementFingerprint; RouteFingerprint = routeFingerprint;
        AllocationFingerprint = allocationFingerprint; SceneFingerprint = sceneFingerprint; IsNormalEligible = isNormalEligible;
        Metrics = metrics ?? new ArchitectureV7AcceptanceValidationMetrics(0, 0, 0, 0, 0, 0, 0, 0, 0);
    }
    public IReadOnlyList<ArchitectureV7AcceptanceFinding> Findings { get; }
    public IReadOnlyDictionary<string, int> Counts { get; }
    public string ProjectionFingerprint { get; }
    public string OwnershipFingerprint { get; }
    public string SizingFingerprint { get; }
    public string ReservationFingerprint { get; }
    public string PlacementFingerprint { get; }
    public string RouteFingerprint { get; }
    public string AllocationFingerprint { get; }
    public string SceneFingerprint { get; }
    public int HardFailureCount => Findings.Count;
    public bool HasHardFailures => Findings.Count > 0;
    public bool IsStrictEligible => !HasHardFailures;
    public bool IsNormalEligible { get; }
    public ArchitectureV7AcceptanceValidationMetrics Metrics { get; }
}
