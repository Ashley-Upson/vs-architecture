using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace StandardIo.ArchitectureDiagram.Core.Models.ArchitectureV7;

/// <summary>
/// A maximal, capability-derived straight-traversal interval in the frozen
/// diagram grid. This is analysis evidence only; it is not a routing or
/// allocation resource.
/// </summary>
public sealed record ArchitectureV7DiscoveredCorridor(
    string CorridorId,
    ArchitectureV7RunOrientation Orientation,
    int FixedCoordinate,
    int StartCoordinate,
    int EndCoordinate,
    IReadOnlyList<ArchitectureV7RouteCell> Cells,
    IReadOnlyList<ArchitectureV7CellCapability> CapabilityClasses,
    string Provenance)
{
    public int CellCount => Cells.Count;
}

public sealed class ArchitectureV7CorridorDiscoveryResult
{
    public ArchitectureV7CorridorDiscoveryResult(
        IReadOnlyList<ArchitectureV7DiscoveredCorridor> horizontal,
        IReadOnlyList<ArchitectureV7DiscoveredCorridor> vertical)
    {
        Horizontal = Freeze(horizontal);
        Vertical = Freeze(vertical);
        All = Array.AsReadOnly(Horizontal.Concat(Vertical).OrderBy(item => item.CorridorId, StringComparer.Ordinal).ToArray());
        Fingerprint = ComputeFingerprint(All);
    }

    public IReadOnlyList<ArchitectureV7DiscoveredCorridor> Horizontal { get; }
    public IReadOnlyList<ArchitectureV7DiscoveredCorridor> Vertical { get; }
    public IReadOnlyList<ArchitectureV7DiscoveredCorridor> All { get; }
    public string Fingerprint { get; }

    private static IReadOnlyList<ArchitectureV7DiscoveredCorridor> Freeze(IReadOnlyList<ArchitectureV7DiscoveredCorridor>? corridors) =>
        Array.AsReadOnly((corridors ?? Array.Empty<ArchitectureV7DiscoveredCorridor>())
            .OrderBy(item => item.CorridorId, StringComparer.Ordinal)
            .ToArray());

    private static string ComputeFingerprint(IReadOnlyList<ArchitectureV7DiscoveredCorridor> corridors)
    {
        var canonical = string.Join("\n", corridors.Select(item =>
            string.Join("|", item.CorridorId, item.Orientation, item.FixedCoordinate, item.StartCoordinate,
                item.EndCoordinate, string.Join(",", item.Cells.Select(cell => $"{cell.Row}:{cell.Column}")),
                string.Join(",", item.CapabilityClasses.Select(capability => capability.ToString())))));
        using (var sha256 = SHA256.Create())
        {
            return BitConverter.ToString(sha256.ComputeHash(Encoding.UTF8.GetBytes(canonical))).Replace("-", string.Empty);
        }
    }
}
