using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace StandardIo.ArchitectureDiagram.Core.Models.ArchitectureV7;

public sealed record ArchitectureV7CorridorUsage(
    string UsageId,
    string PhysicalLinkId,
    string CorridorId,
    ArchitectureV7RunOrientation Orientation,
    int StartRouteIndex,
    int EndRouteIndex,
    IReadOnlyList<ArchitectureV7RouteCell> TraversalCells,
    string Provenance);

public sealed record ArchitectureV7UnprojectedCorridorRun(
    string PhysicalLinkId,
    ArchitectureV7RunOrientation Orientation,
    int StartRouteIndex,
    int EndRouteIndex,
    IReadOnlyList<ArchitectureV7RouteCell> TraversalCells,
    string Reason);

public sealed class ArchitectureV7RouteCorridorProjectionFreeze
{
    public ArchitectureV7RouteCorridorProjectionFreeze(
        IReadOnlyList<ArchitectureV7CorridorUsage> usages,
        IReadOnlyList<ArchitectureV7UnprojectedCorridorRun> unprojectedRuns,
        string placementFingerprint,
        string routeFingerprint,
        string corridorFingerprint)
    {
        Usages = Array.AsReadOnly((usages ?? Array.Empty<ArchitectureV7CorridorUsage>()).ToArray());
        UnprojectedRuns = Array.AsReadOnly((unprojectedRuns ?? Array.Empty<ArchitectureV7UnprojectedCorridorRun>()).ToArray());
        PlacementFingerprint = placementFingerprint ?? throw new ArgumentNullException(nameof(placementFingerprint));
        RouteFingerprint = routeFingerprint ?? throw new ArgumentNullException(nameof(routeFingerprint));
        CorridorFingerprint = corridorFingerprint ?? throw new ArgumentNullException(nameof(corridorFingerprint));
        Fingerprint = ComputeFingerprint();
    }

    public IReadOnlyList<ArchitectureV7CorridorUsage> Usages { get; }
    public IReadOnlyList<ArchitectureV7UnprojectedCorridorRun> UnprojectedRuns { get; }
    public string PlacementFingerprint { get; }
    public string RouteFingerprint { get; }
    public string CorridorFingerprint { get; }
    public string Fingerprint { get; }

    private string ComputeFingerprint()
    {
        var text = string.Join(";", Usages.Select(usage => usage.UsageId + ":" + usage.CorridorId + ":" +
            usage.StartRouteIndex + ":" + usage.EndRouteIndex + ":" + string.Join(",", usage.TraversalCells.Select(cell => cell.Row + "/" + cell.Column)))) +
            "|unprojected=" + string.Join(";", UnprojectedRuns.Select(run => run.PhysicalLinkId + ":" + run.StartRouteIndex + ":" + run.EndRouteIndex + ":" + run.Reason));
        using var sha = SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(PlacementFingerprint + "|" + RouteFingerprint + "|" + CorridorFingerprint + "|" + text))).Replace("-", string.Empty).ToLowerInvariant();
    }
}
