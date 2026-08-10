using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using StandardIo.ArchitectureDiagram.Core.Models.ArchitectureV7;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.ArchitectureV7;

public sealed class ArchitectureV7RecursiveTreeGridStage
{
    public ArchitectureV7RecursiveTreeGridResult Build(
        ArchitectureV7NodeSpanSizingResult sizing,
        ArchitectureV7FrozenReservationTable reservations)
    {
        if (sizing is null) throw new ArgumentNullException(nameof(sizing));
        if (reservations is null) throw new ArgumentNullException(nameof(reservations));

        var projection = sizing.Ownership.Projection;
        var nodes = projection.PhysicalNodes.ToDictionary(node => node.PhysicalNodeId, StringComparer.Ordinal);
        var decisions = sizing.Ownership.Decisions.ToDictionary(decision => decision.PhysicalNodeId, StringComparer.Ordinal);
        var spans = sizing.Requirements.ToDictionary(requirement => requirement.PhysicalNodeId, StringComparer.Ordinal);
        if (nodes.Count != spans.Count || nodes.Keys.Any(id => !spans.ContainsKey(id)) || nodes.Keys.Any(id => !decisions.ContainsKey(id)))
            throw new InvalidOperationException("V7 tree construction requires a span and ownership decision for every physical node.");

        var children = BuildChildren(projection, decisions);
        var roots = decisions.Values.Where(decision => decision.PositionalParentPhysicalNodeId is null)
            .OrderBy(decision => nodes[decision.PhysicalNodeId].AnalyserOrdinal < 0 ? int.MaxValue : nodes[decision.PhysicalNodeId].AnalyserOrdinal)
            .ThenBy(decision => decision.PhysicalNodeId, StringComparer.Ordinal).ToArray();
        if (roots.Length == 0) throw new InvalidOperationException("V7 tree construction found no positional tree root.");

        var parallelStopwatch = Stopwatch.StartNew();
        var treeResults = roots.AsParallel().AsUnordered().Select(BuildTree).ToArray();
        parallelStopwatch.Stop();
        var joinStopwatch = Stopwatch.StartNew();
        var trees = treeResults
            .OrderBy(tree => tree.AnalyserOrdinal < 0 ? int.MaxValue : tree.AnalyserOrdinal)
            .ThenBy(tree => tree.TreeId, StringComparer.Ordinal).ToArray();
        joinStopwatch.Stop();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tree in trees)
        {
            foreach (var placement in tree.Placements) visited.Add(placement.PhysicalNodeId);
        }

        if (visited.Count != nodes.Count)
            throw new InvalidOperationException("V7 tree construction left a physical node outside recursive or detached construction.");

        var allTreePlacements = trees.SelectMany(tree => tree.Placements).ToArray();
        var duplicatePlacements = allTreePlacements.GroupBy(item => item.PhysicalNodeId, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key + " => " + string.Join(" | ", group.Select(item =>
                item.Provenance + " @ row=" + item.LocalRow + ", column=" + item.LocalColumn + ", span=" + item.LogicalSpan)))
            .ToArray();
        if (duplicatePlacements.Length > 0)
            throw new InvalidOperationException("V7 recursive tree construction produced duplicate physical placements before project composition: " + string.Join("; ", duplicatePlacements));

        var missingPlacements = nodes.Keys.Except(allTreePlacements.Select(item => item.PhysicalNodeId), StringComparer.Ordinal).ToArray();
        if (missingPlacements.Length > 0)
            throw new InvalidOperationException("V7 recursive tree construction omitted projected physical nodes before project composition: " + string.Join(", ", missingPlacements));

        var unknownPlacements = allTreePlacements.Select(item => item.PhysicalNodeId).Except(nodes.Keys, StringComparer.Ordinal).ToArray();
        if (unknownPlacements.Length > 0)
            throw new InvalidOperationException("V7 recursive tree construction produced placements for unknown physical nodes before project composition: " + string.Join(", ", unknownPlacements));

        var fingerprintText = sizing.FreezeFingerprint + "#" + reservations.Fingerprint + "#" + string.Join("|", trees.Select(tree =>
            tree.TreeId + ":" + tree.AnalyserOrdinal + ":" + tree.Width + ":" + tree.Height + ":" + string.Join(",", tree.Placements.Select(item => item.PhysicalNodeId + "@" + item.LocalRow + ":" + item.LocalColumn))));
        using var sha = SHA256.Create();
        var fingerprint = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(fingerprintText))).Replace("-", string.Empty);
        return new ArchitectureV7RecursiveTreeGridResult(sizing, reservations, trees, fingerprint,
            roots.Length, parallelStopwatch.ElapsedMilliseconds, joinStopwatch.ElapsedMilliseconds);

        ArchitectureV7TopLevelTreeGrid BuildTree(ArchitectureV7PositionalOwnershipDecision root)
        {
            var stopwatch = Stopwatch.StartNew();
            var built = BuildNode(root.PhysicalNodeId, null, false);
            var mainUnit = OffsetUnit(built.Main, 0, false);
            var detached = built.Detached.ToList();
            var allPlacements = new List<ArchitectureV7TreeGridNodePlacement>(mainUnit.Placements);
            allPlacements.AddRange(detached.SelectMany(unit => unit.Placements));
            var width = built.Complete.Width;
            var height = allPlacements.Count == 0 ? 0 : allPlacements.Max(item => item.LocalRow) + 1;
            stopwatch.Stop();
            return new ArchitectureV7TopLevelTreeGrid(
                "tree:" + root.PhysicalNodeId, root.PhysicalNodeId, mainUnit, detached, allPlacements, width, height,
                projection.FreezeFingerprint, sizing.Ownership.FreezeFingerprint, sizing.FreezeFingerprint,
                reservations.Fingerprint, "v7-recursive-tree-grid;root=" + root.PhysicalNodeId + ";construction-ms=" + stopwatch.ElapsedMilliseconds,
                nodes[root.PhysicalNodeId].AnalyserOrdinal, BuildCells(allPlacements, width, height), stopwatch.ElapsedMilliseconds);
        }

        int NaturalLayer(string physicalNodeId)
        {
            var decision = decisions[physicalNodeId];
            return decision.PositionalParentPhysicalNodeId is null ? 0 : NaturalLayer(decision.PositionalParentPhysicalNodeId) + 1;
        }

        int? ReservedLayer(ArchitectureV7PhysicalNode node)
        {
            if (node.IsExternal) return ArchitectureV7ReservationCoordinates.TreeLayerFromReservedNodeRow(reservations.External.NodeRow);
            foreach (var reservation in reservations.Reservations.Where(item => !item.IsExternal).OrderBy(item => item.Order))
            {
                var suffix = reservation.Pattern.Trim();
                if (suffix.StartsWith("*", StringComparison.Ordinal)) suffix = suffix.Substring(1);
                if (suffix.EndsWith("$", StringComparison.Ordinal)) suffix = suffix.Substring(0, suffix.Length - 1);
                if (suffix.Length > 0 && node.Name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    return ArchitectureV7ReservationCoordinates.TreeLayerFromReservedNodeRow(reservation.NodeRow);
            }
            return null;
        }

        BuiltNode BuildNode(string physicalNodeId, int? parentLayer, bool detached)
        {
            var node = nodes[physicalNodeId];
            var span = spans[physicalNodeId];
            var naturalLayer = NaturalLayer(physicalNodeId);
            var reservedLayer = ReservedLayer(node);
            var targetLayer = Math.Max(naturalLayer, reservedLayer ?? 0);
            if (parentLayer.HasValue && targetLayer <= parentLayer.Value)
                return BuildDetachedUnit(physicalNodeId, targetLayer, detached);

            var layer = parentLayer.HasValue ? Math.Max(targetLayer, parentLayer.Value + 1) : targetLayer;
            var childUnits = new List<BuiltNode>();
            foreach (var childId in children.TryGetValue(physicalNodeId, out var childIds) ? childIds : Array.Empty<string>())
            {
                var childTarget = Math.Max(NaturalLayer(childId), ReservedLayer(nodes[childId]) ?? 0);
                childUnits.Add(childTarget <= layer ? BuildDetachedUnit(childId, childTarget, true) : BuildNode(childId, layer, false));
            }
            var directChildren = childUnits.Where(unit => !unit.IsDetached).ToArray();
            var detachedChildren = childUnits.SelectMany(unit => unit.IsDetached
                ? new[] { unit.Complete }
                : unit.Detached).ToList();
            var childWidth = 0;
            var positionedChildren = new List<ArchitectureV7TreeGridPlacementUnit>();
            foreach (var child in directChildren)
            {
                var normalizedChild = NormalizeUnit(child.Main, false);
                var placed = OffsetUnit(normalizedChild, childWidth, false);
                positionedChildren.Add(placed);
                childWidth = checked(childWidth + RequiredWidth(normalizedChild) + 1);
            }
            if (positionedChildren.Count > 0) childWidth--;
            AssertPackedUnits(positionedChildren);

            var parentCentre = positionedChildren.Count == 0
                ? (span.LogicalSpan - 1) / 2
                : (positionedChildren.First().RootCentreCell + positionedChildren.Last().RootCentreCell) / 2;
            var left = parentCentre - (span.LogicalSpan - 1) / 2;
            var shift = left < 0 ? -left : 0;
            if (shift > 0)
            {
                positionedChildren = positionedChildren.Select(unit => OffsetUnit(unit, shift, unit.IsDetached)).ToList();
                childWidth = checked(childWidth + shift);
                parentCentre += shift;
            }
            var width = Math.Max(Math.Max(span.LogicalSpan, childWidth), parentCentre + (span.LogicalSpan - 1) / 2 + 1);
            var placement = new ArchitectureV7TreeGridNodePlacement(physicalNodeId, node.SemanticNodeId, checked(layer * 2),
                checked(parentCentre - (span.LogicalSpan - 1) / 2), span.LogicalSpan, parentCentre, layer, detached,
                "v7-recursive;natural-layer=" + naturalLayer + ";reserved-layer=" + (reservedLayer?.ToString() ?? "none"));
            var placements = new List<ArchitectureV7TreeGridNodePlacement> { placement };
            placements.AddRange(positionedChildren.SelectMany(unit => unit.Placements));
            var main = new ArchitectureV7TreeGridPlacementUnit("unit:" + physicalNodeId, physicalNodeId, placements, width,
                placements.Max(item => item.LocalRow) + 1, parentCentre, placement.LocalRow, detached,
                "v7-recursive-main;root=" + physicalNodeId,
                BuildCells(placements, width, placements.Max(item => item.LocalRow) + 1));
            var detachedUnits = new List<ArchitectureV7TreeGridPlacementUnit>();
            var detachedCursor = Math.Max(width, placements.Max(item => item.LocalColumn + item.LogicalSpan));
            foreach (var child in detachedChildren)
            {
                var detachedUnit = NormalizeUnit(child, true);
                detachedCursor = checked(detachedCursor + 1);
                detachedUnits.Add(OffsetUnit(detachedUnit, detachedCursor, true));
                detachedCursor = checked(detachedCursor + RequiredWidth(detachedUnit));
            }
            var completePlacements = placements.Concat(detachedUnits.SelectMany(unit => unit.Placements)).ToArray();
            var completeWidth = Math.Max(detachedCursor, completePlacements.Max(item => item.LocalColumn + item.LogicalSpan));
            var completeHeight = completePlacements.Max(item => item.LocalRow) + 1;
            var complete = new ArchitectureV7TreeGridPlacementUnit("complete:" + physicalNodeId, physicalNodeId, completePlacements,
                completeWidth, completeHeight, parentCentre, placement.LocalRow, detached,
                "v7-recursive-complete;root=" + physicalNodeId,
                BuildCells(completePlacements, completeWidth, completeHeight));
            AssertNoOverlap(complete.Placements, "tree=" + physicalNodeId);
            return new BuiltNode(main, detachedUnits, complete, detached, completePlacements);
        }

        BuiltNode BuildDetachedUnit(string physicalNodeId, int targetLayer, bool detached)
        {
            var unit = BuildNode(physicalNodeId, null, true);
            return unit with { IsDetached = true };
        }

        void AssertPackedUnits(IReadOnlyList<ArchitectureV7TreeGridPlacementUnit> units)
        {
            for (var index = 0; index < units.Count; index++)
                for (var other = index + 1; other < units.Count; other++)
                {
                    var left = units[index];
                    var right = units[other];
                    if (left.Placements.Any(a => right.Placements.Any(b => a.LocalRow == b.LocalRow && a.LocalColumn < b.LocalColumn + b.LogicalSpan && b.LocalColumn < a.LocalColumn + a.LogicalSpan)))
                        throw new InvalidOperationException("V7 recursive tree packing produced overlapping child units before project composition.");
                    var leftRight = left.Placements.Max(item => item.LocalColumn + item.LogicalSpan);
                    var rightLeft = right.Placements.Min(item => item.LocalColumn);
                    if (leftRight + 1 > rightLeft)
                        throw new InvalidOperationException("V7 recursive tree packing lost the required one-column child-unit separation.");
                }
        }

        static int RequiredWidth(ArchitectureV7TreeGridPlacementUnit unit) =>
            unit.Placements.Count == 0 ? 0 : unit.Placements.Max(item => item.LocalColumn + item.LogicalSpan);

        static ArchitectureV7TreeGridPlacementUnit NormalizeUnit(ArchitectureV7TreeGridPlacementUnit unit, bool detached)
        {
            if (unit.Placements.Count == 0)
                return new ArchitectureV7TreeGridPlacementUnit(unit.UnitId, unit.RootPhysicalNodeId, unit.Placements,
                    0, unit.Height, unit.RootCentreCell, unit.RootRow, detached, unit.Provenance,
                    BuildCells(unit.Placements, 0, unit.Height));
            var minimum = unit.Placements.Min(item => item.LocalColumn);
            var reframed = OffsetUnit(unit, -minimum, detached);
            return new ArchitectureV7TreeGridPlacementUnit(reframed.UnitId, reframed.RootPhysicalNodeId, reframed.Placements,
                RequiredWidth(reframed), reframed.Height, reframed.RootCentreCell, reframed.RootRow, detached, reframed.Provenance,
                BuildCells(reframed.Placements, RequiredWidth(reframed), reframed.Height));
        }

        static void AssertNoOverlap(IReadOnlyList<ArchitectureV7TreeGridNodePlacement> placements, string context)
        {
            for (var index = 0; index < placements.Count; index++)
                for (var other = index + 1; other < placements.Count; other++)
                {
                    var left = placements[index];
                    var right = placements[other];
                    if (left.LocalRow == right.LocalRow && left.LocalColumn < right.LocalColumn + right.LogicalSpan && right.LocalColumn < left.LocalColumn + left.LogicalSpan)
                        throw new InvalidOperationException($"V7 recursive tree footprint overlap: {context}; {left.PhysicalNodeId}@{left.LocalRow}:{left.LocalColumn}+{left.LogicalSpan} with {right.PhysicalNodeId}@{right.LocalRow}:{right.LocalColumn}+{right.LogicalSpan}");
                }
        }
    }

    private static Dictionary<string, string[]> BuildChildren(ArchitectureV7PhysicalProjectionResult projection,
        IReadOnlyDictionary<string, ArchitectureV7PositionalOwnershipDecision> decisions)
    {
        var children = decisions.Keys.ToDictionary(id => id, _ => new List<string>(), StringComparer.Ordinal);
        foreach (var link in projection.PhysicalLinks
                     .OrderBy(link => link.AnalyserOrdinal < 0 ? int.MaxValue : link.AnalyserOrdinal)
                     .ThenBy(link => link.SemanticLinkId, StringComparer.Ordinal)
                     .ThenBy(link => link.PhysicalLinkId, StringComparer.Ordinal))
        {
            if (!decisions.TryGetValue(link.DestinationPhysicalNodeId, out var decision) || decision.PositionalParentPhysicalNodeId != link.SourcePhysicalNodeId) continue;
            var list = children[link.SourcePhysicalNodeId];
            if (!list.Contains(link.DestinationPhysicalNodeId, StringComparer.Ordinal)) list.Add(link.DestinationPhysicalNodeId);
        }
        return children.ToDictionary(item => item.Key, item => item.Value.ToArray(), StringComparer.Ordinal);
    }

    private static ArchitectureV7TreeGridPlacementUnit OffsetUnit(ArchitectureV7TreeGridPlacementUnit unit, int offset, bool detached)
    {
        if (offset == 0 && unit.IsDetached == detached) return unit;
        var placements = unit.Placements.Select(placement => placement with
        {
            LocalColumn = checked(placement.LocalColumn + offset),
            CentreCell = checked(placement.CentreCell + offset),
            IsDetached = detached || placement.IsDetached
        }).ToArray();
        return new ArchitectureV7TreeGridPlacementUnit(unit.UnitId, unit.RootPhysicalNodeId, placements, unit.Width, unit.Height,
            checked(unit.RootCentreCell + offset), unit.RootRow, detached, unit.Provenance,
            BuildCells(placements, unit.Width, unit.Height));
    }

    private static IReadOnlyList<ArchitectureV7TreeGridCell> BuildCells(
        IReadOnlyList<ArchitectureV7TreeGridNodePlacement> placements, int width, int height)
    {
        var cells = new List<ArchitectureV7TreeGridCell>(Math.Max(0, width) * Math.Max(0, height));
        for (var row = 0; row < height; row++)
            for (var column = 0; column < width; column++)
            {
                var placement = placements.FirstOrDefault(item => item.LocalRow == row &&
                    item.LocalColumn <= column && column < item.LocalColumn + item.LogicalSpan);
                var capability = placement is not null
                    ? ArchitectureV7CellCapability.Blocked
                    : row % 2 == 0
                        ? ArchitectureV7CellCapability.NodeAllowed
                        : ArchitectureV7CellCapability.RoutingAllowed | ArchitectureV7CellCapability.GeneralRouting;
                cells.Add(new ArchitectureV7TreeGridCell(row, column, capability, placement?.PhysicalNodeId));
            }
        return cells;
    }

    private sealed record BuiltNode(ArchitectureV7TreeGridPlacementUnit Main,
        IReadOnlyList<ArchitectureV7TreeGridPlacementUnit> Detached,
        ArchitectureV7TreeGridPlacementUnit Complete, bool IsDetached,
        IReadOnlyList<ArchitectureV7TreeGridNodePlacement> AllPlacements);
}
