using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using StandardIo.ArchitectureDiagram.Core.Models.ArchitectureV7;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.ArchitectureV7;

/// <summary>
/// Freezes the ordinary dependency-node rows before tree geometry is built.
/// Hard reservations are never inferred or renamed here; ordinary nodes simply
/// take the deepest legal row strictly above their positional children.
/// </summary>
public sealed class ArchitectureV7OrdinaryLayerSchedulingStage
{
    public ArchitectureV7FrozenOrdinaryLayerSchedule Schedule(
        ArchitectureV7PositionalOwnershipResult ownership,
        ArchitectureV7ReservationReconciliationResult reservation)
    {
        if (ownership is null) throw new ArgumentNullException(nameof(ownership));
        if (reservation is null) throw new ArgumentNullException(nameof(reservation));

        var projection = ownership.Projection;
        var nodes = projection.PhysicalNodes.ToDictionary(node => node.PhysicalNodeId, StringComparer.Ordinal);
        var decisions = ownership.Decisions.ToDictionary(item => item.PhysicalNodeId, StringComparer.Ordinal);
        var children = decisions.Keys.ToDictionary(id => id, _ => new List<string>(), StringComparer.Ordinal);
        foreach (var decision in decisions.Values)
            if (decision.PositionalParentPhysicalNodeId is { } parent && children.TryGetValue(parent, out var list)) list.Add(decision.PhysicalNodeId);
        foreach (var list in children.Values)
            list.Sort((left, right) => Compare(nodes[left], nodes[right]));

        var layers = new Dictionary<string, int>(StringComparer.Ordinal);
        var diagnostics = new List<string>();

        void Assign(string id, int? parentLayer)
        {
            if (layers.ContainsKey(id)) return;
            var node = nodes[id];
            var reserved = ReservedLayer(node);
            var candidate = reserved ?? (parentLayer.HasValue ? checked(parentLayer.Value + 1) : 0);
            layers[id] = candidate;
            foreach (var child in children[id]) Assign(child, candidate);
        }

        foreach (var id in decisions.Values.Where(item => item.PositionalParentPhysicalNodeId is null)
                     .OrderBy(item => nodes[item.PhysicalNodeId].AnalyserOrdinal < 0 ? int.MaxValue : nodes[item.PhysicalNodeId].AnalyserOrdinal)
                     .ThenBy(item => item.PhysicalNodeId, StringComparer.Ordinal)
                     .Select(item => item.PhysicalNodeId))
            Assign(id, null);
        foreach (var id in nodes.Keys.OrderBy(id => nodes[id].AnalyserOrdinal < 0 ? int.MaxValue : nodes[id].AnalyserOrdinal).ThenBy(id => id, StringComparer.Ordinal))
            Assign(id, null);

        var maximumNonExternalLayer = layers.Where(item => !nodes[item.Key].IsExternal).Select(item => item.Value).DefaultIfEmpty(-1).Max();
        var externalLayer = ArchitectureV7ReservationCoordinates.TreeLayerFromReservedNodeRow(reservation.Table.External.NodeRow);
        var externalShift = Math.Max(0, maximumNonExternalLayer - externalLayer + 1);
        var frozenReservations = reservation.Table;
        if (externalShift > 0)
        {
            var shifted = reservation.Table.Reservations.Select(item => item.IsExternal
                ? item with { NodeRow = checked(item.NodeRow + externalShift * 2) }
                : item).ToArray();
            var shiftedText = reservation.Table.Fingerprint + "#external-shift=" + externalShift;
            using var shiftedSha = SHA256.Create();
            var shiftedFingerprint = BitConverter.ToString(shiftedSha.ComputeHash(Encoding.UTF8.GetBytes(shiftedText))).Replace("-", string.Empty);
            frozenReservations = new ArchitectureV7FrozenReservationTable(shifted, shiftedFingerprint);
            foreach (var id in nodes.Values.Where(node => node.IsExternal).Select(node => node.PhysicalNodeId))
                layers[id] = checked(layers[id] + externalShift);
            diagnostics.Add("v7-external-shift=" + externalShift);
        }

        var fingerprintText = frozenReservations.Fingerprint + "#" + string.Join("|", layers.OrderBy(item => item.Key, StringComparer.Ordinal).Select(item => item.Key + "@" + item.Value));
        using var sha = SHA256.Create();
        var fingerprint = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(fingerprintText))).Replace("-", string.Empty);
        return new ArchitectureV7FrozenOrdinaryLayerSchedule(frozenReservations, layers, diagnostics, fingerprint);

        int? ReservedLayer(ArchitectureV7PhysicalNode node)
        {
            if (node.IsExternal) return ArchitectureV7ReservationCoordinates.TreeLayerFromReservedNodeRow(reservation.Table.External.NodeRow);
            foreach (var item in reservation.Table.Reservations.Where(item => !item.IsExternal).OrderBy(item => item.Order))
            {
                var suffix = item.Pattern.Trim();
                if (suffix.StartsWith("*", StringComparison.Ordinal)) suffix = suffix.Substring(1);
                if (suffix.EndsWith("$", StringComparison.Ordinal)) suffix = suffix.Substring(0, suffix.Length - 1);
                if (suffix.Length > 0 && node.Name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    return ArchitectureV7ReservationCoordinates.TreeLayerFromReservedNodeRow(item.NodeRow);
            }
            return null;
        }
    }

    private static int Compare(ArchitectureV7PhysicalNode left, ArchitectureV7PhysicalNode right)
    {
        var ordinal = left.AnalyserOrdinal < 0 ? int.MaxValue : left.AnalyserOrdinal;
        var other = right.AnalyserOrdinal < 0 ? int.MaxValue : right.AnalyserOrdinal;
        return ordinal != other ? ordinal.CompareTo(other) : StringComparer.Ordinal.Compare(left.PhysicalNodeId, right.PhysicalNodeId);
    }
}
