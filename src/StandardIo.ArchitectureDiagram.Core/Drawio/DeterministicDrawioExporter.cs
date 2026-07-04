using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using StandardIo.ArchitectureDiagram.Core.Graph;
using StandardIo.ArchitectureDiagram.Core.Settings;

namespace StandardIo.ArchitectureDiagram.Core.Drawio;

public sealed class DeterministicDrawioExporter
{
    private const int TextWidth = 8;

    public string Export(DiagramModel diagram, DiagramSettings settings)
    {
        if (diagram is null)
        {
            throw new ArgumentNullException(nameof(diagram));
        }

        settings ??= DiagramSettings.CreateDefault();
        var graph = RenderGraph.From(diagram);
        var layout = RenderLayout.Build(graph, settings);

        return new DrawioXmlWriter(settings).Write(layout);
    }

    private sealed class RenderGraph
    {
        private RenderGraph(
            IReadOnlyList<RenderProject> projects,
            IReadOnlyList<RenderNode> nodes,
            IReadOnlyList<RenderLink> links)
        {
            Projects = projects;
            Nodes = nodes;
            Links = links;
        }

        public IReadOnlyList<RenderProject> Projects { get; }

        public IReadOnlyList<RenderNode> Nodes { get; }

        public IReadOnlyList<RenderLink> Links { get; }

        public static RenderGraph From(DiagramModel diagram)
        {
            var projects = diagram.Projects
                .Select((project, order) => new RenderProject(project.Id, project.Name, order))
                .ToArray();
            var nodes = new List<RenderNode>();
            var seenNodeIds = new HashSet<string>(StringComparer.Ordinal);

            foreach (var project in diagram.Projects)
            {
                foreach (var type in project.Types)
                {
                    if (seenNodeIds.Add(type.Id))
                    {
                        nodes.Add(new RenderNode(type.Id, type.ProjectId, type.Name, type.FullName, type.Kind, false, string.Empty, nodes.Count));
                    }
                }
            }

            foreach (var external in diagram.ExternalDependencies)
            {
                if (seenNodeIds.Add(external.Id))
                {
                    var fullName = string.IsNullOrWhiteSpace(external.FullName) ? external.Name : external.FullName;
                    var tag = string.IsNullOrWhiteSpace(external.Tag) ? "[External]" : external.Tag;
                    nodes.Add(new RenderNode(external.Id, null, external.Name, fullName, "External", true, tag, nodes.Count));
                }
            }

            var nodeIds = new HashSet<string>(nodes.Select(node => node.Id), StringComparer.Ordinal);
            var links = diagram.Edges
                .Where(edge => nodeIds.Contains(edge.SourceId) && nodeIds.Contains(edge.TargetId))
                .Select((edge, order) => new RenderLink(edge.Id, edge.SourceId, edge.TargetId, edge.Kind, order))
                .ToArray();

            return new RenderGraph(projects, nodes, links);
        }
    }

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
            var widths = CalculateWidths(graph, settings);
            var nodes = PositionNodes(graph, settings, depths, widths);
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

        private static Dictionary<string, NodeLayout> PositionNodes(
            RenderGraph graph,
            DiagramSettings settings,
            IReadOnlyDictionary<string, int> depths,
            IReadOnlyDictionary<string, int> widths)
        {
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
                        new Rect(x, NodeY(layer.Key, settings), widths[node.Id], settings.Layout.NodeHeight),
                        layer.Key,
                        false);
                    x += widths[node.Id] + settings.Layout.HorizontalSpacing;
                }
            }

            CenterParentsOverChildren(graph, settings, result);
            ResolveLayerOverlaps(settings, result);

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
            var result = new Dictionary<string, LinkLayout>(StringComparer.Ordinal);
            var sourceGroups = graph.Links.GroupBy(link => link.SourceId).ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
            var targetGroups = graph.Links.GroupBy(link => link.TargetId).ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
            var usedCorners = new HashSet<string>(StringComparer.Ordinal);
            var occupiedSegments = new List<Segment>();

            foreach (var link in graph.Links.OrderBy(link => link.Order))
            {
                var source = nodes[link.SourceId].Rect;
                var target = nodes[link.TargetId].Rect;
                var sourceOffset = PortOffset(sourceGroups[link.SourceId], link, settings.Layout.EdgePortSpacing);
                var targetOffset = PortOffset(targetGroups[link.TargetId], link, settings.Layout.EdgePortSpacing);
                var sourcePoint = new Point(source.CenterX + sourceOffset, source.Bottom);
                var targetPoint = new Point(target.CenterX + targetOffset, target.Y);
                var obstacles = nodes.Values
                    .Where(node => !node.IsStandalone)
                    .Select(node => node.Rect)
                    .ToArray();
                var route = BuildRoute(sourcePoint, targetPoint, obstacles, settings, link.Order, usedCorners, occupiedSegments);
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

        private static IReadOnlyList<Point> BuildRoute(
            Point sourcePoint,
            Point targetPoint,
            IReadOnlyList<Rect> obstacles,
            DiagramSettings settings,
            int order,
            HashSet<string> usedCorners,
            List<Segment> occupiedSegments)
        {
            var sourceExit = new Point(sourcePoint.X, sourcePoint.Y + settings.Layout.LinkPadding);
            var targetEntry = new Point(targetPoint.X, targetPoint.Y - settings.Layout.LinkPadding);
            var laneOffset = order * settings.Layout.ParallelLaneSpacing;
            var midY = sourceExit.Y <= targetEntry.Y
                ? sourceExit.Y + (targetEntry.Y - sourceExit.Y) / 2 + laneOffset
                : sourceExit.Y + settings.Layout.VerticalSpacing + laneOffset;
            var points = new List<Point>
            {
                sourceExit,
                new(sourceExit.X, midY),
                new(targetEntry.X, midY),
                targetEntry
            };

            if (CrossesNode(points, obstacles))
            {
                points = BuildCorridorRoute(sourceExit, targetEntry, obstacles, settings, laneOffset)
                    ?? BuildOutsideRoute(sourceExit, targetEntry, obstacles, settings, laneOffset);
            }

            points = SeparateOverlappingCorners(points, usedCorners, settings.Layout.ParallelLaneSpacing);
            points = SeparateParallelSegments(points, occupiedSegments, settings.Layout.ParallelLaneSpacing);
            if (CrossesNode(points, obstacles))
            {
                points = BuildOutsideRoute(sourceExit, targetEntry, obstacles, settings, laneOffset + settings.Layout.ParallelLaneSpacing);
            }

            return Simplify(points);
        }

        private static List<Point>? BuildCorridorRoute(
            Point sourceExit,
            Point targetEntry,
            IReadOnlyList<Rect> obstacles,
            DiagramSettings settings,
            int laneOffset)
        {
            foreach (var corridorX in CorridorCandidates(sourceExit, targetEntry, obstacles, settings, laneOffset))
            {
                var route = new List<Point>
                {
                    sourceExit,
                    new(corridorX, sourceExit.Y),
                    new(corridorX, targetEntry.Y),
                    targetEntry
                };

                if (!CrossesNode(route, obstacles))
                {
                    return route;
                }
            }

            return null;
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
            var result = new List<Point>();
            foreach (var point in points)
            {
                var adjusted = point;
                while (!usedCorners.Add($"{adjusted.X}:{adjusted.Y}"))
                {
                    adjusted = adjusted with { Y = adjusted.Y + spacing };
                }

                result.Add(adjusted);
            }

            return result;
        }

        private static List<Point> SeparateParallelSegments(List<Point> points, List<Segment> occupiedSegments, int spacing)
        {
            for (var index = 0; index < points.Count - 1; index++)
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

        private static int NodeY(int depth, DiagramSettings settings)
        {
            return settings.Layout.ContainerPadding * 2 +
                depth * (settings.Layout.NodeHeight + settings.Layout.VerticalSpacing);
        }
    }

    private sealed class DrawioXmlWriter
    {
        private readonly DiagramSettings _settings;
        private readonly StyleResolver _styleResolver;

        public DrawioXmlWriter(DiagramSettings settings)
        {
            _settings = settings;
            _styleResolver = new StyleResolver(settings);
        }

        public string Write(RenderLayout layout)
        {
            var root = new XElement("root",
                new XElement("mxCell", new XAttribute("id", "0")),
                new XElement("mxCell", new XAttribute("id", "1"), new XAttribute("parent", "0")));

            foreach (var project in layout.Graph.Projects)
            {
                if (_settings.ShowProjectContainers && layout.Projects.TryGetValue(project.Id, out var projectLayout))
                {
                    root.Add(Vertex(project.Id, project.Name, BuildNodeStyle(_settings.ProjectContainerStyle), "1", projectLayout.Rect));
                }

                foreach (var nodeLayout in layout.Nodes.Values
                    .Where(node => string.Equals(node.Node.ProjectId, project.Id, StringComparison.Ordinal))
                    .OrderBy(node => node.Node.Order))
                {
                    var rect = nodeLayout.Rect;
                    ProjectLayout? parentProjectLayout = null;
                    var hasProjectParent = _settings.ShowProjectContainers &&
                        layout.Projects.TryGetValue(project.Id, out parentProjectLayout);
                    var parent = hasProjectParent ? project.Id : "1";
                    if (hasProjectParent)
                    {
                        rect = rect with
                        {
                            X = rect.X - parentProjectLayout!.Rect.X,
                            Y = rect.Y - parentProjectLayout.Rect.Y
                        };
                    }

                    root.Add(Vertex(
                        nodeLayout.Node.Id,
                        nodeLayout.Node.Name,
                        BuildNodeStyle(_styleResolver.Resolve(ToTypeNode(nodeLayout.Node))),
                        parent,
                        rect));
                }
            }

            foreach (var nodeLayout in layout.Nodes.Values
                .Where(node => node.Node.IsExternal)
                .OrderBy(node => node.Node.Order))
            {
                root.Add(Vertex(
                    nodeLayout.Node.Id,
                    $"{nodeLayout.Node.Tag}\n{nodeLayout.Node.Name}\n{nodeLayout.Node.FullName}",
                    BuildNodeStyle(_settings.ExternalDependencyStyle),
                    "1",
                    nodeLayout.Rect));
            }

            foreach (var linkLayout in layout.Links.Values.OrderBy(link => link.Link.Order))
            {
                root.Add(Edge(linkLayout));
            }

            var model = new XElement("mxGraphModel",
                new XAttribute("dx", "1200"),
                new XAttribute("dy", "900"),
                new XAttribute("grid", "1"),
                new XAttribute("gridSize", "10"),
                new XAttribute("guides", "1"),
                new XAttribute("tooltips", "1"),
                new XAttribute("connect", "1"),
                new XAttribute("arrows", "1"),
                new XAttribute("fold", "1"),
                new XAttribute("page", "1"),
                new XAttribute("pageScale", "1"),
                new XAttribute("pageWidth", "1600"),
                new XAttribute("pageHeight", "1200"),
                new XAttribute("background", _settings.Canvas.BackgroundColor),
                root);

            var file = new XElement("mxfile",
                new XAttribute("host", "StandardIo.ArchitectureDiagram"),
                new XElement("diagram", new XAttribute("name", "Architecture"), model));

            return new XDocument(file).ToString(SaveOptions.DisableFormatting);
        }

        private static XElement Vertex(string id, string value, string style, string parent, Rect rect)
        {
            return new XElement("mxCell",
                new XAttribute("id", id),
                new XAttribute("value", value),
                new XAttribute("style", style),
                new XAttribute("vertex", "1"),
                new XAttribute("parent", parent),
                new XElement("mxGeometry",
                    new XAttribute("x", rect.X),
                    new XAttribute("y", rect.Y),
                    new XAttribute("width", rect.Width),
                    new XAttribute("height", rect.Height),
                    new XAttribute("as", "geometry")));
        }

        private XElement Edge(LinkLayout link)
        {
            // mxCell stores edge terminals in source/target, style as key=value pairs, and mxGeometry points as waypoints.
            // See https://jgraph.github.io/mxgraph/docs/js-api/files/model/mxCell-js.html and
            // https://jgraph.github.io/mxgraph/docs/js-api/files/model/mxGeometry-js.html.
            return new XElement("mxCell",
                new XAttribute("id", link.Link.Id),
                new XAttribute("style", BuildConnectorStyle(_settings.Connector, link)),
                new XAttribute("edge", "1"),
                new XAttribute("parent", "1"),
                new XAttribute("source", link.Link.SourceId),
                new XAttribute("target", link.Link.TargetId),
                new XElement("mxGeometry",
                    new XAttribute("relative", "1"),
                    new XAttribute("as", "geometry"),
                    new XElement("Array",
                        new XAttribute("as", "points"),
                        link.Points.Select(point => new XElement("mxPoint",
                            new XAttribute("x", point.X),
                            new XAttribute("y", point.Y))))));
        }

        private static TypeNode ToTypeNode(RenderNode node)
        {
            return new TypeNode(node.Id, node.ProjectId ?? string.Empty, node.Name, node.FullName, node.Kind);
        }

        private static string BuildNodeStyle(NodeStyle style)
        {
            var shape = style.Shape == "rounded" ? "rounded=1;whiteSpace=wrap;html=1;" : $"shape={style.Shape};whiteSpace=wrap;html=1;";
            var shadow = style.Shadow ? "shadow=1;" : string.Empty;
            return $"{shape}fillColor={style.FillColor};strokeColor={style.StrokeColor};fontColor={style.FontColor};{shadow}{style.ExtraStyle}";
        }

        private static string BuildConnectorStyle(ConnectorStyle style, LinkLayout link)
        {
            var rounded = style.Rounded ? "rounded=1;" : "rounded=0;";
            return $"edgeStyle=orthogonalEdgeStyle;html=1;{rounded}orthogonalLoop=1;jettySize=auto;endArrow=block;endFill=1;strokeColor={style.StrokeColor};strokeWidth={style.StrokeWidth};exitX={FormatRatio(link.ExitX)};exitY=1;entryX={FormatRatio(link.EntryX)};entryY=0;";
        }

        private static string FormatRatio(double value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }
    }

    private sealed record RenderProject(string Id, string Name, int Order);
    private sealed record RenderNode(string Id, string? ProjectId, string Name, string FullName, string Kind, bool IsExternal, string Tag, int Order);
    private sealed record RenderLink(string Id, string SourceId, string TargetId, string Kind, int Order);
    private sealed record NodeLayout(RenderNode Node, Rect Rect, int Depth, bool IsStandalone);
    private sealed record ProjectLayout(RenderProject Project, Rect Rect);
    private sealed record LinkLayout(RenderLink Link, Point SourcePoint, Point TargetPoint, IReadOnlyList<Point> Points, double ExitX, double EntryX);

    private readonly record struct Point(int X, int Y);

    private readonly record struct Rect(int X, int Y, int Width, int Height)
    {
        public int Right => X + Width;

        public int Bottom => Y + Height;

        public int CenterX => X + Width / 2;

        public bool Contains(Point point)
        {
            return point.X > X && point.X < Right && point.Y > Y && point.Y < Bottom;
        }
    }

    private readonly record struct Segment(Point Start, Point End)
    {
        public bool IsHorizontal => Start.Y == End.Y;

        public bool IsVertical => Start.X == End.X;

        public bool Intersects(Rect rect)
        {
            if (IsHorizontal)
            {
                return Start.Y > rect.Y &&
                    Start.Y < rect.Bottom &&
                    Math.Max(Start.X, End.X) > rect.X &&
                    Math.Min(Start.X, End.X) < rect.Right;
            }

            if (IsVertical)
            {
                return Start.X > rect.X &&
                    Start.X < rect.Right &&
                    Math.Max(Start.Y, End.Y) > rect.Y &&
                    Math.Min(Start.Y, End.Y) < rect.Bottom;
            }

            return rect.Contains(Start) || rect.Contains(End);
        }
    }
}
