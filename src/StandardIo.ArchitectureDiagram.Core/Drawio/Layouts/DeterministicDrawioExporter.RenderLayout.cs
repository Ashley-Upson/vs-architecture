using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using StandardIo.ArchitectureDiagram.Core.Graph;
using StandardIo.ArchitectureDiagram.Core.Settings;

namespace StandardIo.ArchitectureDiagram.Core.Drawio;

public sealed partial class DeterministicDrawioExporter
{
    private sealed class RenderLayout
    {
        private RenderLayout(
            RenderGraph graph,
            IReadOnlyDictionary<string, NodeLayout> nodes,
            IReadOnlyDictionary<string, ProjectLayout> projects,
            IReadOnlyDictionary<string, LinkLayout> links)
        {
            Graph = graph;
            Nodes = nodes;
            Projects = projects;
            Links = links;
        }

        public RenderGraph Graph { get; }

        public IReadOnlyDictionary<string, NodeLayout> Nodes { get; }

        public IReadOnlyDictionary<string, ProjectLayout> Projects { get; }

        public IReadOnlyDictionary<string, LinkLayout> Links { get; }

        public static RenderLayout Build(RenderGraph graph, DiagramSettings settings)
        {
            var depths = CalculateDepths(graph);
            var depthOffsets = CalculateDepthOffsets(graph, settings, depths);
            var widths = CalculateWidths(graph, settings);
            var nodes = PositionNodes(graph, settings, depths, depthOffsets, widths);
            var projects = PositionProjects(graph, settings, nodes);
            var links = PositionLinks(graph, settings, nodes);

            return new RenderLayout(graph, nodes, projects, links);
        }

        private static Dictionary<string, int> CalculateDepths(RenderGraph graph)
        {
            var components = StrongComponents(graph);
            var componentByNode = components
                .SelectMany((component, index) => component.Select(nodeId => new { nodeId, index }))
                .ToDictionary(item => item.nodeId, item => item.index, StringComparer.Ordinal);
            var outgoing = components
                .Select(_ => new HashSet<int>())
                .ToArray();
            var incoming = components
                .Select(_ => 0)
                .ToArray();

            foreach (var link in graph.Links)
            {
                var source = componentByNode[link.SourceId];
                var target = componentByNode[link.TargetId];
                if (source == target || !outgoing[source].Add(target))
                {
                    continue;
                }

                incoming[target]++;
            }

            var incomingForTopologicalSort = incoming.ToArray();
            var queue = new Queue<int>(incoming
                .Select((count, index) => new { count, index })
                .Where(item => item.count == 0)
                .Select(item => item.index));
            var topologicalOrder = new List<int>();

            while (queue.Count > 0)
            {
                var component = queue.Dequeue();
                topologicalOrder.Add(component);
                foreach (var target in outgoing[component])
                {
                    incomingForTopologicalSort[target]--;
                    if (incomingForTopologicalSort[target] == 0)
                    {
                        queue.Enqueue(target);
                    }
                }
            }

            var distanceToExitByComponent = components
                .Select((_, index) => new { index, distance = 0 })
                .ToDictionary(item => item.index, item => item.distance);

            foreach (var component in topologicalOrder.AsEnumerable().Reverse())
            {
                if (outgoing[component].Count == 0)
                {
                    continue;
                }

                distanceToExitByComponent[component] = outgoing[component]
                    .Select(target => distanceToExitByComponent[target] + 1)
                    .Max();
            }

            var maxDistanceToExit = distanceToExitByComponent.Values.DefaultIfEmpty(0).Max();
            var depthByComponent = distanceToExitByComponent.ToDictionary(
                item => item.Key,
                item => maxDistanceToExit - item.Value);

            return graph.Nodes.ToDictionary(
                node => node.Id,
                node => depthByComponent[componentByNode[node.Id]],
                StringComparer.Ordinal);
        }

        private static IReadOnlyList<IReadOnlyList<string>> StrongComponents(RenderGraph graph)
        {
            var outgoing = graph.Nodes.ToDictionary(
                node => node.Id,
                node => graph.Links.Where(link => link.SourceId == node.Id).Select(link => link.TargetId).ToArray(),
                StringComparer.Ordinal);
            var index = 0;
            var stack = new Stack<string>();
            var onStack = new HashSet<string>(StringComparer.Ordinal);
            var indices = new Dictionary<string, int>(StringComparer.Ordinal);
            var lowLinks = new Dictionary<string, int>(StringComparer.Ordinal);
            var components = new List<IReadOnlyList<string>>();

            foreach (var node in graph.Nodes)
            {
                if (!indices.ContainsKey(node.Id))
                {
                    Visit(node.Id);
                }
            }

            return components;

            void Visit(string nodeId)
            {
                indices[nodeId] = index;
                lowLinks[nodeId] = index;
                index++;
                stack.Push(nodeId);
                onStack.Add(nodeId);

                foreach (var targetId in outgoing[nodeId])
                {
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

                if (lowLinks[nodeId] != indices[nodeId])
                {
                    return;
                }

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
                    graph.Nodes.Single(node => node.Id == left).Order.CompareTo(graph.Nodes.Single(node => node.Id == right).Order));
                components.Add(component);
            }
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
            IReadOnlyDictionary<string, int> widths)
        {
            if (graph.Nodes.Count >= settings.Layout.ExposureTreeLayoutThreshold &&
                graph.Nodes.Any(node => node.Id.StartsWith(ExposureTreeIdPrefix, StringComparison.Ordinal)))
            {
                return PositionExposureTrees(graph, settings, depths, widths);
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
                var x = settings.Layout.ContainerPadding * 2;
                foreach (var node in layer.OrderBy(node => node.Order))
                {
                    result[node.Id] = new NodeLayout(
                        node,
                        new Rect(x, NodeY(layer.Key, settings, depthOffsets), widths[node.Id], settings.Layout.NodeHeight),
                        layer.Key,
                        false);
                    x += widths[node.Id] + settings.Layout.HorizontalSpacing;
                }
            }

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
            IReadOnlyDictionary<string, int> widths)
        {
            var result = new Dictionary<string, NodeLayout>(StringComparer.Ordinal);
            var incoming = graph.Nodes.ToDictionary(node => node.Id, _ => 0, StringComparer.Ordinal);
            var outgoing = graph.Links
                .GroupBy(link => link.SourceId, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => group
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
                .ToArray();
            var placed = new HashSet<string>(StringComparer.Ordinal);
            var cursorX = settings.Layout.ContainerPadding * 2;
            var treeGap = settings.Layout.NodeWidth + settings.Layout.StandaloneGroupSpacing;

            foreach (var root in roots)
            {
                var subtree = MeasureExposureSubtree(root.Id, graph, settings, widths, outgoing, new HashSet<string>(StringComparer.Ordinal), 0);
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
                    new HashSet<string>(StringComparer.Ordinal));
                cursorX += subtree.Width + treeGap;
            }

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

        private static SubtreeMeasure MeasureExposureSubtree(
            string nodeId,
            RenderGraph graph,
            DiagramSettings settings,
            IReadOnlyDictionary<string, int> widths,
            IReadOnlyDictionary<string, RenderLink[]> outgoing,
            HashSet<string> ancestors,
            int depth)
        {
            if (!ancestors.Add(nodeId))
            {
                return new SubtreeMeasure(widths[nodeId], settings.Layout.NodeHeight);
            }

            var childMeasures = outgoing.TryGetValue(nodeId, out var childLinks)
                ? childLinks
                    .Select(link => MeasureExposureSubtree(link.TargetId, graph, settings, widths, outgoing, new HashSet<string>(ancestors, StringComparer.Ordinal), depth + 1))
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
            HashSet<string> ancestors)
        {
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
                    Measure = MeasureExposureSubtree(link.TargetId, graph, settings, widths, outgoing, new HashSet<string>(ancestors, StringComparer.Ordinal), depth + 1)
                })
                .ToArray();
            var rowWidth = childMeasures.Sum(child => child.Measure.Width) + Math.Max(0, childMeasures.Length - 1) * gap;
            var childLeft = left + Math.Max(0, (subtreeWidth - rowWidth) / 2);
            var childTop = top + settings.Layout.NodeHeight + Math.Max(settings.Layout.ExposureTreeMinVerticalSpacing, settings.Layout.VerticalSpacing);

            foreach (var child in childMeasures)
            {
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
                    new HashSet<string>(ancestors, StringComparer.Ordinal));
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

        private static void AlignBaselineNodes(DiagramSettings settings, Dictionary<string, NodeLayout> nodes)
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
                var links = group.OrderBy(link => link.Order).ToArray();
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
                .OrderBy(node => node.Node.Order))
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

        private static Dictionary<string, ProjectLayout> PositionProjects(
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

        private static Dictionary<string, LinkLayout> PositionLinks(
            RenderGraph graph,
            DiagramSettings settings,
            IReadOnlyDictionary<string, NodeLayout> nodes)
        {
            if (graph.Nodes.Any(node => node.Id.StartsWith(ExposureTreeIdPrefix, StringComparison.Ordinal)))
            {
                return PositionExposureTreeLinks(graph, settings, nodes);
            }

            var result = new Dictionary<string, LinkLayout>(StringComparer.Ordinal);
            var sourceGroups = graph.Links.GroupBy(link => link.SourceId).ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
            var targetGroups = graph.Links.GroupBy(link => link.TargetId).ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
            var usedCorners = new HashSet<string>(StringComparer.Ordinal);
            var occupiedSegments = new List<Segment>();
            var routeLaneIndexes = CalculateRouteLaneIndexes(graph, nodes);

            foreach (var link in graph.Links.OrderBy(link => link.Order))
            {
                var source = nodes[link.SourceId].Rect;
                var target = nodes[link.TargetId].Rect;
                var sourceOffset = PortOffset(sourceGroups[link.SourceId], link, settings.Layout.EdgePortSpacing);
                var targetOffset = PortOffset(targetGroups[link.TargetId], link, settings.Layout.EdgePortSpacing);
                var sourcePoint = new Point(source.CenterX + sourceOffset, source.Bottom);
                var targetPoint = new Point(target.CenterX + targetOffset, target.Y);
                var obstacles = RoutingObstacles(nodes, link, settings.Layout.LinkPadding);
                var routeLaneIndex = routeLaneIndexes.TryGetValue(link.Id, out var index) ? index : 0;
                var route = BuildRoute(sourcePoint, targetPoint, obstacles, settings, routeLaneIndex, usedCorners, occupiedSegments);
                occupiedSegments.AddRange(Segments(route));

                result[link.Id] = new LinkLayout(
                    link,
                    sourcePoint,
                    targetPoint,
                    route,
                    Ratio(sourcePoint.X, source),
                    Ratio(targetPoint.X, target));
            }

            return result;
        }

        private static Dictionary<string, LinkLayout> PositionExposureTreeLinks(
            RenderGraph graph,
            DiagramSettings settings,
            IReadOnlyDictionary<string, NodeLayout> nodes)
        {
            var result = new Dictionary<string, LinkLayout>(StringComparer.Ordinal);
            foreach (var link in graph.Links.OrderBy(link => link.Order))
            {
                if (!nodes.TryGetValue(link.SourceId, out var sourceLayout) ||
                    !nodes.TryGetValue(link.TargetId, out var targetLayout))
                {
                    continue;
                }

                var source = sourceLayout.Rect;
                var target = targetLayout.Rect;
                var sourcePoint = new Point(source.CenterX, source.Bottom);
                var targetPoint = new Point(target.CenterX, target.Y);
                var points = BuildExposureTreeRoute(
                    sourcePoint,
                    targetPoint,
                    RoutingObstacles(nodes, link, settings.Layout.LinkPadding),
                    settings);

                result[link.Id] = new LinkLayout(
                    link,
                    sourcePoint,
                    targetPoint,
                    points,
                    Ratio(sourcePoint.X, source),
                    Ratio(targetPoint.X, target));
            }

            return result;
        }

        private static IReadOnlyList<Point> BuildExposureTreeRoute(
            Point sourcePoint,
            Point targetPoint,
            IReadOnlyList<Rect> obstacles,
            DiagramSettings settings)
        {
            var direct = DirectExposureTreeRoute(sourcePoint, targetPoint, settings.Layout.ExposureTreeConnectorMinSegment);
            if (!CrossesNode(WithTerminals(sourcePoint, direct, targetPoint), obstacles))
            {
                return direct;
            }

            var crossedObstacles = CrossedObstacles(WithTerminals(sourcePoint, direct, targetPoint), obstacles).ToArray();
            if (crossedObstacles.Length == 0)
            {
                return direct;
            }

            var clearance = Math.Max(
                settings.Layout.ExposureTreeConnectorMinSegment,
                settings.Layout.LinkPadding * settings.Layout.ExposureTreeConnectorClearanceMultiplier);
            var blocked = new HashSet<Rect>(crossedObstacles);
            IReadOnlyList<Point> best = direct;
            for (var attempt = 0; attempt < settings.Layout.ExposureTreeConnectorDetourAttempts; attempt++)
            {
                var topY = Math.Max(sourcePoint.Y + clearance, blocked.Min(obstacle => obstacle.Y) - clearance);
                var bottomY = Math.Min(targetPoint.Y - clearance, blocked.Max(obstacle => obstacle.Bottom) + clearance);
                if (bottomY <= topY)
                {
                    bottomY = topY + clearance;
                }

                var leftX = blocked.Min(obstacle => obstacle.X) - clearance;
                var rightX = blocked.Max(obstacle => obstacle.Right) + clearance;
                var candidates = new[]
                {
                    SideExposureTreeRoute(sourcePoint, targetPoint, leftX, topY, bottomY),
                    SideExposureTreeRoute(sourcePoint, targetPoint, rightX, topY, bottomY)
                };

                best = candidates
                    .OrderBy(route => CrossedObstacles(WithTerminals(sourcePoint, route, targetPoint), obstacles).Count())
                    .ThenBy(route => RouteScore(WithTerminals(sourcePoint, route, targetPoint), sourcePoint, targetPoint))
                    .First();
                var remaining = CrossedObstacles(WithTerminals(sourcePoint, best, targetPoint), obstacles).ToArray();
                if (remaining.Length == 0)
                {
                    return best;
                }

                foreach (var obstacle in remaining)
                {
                    blocked.Add(obstacle);
                }
            }

            return best;
        }

        private static IReadOnlyList<Point> DirectExposureTreeRoute(Point sourcePoint, Point targetPoint, int minSegment)
        {
            if (sourcePoint.X == targetPoint.X)
            {
                return Array.Empty<Point>();
            }

            var midY = sourcePoint.Y + Math.Max(minSegment, (targetPoint.Y - sourcePoint.Y) / 2);
            return new[]
            {
                new Point(sourcePoint.X, midY),
                new Point(targetPoint.X, midY)
            };
        }

        private static IReadOnlyList<Point> SideExposureTreeRoute(
            Point sourcePoint,
            Point targetPoint,
            int sideX,
            int topY,
            int bottomY)
        {
            return new[]
            {
                new Point(sourcePoint.X, topY),
                new Point(sideX, topY),
                new Point(sideX, bottomY),
                new Point(targetPoint.X, bottomY)
            };
        }

        private static List<Point> WithTerminals(
            Point sourcePoint,
            IReadOnlyList<Point> points,
            Point targetPoint)
        {
            var route = new List<Point> { sourcePoint };
            route.AddRange(points);
            route.Add(targetPoint);
            return route;
        }

        private static IEnumerable<Rect> CrossedObstacles(
            IReadOnlyList<Point> route,
            IReadOnlyList<Rect> obstacles)
        {
            return obstacles.Where(obstacle => Segments(route).Any(segment => segment.Intersects(obstacle)));
        }

        private static Rect[] RoutingObstacles(
            IReadOnlyDictionary<string, NodeLayout> nodes,
            RenderLink link,
            int padding)
        {
            return nodes.Values
                .Where(node => !node.IsStandalone &&
                    !string.Equals(node.Node.Id, link.SourceId, StringComparison.Ordinal) &&
                    !string.Equals(node.Node.Id, link.TargetId, StringComparison.Ordinal))
                .Select(node => node.Rect.Inflate(padding))
                .ToArray();
        }

        private static Dictionary<string, int> CalculateRouteLaneIndexes(
            RenderGraph graph,
            IReadOnlyDictionary<string, NodeLayout> nodes)
        {
            var sourceIndexes = graph.Links
                .Where(link => nodes.ContainsKey(link.SourceId) && nodes.ContainsKey(link.TargetId))
                .GroupBy(link => link.SourceId, StringComparer.Ordinal)
                .SelectMany(group => group
                    .OrderBy(link => link.Order)
                    .Select((link, index) => new { link.Id, Index = index }))
                .ToDictionary(item => item.Id, item => item.Index, StringComparer.Ordinal);

            var targetIndexes = graph.Links
                .Where(link => nodes.ContainsKey(link.SourceId) && nodes.ContainsKey(link.TargetId))
                .GroupBy(link => link.TargetId, StringComparer.Ordinal)
                .SelectMany(group => group
                    .OrderBy(link => link.Order)
                    .Select((link, index) => new { link.Id, Index = index }))
                .ToDictionary(item => item.Id, item => item.Index, StringComparer.Ordinal);

            return graph.Links
                .Where(link => nodes.ContainsKey(link.SourceId) && nodes.ContainsKey(link.TargetId))
                .ToDictionary(
                    link => link.Id,
                    link => Math.Max(sourceIndexes[link.Id], targetIndexes[link.Id]),
                    StringComparer.Ordinal);
        }

        private static IReadOnlyList<Point> BuildRoute(
            Point sourcePoint,
            Point targetPoint,
            IReadOnlyList<Rect> obstacles,
            DiagramSettings settings,
            int laneIndex,
            HashSet<string> usedCorners,
            List<Segment> occupiedSegments)
        {
            var sourceExit = new Point(sourcePoint.X, sourcePoint.Y + settings.Layout.LinkPadding);
            var targetEntry = new Point(targetPoint.X, targetPoint.Y - settings.Layout.LinkPadding);
            var laneOffset = laneIndex * settings.Layout.ParallelLaneSpacing;
            var points = SelectBestRoute(sourceExit, targetEntry, obstacles, settings, laneOffset);

            points = SeparateOverlappingCorners(points, usedCorners, settings.Layout.ParallelLaneSpacing);
            points = SeparateParallelSegments(points, occupiedSegments, settings.Layout.ParallelLaneSpacing);
            if (CrossesNode(points, obstacles))
            {
                points = SelectBestRoute(sourceExit, targetEntry, obstacles, settings, laneOffset + settings.Layout.ParallelLaneSpacing);
            }

            return Simplify(points);
        }

        private static List<Point> SelectBestRoute(
            Point sourceExit,
            Point targetEntry,
            IReadOnlyList<Rect> obstacles,
            DiagramSettings settings,
            int laneOffset)
        {
            var candidates = BuildRouteCandidates(sourceExit, targetEntry, obstacles, settings, laneOffset)
                .Select(Simplify)
                .Where(route => !CrossesNode(route, obstacles))
                .OrderBy(route => RouteScore(route, sourceExit, targetEntry))
                .ToArray();

            return candidates.FirstOrDefault()
                ?? BuildOutsideRoute(sourceExit, targetEntry, obstacles, settings, laneOffset);
        }

        private static IEnumerable<List<Point>> BuildRouteCandidates(
            Point sourceExit,
            Point targetEntry,
            IReadOnlyList<Rect> obstacles,
            DiagramSettings settings,
            int laneOffset)
        {
            var midY = sourceExit.Y <= targetEntry.Y
                ? sourceExit.Y + (targetEntry.Y - sourceExit.Y) / 2 + laneOffset
                : sourceExit.Y + settings.Layout.VerticalSpacing + laneOffset;

            yield return new List<Point>
            {
                sourceExit,
                new(sourceExit.X, midY),
                new(targetEntry.X, midY),
                targetEntry
            };

            yield return new List<Point>
            {
                sourceExit,
                new(sourceExit.X, sourceExit.Y + laneOffset),
                new(targetEntry.X, sourceExit.Y + laneOffset),
                targetEntry
            };

            yield return new List<Point>
            {
                sourceExit,
                new(sourceExit.X, targetEntry.Y - laneOffset),
                new(targetEntry.X, targetEntry.Y - laneOffset),
                targetEntry
            };

            foreach (var corridorX in CorridorCandidates(sourceExit, targetEntry, obstacles, settings, laneOffset))
            {
                yield return new List<Point>
                {
                    sourceExit,
                    new(corridorX, sourceExit.Y),
                    new(corridorX, targetEntry.Y),
                    targetEntry
                };

                yield return new List<Point>
                {
                    sourceExit,
                    new(sourceExit.X, midY),
                    new(corridorX, midY),
                    new(corridorX, targetEntry.Y),
                    targetEntry
                };
            }
        }

        private static int RouteScore(IReadOnlyList<Point> route, Point sourceExit, Point targetEntry)
        {
            var length = 0;
            for (var index = 0; index < route.Count - 1; index++)
            {
                length += Math.Abs(route[index].X - route[index + 1].X) +
                    Math.Abs(route[index].Y - route[index + 1].Y);
            }

            var localLeft = Math.Min(sourceExit.X, targetEntry.X);
            var localRight = Math.Max(sourceExit.X, targetEntry.X);
            var routeLeft = route.Min(point => point.X);
            var routeRight = route.Max(point => point.X);
            var escapePenalty = Math.Max(0, localLeft - routeLeft) + Math.Max(0, routeRight - localRight);
            var cornerPenalty = Math.Max(0, route.Count - 4) * 50;
            return length + escapePenalty * 10 + cornerPenalty;
        }

        private static IEnumerable<int> CorridorCandidates(
            Point sourceExit,
            Point targetEntry,
            IReadOnlyList<Rect> obstacles,
            DiagramSettings settings,
            int laneOffset)
        {
            var desired = sourceExit.X + (targetEntry.X - sourceExit.X) / 2;
            var minX = Math.Min(sourceExit.X, targetEntry.X);
            var maxX = Math.Max(sourceExit.X, targetEntry.X);
            var padding = settings.Layout.LinkPadding + Math.Abs(laneOffset);
            var sorted = obstacles.OrderBy(obstacle => obstacle.X).ToArray();

            return sorted
                .Zip(sorted.Skip(1), (left, right) => new { Left = left.Right + padding, Right = right.X - padding })
                .Where(gap => gap.Right > gap.Left)
                .Select(gap => gap.Left + (gap.Right - gap.Left) / 2)
                .Where(candidate => candidate >= minX - settings.Layout.HorizontalSpacing &&
                    candidate <= maxX + settings.Layout.HorizontalSpacing)
                .Distinct()
                .OrderBy(candidate => Math.Abs(candidate - desired))
                .ThenBy(candidate => candidate);
        }

        private static List<Point> BuildOutsideRoute(
            Point sourceExit,
            Point targetEntry,
            IReadOnlyList<Rect> obstacles,
            DiagramSettings settings,
            int laneOffset)
        {
            var sideX = obstacles
                .Select(obstacle => obstacle.Right)
                .DefaultIfEmpty(Math.Max(sourceExit.X, targetEntry.X))
                .Max() + settings.Layout.HorizontalSpacing + Math.Abs(laneOffset);
            return new List<Point>
            {
                sourceExit,
                new(sourceExit.X, sourceExit.Y + settings.Layout.LinkPadding + laneOffset),
                new(sideX, sourceExit.Y + settings.Layout.LinkPadding + laneOffset),
                new(sideX, targetEntry.Y - settings.Layout.LinkPadding - laneOffset),
                new(targetEntry.X, targetEntry.Y - settings.Layout.LinkPadding - laneOffset),
                targetEntry
            };
        }

        private static List<Point> SeparateOverlappingCorners(List<Point> points, HashSet<string> usedCorners, int spacing)
        {
            if (points.Count <= 2)
            {
                return points;
            }

            var result = new List<Point> { points[0] };
            for (var index = 1; index < points.Count - 1; index++)
            {
                var point = points[index];
                var adjusted = point;
                while (!usedCorners.Add($"{adjusted.X}:{adjusted.Y}"))
                {
                    adjusted = adjusted with { Y = adjusted.Y + spacing };
                }

                result.Add(adjusted);
            }

            result.Add(points[points.Count - 1]);
            return result;
        }

        private static List<Point> SeparateParallelSegments(List<Point> points, List<Segment> occupiedSegments, int spacing)
        {
            for (var index = 1; index < points.Count - 2; index++)
            {
                var segment = new Segment(points[index], points[index + 1]);
                if (!occupiedSegments.Any(occupied => ParallelOverlap(segment, occupied, spacing)))
                {
                    continue;
                }

                if (segment.IsHorizontal)
                {
                    points[index] = points[index] with { Y = points[index].Y + spacing };
                    points[index + 1] = points[index + 1] with { Y = points[index + 1].Y + spacing };
                }
                else if (segment.IsVertical)
                {
                    points[index] = points[index] with { X = points[index].X + spacing };
                    points[index + 1] = points[index + 1] with { X = points[index + 1].X + spacing };
                }
            }

            return points;
        }

        private static bool ParallelOverlap(Segment left, Segment right, int spacing)
        {
            if (left.IsHorizontal && right.IsHorizontal && Math.Abs(left.Start.Y - right.Start.Y) < spacing)
            {
                return RangesOverlap(left.Start.X, left.End.X, right.Start.X, right.End.X);
            }

            if (left.IsVertical && right.IsVertical && Math.Abs(left.Start.X - right.Start.X) < spacing)
            {
                return RangesOverlap(left.Start.Y, left.End.Y, right.Start.Y, right.End.Y);
            }

            return false;
        }

        private static bool RangesOverlap(int firstStart, int firstEnd, int secondStart, int secondEnd)
        {
            var firstMin = Math.Min(firstStart, firstEnd);
            var firstMax = Math.Max(firstStart, firstEnd);
            var secondMin = Math.Min(secondStart, secondEnd);
            var secondMax = Math.Max(secondStart, secondEnd);
            return firstMin <= secondMax && secondMin <= firstMax;
        }

        private static bool CrossesNode(IReadOnlyList<Point> points, IReadOnlyList<Rect> obstacles)
        {
            return Segments(points).Any(segment => obstacles.Any(obstacle => segment.Intersects(obstacle)));
        }

        private static IEnumerable<Segment> Segments(IReadOnlyList<Point> points)
        {
            for (var index = 0; index < points.Count - 1; index++)
            {
                yield return new Segment(points[index], points[index + 1]);
            }
        }

        private static List<Point> Simplify(List<Point> points)
        {
            var result = new List<Point>();
            foreach (var point in points)
            {
                if (result.Count > 0 && result[result.Count - 1] == point)
                {
                    continue;
                }

                if (result.Count >= 2)
                {
                    var previous = result[result.Count - 1];
                    var beforePrevious = result[result.Count - 2];
                    if ((beforePrevious.X == previous.X && previous.X == point.X) ||
                        (beforePrevious.Y == previous.Y && previous.Y == point.Y))
                    {
                        result[result.Count - 1] = point;
                        continue;
                    }
                }

                result.Add(point);
            }

            return result;
        }

        private static int PortOffset(IReadOnlyList<RenderLink> group, RenderLink link, int spacing)
        {
            var ordered = group.OrderBy(item => item.Order).ToArray();
            var index = Array.FindIndex(ordered, item => item.Id == link.Id);
            var center = (ordered.Length - 1) / 2.0;
            return (int)Math.Round((index - center) * spacing);
        }

        private static double Ratio(int x, Rect rect)
        {
            return Math.Max(0, Math.Min(1, (x - rect.X) / (double)rect.Width));
        }

        private static int NodeY(int depth, DiagramSettings settings, IReadOnlyDictionary<int, int> depthOffsets)
        {
            return settings.Layout.ContainerPadding * 2 +
                depth * (settings.Layout.NodeHeight + settings.Layout.VerticalSpacing) +
                (depthOffsets.TryGetValue(depth, out var offset) ? offset : 0);
        }
    }

}
