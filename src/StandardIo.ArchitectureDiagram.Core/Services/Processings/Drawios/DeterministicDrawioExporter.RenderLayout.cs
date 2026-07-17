using System;
using System.Collections.Generic;
using System.Globalization;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using StandardIo.ArchitectureDiagram.Core.Models;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal sealed class RenderLayout
{
    private const int TextWidth = 8;
    private const string ExposureTreeIdPrefix = "tree_";
    private const long MaximumGlobalPathSelectionEstimatedWork = 2_000_000;

        private RenderLayout(
            RenderGraph graph,
            IReadOnlyDictionary<string, NodeLayout> nodes,
            IReadOnlyDictionary<string, ProjectLayout> projects,
            IReadOnlyDictionary<string, LinkLayout> links,
            CorridorPathSelectionResult? pathSelection,
            RegionalPathSelectionResult? regionalPathSelection,
            EdgeTraversalCompilation traversals,
            TraceabilityValidationResult traceability,
            CorridorObservation corridors,
            CorridorLaneAllocation lanes,
            TraceabilityValidationResult? preRepairTraceability = null,
            IReadOnlyList<RouteRepairAttempt>? repairAttempts = null,
            int repairWorkUsed = 0,
            bool repairBudgetExhausted = false,
            string repairRunReason = "RepairableFindingsPresent",
            IReadOnlyList<PipelineStageMetric>? stageTimings = null,
            int routesInvalidated = 0,
            long routePairsRevalidated = 0,
            int corridorRebuildCount = 0,
            int capacityFailureCount = 0,
            int capacityExpansionCount = 0)
        {
            Graph = graph;
            Nodes = nodes;
            Projects = projects;
            Links = links;
            PathSelection = pathSelection;
            RegionalPathSelection = regionalPathSelection;
            Traversals = traversals;
            Traceability = traceability;
            Corridors = corridors;
            Lanes = lanes;
            PreRepairTraceability = preRepairTraceability ?? traceability;
            RepairAttempts = repairAttempts ?? Array.Empty<RouteRepairAttempt>();
            RepairWorkUsed = repairWorkUsed;
            RepairBudgetExhausted = repairBudgetExhausted;
            RepairRunReason = repairRunReason;
            StageTimings = stageTimings ?? Array.Empty<PipelineStageMetric>();
            RoutesInvalidated = routesInvalidated;
            RoutePairsRevalidated = routePairsRevalidated;
            CorridorRebuildCount = corridorRebuildCount;
            CapacityFailureCount = capacityFailureCount;
            CapacityExpansionCount = capacityExpansionCount;
        }

        public RenderGraph Graph { get; }

        public IReadOnlyDictionary<string, NodeLayout> Nodes { get; }

        public IReadOnlyDictionary<string, ProjectLayout> Projects { get; }

        public IReadOnlyDictionary<string, LinkLayout> Links { get; }

        public CorridorPathSelectionResult? PathSelection { get; }

        public RegionalPathSelectionResult? RegionalPathSelection { get; }

        public EdgeTraversalCompilation Traversals { get; }

        public TraceabilityValidationResult Traceability { get; }

        public CorridorObservation Corridors { get; }

        public CorridorLaneAllocation Lanes { get; }

        public TraceabilityValidationResult PreRepairTraceability { get; }

        public IReadOnlyList<RouteRepairAttempt> RepairAttempts { get; }

        public int RepairWorkUsed { get; }

        public bool RepairBudgetExhausted { get; }

        public string RepairRunReason { get; }

        public IReadOnlyList<PipelineStageMetric> StageTimings { get; }

        public int RoutesInvalidated { get; }

        public long RoutePairsRevalidated { get; }

        public int CorridorRebuildCount { get; }

        public int CapacityFailureCount { get; }

        public int CapacityExpansionCount { get; }

        public RenderLayout WithProjects(IReadOnlyDictionary<string, ProjectLayout> projects) =>
            new(Graph, Nodes, projects, Links, PathSelection, RegionalPathSelection, Traversals, Traceability, Corridors, Lanes,
                PreRepairTraceability, RepairAttempts, RepairWorkUsed, RepairBudgetExhausted, RepairRunReason, StageTimings,
                RoutesInvalidated, RoutePairsRevalidated, CorridorRebuildCount, CapacityFailureCount, CapacityExpansionCount);

        public static RenderLayout Build(RenderGraph graph, DiagramSettings settings)
        {
            var timings = new List<PipelineStageMetric>();
            T Measure<T>(string stage, Func<T> action)
            {
                var timer = Stopwatch.StartNew();
                var result = action();
                timer.Stop();
                timings.Add(new PipelineStageMetric(stage, timer.ElapsedMilliseconds,
                    timings.Count(item => item.Stage == stage) + 1));
                return result;
            }

            var placement = Measure("node placement", () =>
            {
                var depths = CalculateDepths(graph);
                var depthOffsets = CalculateDepthOffsets(graph, settings, depths);
                var widths = CalculateWidths(graph, settings);
                var positionedNodes = PositionNodes(graph, settings, depths, depthOffsets, widths);
                return (Nodes: positionedNodes, Projects: PositionProjects(graph, settings, positionedNodes));
            });
            var nodes = placement.Nodes;
            var projects = placement.Projects;
            var positionedLinks = Measure("candidate construction and selection", () => PositionLinks(graph, settings, nodes));
            var provisionalLinks = positionedLinks.Links;
            var corridors = Measure("corridor observation", () => CorridorObserver.Observe(
                nodes,
                provisionalLinks,
                settings.Layout.ParallelLaneSpacing,
                settings.Layout.LinkPadding));
            var lanes = Measure("lane allocation", () => CorridorLaneAllocator.Allocate(corridors));
            var capacityFailureCount = lanes.CapacityRequests?.Count ?? 0;
            var capacityAttempts = new List<RouteRepairAttempt>();
            for (var capacityPass = 0; capacityPass < 2 && lanes.CapacityRequests?.Count > 0; capacityPass++)
            {
                var capacityExpansion = Measure("capacity requests", () => ExpandLayersForCapacityRequests(
                    nodes,
                    provisionalLinks,
                    corridors,
                    lanes,
                    settings));
                capacityAttempts.AddRange(capacityExpansion.Attempts);
                if (!capacityExpansion.Changed)
                {
                    break;
                }

                nodes = capacityExpansion.Nodes;
                projects = PositionProjects(graph, settings, nodes);
                positionedLinks = Measure("candidate construction and selection", () => PositionLinks(graph, settings, nodes));
                provisionalLinks = positionedLinks.Links;
                corridors = Measure("corridor observation", () => CorridorObserver.Observe(
                    nodes,
                    provisionalLinks,
                    settings.Layout.ParallelLaneSpacing,
                    settings.Layout.LinkPadding));
                lanes = Measure("lane allocation", () => CorridorLaneAllocator.Allocate(corridors));
                capacityFailureCount += lanes.CapacityRequests?.Count ?? 0;
            }
            var links = Measure("lane geometry compilation", () => CorridorLaneGeometryCompiler.Compile(provisionalLinks, corridors, lanes));
            var traversals = Measure("traversal compilation", () => EdgeTraversalCompiler.Compile(links, corridors, lanes, nodes, provisionalLinks));
            links = EdgeTraversalCompiler.Apply(links, traversals);
            links = Measure("normalization", () => LogicalRouteNormalizer.Normalize(nodes, links, settings.Layout.LinkPadding));
            var traceability = Measure("validation", () => TraceabilityValidator.Validate(nodes, links, settings.Layout.ParallelLaneSpacing));

            var originalTraceability = traceability;
            var expansion = ExpandLayersForLaneDemand(nodes, links, traceability, settings);
            var expansionAttempts = capacityAttempts.Concat(expansion.Attempts).ToArray();
            if (expansion.Changed)
            {
                nodes = expansion.Nodes;
                projects = PositionProjects(graph, settings, nodes);
                positionedLinks = PositionLinks(graph, settings, nodes);
                links = positionedLinks.Links;
            }

            var repairBudget = links.Count > 256
                ? new RouteRepairBudget(16, 2, 1, 24)
                : links.Count > 128
                    ? new RouteRepairBudget(32, 4, 2, 128)
                    : new RouteRepairBudget();
            var duplicateExposureMode = settings.NodeDuplication.AllowDuplicateNodes &&
                graph.PlacementParentByNode.Count == 0 &&
                graph.Nodes.Any(node => node.Id.StartsWith(ExposureTreeIdPrefix, StringComparison.Ordinal));
            var duplicateNeedsRepair = traceability.Violations.Any(violation =>
                violation.Code == TraceabilityViolationCode.NodeCollision ||
                violation.Code == TraceabilityViolationCode.SharedSegment &&
                violation.Magnitude >= settings.Layout.NodeWidth);
            var repair = Measure("repair passes", () => duplicateExposureMode && !duplicateNeedsRepair
                ? RouteRepairCoordinator.CompileOnly(
                    nodes,
                    links,
                    settings,
                    "SkippedDuplicatedModeNonBlockingAdvisories")
                : RouteRepairCoordinator.Repair(nodes, links, settings, repairBudget));
            links = repair.Links;
            corridors = repair.Corridors;
            lanes = repair.Lanes;
            traversals = repair.Traversals;
            traceability = repair.PostRepairValidation;

            return new RenderLayout(graph, nodes, projects, links, positionedLinks.Selection, positionedLinks.RegionalSelection,
                traversals, traceability, corridors, lanes, originalTraceability,
                expansionAttempts.Concat(repair.Attempts).ToArray(),
                repair.EstimatedWorkUsed, repair.WorkBudgetExhausted, repair.RunReason, timings,
                repair.RoutesInvalidated, repair.RoutePairsRevalidated, repair.CorridorRebuildCount,
                capacityFailureCount, capacityAttempts.Count(attempt => attempt.Applied));
        }

        internal static LayerExpansionResult ExpandLayersForLaneDemand(
            IReadOnlyDictionary<string, NodeLayout> nodes,
            IReadOnlyDictionary<string, LinkLayout> links,
            TraceabilityValidationResult validation,
            DiagramSettings settings)
        {
            var demandByDepth = new Dictionary<int, int>();
            foreach (var finding in validation.Violations.Where(item =>
                item.Code == TraceabilityViolationCode.SharedSegment ||
                item.Code == TraceabilityViolationCode.ParallelSpacing))
            {
                if (!links.TryGetValue(finding.EdgeId, out var link) ||
                    !nodes.TryGetValue(link.Link.SourceId, out var source) ||
                    !nodes.TryGetValue(link.Link.TargetId, out var target))
                {
                    continue;
                }

                var depth = Math.Max(source.Depth, target.Depth);
                var required = finding.Code == TraceabilityViolationCode.ParallelSpacing
                    ? Math.Max(1, finding.Magnitude)
                    : settings.Layout.ParallelLaneSpacing;
                demandByDepth[depth] = Math.Min(
                    settings.Layout.VerticalSpacing,
                    demandByDepth.TryGetValue(depth, out var current) ? current + required : required);
            }

            if (demandByDepth.Count == 0)
            {
                return new LayerExpansionResult(
                    nodes.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal),
                    Array.Empty<RouteRepairAttempt>(),
                    false);
            }

            var expanded = nodes.ToDictionary(
                item => item.Key,
                item =>
                {
                    var delta = demandByDepth.Where(demand => item.Value.Depth >= demand.Key).Sum(demand => demand.Value);
                    return item.Value with { Rect = item.Value.Rect with { Y = item.Value.Rect.Y + delta } };
                },
                StringComparer.Ordinal);
            AlignBaselineNodes(settings, expanded);
            var attempts = demandByDepth.OrderBy(item => item.Key).Select(item => new RouteRepairAttempt(
                $"layer-depth-{item.Key}",
                "AdaptiveLayerSpacing",
                true,
                $"Expanded downstream layer geometry by {item.Value}px from observed lane demand.",
                Array.Empty<ValidationPoint>(),
                Array.Empty<ValidationPoint>())).ToArray();
            return new LayerExpansionResult(expanded, attempts, true);
        }

        internal sealed record LayerExpansionResult(
            Dictionary<string, NodeLayout> Nodes,
            IReadOnlyList<RouteRepairAttempt> Attempts,
            bool Changed);

        internal static LayerExpansionResult ExpandLayersForCapacityRequests(
            IReadOnlyDictionary<string, NodeLayout> nodes,
            IReadOnlyDictionary<string, LinkLayout> links,
            CorridorObservation corridors,
            CorridorLaneAllocation lanes,
            DiagramSettings settings)
        {
            var requests = lanes.CapacityRequests ?? Array.Empty<CapacityRequest>();
            var demandByNode = new Dictionary<string, int>(StringComparer.Ordinal);
            var outgoing = links.Values
                .GroupBy(link => link.Link.SourceId, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Select(link => link.Link.TargetId).Distinct(StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);
            foreach (var request in requests
                .Where(request => request.SmallestExpansion > 0 &&
                    corridors.Corridors.TryGetValue(request.CorridorId, out var corridor) &&
                    corridor.Orientation == CorridorOrientation.Horizontal)
                .OrderBy(request => request.CorridorId, StringComparer.Ordinal))
            {
                var affectedLinks = request.RouteRevisions.Keys
                    .Where(links.ContainsKey)
                    .Select(edgeId => links[edgeId])
                    .ToArray();
                if (affectedLinks.Length == 0)
                {
                    continue;
                }

                var expansion = Math.Min(
                    request.SmallestExpansion,
                    settings.Layout.VerticalSpacing * 2);
                var queue = new Queue<string>(affectedLinks.Select(link => link.Link.TargetId));
                var closure = new HashSet<string>(StringComparer.Ordinal);
                while (queue.Count > 0)
                {
                    var nodeId = queue.Dequeue();
                    if (!nodes.ContainsKey(nodeId) || !closure.Add(nodeId))
                    {
                        continue;
                    }

                    if (outgoing.TryGetValue(nodeId, out var children))
                    {
                        foreach (var child in children)
                        {
                            queue.Enqueue(child);
                        }
                    }
                }

                foreach (var nodeId in closure)
                {
                    demandByNode[nodeId] = Math.Max(
                        demandByNode.TryGetValue(nodeId, out var current) ? current : 0,
                        expansion);
                }
            }

            if (demandByNode.Count == 0)
            {
                return new LayerExpansionResult(
                    nodes.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal),
                    requests.Select(request => new RouteRepairAttempt(
                        $"capacity:{request.CorridorId}",
                        "CapacityRequest",
                        false,
                        "No bounded horizontal layer expansion was available for this capacity request.",
                        Array.Empty<ValidationPoint>(),
                        Array.Empty<ValidationPoint>())).ToArray(),
                    false);
            }

            var expanded = nodes.ToDictionary(
                item => item.Key,
                item =>
                {
                    var delta = demandByNode.TryGetValue(item.Key, out var demand) ? demand : 0;
                    return item.Value with { Rect = item.Value.Rect with { Y = item.Value.Rect.Y + delta } };
                },
                StringComparer.Ordinal);
            var attempts = requests.OrderBy(item => item.CorridorId, StringComparer.Ordinal).Select(item => new RouteRepairAttempt(
                $"capacity:{item.CorridorId}",
                "CapacityRequest",
                true,
                $"Expanded the bounded downstream route closure for {item.RequiredLaneCount} required lane(s).",
                Array.Empty<ValidationPoint>(),
                Array.Empty<ValidationPoint>())).ToArray();
            return new LayerExpansionResult(expanded, attempts, true);
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
            if (graph.Nodes.Any(node => node.Id.StartsWith(ExposureTreeIdPrefix, StringComparison.Ordinal)) &&
                (graph.Nodes.Count >= settings.Layout.ExposureTreeLayoutThreshold || IsRootedExposureForest(graph)))
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
                    new HashSet<string>(StringComparer.Ordinal),
                    depthGaps);
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
            HashSet<string> ancestors,
            IReadOnlyDictionary<int, int> depthGaps)
        {
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
                    Measure = MeasureExposureSubtree(link.TargetId, graph, settings, widths, outgoing, new HashSet<string>(ancestors, StringComparer.Ordinal), depth + 1)
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
                    depthGaps);
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

        private static PositionedLinkLayouts PositionLinks(
            RenderGraph graph,
            DiagramSettings settings,
            IReadOnlyDictionary<string, NodeLayout> nodes)
        {
            if (graph.PlacementParentByNode.Count == 0 &&
                graph.Nodes.Any(node => node.Id.StartsWith(ExposureTreeIdPrefix, StringComparison.Ordinal)))
            {
                return OptimiseRegionalLinks(
                    graph,
                    settings,
                    nodes,
                    PositionExposureTreeLinks(graph, settings, nodes),
                    true);
            }

            var result = new Dictionary<string, LinkLayout>(StringComparer.Ordinal);
            var sourceGroups = graph.Links.GroupBy(link => link.SourceId).ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
            var targetGroups = graph.Links.GroupBy(link => link.TargetId).ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
            var usedCorners = new HashSet<string>(StringComparer.Ordinal);
            var routeLaneIndexes = CalculateRouteLaneIndexes(graph, nodes);
            var terminalLaneSpacing = Math.Max(
                settings.Layout.EdgePortSpacing,
                settings.Layout.ParallelLaneSpacing * 2);

            foreach (var link in graph.Links.OrderBy(link => link.Order).ThenBy(link => link.Id, StringComparer.Ordinal))
            {
                var source = nodes[link.SourceId].Rect;
                var target = nodes[link.TargetId].Rect;
                var sourceOffset = PortOffset(sourceGroups[link.SourceId], link, terminalLaneSpacing, nodes, true);
                var targetOffset = PortOffset(targetGroups[link.TargetId], link, terminalLaneSpacing, nodes, false);
                var sourcePoint = new Point(source.CenterX + sourceOffset, source.Bottom);
                var targetPoint = new Point(target.CenterX + targetOffset, target.Y);
                var obstacles = RoutingObstacles(nodes, link, settings.Layout.LinkPadding);
                var routeLaneIndex = routeLaneIndexes.TryGetValue(link.Id, out var index) ? index : 0;
                var route = BuildRoute(sourcePoint, targetPoint, obstacles, settings, routeLaneIndex, usedCorners);

                result[link.Id] = new LinkLayout(
                    link,
                    sourcePoint,
                    targetPoint,
                    route,
                    Ratio(sourcePoint.X, source),
                    Ratio(targetPoint.X, target));
            }

            var candidatesByEdge = new Dictionary<string, IReadOnlyList<CorridorPathCandidate>>(StringComparer.Ordinal);
            foreach (var link in graph.Links.OrderBy(link => link.Order).ThenBy(link => link.Id, StringComparer.Ordinal))
            {
                var accepted = result[link.Id];
                var source = nodes[link.SourceId].Rect;
                var target = nodes[link.TargetId].Rect;
                var obstacles = RoutingObstacles(nodes, link, settings.Layout.LinkPadding);
                var routeLaneIndex = routeLaneIndexes.TryGetValue(link.Id, out var index) ? index : 0;
                var laneOffset = routeLaneIndex * settings.Layout.ParallelLaneSpacing;
                var sourceExit = accepted.Points.Count > 0
                    ? accepted.Points[0]
                    : new Point(accepted.SourcePoint.X, accepted.SourcePoint.Y + settings.Layout.LinkPadding);
                var targetEntry = accepted.Points.Count > 0
                    ? accepted.Points[accepted.Points.Count - 1]
                    : new Point(accepted.TargetPoint.X, accepted.TargetPoint.Y - settings.Layout.LinkPadding);
                var alternatives = BuildRouteCandidates(sourceExit, targetEntry, obstacles, settings, laneOffset)
                    .Concat(BuildRouteCandidates(
                        sourceExit,
                        targetEntry,
                        obstacles,
                        settings,
                        laneOffset + settings.Layout.ParallelLaneSpacing))
                    .Append(BuildOutsideRoute(sourceExit, targetEntry, obstacles, settings, laneOffset))
                    .Concat(graph.PlacementParentByNode.Count > 0
                        ? CanonicalSharedNodeRouteCandidateBuilder.BuildExteriorRoutes(
                            sourceExit,
                            targetEntry,
                            obstacles,
                            settings,
                            laneOffset).Select(route => route.ToList())
                        : Enumerable.Empty<List<Point>>())
                    .Select(Simplify)
                    .Where(route => !CrossesNode(route, obstacles))
                    .Select(route => PathCandidate(link.Id, accepted.SourcePoint, accepted.TargetPoint, route, false))
                    .Append(PathCandidate(link.Id, accepted.SourcePoint, accepted.TargetPoint, accepted.Points.ToList(), true))
                    .GroupBy(candidate => string.Join(";", candidate.Points.Select(point => $"{point.X},{point.Y}")), StringComparer.Ordinal)
                    .Select(group => group.First());
                candidatesByEdge[link.Id] = CorridorPathCandidateReducer.Retain(
                    alternatives,
                    8,
                    settings.Layout.HorizontalSpacing * 2 + settings.Layout.VerticalSpacing * 2);
            }

            var estimatedWork = EstimateGlobalPathSelectionWork(candidatesByEdge, 4);
            if (estimatedWork > MaximumGlobalPathSelectionEstimatedWork)
            {
                return OptimiseRegionalLinks(graph, settings, nodes, result, false);
            }

            var selection = GlobalCorridorPathSelector.Select(
                candidatesByEdge,
                new Dictionary<string, int>(StringComparer.Ordinal),
                settings.Layout.ParallelLaneSpacing,
                4);
            var selectedLayouts = result.Values.ToDictionary(
                layout => layout.Link.Id,
                layout =>
                {
                    var points = selection.Selected[layout.Link.Id].Points;
                    return new LinkLayout(
                        layout.Link,
                        points[0],
                        points[points.Count - 1],
                        points.Skip(1).Take(points.Count - 2),
                        layout.ExitX,
                        layout.EntryX,
                        layout.ExitY,
                        layout.EntryY);
                },
                StringComparer.Ordinal);
            return new PositionedLinkLayouts(selectedLayouts, selection, null);
        }

        internal static long EstimateGlobalPathSelectionWork(
            IReadOnlyDictionary<string, IReadOnlyList<CorridorPathCandidate>> candidates,
            int maximumPasses)
        {
            var edgeCount = candidates.Count;
            var routePairs = (long)edgeCount * Math.Max(0, edgeCount - 1) / 2;
            var alternatives = candidates.Values.Sum(items => Math.Max(0, items.Count - 1));
            return alternatives * Math.Max(1, routePairs) * Math.Max(1, maximumPasses);
        }

        private static PositionedLinkLayouts OptimiseRegionalLinks(
            RenderGraph graph,
            DiagramSettings settings,
            IReadOnlyDictionary<string, NodeLayout> nodes,
            IReadOnlyDictionary<string, LinkLayout> acceptedLayouts,
            bool exposureTree)
        {
            var acceptedCandidates = acceptedLayouts.Values.ToDictionary(
                layout => layout.Link.Id,
                layout =>
                {
                    var scope = ExposureScope(layout.Link.Id, exposureTree);
                    var fanouts = FanoutMemberships(graph, nodes, acceptedLayouts, layout.Link);
                    var candidate = PathCandidate(
                        layout.Link.Id,
                        layout.SourcePoint,
                        layout.TargetPoint,
                        layout.Points.ToList(),
                        true,
                        scope.RootId,
                        scope.BranchId,
                        fanouts);
                    var obstacles = RoutingObstacles(nodes, layout.Link, settings.Layout.LinkPadding);
                    return candidate with
                    {
                        HasInvalidGeometry = CanonicalSharedNodeRouteCandidateBuilder.HasInvalidGeometry(
                            candidate.Points,
                            obstacles)
                    };
                },
                StringComparer.Ordinal);
            var interactions = RegionalCorridorPathOptimizer.DiscoverInteractions(
                acceptedCandidates,
                settings.Layout.ParallelLaneSpacing);
            var relevantEdges = new HashSet<string>(
                interactions.SelectMany(interaction => new[] { interaction.FirstEdgeId, interaction.SecondEdgeId }),
                StringComparer.Ordinal);
            relevantEdges.UnionWith(acceptedCandidates.Values
                .Where(candidate => candidate.HasInvalidGeometry)
                .Select(candidate => candidate.EdgeId));
            var routeLaneIndexes = CalculateRouteLaneIndexes(graph, nodes);
            var candidatesByEdge = new Dictionary<string, IReadOnlyList<CorridorPathCandidate>>(StringComparer.Ordinal);
            foreach (var link in graph.Links.OrderBy(item => item.Order).ThenBy(item => item.Id, StringComparer.Ordinal))
            {
                var accepted = acceptedLayouts[link.Id];
                var acceptedCandidate = acceptedCandidates[link.Id];
                if (!relevantEdges.Contains(link.Id) || accepted.Points.Count == 0)
                {
                    candidatesByEdge[link.Id] = new[] { acceptedCandidate };
                    continue;
                }

                var obstacles = RoutingObstacles(nodes, link, settings.Layout.LinkPadding);
                var routeLaneIndex = routeLaneIndexes.TryGetValue(link.Id, out var index) ? index : 0;
                var laneOffset = routeLaneIndex * settings.Layout.ParallelLaneSpacing;
                var sourceExit = accepted.Points[0];
                var targetEntry = accepted.Points[accepted.Points.Count - 1];
                var localCandidates = BuildRouteCandidates(sourceExit, targetEntry, obstacles, settings, laneOffset)
                    .Concat(BuildRouteCandidates(sourceExit, targetEntry, obstacles, settings,
                        laneOffset + settings.Layout.ParallelLaneSpacing))
                    .Append(BuildOutsideRoute(sourceExit, targetEntry, obstacles, settings, laneOffset))
                    .Select(Simplify)
                    .ToArray();
                var nodeSafeLocalCandidates = localCandidates
                    .Where(route => !CrossesNode(route, obstacles))
                    .Select(route => PathCandidate(
                        link.Id,
                        accepted.SourcePoint,
                        accepted.TargetPoint,
                        route,
                        false,
                        acceptedCandidate.ExposureRootId,
                        acceptedCandidate.ExposureBranchId,
                        acceptedCandidate.FanoutMemberships))
                    .ToArray();
                var exteriorCandidates = (graph.PlacementParentByNode.Count > 0
                    ? CanonicalExteriorCandidates(acceptedCandidate, obstacles, settings, laneOffset)
                    : Enumerable.Empty<CorridorPathCandidate>()).ToArray();
                var compatibleCandidates = nodeSafeLocalCandidates
                    .Concat(exteriorCandidates)
                    .Where(candidate => PreservesTerminalFanoutGeometry(acceptedCandidate, candidate))
                    .ToArray();
                var alternatives = compatibleCandidates
                    .Append(acceptedCandidate)
                    .GroupBy(candidate => string.Join(";", candidate.Points.Select(point => $"{point.X},{point.Y}")), StringComparer.Ordinal)
                    .Select(group => group.First());
                candidatesByEdge[link.Id] = CorridorPathCandidateReducer.Retain(
                    alternatives,
                    8,
                    settings.Layout.HorizontalSpacing * 2 + settings.Layout.VerticalSpacing * 2);
            }

            var regional = RegionalCorridorPathOptimizer.Optimise(
                candidatesByEdge,
                new Dictionary<string, int>(StringComparer.Ordinal),
                settings.Layout.ParallelLaneSpacing,
                new RegionalOptimisationLimits());
            var selectedLayouts = acceptedLayouts.Values.ToDictionary(
                layout => layout.Link.Id,
                layout =>
                {
                    var points = regional.Selected[layout.Link.Id].Points;
                    return new LinkLayout(
                        layout.Link,
                        points[0],
                        points[points.Count - 1],
                        points.Skip(1).Take(points.Count - 2),
                        layout.ExitX,
                        layout.EntryX,
                        layout.ExitY,
                        layout.EntryY);
                },
                StringComparer.Ordinal);
            return new PositionedLinkLayouts(selectedLayouts, null, regional);
        }


        private static CorridorPathCandidate PathCandidate(
            string edgeId,
            Point source,
            Point target,
            IReadOnlyList<Point> route,
            bool isAcceptedPath,
            string? exposureRootId = null,
            string? exposureBranchId = null,
            IReadOnlyList<TerminalFanoutMembership>? fanoutMemberships = null)
        {
            var complete = new[] { source }.Concat(route).Concat(new[] { target }).ToArray();
            var length = Segments(complete).Sum(segment => segment.Length);
            var localLeft = Math.Min(source.X, target.X);
            var localRight = Math.Max(source.X, target.X);
            var localTop = Math.Min(source.Y, target.Y);
            var localBottom = Math.Max(source.Y, target.Y);
            var escape = Math.Max(0, localLeft - complete.Min(point => point.X)) +
                Math.Max(0, complete.Max(point => point.X) - localRight) +
                Math.Max(0, localTop - complete.Min(point => point.Y)) +
                Math.Max(0, complete.Max(point => point.Y) - localBottom);
            var signature = RoutePathSignature(complete, source, target);
            return new CorridorPathCandidate(
                edgeId,
                new[] { signature },
                Array.Empty<string>(),
                new CorridorPathSignature(signature),
                new CorridorPathLocalCost(length, Math.Max(0, complete.Length - 2), escape),
                complete,
                IsAcceptedPath: isAcceptedPath,
                ExposureRootId: exposureRootId,
                ExposureBranchId: exposureBranchId,
                FanoutMemberships: fanoutMemberships);
        }

        private static IEnumerable<CorridorPathCandidate> CanonicalExteriorCandidates(
            CorridorPathCandidate accepted,
            IReadOnlyList<Rect> obstacles,
            DiagramSettings settings,
            int laneOffset)
        {
            var preservedLength = 2;
            if (accepted.Points.Count < preservedLength * 2)
            {
                return Array.Empty<CorridorPathCandidate>();
            }

            var sourceAnchorIndex = preservedLength - 1;
            var targetAnchorIndex = accepted.Points.Count - preservedLength;
            return CanonicalSharedNodeRouteCandidateBuilder.BuildExteriorRoutes(
                    accepted.Points[sourceAnchorIndex],
                    accepted.Points[targetAnchorIndex],
                    obstacles,
                    settings,
                    laneOffset)
                .Select(exterior =>
                {
                    var complete = accepted.Points.Take(sourceAnchorIndex)
                        .Concat(exterior)
                        .Concat(accepted.Points.Skip(targetAnchorIndex + 1))
                        .ToArray();
                    return PathCandidate(
                        accepted.EdgeId,
                        complete[0],
                        complete[complete.Length - 1],
                        complete.Skip(1).Take(complete.Length - 2).ToArray(),
                        false,
                        accepted.ExposureRootId,
                        accepted.ExposureBranchId,
                        accepted.FanoutMemberships);
                });
        }

        private static IReadOnlyList<TerminalFanoutMembership> FanoutMemberships(
            RenderGraph graph,
            IReadOnlyDictionary<string, NodeLayout> nodes,
            IReadOnlyDictionary<string, LinkLayout> layouts,
            RenderLink link)
        {
            var memberships = new List<TerminalFanoutMembership>();
            Add(graph.Links.Where(item => item.SourceId == link.SourceId).ToArray(), FanoutDirection.Source, link.SourceId, true);
            Add(graph.Links.Where(item => item.TargetId == link.TargetId).ToArray(), FanoutDirection.Target, link.TargetId, false);
            return memberships;

            void Add(IReadOnlyList<RenderLink> group, FanoutDirection direction, string sharedNodeId, bool source)
            {
                if (group.Count < 2)
                {
                    return;
                }

                var orderedTerminals = group.OrderBy(item => source ? layouts[item.Id].SourcePoint.X : layouts[item.Id].TargetPoint.X)
                    .ThenBy(item => item.Id, StringComparer.Ordinal).ToArray();
                var orderedRemote = group.OrderBy(item => source ? nodes[item.TargetId].Rect.CenterX : nodes[item.SourceId].Rect.CenterX)
                    .ThenBy(item => item.Id, StringComparer.Ordinal).ToArray();
                var terminalOrder = Array.FindIndex(orderedTerminals, item => item.Id == link.Id);
                var remoteOrder = Array.FindIndex(orderedRemote, item => item.Id == link.Id);
                var sharedX = nodes[sharedNodeId].Rect.CenterX;
                var remoteX = source ? nodes[link.TargetId].Rect.CenterX : nodes[link.SourceId].Rect.CenterX;
                memberships.Add(new TerminalFanoutMembership(
                    $"{direction.ToString().ToLowerInvariant()}:{sharedNodeId}",
                    direction,
                    sharedNodeId,
                    terminalOrder,
                    terminalOrder,
                    remoteOrder,
                    remoteX < sharedX ? FanoutSide.Left : FanoutSide.Right));
            }
        }

        private static bool PreservesTerminalFanoutGeometry(
            CorridorPathCandidate accepted,
            CorridorPathCandidate candidate)
        {
            if (accepted.FanoutMemberships is null || accepted.FanoutMemberships.Count == 0)
            {
                return true;
            }

            return TerminalRouteCompatibility.Preserves(accepted, candidate);
        }

        private static (string? RootId, string? BranchId) ExposureScope(string edgeId, bool exposureTree)
        {
            if (!exposureTree || !edgeId.StartsWith(ExposureTreeIdPrefix, StringComparison.Ordinal))
            {
                return (null, null);
            }

            var parts = edgeId.Split(new[] { "__" }, StringSplitOptions.None);
            return (parts.Length > 0 ? parts[0] : edgeId, parts.Length > 1 ? parts[1] : edgeId);
        }

        private static string RoutePathSignature(IReadOnlyList<Point> points, Point source, Point target)
        {
            var orientations = string.Join(string.Empty, Segments(points).Select(segment =>
                segment.IsHorizontal ? "H" : segment.IsVertical ? "V" : "D"));
            var minX = Math.Min(source.X, target.X);
            var maxX = Math.Max(source.X, target.X);
            var minY = Math.Min(source.Y, target.Y);
            var maxY = Math.Max(source.Y, target.Y);
            var bands = string.Join(string.Empty, points.Skip(1).Take(Math.Max(0, points.Count - 2)).Select(point =>
                $"{(point.X < minX ? 'L' : point.X > maxX ? 'R' : 'M')}{(point.Y < minY ? 'A' : point.Y > maxY ? 'B' : 'M')}"));
            return $"{orientations}:{bands}";
        }

        private static Dictionary<string, LinkLayout> PositionExposureTreeLinks(
            RenderGraph graph,
            DiagramSettings settings,
            IReadOnlyDictionary<string, NodeLayout> nodes)
        {
            var result = new Dictionary<string, LinkLayout>(StringComparer.Ordinal);
            var sourceGroups = graph.Links.GroupBy(link => link.SourceId, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
            var targetGroups = graph.Links.GroupBy(link => link.TargetId, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
            var fanoutLaneY = CalculateExposureFanoutLaneY(graph, nodes, settings);
            foreach (var link in graph.Links.OrderBy(link => link.Order).ThenBy(link => link.Id, StringComparer.Ordinal))
            {
                if (!nodes.TryGetValue(link.SourceId, out var sourceLayout) ||
                    !nodes.TryGetValue(link.TargetId, out var targetLayout))
                {
                    continue;
                }

                var source = sourceLayout.Rect;
                var target = targetLayout.Rect;
                var terminalLaneSpacing = Math.Max(
                    settings.Layout.EdgePortSpacing,
                    settings.Layout.ParallelLaneSpacing * 2);
                var sourceOffset = PortOffset(sourceGroups[link.SourceId], link, terminalLaneSpacing, nodes, true);
                var targetOffset = PortOffset(targetGroups[link.TargetId], link, terminalLaneSpacing, nodes, false);
                var sourcePoint = new Point(source.CenterX + sourceOffset, source.Bottom);
                var targetPoint = new Point(target.CenterX + targetOffset, target.Y);
                var points = BuildExposureTreeRoute(
                    sourcePoint,
                    targetPoint,
                    RoutingObstacles(nodes, link, settings.Layout.LinkPadding),
                    settings,
                    fanoutLaneY.TryGetValue(link.Id, out var laneY) ? laneY : null);

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
            DiagramSettings settings,
            int? preferredFirstLaneY = null)
        {
            var direct = DirectExposureTreeRoute(
                sourcePoint,
                targetPoint,
                settings.Layout.ExposureTreeConnectorMinSegment,
                preferredFirstLaneY);
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
                var topY = preferredFirstLaneY ??
                    Math.Max(sourcePoint.Y + clearance, blocked.Min(obstacle => obstacle.Y) - clearance);
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

        private static IReadOnlyList<Point> DirectExposureTreeRoute(
            Point sourcePoint,
            Point targetPoint,
            int minSegment,
            int? preferredFirstLaneY = null)
        {
            if (sourcePoint.X == targetPoint.X)
            {
                return Array.Empty<Point>();
            }

            var midY = preferredFirstLaneY ??
                sourcePoint.Y + Math.Max(minSegment, (targetPoint.Y - sourcePoint.Y) / 2);
            return new[]
            {
                new Point(sourcePoint.X, midY),
                new Point(targetPoint.X, midY)
            };
        }

        private static IReadOnlyDictionary<string, int> CalculateExposureFanoutLaneY(
            RenderGraph graph,
            IReadOnlyDictionary<string, NodeLayout> nodes,
            DiagramSettings settings)
        {
            var result = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var sourceGroup in graph.Links
                .Where(link => nodes.ContainsKey(link.SourceId) && nodes.ContainsKey(link.TargetId))
                .GroupBy(link => link.SourceId, StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal))
            {
                var source = nodes[sourceGroup.Key].Rect;
                foreach (var side in sourceGroup
                    .Where(link => nodes[link.TargetId].Rect.CenterX != source.CenterX)
                    .GroupBy(link => nodes[link.TargetId].Rect.CenterX > source.CenterX))
                {
                    var ordered = side
                        .OrderBy(link => Math.Abs(nodes[link.TargetId].Rect.CenterX - source.CenterX))
                        .ThenBy(link => link.Id, StringComparer.Ordinal)
                        .ToArray();
                    var anchor = ordered
                        .Select(link =>
                        {
                            var target = nodes[link.TargetId].Rect;
                            return source.Bottom + Math.Max(
                                settings.Layout.ExposureTreeConnectorMinSegment,
                                (target.Y - source.Bottom) / 2);
                        })
                        .Max();
                    var desired = ordered
                        .Select((link, index) => new
                        {
                            link.Id,
                            Y = anchor - index * settings.Layout.ParallelLaneSpacing
                        })
                        .ToArray();
                    if (desired.Any(item => item.Y <= source.Bottom))
                    {
                        continue;
                    }

                    foreach (var item in desired)
                    {
                        result[item.Id] = item.Y;
                    }
                }
            }

            return result;
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
            HashSet<string> usedCorners)
        {
            var sourceExit = new Point(sourcePoint.X, sourcePoint.Y + settings.Layout.LinkPadding);
            var targetEntry = new Point(targetPoint.X, targetPoint.Y - settings.Layout.LinkPadding);
            var laneOffset = laneIndex * settings.Layout.ParallelLaneSpacing;
            var points = SelectBestRoute(sourceExit, targetEntry, obstacles, settings, laneOffset);

            points = SeparateOverlappingCorners(points, usedCorners, settings.Layout.ParallelLaneSpacing);
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

        private static int PortOffset(
            IReadOnlyList<RenderLink> group,
            RenderLink link,
            int spacing,
            IReadOnlyDictionary<string, NodeLayout> nodes,
            bool sourcePort)
        {
            var ordered = group
                .OrderBy(item => sourcePort
                    ? nodes[item.TargetId].Rect.CenterX
                    : nodes[item.SourceId].Rect.CenterX)
                .ThenBy(item => item.Id, StringComparer.Ordinal)
                .ToArray();
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

        private sealed record PositionedLinkLayouts(
            IReadOnlyDictionary<string, LinkLayout> Links,
            CorridorPathSelectionResult? Selection,
            RegionalPathSelectionResult? RegionalSelection);
    }
