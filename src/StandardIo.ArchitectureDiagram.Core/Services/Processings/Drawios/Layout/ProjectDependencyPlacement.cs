using System;
using System.Collections.Generic;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core.Models;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal static class ProjectDependencyPlacement
{
    public static IReadOnlyDictionary<string, NodeLayout> Apply(
        RenderGraph completeGraph,
        IReadOnlyDictionary<string, NodeLayout> placement,
        DiagramSettings settings)
    {
        var nodes = placement.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        var internalIds = new HashSet<string>(nodes.Keys, StringComparer.Ordinal);
        var links = completeGraph.Links.Where(link =>
            internalIds.Contains(link.SourceId) || internalIds.Contains(link.TargetId)).ToArray();
        var parents = links.GroupBy(link => link.TargetId, StringComparer.Ordinal).ToDictionary(
            group => group.Key, group => group.Select(link => link.SourceId).Distinct(StringComparer.Ordinal).ToArray(),
            StringComparer.Ordinal);
        var children = links.GroupBy(link => link.SourceId, StringComparer.Ordinal).ToDictionary(
            group => group.Key, group => group.Select(link => link.TargetId).Distinct(StringComparer.Ordinal).ToArray(),
            StringComparer.Ordinal);

        foreach (var id in internalIds.Where(id => ParentIds(id).Length > 1).OrderBy(id => id, StringComparer.Ordinal))
            nodes[id] = nodes[id] with
            {
                PlacementAuthority = NodePlacementAuthority.SharedDependency,
                PlacementOriginalRect = nodes[id].Rect,
                PlacementReason = "Multiple rendered parents require shared dependency placement."
            };

        var chainChildren = internalIds.Where(childId =>
                nodes[childId].PlacementAuthority == NodePlacementAuthority.GeneralFallback &&
                ParentIds(childId).Length == 1 && internalIds.Contains(ParentIds(childId)[0]) &&
                ChildIds(ParentIds(childId)[0]).Length == 1)
            .OrderBy(childId => nodes[ParentIds(childId)[0]].Rect.X)
            .ThenBy(childId => childId, StringComparer.Ordinal).ToArray();
        foreach (var childId in chainChildren)
        {
            var parentId = ParentIds(childId)[0];
            var child = nodes[childId];
            var targetX = nodes[parentId].Rect.CenterX - child.Rect.Width / 2;
            targetX = ClosestAvailableX(childId, child.Depth, targetX, child.Rect.Width, nodes,
                settings.Layout.HorizontalSpacing);
            nodes[childId] = child with
            {
                Rect = child.Rect with { X = targetX },
                PlacementAuthority = NodePlacementAuthority.ExclusiveOneToOneChain,
                PlacementOriginalRect = child.Rect,
                PlacementReason = "Only child aligned to its only rendered parent."
            };
        }

        var siblingParents = internalIds.Where(parentId =>
                ChildIds(parentId).Where(internalIds.Contains).Count() >= 2 &&
                ChildIds(parentId).Where(internalIds.Contains).All(childId => ParentIds(childId).Length == 1))
            .OrderBy(parentId => nodes[parentId].Rect.X).ThenBy(parentId => parentId, StringComparer.Ordinal).ToArray();
        foreach (var parentId in siblingParents)
        {
            var childRoots = ChildIds(parentId).Where(internalIds.Contains)
                .OrderBy(id => nodes[id].Rect.X).ThenBy(id => id, StringComparer.Ordinal).ToArray();
            var subtrees = childRoots.Select(root => ExclusiveSubtree(root, internalIds, children, parents)).ToArray();
            var cursor = subtrees.SelectMany(ids => ids).Min(id => nodes[id].Rect.X);
            foreach (var subtree in subtrees)
            {
                var bounds = Bounds(subtree.Select(id => nodes[id].Rect));
                var delta = cursor - bounds.X;
                foreach (var id in subtree)
                {
                    var current = nodes[id];
                    nodes[id] = current with
                    {
                        Rect = current.Rect.Translate(delta, 0),
                        PlacementAuthority = current.PlacementAuthority == NodePlacementAuthority.SharedDependency
                            ? current.PlacementAuthority
                            : NodePlacementAuthority.ExclusiveSiblingSubtree,
                        PlacementOriginalRect = current.PlacementOriginalRect ?? current.Rect,
                        PlacementReason = current.PlacementReason ??
                            "Exclusive descendant retained in its parent sibling subtree."
                    };
                }
                cursor += bounds.Width + settings.Layout.HorizontalSpacing;
            }
            var combined = Bounds(subtrees.SelectMany(ids => ids).Select(id => nodes[id].Rect));
            var parent = nodes[parentId];
            nodes[parentId] = parent with
            {
                Rect = parent.Rect with { X = combined.CenterX - parent.Rect.Width / 2 },
                PlacementAuthority = parent.PlacementAuthority == NodePlacementAuthority.SharedDependency
                    ? parent.PlacementAuthority
                    : NodePlacementAuthority.ExclusiveSiblingSubtree,
                PlacementOriginalRect = parent.PlacementOriginalRect ?? parent.Rect,
                PlacementReason = parent.PlacementReason ??
                    "Parent centred over exclusive child subtree bounds."
            };
        }
        PlacementPipeline.ResolveLayerOverlaps(settings, nodes);
        return nodes;

        string[] ParentIds(string id) => parents.TryGetValue(id, out var values) ? values : Array.Empty<string>();
        string[] ChildIds(string id) => children.TryGetValue(id, out var values) ? values : Array.Empty<string>();
    }

    private static string[] ExclusiveSubtree(string root, ISet<string> internalIds,
        IReadOnlyDictionary<string, string[]> children, IReadOnlyDictionary<string, string[]> parents)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        Visit(root);
        return result.OrderBy(id => id, StringComparer.Ordinal).ToArray();
        void Visit(string id)
        {
            if (!internalIds.Contains(id) || !result.Add(id)) return;
            if (!children.TryGetValue(id, out var childIds)) return;
            foreach (var child in childIds.OrderBy(value => value, StringComparer.Ordinal))
                if (internalIds.Contains(child) && parents.TryGetValue(child, out var parentIds) &&
                    parentIds.Length == 1 && string.Equals(parentIds[0], id, StringComparison.Ordinal)) Visit(child);
        }
    }

    private static int ClosestAvailableX(string nodeId, int depth, int preferredX, int width,
        IReadOnlyDictionary<string, NodeLayout> nodes, int spacing)
    {
        var occupied = nodes.Values.Where(node => node.Node.Id != nodeId && node.Depth == depth)
            .OrderBy(node => node.Rect.X).ThenBy(node => node.Node.Id, StringComparer.Ordinal).ToArray();
        bool Clear(int x) => occupied.All(node => x + width + spacing <= node.Rect.X ||
            x >= node.Rect.Right + spacing);
        if (Clear(preferredX)) return preferredX;
        var candidates = occupied.SelectMany(node => new[]
            {
                node.Rect.X - spacing - width,
                node.Rect.Right + spacing
            }).Where(Clear).OrderBy(x => Math.Abs(x - preferredX)).ThenBy(x => x).ToArray();
        return candidates.Length == 0 ? preferredX : candidates[0];
    }

    private static Rect Bounds(IEnumerable<Rect> rectangles)
    {
        var values = rectangles.ToArray();
        var left = values.Min(rect => rect.X);
        var top = values.Min(rect => rect.Y);
        var right = values.Max(rect => rect.Right);
        var bottom = values.Max(rect => rect.Bottom);
        return new Rect(left, top, right - left, bottom - top);
    }
}
