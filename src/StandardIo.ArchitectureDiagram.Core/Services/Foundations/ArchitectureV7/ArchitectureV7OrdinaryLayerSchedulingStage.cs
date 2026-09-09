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
        var frozenReservations = reservation.Table;

        var externalIds = new HashSet<string>(nodes.Values.Where(node => node.IsExternal).Select(node => node.PhysicalNodeId), StringComparer.Ordinal);
        var treeMembersByRoot = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        var depthByNode = new Dictionary<string, int>(StringComparer.Ordinal);
        var externalLinkedRoots = new HashSet<string>(StringComparer.Ordinal);

        foreach (var root in decisions.Values.Where(item => item.PositionalParentPhysicalNodeId is null)
                     .OrderBy(item => nodes[item.PhysicalNodeId].AnalyserOrdinal < 0 ? int.MaxValue : nodes[item.PhysicalNodeId].AnalyserOrdinal)
                     .ThenBy(item => item.PhysicalNodeId, StringComparer.Ordinal)
                     .Select(item => item.PhysicalNodeId))
        {
            if (nodes[root].IsExternal) continue;
            var members = new List<string>();
            void Visit(string id, int depth)
            {
                // External remains a relationship/lower-anchor constraint. It
                // is never made a positional child of an ordinary tree by the
                // scheduling stage, even if an ownership fixture exposes it in
                // the child adjacency.
                if (nodes[id].IsExternal) return;
                if (depthByNode.ContainsKey(id)) return;
                depthByNode[id] = depth;
                members.Add(id);
                foreach (var child in children[id]) Visit(child, depth + 1);
            }
            Visit(root, 0);
            treeMembersByRoot[root] = members;
            if (projection.PhysicalLinks.Any(link => members.Contains(link.SourcePhysicalNodeId, StringComparer.Ordinal) &&
                externalIds.Contains(link.DestinationPhysicalNodeId)))
                externalLinkedRoots.Add(root);
        }

        var externalLayer = ArchitectureV7ReservationCoordinates.TreeLayerFromReservedNodeRow(frozenReservations.External.NodeRow);
        foreach (var root in treeMembersByRoot.Keys.OrderBy(id => nodes[id].AnalyserOrdinal < 0 ? int.MaxValue : nodes[id].AnalyserOrdinal)
                     .ThenBy(id => id, StringComparer.Ordinal))
        {
            var members = treeMembersByRoot[root];
            var maximumDepth = members.Max(id => depthByNode[id]);
            var offsets = members.Select(id => (NodeId: id, Offset: ReservedLayer(nodes[id]) is { } reserved ? reserved - depthByNode[id] : (int?)null))
                .Where(item => item.Offset.HasValue).Select(item => item.Offset.GetValueOrDefault()).Distinct().ToArray();
            if (offsets.Length > 1)
                diagnostics.Add("v7-hard-reservation-detach-required=" + root + ";constraints=" +
                    string.Join(",", members.Where(id => ReservedLayer(nodes[id]).HasValue).Select(id => id + "@" + ReservedLayer(nodes[id]) + "/depth=" + depthByNode[id])));

            var incompatibleReservations = offsets.Length > 1;
            var offset = offsets.Length == 1 ? offsets[0] : 0;
            if (!incompatibleReservations && externalLinkedRoots.Contains(root))
            {
                var packedOffset = checked(externalLayer - 1 - maximumDepth);
                if (offsets.Length == 1 && offset != packedOffset)
                {
                    if (packedOffset < offset)
                    {
                        // A hard reservation is an anchor. If the ordinary tree
                        // reaches the current External slot, move the lower
                        // anchor through the typed layer sequence instead of
                        // moving the reserved node or rejecting the tree using
                        // the old one-number offset equation. Keep one free
                        // node-capable layer plus its normal routing layer.
                        var requiredExternalLayer = checked(offset + maximumDepth + 2);
                        var anchorShift = Math.Max(0, requiredExternalLayer - externalLayer);
                        if (anchorShift > 0)
                        {
                            var anchorShiftedReservations = frozenReservations.Reservations.Select(item => item.IsExternal
                                ? item with { NodeRow = checked(item.NodeRow + anchorShift * 2) }
                                : item).ToArray();
                            var anchorShiftText = frozenReservations.Fingerprint + "#typed-layer-external-shift=" + anchorShift + ":" + root;
                            using var anchorShiftSha = SHA256.Create();
                            var anchorShiftFingerprint = BitConverter.ToString(anchorShiftSha.ComputeHash(Encoding.UTF8.GetBytes(anchorShiftText))).Replace("-", string.Empty);
                            frozenReservations = new ArchitectureV7FrozenReservationTable(anchorShiftedReservations, anchorShiftFingerprint);
                            externalLayer = checked(externalLayer + anchorShift);
                            ReapplyReservedLayers();
                            diagnostics.Add("v7-shifted-external-around-hard-reservation=" + root + ";external-layer=" + externalLayer + ";tree-layer=" + offset);
                        }
                        packedOffset = offset;
                    }
                    var shift = packedOffset - offset;
                    var firstReservedLayer = members.Select(id => ReservedLayer(nodes[id])).Where(value => value.HasValue).Select(value => value.GetValueOrDefault()).Min();
                    var shifted = frozenReservations.Reservations.Select(item => item.IsExternal || item.NodeRow / 2 < firstReservedLayer
                        ? item
                        : item with { NodeRow = checked(item.NodeRow + shift * 2) }).ToArray();
                    if (shifted.Where(item => !item.IsExternal).Any(item => item.NodeRow >= frozenReservations.External.NodeRow))
                    {
                        shifted = frozenReservations.Reservations.Select(item => item.NodeRow / 2 < firstReservedLayer
                            ? item
                            : item with { NodeRow = checked(item.NodeRow + shift * 2) }).ToArray();
                        externalLayer = ArchitectureV7ReservationCoordinates.TreeLayerFromReservedNodeRow(shifted.Last(item => item.IsExternal).NodeRow);
                    }
                    var shiftedText = frozenReservations.Fingerprint + "#ordinary-insertion=" + firstReservedLayer + ":" + shift;
                    using var shiftedSha = SHA256.Create();
                    var shiftedFingerprint = BitConverter.ToString(shiftedSha.ComputeHash(Encoding.UTF8.GetBytes(shiftedText))).Replace("-", string.Empty);
                    frozenReservations = new ArchitectureV7FrozenReservationTable(shifted, shiftedFingerprint);
                    ReapplyReservedLayers();
                    diagnostics.Add("v7-inserted-ordinary-layer-before-reservation=" + firstReservedLayer + ";shift=" + shift + ";tree=" + root);
                    offset = checked(externalLayer - 1 - maximumDepth);
                }
                else if (offsets.Length == 0 || offsets.Length > 1)
                    offset = packedOffset;
                diagnostics.Add("v7-tree-packed-against-external=" + root + ";external-layer=" + externalLayer + ";depth=" + maximumDepth);
            }
            if (incompatibleReservations)
            {
                AssignConflictingTreeLayers(members, depthByNode, children, nodes, layers);
                diagnostics.Add("v7-hard-reservation-detach-required=" + root + ";action=detach-conflicting-units");
                continue;
            }
            if (offset < 0)
                throw new InvalidOperationException("V7 ordinary scheduling cannot place tree " + root + " above its lower anchor without a negative layer.");
            foreach (var id in members) layers[id] = checked(offset + depthByNode[id]);
        }
        foreach (var id in nodes.Keys.Where(id => !layers.ContainsKey(id)).OrderBy(id => nodes[id].AnalyserOrdinal < 0 ? int.MaxValue : nodes[id].AnalyserOrdinal).ThenBy(id => id, StringComparer.Ordinal))
            layers[id] = ReservedLayer(nodes[id]) ?? 0;

        var maximumNonExternalLayer = layers.Where(item => !nodes[item.Key].IsExternal).Select(item => item.Value).DefaultIfEmpty(-1).Max();
        var externalShift = Math.Max(0, maximumNonExternalLayer - externalLayer + 1);
        if (externalShift > 0)
        {
            var shifted = frozenReservations.Reservations.Select(item => item.IsExternal
                ? item with { NodeRow = checked(item.NodeRow + externalShift * 2) }
                : item).ToArray();
            var shiftedText = frozenReservations.Fingerprint + "#external-shift=" + externalShift;
            using var shiftedSha = SHA256.Create();
            var shiftedFingerprint = BitConverter.ToString(shiftedSha.ComputeHash(Encoding.UTF8.GetBytes(shiftedText))).Replace("-", string.Empty);
            frozenReservations = new ArchitectureV7FrozenReservationTable(shifted, shiftedFingerprint);
            ReapplyReservedLayers();
            foreach (var id in nodes.Values.Where(node => node.IsExternal).Select(node => node.PhysicalNodeId))
                layers[id] = checked(layers[id] + externalShift);
            diagnostics.Add("v7-external-shift=" + externalShift);
        }

        // Reassert the frozen hard anchors after all tree/external adjustments.
        // Ordinary packing may move around these rows, but must never overwrite
        // the reserved node's reconciled layer.
        ReapplyReservedLayers();
        foreach (var node in nodes.Values)
            if (ReservedLayer(node) is { } reserved && (!layers.TryGetValue(node.PhysicalNodeId, out var scheduled) || scheduled != reserved))
                throw new InvalidOperationException("V7 hard reservation authority was lost during ordinary scheduling: node=" + node.PhysicalNodeId + ";scheduledLayer=" +
                    (layers.TryGetValue(node.PhysicalNodeId, out var actual) ? actual.ToString() : "missing") + ";reservedLayer=" + reserved + ";name=" + node.Name + ";matches=" +
                    string.Join(",", frozenReservations.Reservations.Where(item => !item.IsExternal).OrderBy(item => item.Order).Select(item => item.Name + "=" + item.Pattern)));

        var fingerprintText = frozenReservations.Fingerprint + "#" + string.Join("|", layers.OrderBy(item => item.Key, StringComparer.Ordinal).Select(item => item.Key + "@" + item.Value));
        using var sha = SHA256.Create();
        var fingerprint = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(fingerprintText))).Replace("-", string.Empty);
        return new ArchitectureV7FrozenOrdinaryLayerSchedule(frozenReservations, reservation.Inspection, layers, diagnostics, fingerprint);

        void ReapplyReservedLayers()
        {
            foreach (var node in nodes.Values)
                if (ReservedLayer(node) is { } reserved && layers.ContainsKey(node.PhysicalNodeId))
                    layers[node.PhysicalNodeId] = reserved;
        }

        void AssignConflictingTreeLayers(
            IReadOnlyList<string> members,
            IReadOnlyDictionary<string, int> depths,
            IReadOnlyDictionary<string, List<string>> children,
            IReadOnlyDictionary<string, ArchitectureV7PhysicalNode> nodes,
            IDictionary<string, int> layers)
        {
            var memberSet = new HashSet<string>(members, StringComparer.Ordinal);
            foreach (var id in members.OrderBy(id => depths[id]).ThenBy(id => id, StringComparer.Ordinal))
            {
                var parent = children.FirstOrDefault(item => item.Value.Contains(id, StringComparer.Ordinal)).Key;
                var parentLayer = parent is not null && layers.TryGetValue(parent, out var assignedParent) ? assignedParent : (int?)null;
                if (parentLayer.HasValue && (parent is null || !memberSet.Contains(parent))) parentLayer = null;
                if (ReservedLayer(nodes[id]) is { } reserved)
                    layers[id] = reserved;
                else
                    layers[id] = parentLayer.HasValue ? Math.Max(depths[id], parentLayer.Value + 1) : depths[id];
            }
        }

        int? ReservedLayer(ArchitectureV7PhysicalNode node)
        {
            if (node.IsExternal) return ArchitectureV7ReservationCoordinates.TreeLayerFromReservedNodeRow(frozenReservations.External.NodeRow);
            var requirement = reservation.Inspection.Requirements
                .Where(item => !string.Equals(item.ReservationName, "External", StringComparison.Ordinal))
                .OrderBy(item => item.Order)
                .FirstOrDefault(item => item.Constraints.Any(constraint => string.Equals(constraint.PhysicalNodeId, node.PhysicalNodeId, StringComparison.Ordinal)));
            if (requirement is not null)
            {
                var frozen = frozenReservations.Reservations.FirstOrDefault(item => string.Equals(item.Name, requirement.ReservationName, StringComparison.Ordinal));
                if (frozen is not null) return ArchitectureV7ReservationCoordinates.TreeLayerFromReservedNodeRow(frozen.NodeRow);
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
