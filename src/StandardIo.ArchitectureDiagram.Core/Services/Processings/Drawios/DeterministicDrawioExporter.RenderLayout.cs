using System;
using System.Collections.Generic;
using System.Globalization;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using StandardIo.ArchitectureDiagram.Core.Models;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal sealed partial class RenderLayout
{
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
            var placed = MeasureStage(timings, "node placement", () =>
                PlacementPipeline.Place(graph, settings, new LayoutRevision(0)));
            var routed = LegacyRoutingPipeline.Run(placed, settings, timings);

            return new RenderLayout(graph, routed.Placement.Nodes, routed.Placement.Projects, routed.Links,
                routed.PositionedLinks.Selection, routed.PositionedLinks.RegionalSelection,
                routed.Traversals, routed.Traceability, routed.Corridors, routed.Lanes, routed.PreRepairTraceability,
                routed.RepairAttempts, routed.RepairWorkUsed, routed.RepairBudgetExhausted, routed.RepairRunReason,
                timings, routed.RoutesInvalidated, routed.RoutePairsRevalidated, routed.CorridorRebuildCount,
                routed.CapacityFailureCount, routed.CapacityExpansionCount);
        }

        private static T MeasureStage<T>(
            ICollection<PipelineStageMetric> timings,
            string stage,
            Func<T> action)
        {
            var timer = Stopwatch.StartNew();
            var result = action();
            timer.Stop();
            timings.Add(new PipelineStageMetric(stage, timer.ElapsedMilliseconds,
                timings.Count(item => item.Stage == stage) + 1));
            return result;
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
            PlacementPipeline.AlignBaselineNodes(settings, expanded);
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

        internal sealed record PositionedLinkLayouts(
            IReadOnlyDictionary<string, LinkLayout> Links,
            CorridorPathSelectionResult? Selection,
            RegionalPathSelectionResult? RegionalSelection);
    }
