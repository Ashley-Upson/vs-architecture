using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal static class HierarchyAnalyzer
{
    private const string ExposureTreeIdPrefix = "tree_";

    public static LayoutHierarchy Analyze(
        RenderGraph graph,
        LayoutRevision revision,
        CancellationToken cancellationToken = default)
    {
        if (graph is null) throw new ArgumentNullException(nameof(graph));
        cancellationToken.ThrowIfCancellationRequested();

        var stableNodes = graph.Nodes
            .OrderBy(node => node.Order)
            .ThenBy(node => node.Id, StringComparer.Ordinal)
            .ToArray();
        var nodeById = stableNodes.ToDictionary(node => node.Id, StringComparer.Ordinal);
        var outgoingLinks = stableNodes.ToDictionary(
            node => node.Id,
            node => (IReadOnlyList<RenderLink>)graph.Links
                .Where(link => string.Equals(link.SourceId, node.Id, StringComparison.Ordinal))
                .OrderBy(link => link.Order)
                .ThenBy(link => link.Id, StringComparer.Ordinal)
                .ToArray(),
            StringComparer.Ordinal);
        var stronglyConnected = StrongComponents(stableNodes, outgoingLinks, cancellationToken);
        var componentByNode = stronglyConnected
            .SelectMany((component, componentId) => component.Select(nodeId => new { nodeId, componentId }))
            .ToDictionary(item => item.nodeId, item => item.componentId, StringComparer.Ordinal);
        var outgoingComponents = stronglyConnected.Select(_ => new SortedSet<int>()).ToArray();
        var incomingCount = new int[stronglyConnected.Count];
        foreach (var link in graph.Links.OrderBy(link => link.Order).ThenBy(link => link.Id, StringComparer.Ordinal))
        {
            var source = componentByNode[link.SourceId];
            var target = componentByNode[link.TargetId];
            if (source != target && outgoingComponents[source].Add(target))
            {
                incomingCount[target]++;
            }
        }

        var topologicalOrder = TopologicalOrder(outgoingComponents, incomingCount, cancellationToken);
        var distanceToExit = Enumerable.Range(0, stronglyConnected.Count).ToDictionary(id => id, _ => 0);
        foreach (var componentId in topologicalOrder.AsEnumerable().Reverse())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (outgoingComponents[componentId].Count > 0)
            {
                distanceToExit[componentId] = outgoingComponents[componentId]
                    .Max(target => distanceToExit[target] + 1);
            }
        }

        var maximumDistance = distanceToExit.Values.DefaultIfEmpty(0).Max();
        var layerByComponent = distanceToExit.ToDictionary(item => item.Key, item => maximumDistance - item.Value);
        var visualLayerByNode = stableNodes.ToDictionary(
            node => node.Id,
            node => layerByComponent[componentByNode[node.Id]],
            StringComparer.Ordinal);
        var parentByNode = BuildAcyclicParents(graph, stableNodes, componentByNode);
        var childrenByNode = stableNodes.ToDictionary(
            node => node.Id,
            node => (IReadOnlyList<string>)parentByNode
                .Where(item => string.Equals(item.Value, node.Id, StringComparison.Ordinal))
                .Select(item => item.Key)
                .OrderBy(id => nodeById[id].Order)
                .ThenBy(id => id, StringComparer.Ordinal)
                .ToArray(),
            StringComparer.Ordinal);
        var roots = stableNodes.Where(node => !parentByNode.ContainsKey(node.Id)).Select(node => node.Id).ToArray();
        var components = stronglyConnected.Select((nodes, id) => new LayoutComponent(
            id,
            nodes,
            layerByComponent[id],
            topologicalOrder.IndexOf(id),
            incomingCount[id] == 0)).ToArray();
        var edgeDirections = graph.Links.ToDictionary(
            link => link.Id,
            link => visualLayerByNode[link.TargetId] > visualLayerByNode[link.SourceId]
                ? HierarchyEdgeDirection.Downward
                : visualLayerByNode[link.TargetId] < visualLayerByNode[link.SourceId]
                    ? HierarchyEdgeDirection.Upward
                    : HierarchyEdgeDirection.Lateral,
            StringComparer.Ordinal);
        var provenance = stableNodes.ToDictionary(
            node => node.Id,
            node => node.IsExternal
                ? LayoutNodeProvenance.ExternalDependency
                : graph.PlacementParentByNode.ContainsKey(node.Id)
                    ? LayoutNodeProvenance.CanonicalFirstPlacement
                    : node.Id.StartsWith(ExposureTreeIdPrefix, StringComparison.Ordinal)
                        ? LayoutNodeProvenance.DuplicatedExposureClone
                        : LayoutNodeProvenance.Ordinary,
            StringComparer.Ordinal);

        cancellationToken.ThrowIfCancellationRequested();
        return new LayoutHierarchy(
            Snapshot(parentByNode),
            Snapshot(childrenByNode),
            Array.AsReadOnly(roots),
            Snapshot(componentByNode),
            Array.AsReadOnly(components),
            Array.AsReadOnly(stableNodes.Select(node => node.Id).ToArray()),
            Snapshot(visualLayerByNode),
            Snapshot(edgeDirections),
            Snapshot(provenance),
            revision);
    }

    private static IReadOnlyList<IReadOnlyList<string>> StrongComponents(
        IReadOnlyList<RenderNode> stableNodes,
        IReadOnlyDictionary<string, IReadOnlyList<RenderLink>> outgoing,
        CancellationToken cancellationToken)
    {
        var orderByNode = stableNodes.ToDictionary(node => node.Id, node => node.Order, StringComparer.Ordinal);
        var index = 0;
        var stack = new Stack<string>();
        var onStack = new HashSet<string>(StringComparer.Ordinal);
        var indices = new Dictionary<string, int>(StringComparer.Ordinal);
        var lowLinks = new Dictionary<string, int>(StringComparer.Ordinal);
        var components = new List<IReadOnlyList<string>>();

        foreach (var node in stableNodes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!indices.ContainsKey(node.Id)) Visit(node.Id);
        }

        return components;

        void Visit(string nodeId)
        {
            cancellationToken.ThrowIfCancellationRequested();
            indices[nodeId] = index;
            lowLinks[nodeId] = index++;
            stack.Push(nodeId);
            onStack.Add(nodeId);

            foreach (var link in outgoing[nodeId])
            {
                var targetId = link.TargetId;
                if (!indices.ContainsKey(targetId))
                {
                    Visit(targetId);
                    lowLinks[nodeId] = Math.Min(lowLinks[nodeId], lowLinks[targetId]);
                }
                else if (onStack.Contains(targetId))
                {
                    lowLinks[nodeId] = Math.Min(lowLinks[nodeId], indices[targetId]);
                }
            }

            if (lowLinks[nodeId] != indices[nodeId]) return;
            var component = new List<string>();
            string current;
            do
            {
                current = stack.Pop();
                onStack.Remove(current);
                component.Add(current);
            }
            while (!string.Equals(current, nodeId, StringComparison.Ordinal));
            component.Sort((left, right) =>
            {
                var order = orderByNode[left].CompareTo(orderByNode[right]);
                return order != 0 ? order : StringComparer.Ordinal.Compare(left, right);
            });
            components.Add(component);
        }
    }

    private static List<int> TopologicalOrder(
        IReadOnlyList<SortedSet<int>> outgoing,
        IReadOnlyList<int> incoming,
        CancellationToken cancellationToken)
    {
        var remainingIncoming = incoming.ToArray();
        var ready = new SortedSet<int>(Enumerable.Range(0, incoming.Count).Where(id => incoming[id] == 0));
        var result = new List<int>();
        while (ready.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var component = ready.Min;
            ready.Remove(component);
            result.Add(component);
            foreach (var target in outgoing[component])
            {
                if (--remainingIncoming[target] == 0) ready.Add(target);
            }
        }

        return result;
    }

    private static Dictionary<string, string> BuildAcyclicParents(
        RenderGraph graph,
        IReadOnlyList<RenderNode> stableNodes,
        IReadOnlyDictionary<string, int> componentByNode)
    {
        var orderByNode = stableNodes.ToDictionary(node => node.Id, node => node.Order, StringComparer.Ordinal);
        var parentByNode = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var node in stableNodes)
        {
            if (graph.PlacementParentByNode.TryGetValue(node.Id, out var explicitParent) &&
                componentByNode[explicitParent] != componentByNode[node.Id])
            {
                parentByNode[node.Id] = explicitParent;
                continue;
            }

            var parent = graph.Links
                .Where(link => string.Equals(link.TargetId, node.Id, StringComparison.Ordinal) &&
                    componentByNode[link.SourceId] != componentByNode[node.Id])
                .OrderBy(link => orderByNode[link.SourceId])
                .ThenBy(link => link.SourceId, StringComparer.Ordinal)
                .ThenBy(link => link.Order)
                .ThenBy(link => link.Id, StringComparer.Ordinal)
                .Select(link => link.SourceId)
                .FirstOrDefault();
            if (parent is not null) parentByNode[node.Id] = parent;
        }

        return parentByNode;
    }

    private static IReadOnlyDictionary<string, TValue> Snapshot<TValue>(IDictionary<string, TValue> source) =>
        new ReadOnlyDictionary<string, TValue>(
            source.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal));
}
