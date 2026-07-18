using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using StandardIo.ArchitectureDiagram.Core.Models;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal static class PlacementPipeline
{
    private const int TextWidth = 8;
    private const string ExposureTreeIdPrefix = "tree_";

    public static PlacementResult Place(
        RenderGraph graph,
        DiagramSettings settings,
        LayoutRevision revision,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var hierarchy = HierarchyAnalyzer.Analyze(graph, revision, cancellationToken);
        var depthOffsets = CalculateDepthOffsets(graph, settings, hierarchy.VisualLayerByNode);
        var widths = CalculateWidths(graph, settings);
        var nodes = PositionNodes(graph, settings, hierarchy.VisualLayerByNode, depthOffsets, widths, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var projects = PositionProjects(graph, settings, nodes);
        cancellationToken.ThrowIfCancellationRequested();
        return new PlacementResult(hierarchy, nodes, projects);
    }

        private static Dictionary<string, int> CalculateWidths(RenderGraph graph, DiagramSettings settings)
        {
            var linkCounts = graph.Nodes.ToDictionary(
                node => node.Id,
                node => graph.Links.Count(link => link.SourceId == node.Id || link.TargetId == node.Id),
                StringComparer.Ordinal);

            return graph.Nodes.ToDictionary(
                node => node.Id,
                node =>
                {
                    var labelWidth = Math.Max(node.Name.Length, node.FullName.Length / 2) * TextWidth + settings.Layout.LinkNodeWidthPadding;
                    var portWidth = Math.Max(0, linkCounts[node.Id] - 1) * settings.Layout.EdgePortSpacing + settings.Layout.LinkNodeWidthPadding;
                    return Math.Max(settings.Layout.NodeWidth, Math.Max(labelWidth, portWidth));
                },
                StringComparer.Ordinal);
        }

        private static Dictionary<int, int> CalculateDepthOffsets(
            RenderGraph graph,
            DiagramSettings settings,
            IReadOnlyDictionary<string, int> depths)
        {
            var bandPressures = new Dictionary<int, Dictionary<string, int>>();
            foreach (var link in graph.Links)
            {
                var sourceDepth = depths[link.SourceId];
                var targetDepth = depths[link.TargetId];
                var upper = Math.Min(sourceDepth, targetDepth);
                var lower = Math.Max(sourceDepth, targetDepth);
                for (var depth = upper; depth < lower; depth++)
                {
                    if (!bandPressures.TryGetValue(depth, out var pressure))
                    {
                        pressure = new Dictionary<string, int>(StringComparer.Ordinal);
                        bandPressures[depth] = pressure;
                    }

                    pressure[link.SourceId] = pressure.TryGetValue(link.SourceId, out var sourceCount) ? sourceCount + 1 : 1;
                    pressure[link.TargetId] = pressure.TryGetValue(link.TargetId, out var targetCount) ? targetCount + 1 : 1;
                }
            }

            var offsets = new Dictionary<int, int>();
            var cumulativeOffset = 0;
            var maxDepth = depths.Values.DefaultIfEmpty(0).Max();
            for (var depth = 0; depth <= maxDepth; depth++)
            {
                offsets[depth] = cumulativeOffset;
                if (bandPressures.TryGetValue(depth, out var pressure))
                {
                    var linkCount = pressure.Values.DefaultIfEmpty(0).Max();
                    var neededGap = settings.Layout.LinkPadding * 2 +
                        Math.Max(0, linkCount - 1) * settings.Layout.ParallelLaneSpacing;
                    cumulativeOffset += Math.Max(0, neededGap - settings.Layout.VerticalSpacing);
                }
            }

            return offsets;
        }

        private static Dictionary<string, NodeLayout> PositionNodes(
            RenderGraph graph,
            DiagramSettings settings,
            IReadOnlyDictionary<string, int> depths,
            IReadOnlyDictionary<int, int> depthOffsets,
            IReadOnlyDictionary<string, int> widths,
            CancellationToken cancellationToken)
        {
            if (graph.Nodes.Any(node => node.Id.StartsWith(ExposureTreeIdPrefix, StringComparison.Ordinal)) &&
                (graph.Nodes.Count >= settings.Layout.ExposureTreeLayoutThreshold || IsRootedExposureForest(graph)))
            {
                return PositionExposureTrees(graph, settings, depths, widths, cancellationToken);
            }

            var incidentIds = new HashSet<string>(
                graph.Links.SelectMany(link => new[] { link.SourceId, link.TargetId }),
                StringComparer.Ordinal);
            var connected = graph.Nodes.Where(node => incidentIds.Contains(node.Id)).ToArray();
            var standalone = graph.Nodes.Where(node => !incidentIds.Contains(node.Id)).ToArray();
            var result = new Dictionary<string, NodeLayout>(StringComparer.Ordinal);

            foreach (var layer in connected
                .GroupBy(node => depths[node.Id])
                .OrderBy(group => group.Key))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var x = settings.Layout.ContainerPadding * 2;
                foreach (var node in layer.OrderBy(node => node.Order).ThenBy(node => node.Id, StringComparer.Ordinal))
                {
                    result[node.Id] = new NodeLayout(
                        node,
                        new Rect(x, NodeY(layer.Key, settings, depthOffsets), widths[node.Id], settings.Layout.NodeHeight),
                        layer.Key,
                        false);
                    x += widths[node.Id] + settings.Layout.HorizontalSpacing;
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            CenterParentsOverChildren(graph, settings, result);
            ResolveLayerOverlaps(settings, result);
            ReserveLinkCorridors(graph, settings, result);
            ResolveLayerOverlaps(settings, result);
            AlignBaselineNodes(settings, result);
            RelaxNonTopDepthAlignment(graph, settings, result);
            ResolveDepthBandVerticalOverlaps(settings, result);
            AlignBaselineNodes(settings, result);
            PlaceExternalDependencyNodes(graph, settings, result);
            AlignBaselineNodes(settings, result);

            var standaloneX = result.Count == 0
                ? settings.Layout.ContainerPadding * 2
                : result.Values
                    .Where(node => !node.IsStandalone)
                    .Select(node => node.Rect.Right)
                    .DefaultIfEmpty(result.Values.Max(node => node.Rect.Right))
                    .Max() + settings.Layout.StandaloneGroupSpacing + graph.Links.Count * settings.Layout.ParallelLaneSpacing;
            var standaloneNodes = standalone
                .OrderBy(node => depths[node.Id])
                .ThenBy(node => node.Order)
                .ToArray();
            var standaloneNodesPerRow = standaloneNodes.Length == 0
                ? 1
                : (int)Math.Ceiling(Math.Sqrt(standaloneNodes.Length));
            var standaloneColumnWidth = standaloneNodes
                .Select(node => widths[node.Id])
                .DefaultIfEmpty(settings.Layout.NodeWidth)
                .Max() + settings.Layout.HorizontalSpacing;
            for (var index = 0; index < standaloneNodes.Length; index++)
            {
                var node = standaloneNodes[index];
                var column = index % standaloneNodesPerRow;
                var row = index / standaloneNodesPerRow;
                result[node.Id] = new NodeLayout(
                    node,
                    new Rect(
                        standaloneX + column * standaloneColumnWidth,
                        settings.Layout.ContainerPadding * 2 + row * (settings.Layout.NodeHeight + settings.Layout.VerticalSpacing),
                        widths[node.Id],
                        settings.Layout.NodeHeight),
                    depths[node.Id],
                    true);
            }

            return result;
        }

        private static Dictionary<string, NodeLayout> PositionExposureTrees(
            RenderGraph graph,
            DiagramSettings settings,
            IReadOnlyDictionary<string, int> depths,
            IReadOnlyDictionary<string, int> widths,
            CancellationToken cancellationToken)
        {
            var result = new Dictionary<string, NodeLayout>(StringComparer.Ordinal);
            var incoming = graph.Nodes.ToDictionary(node => node.Id, _ => 0, StringComparer.Ordinal);
            var outgoing = graph.Links
                .GroupBy(link => link.SourceId, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => group
                        .Where(link => graph.PlacementParentByNode.Count == 0 ||
                            graph.PlacementParentByNode.TryGetValue(link.TargetId, out var parentId) &&
                            string.Equals(parentId, link.SourceId, StringComparison.Ordinal))
                        .OrderBy(link => graph.Nodes.Single(node => node.Id == link.TargetId).Order)
                        .ThenBy(link => link.Order)
                        .ToArray(),
                    StringComparer.Ordinal);

            foreach (var link in graph.Links)
            {
                if (incoming.ContainsKey(link.TargetId))
                {
                    incoming[link.TargetId]++;
                }
            }

            var connectedIds = new HashSet<string>(
                graph.Links.SelectMany(link => new[] { link.SourceId, link.TargetId }),
                StringComparer.Ordinal);
            var nodeById = graph.Nodes.ToDictionary(node => node.Id, StringComparer.Ordinal);
            var roots = graph.Nodes
                .Where(node => connectedIds.Contains(node.Id) && incoming[node.Id] == 0)
                .OrderBy(node => node.Order)
                .ThenBy(node => node.Id, StringComparer.Ordinal)
                .ToArray();
            var placed = new HashSet<string>(StringComparer.Ordinal);
            var cursorX = settings.Layout.ContainerPadding * 2;
            var treeGap = settings.Layout.NodeWidth + settings.Layout.StandaloneGroupSpacing;
            var exposureDepths = CalculateExposureTraversalDepths(graph, roots);
            var depthGaps = graph.Nodes.Count < settings.Layout.ExposureTreeLayoutThreshold
                ? CalculateExposureDepthGaps(graph, settings, exposureDepths)
                : new Dictionary<int, int>();

            foreach (var root in roots)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var subtree = MeasureExposureSubtree(root.Id, graph, settings, widths, outgoing, new HashSet<string>(StringComparer.Ordinal), 0, cancellationToken);
                PlaceExposureSubtree(
                    root.Id,
                    cursorX,
                    settings.Layout.ContainerPadding * 2,
                    subtree.Width,
                    0,
                    graph,
                    settings,
                    widths,
                    outgoing,
                    nodeById,
                    result,
                    placed,
                    new HashSet<string>(StringComparer.Ordinal),
                    depthGaps,
                    cancellationToken);
                cursorX += subtree.Width + treeGap;
            }

            CenterExposureParentsOverChildren(graph, result);

            var standaloneNodes = graph.Nodes
                .Where(node => !placed.Contains(node.Id))
                .OrderBy(node => depths.TryGetValue(node.Id, out var depth) ? depth : 0)
                .ThenBy(node => node.Order)
                .ToArray();
            var standaloneStartX = result.Values
                .Select(node => node.Rect.Right)
                .DefaultIfEmpty(settings.Layout.ContainerPadding * 2)
                .Max() + settings.Layout.StandaloneGroupSpacing;
            var standaloneColumnWidth = standaloneNodes
                .Select(node => widths[node.Id])
                .DefaultIfEmpty(settings.Layout.NodeWidth)
                .Max() + settings.Layout.HorizontalSpacing;
            var standaloneNodesPerRow = standaloneNodes.Length == 0
                ? 1
                : (int)Math.Ceiling(Math.Sqrt(standaloneNodes.Length));

            for (var index = 0; index < standaloneNodes.Length; index++)
            {
                var node = standaloneNodes[index];
                var column = index % standaloneNodesPerRow;
                var row = index / standaloneNodesPerRow;
                result[node.Id] = new NodeLayout(
                    node,
                    new Rect(
                        standaloneStartX + column * standaloneColumnWidth,
                        settings.Layout.ContainerPadding * 2 + row * (settings.Layout.NodeHeight + settings.Layout.VerticalSpacing),
                        widths[node.Id],
                        settings.Layout.NodeHeight),
                    depths.TryGetValue(node.Id, out var depth) ? depth : 0,
                    true);
            }

            return result;
        }

        private static bool IsRootedExposureForest(RenderGraph graph)
        {
            var incoming = graph.Nodes.ToDictionary(node => node.Id, _ => 0, StringComparer.Ordinal);
            var outgoing = graph.Nodes.ToDictionary(
                node => node.Id,
                node => graph.Links.Where(link => link.SourceId == node.Id).Select(link => link.TargetId).ToArray(),
                StringComparer.Ordinal);
            foreach (var link in graph.Links)
            {
                if (!incoming.ContainsKey(link.TargetId) || ++incoming[link.TargetId] > 1)
                {
                    return false;
                }
            }

            var roots = incoming.Where(item => item.Value == 0).Select(item => item.Key).ToArray();
            if (roots.Length == 0)
            {
                return false;
            }

            var visited = new HashSet<string>(StringComparer.Ordinal);
            var queue = new Queue<string>(roots);
            while (queue.Count > 0)
            {
                var nodeId = queue.Dequeue();
                if (!visited.Add(nodeId))
                {
                    return false;
                }

                foreach (var targetId in outgoing[nodeId])
                {
                    queue.Enqueue(targetId);
                }
            }

            return visited.Count == graph.Nodes.Count;
        }

        private static IReadOnlyDictionary<string, int> CalculateExposureTraversalDepths(
            RenderGraph graph,
            IReadOnlyList<RenderNode> roots)
        {
            var outgoing = graph.Links
                .GroupBy(link => link.SourceId, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
            var result = new Dictionary<string, int>(StringComparer.Ordinal);
            var queue = new Queue<string>();
            foreach (var root in roots)
            {
                result[root.Id] = 0;
                queue.Enqueue(root.Id);
            }

            while (queue.Count > 0)
            {
                var sourceId = queue.Dequeue();
                if (!outgoing.TryGetValue(sourceId, out var links))
                {
                    continue;
                }

                foreach (var link in links.OrderBy(item => item.Id, StringComparer.Ordinal))
                {
                    var candidateDepth = result[sourceId] + 1;
                    if (result.TryGetValue(link.TargetId, out var existingDepth) && existingDepth <= candidateDepth)
                    {
                        continue;
                    }

                    result[link.TargetId] = candidateDepth;
                    queue.Enqueue(link.TargetId);
                }
            }

            return result;
        }

        private static IReadOnlyDictionary<int, int> CalculateExposureDepthGaps(
            RenderGraph graph,
            DiagramSettings settings,
            IReadOnlyDictionary<string, int> depths)
        {
            var maximumDepth = depths.Values.DefaultIfEmpty(0).Max();
            var gaps = new Dictionary<int, int>();
            for (var depth = 0; depth < maximumDepth; depth++)
            {
                var fanoutLaneCount = graph.Links
                    .Where(link => depths.TryGetValue(link.SourceId, out var sourceDepth) && sourceDepth == depth)
                    .GroupBy(link => link.SourceId, StringComparer.Ordinal)
                    .Select(group => group.Count())
                    .DefaultIfEmpty(0)
                    .Max();
                var terminalClearance = Math.Max(
                    settings.Layout.LinkPadding,
                    settings.Layout.ExposureTreeConnectorMinSegment);
                var required = terminalClearance * 2 +
                    Math.Max(0, fanoutLaneCount - 1) * settings.Layout.ParallelLaneSpacing +
                    settings.Layout.LinkPadding * 2;
                gaps[depth] = Math.Max(
                    Math.Max(settings.Layout.ExposureTreeMinVerticalSpacing, settings.Layout.VerticalSpacing),
                    required);
            }

            return gaps;
        }

        private static void CenterExposureParentsOverChildren(
            RenderGraph graph,
            Dictionary<string, NodeLayout> nodes)
        {
            var nodeOrder = graph.Nodes.ToDictionary(node => node.Id, node => node.Order, StringComparer.Ordinal);
            var placementParentByTarget = graph.PlacementParentByNode.Count > 0
                ? graph.PlacementParentByNode
                : graph.Links
                .Where(link => nodes.ContainsKey(link.SourceId) && nodes.ContainsKey(link.TargetId))
                .GroupBy(link => link.TargetId, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => group
                        .OrderBy(link => nodeOrder[link.SourceId])
                        .ThenBy(link => link.Order)
                        .ThenBy(link => link.Id, StringComparer.Ordinal)
                        .First()
                        .SourceId,
                    StringComparer.Ordinal);

            foreach (var source in graph.Nodes
                .Where(node => nodes.ContainsKey(node.Id))
                .OrderByDescending(node => nodes[node.Id].Depth)
                .ThenBy(node => node.Id, StringComparer.Ordinal))
            {
                var children = graph.Links
                    .Where(link => string.Equals(link.SourceId, source.Id, StringComparison.Ordinal) &&
                        nodes.ContainsKey(link.TargetId) &&
                        nodes[link.TargetId].Depth > nodes[source.Id].Depth &&
                        placementParentByTarget.TryGetValue(link.TargetId, out var placementParentId) &&
                        string.Equals(placementParentId, source.Id, StringComparison.Ordinal))
                    .Select(link => nodes[link.TargetId].Rect)
                    .ToArray();
                if (children.Length == 0)
                {
                    continue;
                }

                var left = children.Min(child => child.X);
                var right = children.Max(child => child.Right);
                var current = nodes[source.Id];
                nodes[source.Id] = current with
                {
                    Rect = current.Rect with { X = left + (right - left - current.Rect.Width) / 2 }
                };
            }
        }

        private static SubtreeMeasure MeasureExposureSubtree(
            string nodeId,
            RenderGraph graph,
            DiagramSettings settings,
            IReadOnlyDictionary<string, int> widths,
            IReadOnlyDictionary<string, RenderLink[]> outgoing,
            HashSet<string> ancestors,
            int depth,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!ancestors.Add(nodeId))
            {
                return new SubtreeMeasure(widths[nodeId], settings.Layout.NodeHeight);
            }

            var childMeasures = outgoing.TryGetValue(nodeId, out var childLinks)
                ? childLinks
                    .Select(link => MeasureExposureSubtree(link.TargetId, graph, settings, widths, outgoing, new HashSet<string>(ancestors, StringComparer.Ordinal), depth + 1, cancellationToken))
                    .ToArray()
                : Array.Empty<SubtreeMeasure>();
            if (childMeasures.Length == 0)
            {
                return new SubtreeMeasure(widths[nodeId], settings.Layout.NodeHeight);
            }

            var gap = ExposureTreeHorizontalGap(settings, depth);
            var childrenWidth = childMeasures.Sum(measure => measure.Width) +
                Math.Max(0, childMeasures.Length - 1) * gap;
            var childrenHeight = childMeasures.Select(measure => measure.Height).DefaultIfEmpty(0).Max();
            return new SubtreeMeasure(
                Math.Max(widths[nodeId], childrenWidth),
                settings.Layout.NodeHeight + Math.Max(settings.Layout.ExposureTreeMinVerticalSpacing, settings.Layout.VerticalSpacing) + childrenHeight);
        }

        private static void PlaceExposureSubtree(
            string nodeId,
            int left,
            int top,
            int subtreeWidth,
            int depth,
            RenderGraph graph,
            DiagramSettings settings,
            IReadOnlyDictionary<string, int> widths,
            IReadOnlyDictionary<string, RenderLink[]> outgoing,
            IReadOnlyDictionary<string, RenderNode> nodeById,
            Dictionary<string, NodeLayout> result,
            HashSet<string> placed,
            HashSet<string> ancestors,
            IReadOnlyDictionary<int, int> depthGaps,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (placed.Contains(nodeId))
            {
                return;
            }

            if (!ancestors.Add(nodeId))
            {
                return;
            }

            var width = widths[nodeId];
            var x = left + Math.Max(0, (subtreeWidth - width) / 2);
            result[nodeId] = new NodeLayout(
                nodeById[nodeId],
                new Rect(x, top, width, settings.Layout.NodeHeight),
                depth,
                false);
            placed.Add(nodeId);

            if (!outgoing.TryGetValue(nodeId, out var childLinks) || childLinks.Length == 0)
            {
                return;
            }

            var gap = ExposureTreeHorizontalGap(settings, depth);
            var childMeasures = childLinks
                .Select(link => new
                {
                    Link = link,
                    Measure = MeasureExposureSubtree(link.TargetId, graph, settings, widths, outgoing, new HashSet<string>(ancestors, StringComparer.Ordinal), depth + 1, cancellationToken)
                })
                .ToArray();
            var rowWidth = childMeasures.Sum(child => child.Measure.Width) + Math.Max(0, childMeasures.Length - 1) * gap;
            var childLeft = left + Math.Max(0, (subtreeWidth - rowWidth) / 2);
            var childTop = top + settings.Layout.NodeHeight +
                (depthGaps.TryGetValue(depth, out var requiredGap)
                    ? requiredGap
                    : Math.Max(settings.Layout.ExposureTreeMinVerticalSpacing, settings.Layout.VerticalSpacing));

            foreach (var child in childMeasures)
            {
                cancellationToken.ThrowIfCancellationRequested();
                PlaceExposureSubtree(
                    child.Link.TargetId,
                    childLeft,
                    childTop,
                    child.Measure.Width,
                    depth + 1,
                    graph,
                    settings,
                    widths,
                    outgoing,
                    nodeById,
                    result,
                    placed,
                    new HashSet<string>(ancestors, StringComparer.Ordinal),
                    depthGaps,
                    cancellationToken);
                childLeft += child.Measure.Width + gap;
            }
        }

        private static int ExposureTreeHorizontalGap(DiagramSettings settings, int depth)
        {
            return Math.Max(
                settings.Layout.ExposureTreeMinHorizontalSpacing,
                settings.Layout.HorizontalSpacing +
                    settings.Layout.ExposureTreeHorizontalSpacingBonus -
                    depth * settings.Layout.ExposureTreeDepthSpacingReduction);
        }

        internal static void AlignBaselineNodes(DiagramSettings settings, Dictionary<string, NodeLayout> nodes)
        {
            var baselineNodes = nodes.Values
                .Where(node => !node.IsStandalone && IsBaselineNode(node.Node, settings.Layout.BaselineAlignmentPattern))
                .ToArray();
            if (baselineNodes.Length == 0)
            {
                return;
            }

            var baselineY = baselineNodes.Min(node => node.Rect.Y);
            foreach (var node in baselineNodes)
            {
                nodes[node.Node.Id] = node with { Rect = node.Rect with { Y = baselineY } };
            }
        }

        private static bool IsBaselineNode(RenderNode node, string? baselinePattern)
        {
            var pattern = string.IsNullOrWhiteSpace(baselinePattern)
                ? LayoutSettings.DefaultBaselineAlignmentPattern
                : baselinePattern;

            try
            {
                return Regex.IsMatch(node.Name, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) ||
                    Regex.IsMatch(node.FullName, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            }
            catch (ArgumentException)
            {
                return Regex.IsMatch(node.Name, LayoutSettings.DefaultBaselineAlignmentPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) ||
                    Regex.IsMatch(node.FullName, LayoutSettings.DefaultBaselineAlignmentPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            }
        }

        private static void PlaceExternalDependencyNodes(
            RenderGraph graph,
            DiagramSettings settings,
            Dictionary<string, NodeLayout> nodes)
        {
            foreach (var group in graph.Links
                .Where(link => nodes.ContainsKey(link.SourceId) &&
                    nodes.ContainsKey(link.TargetId) &&
                    nodes[link.TargetId].Node.IsExternal)
                .GroupBy(link => link.SourceId, StringComparer.Ordinal))
            {
                var source = nodes[group.Key];
                var links = group.OrderBy(link => link.Order).ThenBy(link => link.Id, StringComparer.Ordinal).ToArray();
                var totalWidth = links
                    .Select(link => nodes[link.TargetId].Rect.Width)
                    .Sum() + Math.Max(0, links.Length - 1) * settings.Layout.HorizontalSpacing;
                var x = source.Rect.CenterX - totalWidth / 2;
                var y = source.Rect.Bottom + settings.Layout.VerticalSpacing;

                foreach (var link in links)
                {
                    var target = nodes[link.TargetId];
                    nodes[link.TargetId] = target with
                    {
                        Rect = target.Rect with
                        {
                            X = x,
                            Y = y
                        },
                        Depth = source.Depth + 1
                    };
                    x += target.Rect.Width + settings.Layout.HorizontalSpacing;
                }
            }
        }

        private static void RelaxNonTopDepthAlignment(
            RenderGraph graph,
            DiagramSettings settings,
            Dictionary<string, NodeLayout> nodes)
        {
            var connectedNodes = nodes.Values
                .Where(node => !node.IsStandalone)
                .ToDictionary(node => node.Node.Id, StringComparer.Ordinal);

            foreach (var node in connectedNodes.Values
                .Where(node => node.Depth > 0 && !IsBaselineNode(node.Node, settings.Layout.BaselineAlignmentPattern))
                .OrderBy(node => node.Node.Order)
                .ThenBy(node => node.Node.Id, StringComparer.Ordinal))
            {
                var incidentLinkCount = graph.Links.Count(link =>
                    string.Equals(link.SourceId, node.Node.Id, StringComparison.Ordinal) ||
                    string.Equals(link.TargetId, node.Node.Id, StringComparison.Ordinal));
                var crossingLinkCount = graph.Links.Count(link =>
                    IsNonIncidentDepthCrossing(link, node, connectedNodes));
                var pressure = Math.Max(incidentLinkCount, crossingLinkCount);
                if (pressure <= 1)
                {
                    continue;
                }

                var offset = (pressure - 1) * settings.Layout.ParallelLaneSpacing;
                nodes[node.Node.Id] = node with { Rect = node.Rect with { Y = node.Rect.Y + offset } };
            }
        }

        private static bool IsNonIncidentDepthCrossing(
            RenderLink link,
            NodeLayout node,
            IReadOnlyDictionary<string, NodeLayout> nodes)
        {
            if (!nodes.TryGetValue(link.SourceId, out var source) ||
                !nodes.TryGetValue(link.TargetId, out var target) ||
                string.Equals(link.SourceId, node.Node.Id, StringComparison.Ordinal) ||
                string.Equals(link.TargetId, node.Node.Id, StringComparison.Ordinal))
            {
                return false;
            }

            var upperDepth = Math.Min(source.Depth, target.Depth);
            var lowerDepth = Math.Max(source.Depth, target.Depth);
            if (node.Depth <= upperDepth || node.Depth >= lowerDepth)
            {
                return false;
            }

            var corridorX = source.Rect.CenterX + (target.Rect.CenterX - source.Rect.CenterX) / 2;
            return corridorX > node.Rect.X && corridorX < node.Rect.Right;
        }

        private static void ResolveDepthBandVerticalOverlaps(
            DiagramSettings settings,
            Dictionary<string, NodeLayout> nodes)
        {
            var previousBottom = 0;
            foreach (var layer in nodes.Values
                .Where(node => !node.IsStandalone)
                .GroupBy(node => node.Depth)
                .OrderBy(group => group.Key))
            {
                var layerNodes = layer.ToArray();
                if (layer.Key > 0)
                {
                    var minTop = layerNodes.Min(node => node.Rect.Y);
                    var requiredTop = previousBottom + settings.Layout.VerticalSpacing;
                    if (minTop < requiredTop)
                    {
                        var shift = requiredTop - minTop;
                        foreach (var node in layerNodes)
                        {
                            nodes[node.Node.Id] = node with { Rect = node.Rect with { Y = node.Rect.Y + shift } };
                        }

                        layerNodes = layerNodes.Select(node => nodes[node.Node.Id]).ToArray();
                    }
                }

                previousBottom = layerNodes.Max(node => node.Rect.Bottom);
            }
        }

        private static void ReserveLinkCorridors(
            RenderGraph graph,
            DiagramSettings settings,
            Dictionary<string, NodeLayout> nodes)
        {
            var corridorClaims = graph.Links
                .Where(link => nodes.ContainsKey(link.SourceId) && nodes.ContainsKey(link.TargetId))
                .Select(link =>
                {
                    var source = nodes[link.SourceId];
                    var target = nodes[link.TargetId];
                    var corridorX = source.Rect.CenterX + (target.Rect.CenterX - source.Rect.CenterX) / 2;
                    return new
                    {
                        Link = link,
                        UpperDepth = Math.Min(source.Depth, target.Depth),
                        LowerDepth = Math.Max(source.Depth, target.Depth),
                        CorridorX = corridorX
                    };
                })
                .OrderBy(item => item.Link.Order)
                .ThenBy(item => item.Link.Id, StringComparer.Ordinal)
                .ToArray();

            foreach (var claim in corridorClaims)
            {
                for (var depth = claim.UpperDepth; depth <= claim.LowerDepth; depth++)
                {
                    ReserveCorridorOnLayer(settings, nodes, depth, claim.CorridorX);
                }
            }
        }

        private static void ReserveCorridorOnLayer(
            DiagramSettings settings,
            Dictionary<string, NodeLayout> nodes,
            int depth,
            int corridorX)
        {
            var clearance = settings.Layout.LinkPadding * 2 + settings.Layout.ParallelLaneSpacing;
            var layer = nodes.Values
                .Where(node => !node.IsStandalone && node.Depth == depth)
                .OrderBy(node => node.Rect.X)
                .ThenBy(node => node.Node.Order)
                .ToArray();

            foreach (var node in layer)
            {
                if (corridorX <= node.Rect.X - clearance || corridorX >= node.Rect.Right + clearance)
                {
                    continue;
                }

                var shift = node.Rect.Right + clearance - corridorX;
                ShiftLayerNodes(nodes, layer, node.Rect.X, shift);
                return;
            }
        }

        private static void ShiftLayerNodes(
            Dictionary<string, NodeLayout> nodes,
            IReadOnlyList<NodeLayout> layer,
            int fromX,
            int shift)
        {
            foreach (var node in layer.Where(item => item.Rect.X >= fromX))
            {
                nodes[node.Node.Id] = node with { Rect = node.Rect with { X = node.Rect.X + shift } };
            }
        }

        private static void CenterParentsOverChildren(
            RenderGraph graph,
            DiagramSettings settings,
            Dictionary<string, NodeLayout> nodes)
        {
            foreach (var source in graph.Nodes
                .Where(node => nodes.ContainsKey(node.Id))
                .OrderByDescending(node => nodes[node.Id].Depth))
            {
                var children = graph.Links
                    .Where(link => link.SourceId == source.Id && nodes[link.TargetId].Depth > nodes[source.Id].Depth)
                    .Select(link => nodes[link.TargetId].Rect)
                    .ToArray();
                if (children.Length == 0)
                {
                    continue;
                }

                var spanLeft = children.Min(child => child.X);
                var spanRight = children.Max(child => child.Right);
                var current = nodes[source.Id];
                var centeredX = spanLeft + (spanRight - spanLeft - current.Rect.Width) / 2;
                nodes[source.Id] = current with { Rect = current.Rect with { X = Math.Max(settings.Layout.ContainerPadding * 2, centeredX) } };
            }
        }

        private static void ResolveLayerOverlaps(DiagramSettings settings, Dictionary<string, NodeLayout> nodes)
        {
            foreach (var layer in nodes.Values
                .GroupBy(node => node.Depth)
                .OrderBy(group => group.Key))
            {
                var right = settings.Layout.ContainerPadding * 2;
                foreach (var node in layer.OrderBy(node => node.Rect.X).ThenBy(node => node.Node.Order).ToArray())
                {
                    if (node.Rect.X < right)
                    {
                        nodes[node.Node.Id] = node with { Rect = node.Rect with { X = right } };
                    }

                    right = nodes[node.Node.Id].Rect.Right + settings.Layout.HorizontalSpacing;
                }
            }
        }

        internal static Dictionary<string, ProjectLayout> PositionProjects(
            RenderGraph graph,
            DiagramSettings settings,
            IReadOnlyDictionary<string, NodeLayout> nodes)
        {
            var projects = new Dictionary<string, ProjectLayout>(StringComparer.Ordinal);
            foreach (var project in graph.Projects)
            {
                var projectNodes = nodes.Values
                    .Where(node => string.Equals(node.Node.ProjectId, project.Id, StringComparison.Ordinal))
                    .ToArray();
                if (projectNodes.Length == 0)
                {
                    continue;
                }

                var left = projectNodes.Min(node => node.Rect.X) - settings.Layout.ContainerPadding;
                var top = projectNodes.Min(node => node.Rect.Y) - settings.Layout.ProjectHeaderHeight - settings.Layout.ContainerPadding;
                var right = projectNodes.Max(node => node.Rect.Right) + settings.Layout.ContainerPadding;
                var bottom = projectNodes.Max(node => node.Rect.Bottom) + settings.Layout.ContainerPadding;
                projects[project.Id] = new ProjectLayout(project, new Rect(left, top, right - left, bottom - top));
            }

            return projects;
        }

        private static int NodeY(int depth, DiagramSettings settings, IReadOnlyDictionary<int, int> depthOffsets) =>
            settings.Layout.ContainerPadding * 2 + depth * (settings.Layout.NodeHeight + settings.Layout.VerticalSpacing) +
            (depthOffsets.TryGetValue(depth, out var offset) ? offset : 0);
}

internal sealed record PlacementResult(
    LayoutHierarchy Hierarchy,
    IReadOnlyDictionary<string, NodeLayout> Nodes,
    IReadOnlyDictionary<string, ProjectLayout> Projects);
