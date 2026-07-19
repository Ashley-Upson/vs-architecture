using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core.Models;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal static class PositionalHierarchyAnalyzer
{
    public static PositionalHierarchy Analyze(PlacedGraph placement)
    {
        if (placement is null) throw new ArgumentNullException(nameof(placement));
        using var timing = PerformanceAudit.Measure(
            "positional hierarchy", inputNodes: placement.Nodes.Count, inputRoutes: placement.Graph.Links.Count,
            layoutRevision: placement.Revision.Value);
        var graph = placement.Graph;
        var stableNodes = graph.Nodes.OrderBy(node => node.Order).ThenBy(node => node.Id, StringComparer.Ordinal).ToArray();
        var nodeById = stableNodes.ToDictionary(node => node.Id, StringComparer.Ordinal);
        var incomingByNode = graph.Links
            .GroupBy(link => link.TargetId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<string>)group.Select(link => link.SourceId)
                    .Distinct(StringComparer.Ordinal).ToArray(),
                StringComparer.Ordinal);
        var parentByNode = new Dictionary<string, string>(StringComparer.Ordinal);
        var selections = new Dictionary<string, PositionalParentSelection>(StringComparer.Ordinal);

        foreach (var node in stableNodes)
        {
            var candidates = (incomingByNode.TryGetValue(node.Id, out var incoming)
                    ? incoming
                    : Array.Empty<string>())
                .Where(parentId => !string.Equals(parentId, node.Id, StringComparison.Ordinal) &&
                    nodeById.TryGetValue(parentId, out var parent) &&
                    string.Equals(parent.ProjectId, node.ProjectId, StringComparison.Ordinal) &&
                    placement.Hierarchy.ComponentByNode[parentId] != placement.Hierarchy.ComponentByNode[node.Id])
                .Select(parentId => Candidate(parentId, node.Id, placement))
                .OrderByDescending(candidate => candidate.DirectDownwardPath)
                .ThenBy(candidate => candidate.HorizontalMovement)
                .ThenBy(candidate => candidate.ConnectionDistance)
                .ThenBy(candidate => candidate.ParentLeft)
                .ThenBy(candidate => candidate.ParentNodeId, StringComparer.Ordinal)
                .ToArray();
            if (candidates.Length == 0) continue;

            var winner = candidates[0];
            parentByNode[node.Id] = winner.ParentNodeId;
            selections[node.Id] = new PositionalParentSelection(
                node.Id,
                winner.ParentNodeId,
                Reason(candidates),
                Array.AsReadOnly(candidates));
        }

        var children = stableNodes.ToDictionary(
            node => node.Id,
            node => (IReadOnlyList<string>)parentByNode
                .Where(item => string.Equals(item.Value, node.Id, StringComparison.Ordinal))
                .Select(item => item.Key)
                .OrderBy(id => nodeById[id].Order)
                .ThenBy(id => id, StringComparer.Ordinal)
                .ToArray(),
            StringComparer.Ordinal);
        var roots = stableNodes.Where(node => !parentByNode.ContainsKey(node.Id)).Select(node => node.Id).ToArray();
        Dictionary<string, PositionalSubtreeEnvelope> envelopes;
        using (PerformanceAudit.Measure("subtree-envelope calculation", inputNodes: stableNodes.Length,
                   layoutRevision: placement.Revision.Value))
            envelopes = BuildEnvelopes(stableNodes, children, placement);

        return new PositionalHierarchy(
            Snapshot(parentByNode),
            Snapshot(children),
            Array.AsReadOnly(roots),
            Snapshot(selections),
            Snapshot(envelopes),
            placement.Revision);
    }

    private static PositionalParentCandidate Candidate(string parentId, string nodeId, PlacedGraph placement)
    {
        var parent = placement.Nodes[parentId].Rect;
        var node = placement.Nodes[nodeId].Rect;
        var parentCentre = parent.X + parent.Width / 2;
        var nodeCentre = node.X + node.Width / 2;
        var horizontal = Math.Abs(parentCentre - nodeCentre);
        var vertical = Math.Abs(node.Y - parent.Bottom);
        var direct = horizontal == 0 && IsClearVertical(parentCentre, parent.Bottom, node.Y, parentId, nodeId, placement.Nodes);
        return new PositionalParentCandidate(parentId, direct, horizontal, horizontal + vertical, parent.X);
    }

    private static bool IsClearVertical(
        int x,
        int firstY,
        int secondY,
        string parentId,
        string nodeId,
        IReadOnlyDictionary<string, NodeLayout> nodes)
    {
        var minimumY = Math.Min(firstY, secondY);
        var maximumY = Math.Max(firstY, secondY);
        return nodes.All(item => string.Equals(item.Key, parentId, StringComparison.Ordinal) ||
            string.Equals(item.Key, nodeId, StringComparison.Ordinal) ||
            x <= item.Value.Rect.X || x >= item.Value.Rect.Right ||
            maximumY <= item.Value.Rect.Y || minimumY >= item.Value.Rect.Bottom);
    }

    private static PositionalParentSelectionReason Reason(IReadOnlyList<PositionalParentCandidate> candidates)
    {
        if (candidates.Count == 1) return PositionalParentSelectionReason.OnlyCandidate;
        var winner = candidates[0];
        var runnerUp = candidates[1];
        if (winner.DirectDownwardPath != runnerUp.DirectDownwardPath) return PositionalParentSelectionReason.DirectDownwardPath;
        if (winner.HorizontalMovement != runnerUp.HorizontalMovement) return PositionalParentSelectionReason.LeastHorizontalMovement;
        if (winner.ConnectionDistance != runnerUp.ConnectionDistance) return PositionalParentSelectionReason.ShortestConnectionDistance;
        if (winner.ParentLeft != runnerUp.ParentLeft) return PositionalParentSelectionReason.LeftmostParent;
        return PositionalParentSelectionReason.StableNodeId;
    }

    private static Dictionary<string, PositionalSubtreeEnvelope> BuildEnvelopes(
        IReadOnlyList<RenderNode> stableNodes,
        IReadOnlyDictionary<string, IReadOnlyList<string>> children,
        PlacedGraph placement)
    {
        var result = new Dictionary<string, PositionalSubtreeEnvelope>(StringComparer.Ordinal);
        var nodeById = stableNodes.ToDictionary(node => node.Id, StringComparer.Ordinal);
        foreach (var node in stableNodes) Build(node.Id);
        return result;

        PositionalSubtreeEnvelope Build(string rootId)
        {
            if (result.TryGetValue(rootId, out var existing)) return existing;
            var root = placement.Nodes[rootId];
            var byLayer = new Dictionary<int, Rect> { [root.Depth] = root.Rect };
            foreach (var childId in children[rootId])
            {
                var child = Build(childId);
                foreach (var layer in child.BoundsByLayer)
                {
                    byLayer[layer.Key] = byLayer.TryGetValue(layer.Key, out var current)
                        ? Union(new[] { current, layer.Value })
                        : layer.Value;
                }
            }

            var ordered = byLayer.OrderBy(item => item.Key).ToDictionary(item => item.Key, item => item.Value);
            var envelope = new PositionalSubtreeEnvelope(
                rootId,
                Union(ordered.Values),
                new ReadOnlyDictionary<int, Rect>(ordered),
                new ReadOnlyDictionary<int, int>(ordered.ToDictionary(item => item.Key, item => item.Value.X)),
                new ReadOnlyDictionary<int, int>(ordered.ToDictionary(item => item.Key, item => item.Value.Right)),
                ordered.Keys.Min(),
                ordered.Keys.Max(),
                nodeById[rootId].ProjectId,
                placement.Revision);
            result[rootId] = envelope;
            return envelope;
        }
    }

    private static Rect Union(IEnumerable<Rect> rectangles)
    {
        var values = rectangles.ToArray();
        if (values.Length == 0) throw new InvalidOperationException("Cannot calculate an empty positional envelope.");
        var left = values.Min(rect => rect.X);
        var top = values.Min(rect => rect.Y);
        var right = values.Max(rect => rect.Right);
        var bottom = values.Max(rect => rect.Bottom);
        return new Rect(left, top, right - left, bottom - top);
    }

    private static IReadOnlyDictionary<string, TValue> Snapshot<TValue>(IDictionary<string, TValue> values) =>
        new ReadOnlyDictionary<string, TValue>(
            values.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal));
}
