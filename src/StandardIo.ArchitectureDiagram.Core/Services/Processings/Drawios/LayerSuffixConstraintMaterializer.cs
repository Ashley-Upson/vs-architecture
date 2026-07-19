using System;
using System.Collections.Generic;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core.Models;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal static class LayerSuffixConstraintMaterializer
{
    public static GenerationConstraint ProposeMinimumY(
        RailAllocationRegionIdentity region,
        int requiredExtent,
        int currentLowerLayerY)
    {
        if (region.MovementScope is null ||
            region.MovementScope.Value.Kind != MovementScopeKind.LayerAndLowerSuffix)
            throw new ArgumentException("A lower-layer suffix movement scope is required.", nameof(region));
        var missing = Math.Max(0, requiredExtent - region.AllowedAxisRange.Length);
        return new GenerationConstraint(
            new GenerationConstraintKey(region.MovementScope.Value, GenerationConstraintKind.MinimumY),
            currentLowerLayerY + missing,
            $"Common rail extent {region.EnvelopeIdentity}");
    }

    public static LayerSuffixConstraintIteration Materialize(
        PlacedGraph immutableBase,
        IReadOnlyList<GenerationConstraint> constraints,
        DiagramSettings settings,
        IReadOnlyDictionary<string, LinkLayout> routes)
    {
        var nodes = immutableBase.Nodes.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        var layersMoved = new SortedSet<int>();
        var maximumDelta = 0;
        foreach (var constraint in constraints.Where(item =>
                     item.Key.Kind == GenerationConstraintKind.MinimumY &&
                     item.Key.Scope.Kind == MovementScopeKind.LayerAndLowerSuffix)
                 .OrderBy(item => Depth(item.Key.Scope)).ThenBy(item => item.Reason, StringComparer.Ordinal))
        {
            var depth = Depth(constraint.Key.Scope);
            var currentY = nodes.Values.Where(item => !item.IsStandalone && item.Depth == depth)
                .Select(item => item.Rect.Y).DefaultIfEmpty(constraint.Minimum).Min();
            var delta = Math.Max(0, constraint.Minimum - currentY);
            if (delta == 0) continue;
            maximumDelta = Math.Max(maximumDelta, delta);
            foreach (var id in nodes.Where(item => !item.Value.IsStandalone && item.Value.Depth >= depth)
                         .Select(item => item.Key).ToArray())
            {
                nodes[id] = nodes[id] with { Rect = nodes[id].Rect.Translate(0, delta) };
                layersMoved.Add(nodes[id].Depth);
            }
        }

        var moved = nodes.Where(item => item.Value.Rect != immutableBase.Nodes[item.Key].Rect)
            .Select(item => item.Key).OrderBy(item => item, StringComparer.Ordinal).ToArray();
        if (moved.Length == 0)
            return new LayerSuffixConstraintIteration(immutableBase, Array.Empty<int>(), moved,
                Array.Empty<string>(), 0, false);
        var revised = immutableBase.Revise(nodes, PlacementPipeline.PositionProjects(immutableBase.Graph, settings, nodes));
        var changed = new HashSet<string>(moved, StringComparer.Ordinal);
        var invalidated = routes.Values.Where(route =>
                changed.Contains(route.Link.SourceId) || changed.Contains(route.Link.TargetId) ||
                CrossesMovedBoundary(route, immutableBase.Nodes, nodes))
            .Select(route => route.Link.Id).Distinct(StringComparer.Ordinal)
            .OrderBy(item => item, StringComparer.Ordinal).ToArray();
        return new LayerSuffixConstraintIteration(
            revised, layersMoved.ToArray(), moved, invalidated, maximumDelta, true);
    }

    private static bool CrossesMovedBoundary(
        LinkLayout route,
        IReadOnlyDictionary<string, NodeLayout> before,
        IReadOnlyDictionary<string, NodeLayout> after)
    {
        var movedDepths = before.Where(item => item.Value.Rect != after[item.Key].Rect)
            .Select(item => item.Value.Depth).ToArray();
        if (movedDepths.Length == 0) return false;
        var firstMovedDepth = movedDepths.Min();
        if (!before.TryGetValue(route.Link.SourceId, out var source) ||
            !before.TryGetValue(route.Link.TargetId, out var target)) return false;
        return Math.Min(source.Depth, target.Depth) < firstMovedDepth &&
            Math.Max(source.Depth, target.Depth) >= firstMovedDepth;
    }

    private static int Depth(MovementScopeIdentity scope)
    {
        const string prefix = "depth:";
        if (!scope.Id.StartsWith(prefix, StringComparison.Ordinal) ||
            !int.TryParse(scope.Id.Substring(prefix.Length), out var depth))
            throw new InvalidOperationException($"Unsupported layer suffix identity {scope.Id}.");
        return depth;
    }
}
