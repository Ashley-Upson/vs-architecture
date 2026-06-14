using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using StandardIo.ArchitectureDiagram.Core.Graph;
using StandardIo.ArchitectureDiagram.Core.Settings;

namespace StandardIo.ArchitectureDiagram.Core.Drawio;

public sealed class DrawioExporter
{
    private const int MaxGeometryValue = 1_000_000_000;
    private const int EdgeSpacing = 10;
    private const int EdgePadding = 10;
    private const int ApproximateTextWidth = 8;

    public string Export(ArchitectureGraph graph, DiagramSettings settings)
    {
        var positions = Layout(graph, settings);
        var resolver = new StyleResolver(settings);
        var root = new XElement("root",
            new XElement("mxCell", new XAttribute("id", "0")),
            new XElement("mxCell", new XAttribute("id", "1"), new XAttribute("parent", "0")));

        foreach (var project in graph.Projects)
        {
            var projectPosition = positions[project.Id];
            root.Add(new XElement("mxCell",
                new XAttribute("id", project.Id),
                new XAttribute("value", project.Name),
                new XAttribute("style", BuildNodeStyle(settings.ProjectContainerStyle)),
                new XAttribute("vertex", "1"),
                new XAttribute("parent", "1"),
                new XElement("mxGeometry",
                    new XAttribute("x", projectPosition.X),
                    new XAttribute("y", projectPosition.Y),
                    new XAttribute("width", projectPosition.Width),
                    new XAttribute("height", projectPosition.Height),
                    new XAttribute("as", "geometry"))));

            foreach (var type in project.Types)
            {
                var typePosition = positions[type.Id];
                root.Add(new XElement("mxCell",
                    new XAttribute("id", type.Id),
                    new XAttribute("value", type.Name),
                    new XAttribute("style", BuildNodeStyle(resolver.Resolve(type))),
                    new XAttribute("vertex", "1"),
                    new XAttribute("parent", project.Id),
                    new XElement("mxGeometry",
                        new XAttribute("x", typePosition.X - projectPosition.X),
                        new XAttribute("y", typePosition.Y - projectPosition.Y),
                        new XAttribute("width", typePosition.Width),
                        new XAttribute("height", typePosition.Height),
                        new XAttribute("as", "geometry"))));
            }
        }

        foreach (var external in graph.ExternalDependencies)
        {
            var position = positions[external.Id];
            root.Add(new XElement("mxCell",
                new XAttribute("id", external.Id),
                new XAttribute("value", external.Name),
                new XAttribute("style", BuildNodeStyle(settings.ExternalDependencyStyle)),
                new XAttribute("vertex", "1"),
                new XAttribute("parent", "1"),
                new XElement("mxGeometry",
                    new XAttribute("x", position.X),
                    new XAttribute("y", position.Y),
                    new XAttribute("width", position.Width),
                    new XAttribute("height", position.Height),
                    new XAttribute("as", "geometry"))));
        }

        var routeObstacleIds = new HashSet<string>(
            graph.Projects.SelectMany(p => p.Types.Select(t => t.Id))
                .Concat(graph.ExternalDependencies.Select(e => e.Id)));
        var edgeRoutes = BuildEdgeRoutes(graph.Edges, positions);

        foreach (var edge in graph.Edges)
        {
            edgeRoutes.TryGetValue(edge.Id, out var edgeRoute);
            root.Add(new XElement("mxCell",
                new XAttribute("id", edge.Id),
                new XAttribute("style", BuildConnectorStyle(settings.Connector, edgeRoute)),
                new XAttribute("edge", "1"),
                new XAttribute("parent", "1"),
                new XAttribute("source", edge.SourceId),
                new XAttribute("target", edge.TargetId),
                BuildEdgeGeometry(edge, positions, edgeRoute, routeObstacleIds, settings.Layout)));
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
            new XAttribute("background", settings.Canvas.BackgroundColor),
            root);

        var file = new XElement("mxfile",
            new XAttribute("host", "StandardIo.ArchitectureDiagram"),
            new XAttribute("modified", "2026-06-13T00:00:00.000Z"),
            new XElement("diagram", new XAttribute("name", "Architecture"), model));

        return new XDocument(file).ToString(SaveOptions.DisableFormatting);
    }

    private static Dictionary<string, Rect> Layout(ArchitectureGraph graph, DiagramSettings settings)
    {
        var layout = settings.Layout;
        var result = new Dictionary<string, Rect>();
        var types = graph.Projects.SelectMany(p => p.Types).ToDictionary(t => t.Id);
        var externals = graph.ExternalDependencies.ToDictionary(e => e.Id);
        var orderedNodeIds = graph.Projects
            .SelectMany(p => p.Types.Select(t => t.Id))
            .Concat(graph.ExternalDependencies.Select(e => e.Id))
            .ToList();
        var nodeIds = new HashSet<string>(orderedNodeIds);
        var nodeOrder = orderedNodeIds
            .Select((id, index) => new { id, index })
            .ToDictionary(x => x.id, x => x.index);
        var nodeWidths = CalculateNodeWidths(graph, layout, nodeIds);

        var outgoing = graph.Edges
            .Where(e => nodeIds.Contains(e.SourceId) && nodeIds.Contains(e.TargetId))
            .GroupBy(e => e.SourceId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(e => e.TargetId)
                    .Distinct()
                    .OrderBy(id => nodeOrder.TryGetValue(id, out var index) ? index : int.MaxValue)
                    .ToList());
        var depths = CalculateDepths(graph, types, nodeIds, orderedNodeIds, outgoing);
        var depthOffsets = CalculateDepthOffsets(graph.Edges, depths, nodeIds, layout);
        var originTop = layout.ContainerPadding * 2 + 40;
        var originLeft = layout.ContainerPadding * 2;
        var projectLayouts = new List<ProjectLayout>();

        foreach (var project in graph.Projects)
        {
            var projectTypes = project.Types
                .Where(t => nodeIds.Contains(t.Id))
                .ToList();
            if (projectTypes.Count == 0)
            {
                continue;
            }

            projectLayouts.Add(BuildProjectLayout(project, projectTypes, depths, outgoing, nodeWidths, layout));
        }

        var rowNextLeft = new Dictionary<int, int>();
        var rowVerticalOffsets = new Dictionary<int, int>();
        var previousProjectRowBottom = int.MinValue;
        foreach (var projectRow in projectLayouts
            .GroupBy(p => p.FirstDepth)
            .OrderBy(g => g.Key))
        {
            var naturalRowTop = projectRow.Min(p => p.NaturalTop(originTop, layout, depthOffsets));
            var rowTop = previousProjectRowBottom == int.MinValue
                ? naturalRowTop
                : System.Math.Max(naturalRowTop, SafeAdd(previousProjectRowBottom, layout.VerticalSpacing));
            var rowVerticalOffset = SafeSubtract(rowTop, naturalRowTop);
            var projectLeft = originLeft;
            var projectRowBottom = rowTop;

            foreach (var projectLayout in projectRow)
            {
                var projectTop = SafeAdd(projectLayout.NaturalTop(originTop, layout, depthOffsets), rowVerticalOffset);
                var projectBottom = SafeAdd(projectLayout.NaturalBottom(originTop, layout, depthOffsets), rowVerticalOffset);
                result[projectLayout.Project.Id] = new Rect(
                    projectLeft,
                    projectTop,
                    projectLayout.Width,
                    SafeSubtract(projectBottom, projectTop));
                projectRowBottom = System.Math.Max(projectRowBottom, projectBottom);

                foreach (var layerGroup in projectLayout.Layers)
                {
                    foreach (var node in layerGroup.Nodes)
                    {
                        var x = SafeAdd(projectLeft, layout.ContainerPadding, node.X);
                        var y = SafeAdd(
                            NodeTop(originTop, node.Depth, layout, depthOffsets),
                            rowVerticalOffset);
                        result[node.Type.Id] = new Rect(x, y, node.Width, layout.NodeHeight);
                    }
                }

                projectLeft = SafeAdd(projectLeft, projectLayout.Width, layout.HorizontalSpacing);
            }

            rowNextLeft[projectRow.Key] = projectLeft;
            rowVerticalOffsets[projectRow.Key] = rowVerticalOffset;
            previousProjectRowBottom = projectRowBottom;
        }

        foreach (var externalLayer in graph.ExternalDependencies
            .Where(e => nodeIds.Contains(e.Id))
            .GroupBy(e => depths.TryGetValue(e.Id, out var depth) ? depth : 0)
            .OrderBy(g => g.Key))
        {
            var projectRects = graph.Projects
                .Where(p => result.ContainsKey(p.Id))
                .Select(p => result[p.Id])
                .ToList();
            var layer = externalLayer.ToList();
            var y = SafeAdd(
                NodeTop(originTop, externalLayer.Key, layout, depthOffsets),
                rowVerticalOffsets.TryGetValue(externalLayer.Key, out var rowOffset) ? rowOffset : 0);
            var externalLeft = rowNextLeft.TryGetValue(externalLayer.Key, out var rowLeft)
                ? rowLeft
                : originLeft;
            var nextExternalLeft = externalLeft;
            for (var i = 0; i < layer.Count; i++)
            {
                var externalWidth = nodeWidths.TryGetValue(layer[i].Id, out var width)
                    ? width
                    : layout.NodeWidth;
                var position = new Rect(nextExternalLeft, y, externalWidth, layout.NodeHeight);
                while (TryFindOverlappingRect(position, projectRects, out var overlappingProject))
                {
                    position = position with { X = SafeAdd(overlappingProject.Right, layout.HorizontalSpacing) };
                }

                result[layer[i].Id] = position;
                nextExternalLeft = SafeAdd(position.Right, layout.HorizontalSpacing);
            }

            rowNextLeft[externalLayer.Key] = nextExternalLeft;
        }

        return result;
    }

    private static ProjectLayout BuildProjectLayout(
        ProjectContainer project,
        List<TypeNode> projectTypes,
        Dictionary<string, int> depths,
        Dictionary<string, List<string>> outgoing,
        Dictionary<string, int> nodeWidths,
        LayoutSettings layout)
    {
        var projectTypeIds = new HashSet<string>(projectTypes.Select(t => t.Id));
        var projectTypeOrder = projectTypes
            .Select((type, index) => new { type.Id, index })
            .ToDictionary(x => x.Id, x => x.index);
        var incoming = new HashSet<string>(outgoing
            .SelectMany(kv => kv.Value
                .Where(projectTypeIds.Contains)
                .Select(targetId => new { SourceId = kv.Key, TargetId = targetId }))
            .Where(edge => projectTypeIds.Contains(edge.SourceId))
            .Select(edge => edge.TargetId));
        var roots = projectTypes
            .Where(t => !incoming.Contains(t.Id))
            .OrderBy(t => depths.TryGetValue(t.Id, out var depth) ? depth : 0)
            .ThenBy(t => projectTypeOrder[t.Id])
            .ToList();
        var assigned = new HashSet<string>();
        var positionedNodes = new List<PositionedNode>();
        var nextGroupX = 0;
        var groupSpacing = SafeMultiply(layout.HorizontalSpacing, 2);

        foreach (var root in roots.Concat(projectTypes.Where(t => !assigned.Contains(t.Id))))
        {
            if (assigned.Contains(root.Id))
            {
                continue;
            }

            var rootDepth = depths.TryGetValue(root.Id, out var depth) ? depth : 0;
            var group = BuildSubtree(root, rootDepth, new HashSet<string>());
            positionedNodes.AddRange(group.Nodes.Select(n => n with { X = SafeAdd(n.X, nextGroupX) }));
            nextGroupX = SafeAdd(nextGroupX, group.Width, groupSpacing);
        }

        var minX = positionedNodes.Select(n => n.X).DefaultIfEmpty(0).Min();
        if (minX != 0)
        {
            positionedNodes = positionedNodes
                .Select(n => n with { X = SafeSubtract(n.X, minX) })
                .ToList();
        }

        var positionedLayers = positionedNodes
            .GroupBy(n => n.Depth)
            .OrderBy(g => g.Key)
            .Select(g => new ProjectLayer(g.Key, g.OrderBy(n => n.X).ToList()))
            .ToList();
        var width = SafeAdd(
            SafeMultiply(layout.ContainerPadding, 2),
            positionedNodes.Select(n => SafeAdd(n.X, n.Width)).DefaultIfEmpty(0).Max());

        return new ProjectLayout(project, positionedLayers, width);

        SubtreeLayout BuildSubtree(TypeNode node, int depth, HashSet<string> active)
        {
            assigned.Add(node.Id);
            active.Add(node.Id);
            var nodeWidth = nodeWidths.TryGetValue(node.Id, out var width)
                ? width
                : layout.NodeWidth;

            var childLayouts = outgoing.TryGetValue(node.Id, out var targets)
                ? targets
                    .Where(projectTypeIds.Contains)
                    .Where(targetId =>
                        depths.TryGetValue(targetId, out var targetDepth) &&
                        depths.TryGetValue(node.Id, out var sourceDepth) &&
                        targetDepth > sourceDepth)
                    .Where(targetId => !assigned.Contains(targetId))
                    .Where(targetId => !active.Contains(targetId))
                    .Select(targetId => BuildSubtree(
                        projectTypes[projectTypeOrder[targetId]],
                        SafeAdd(depth, 1),
                        new HashSet<string>(active)))
                    .ToList()
                : new List<SubtreeLayout>();
            active.Remove(node.Id);

            if (childLayouts.Count == 0)
            {
                return new SubtreeLayout(
                    nodeWidth,
                    new List<PositionedNode> { new(node, 0, depth, nodeWidth) });
            }

            var childSpan = SafeAdd(
                childLayouts.Sum(child => child.Width),
                SafeMultiply(System.Math.Max(0, childLayouts.Count - 1), layout.HorizontalSpacing));
            var subtreeWidth = System.Math.Max(nodeWidth, childSpan);
            var childX = SafeSubtract(subtreeWidth, childSpan) / 2;
            var nodes = new List<PositionedNode>
            {
                new(node, SafeSubtract(subtreeWidth, nodeWidth) / 2, depth, nodeWidth)
            };

            foreach (var child in childLayouts)
            {
                nodes.AddRange(child.Nodes.Select(n => n with { X = SafeAdd(n.X, childX) }));
                childX = SafeAdd(childX, child.Width, layout.HorizontalSpacing);
            }

            return new SubtreeLayout(subtreeWidth, nodes);
        }
    }

    private static XElement BuildEdgeGeometry(
        DependencyEdge edge,
        Dictionary<string, Rect> positions,
        EdgeRoute route,
        HashSet<string> routeObstacleIds,
        LayoutSettings layout)
    {
        var geometry = new XElement("mxGeometry",
            new XAttribute("relative", "1"),
            new XAttribute("as", "geometry"));

        if (!positions.TryGetValue(edge.SourceId, out var source) ||
            !positions.TryGetValue(edge.TargetId, out var target))
        {
            return geometry;
        }

        var obstacles = positions
            .Where(kv => routeObstacleIds.Contains(kv.Key) &&
                kv.Key != edge.SourceId &&
                kv.Key != edge.TargetId)
            .Select(kv => kv.Value.Expand(8))
            .ToList();
        var points = RouteEdge(source, target, route, obstacles, layout);
        if (points.Count == 0)
        {
            return geometry;
        }

        geometry.Add(new XElement("Array",
            new XAttribute("as", "points"),
            points.Select(p => new XElement("mxPoint",
                new XAttribute("x", p.X),
                new XAttribute("y", p.Y)))));

        return geometry;
    }

    private static List<Point> RouteEdge(
        Rect source,
        Rect target,
        EdgeRoute route,
        List<Rect> obstacles,
        LayoutSettings layout)
    {
        var sourcePoint = route.SourcePoint;
        var targetPoint = route.TargetPoint;
        var gap = System.Math.Max(20, layout.VerticalSpacing / 2);
        var sourceLaneY = System.Math.Max(
            SafeAdd(source.Bottom, EdgePadding),
            SafeAdd(source.Bottom, gap, route.LaneOffset));
        var targetLaneY = System.Math.Min(
            SafeSubtract(target.Y, EdgePadding),
            SafeAdd(SafeSubtract(target.Y, gap), route.LaneOffset));

        if (sourceLaneY < targetLaneY)
        {
            var midY = SafeAdd(sourceLaneY, SafeSubtract(targetLaneY, sourceLaneY) / 2);
            var directPoints = new List<Point>
            {
                new(sourcePoint.X, midY),
                new(targetPoint.X, midY)
            };

            if (!RouteIntersects(sourcePoint, targetPoint, directPoints, obstacles))
            {
                return directPoints;
            }
        }

        foreach (var laneX in CandidateLaneXs(source, target, obstacles, layout)
            .Distinct()
            .OrderBy(x => System.Math.Abs(x - ((source.CenterX + target.CenterX) / 2))))
        {
            var candidate = new List<Point>
            {
                new(sourcePoint.X, sourceLaneY),
                new(laneX, sourceLaneY),
                new(laneX, targetLaneY),
                new(targetPoint.X, targetLaneY)
            };

            if (!RouteIntersects(sourcePoint, targetPoint, candidate, obstacles))
            {
                return candidate;
            }
        }

        var fallbackLaneX = SafeAdd(
            obstacles
                .Select(o => o.Right)
                .DefaultIfEmpty(System.Math.Max(source.Right, target.Right))
                .Max(),
            layout.HorizontalSpacing);
        return new List<Point>
        {
            new(sourcePoint.X, sourceLaneY),
            new(fallbackLaneX, sourceLaneY),
            new(fallbackLaneX, targetLaneY),
            new(targetPoint.X, targetLaneY)
        };
    }

    private static IEnumerable<int> CandidateLaneXs(
        Rect source,
        Rect target,
        List<Rect> obstacles,
        LayoutSettings layout)
    {
        var minimumGap = System.Math.Max(12, layout.HorizontalSpacing / 4);
        var bandTop = System.Math.Min(source.Y, target.Y);
        var bandBottom = System.Math.Max(source.Bottom, target.Bottom);
        yield return SafeSubtract(System.Math.Min(source.X, target.X), minimumGap);
        yield return SafeAdd(System.Math.Max(source.Right, target.Right), minimumGap);
        yield return SafeSubtract(source.X, minimumGap);
        yield return SafeAdd(source.Right, minimumGap);
        yield return SafeSubtract(target.X, minimumGap);
        yield return SafeAdd(target.Right, minimumGap);

        foreach (var obstacle in obstacles.Where(o => o.Bottom >= bandTop && o.Y <= bandBottom))
        {
            yield return SafeSubtract(obstacle.X, minimumGap);
            yield return SafeAdd(obstacle.Right, minimumGap);
        }
    }

    private static bool RouteIntersects(Point source, Point target, List<Point> points, List<Rect> obstacles)
    {
        var routePoints = new List<Point> { source };
        routePoints.AddRange(points);
        routePoints.Add(target);

        for (var i = 0; i < routePoints.Count - 1; i++)
        {
            foreach (var obstacle in obstacles)
            {
                if (SegmentIntersects(routePoints[i], routePoints[i + 1], obstacle))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool SegmentIntersects(Point start, Point end, Rect rect)
    {
        if (start.X == end.X)
        {
            return start.X >= rect.X &&
                start.X <= rect.Right &&
                System.Math.Max(start.Y, end.Y) >= rect.Y &&
                System.Math.Min(start.Y, end.Y) <= rect.Bottom;
        }

        if (start.Y == end.Y)
        {
            return start.Y >= rect.Y &&
                start.Y <= rect.Bottom &&
                System.Math.Max(start.X, end.X) >= rect.X &&
                System.Math.Min(start.X, end.X) <= rect.Right;
        }

        return false;
    }

    private static Dictionary<string, EdgeRoute> BuildEdgeRoutes(
        IReadOnlyList<DependencyEdge> edges,
        Dictionary<string, Rect> positions)
    {
        var sourceGroups = new Dictionary<string, List<DependencyEdge>>();
        var targetGroups = new Dictionary<string, List<DependencyEdge>>();
        var laneGroups = new Dictionary<string, List<DependencyEdge>>();

        foreach (var edge in edges)
        {
            if (!positions.TryGetValue(edge.SourceId, out var source) ||
                !positions.TryGetValue(edge.TargetId, out var target))
            {
                continue;
            }

            AddToGroup(sourceGroups, PortKey(edge.SourceId, "bottom"), edge);
            AddToGroup(targetGroups, PortKey(edge.TargetId, "top"), edge);
            AddToGroup(laneGroups, LaneKey(source, target), edge);
        }

        var sourceOffsets = CalculatePortOffsets(sourceGroups);
        var targetOffsets = CalculatePortOffsets(targetGroups);
        var laneOffsets = CalculatePortOffsets(laneGroups);
        var routes = new Dictionary<string, EdgeRoute>();
        foreach (var edge in edges)
        {
            if (!positions.TryGetValue(edge.SourceId, out var source) ||
                !positions.TryGetValue(edge.TargetId, out var target))
            {
                continue;
            }

            var sourceOffset = sourceOffsets.TryGetValue(edge.Id, out var sourcePortOffset)
                ? sourcePortOffset
                : 0;
            var targetOffset = targetOffsets.TryGetValue(edge.Id, out var targetPortOffset)
                ? targetPortOffset
                : 0;
            var laneOffset = laneOffsets.TryGetValue(edge.Id, out var globalLaneOffset)
                ? globalLaneOffset
                : 0;
            var sourceLocalX = PortLocalX(source, sourceOffset);
            var targetLocalX = PortLocalX(target, targetOffset);
            var sourcePoint = new Point(SafeAdd(source.X, sourceLocalX), source.Bottom);
            var targetPoint = new Point(SafeAdd(target.X, targetLocalX), target.Y);

            routes[edge.Id] = new EdgeRoute(
                sourcePoint,
                targetPoint,
                sourceLocalX / (double)source.Width,
                targetLocalX / (double)target.Width,
                "1",
                "0",
                SafeAdd(sourceOffset, laneOffset));
        }

        return routes;

        static void AddToGroup(Dictionary<string, List<DependencyEdge>> groups, string key, DependencyEdge edge)
        {
            if (!groups.TryGetValue(key, out var group))
            {
                group = new List<DependencyEdge>();
                groups[key] = group;
            }

            group.Add(edge);
        }

        static string PortKey(string nodeId, string side)
        {
            return $"{nodeId}:{side}";
        }

        static string LaneKey(Rect source, Rect target)
        {
            var upper = System.Math.Min(source.Bottom, target.Y);
            var lower = System.Math.Max(source.Bottom, target.Y);
            return $"{upper}:{lower}";
        }
    }

    private static Dictionary<string, int> CalculateNodeWidths(
        ArchitectureGraph graph,
        LayoutSettings layout,
        HashSet<string> nodeIds)
    {
        var maxPortCounts = new Dictionary<string, int>();
        foreach (var group in graph.Edges
            .Where(e => nodeIds.Contains(e.SourceId) && nodeIds.Contains(e.TargetId))
            .GroupBy(e => e.SourceId))
        {
            maxPortCounts[group.Key] = System.Math.Max(
                maxPortCounts.TryGetValue(group.Key, out var count) ? count : 0,
                group.Count());
        }

        foreach (var group in graph.Edges
            .Where(e => nodeIds.Contains(e.SourceId) && nodeIds.Contains(e.TargetId))
            .GroupBy(e => e.TargetId))
        {
            maxPortCounts[group.Key] = System.Math.Max(
                maxPortCounts.TryGetValue(group.Key, out var count) ? count : 0,
                group.Count());
        }

        var widths = new Dictionary<string, int>();
        foreach (var type in graph.Projects.SelectMany(p => p.Types).Where(t => nodeIds.Contains(t.Id)))
        {
            widths[type.Id] = RequiredNodeWidth(type.Name, type.Id);
        }

        foreach (var external in graph.ExternalDependencies.Where(e => nodeIds.Contains(e.Id)))
        {
            widths[external.Id] = RequiredNodeWidth(external.Name, external.Id);
        }

        return widths;

        int RequiredNodeWidth(string label, string id)
        {
            var textWidth = SafeAdd(SafeMultiply(label.Length, ApproximateTextWidth), EdgePadding * 2);
            var portWidth = maxPortCounts.TryGetValue(id, out var count)
                ? SafeAdd(SafeMultiply(count, EdgeSpacing), EdgePadding * 2)
                : 0;
            return System.Math.Max(layout.NodeWidth, System.Math.Max(textWidth, portWidth));
        }
    }

    private static Dictionary<int, int> CalculateDepthOffsets(
        IReadOnlyList<DependencyEdge> edges,
        Dictionary<string, int> depths,
        HashSet<string> nodeIds,
        LayoutSettings layout)
    {
        var bandCounts = new Dictionary<int, int>();
        foreach (var edge in edges.Where(e => nodeIds.Contains(e.SourceId) && nodeIds.Contains(e.TargetId)))
        {
            if (!depths.TryGetValue(edge.SourceId, out var sourceDepth) ||
                !depths.TryGetValue(edge.TargetId, out var targetDepth))
            {
                continue;
            }

            var upperDepth = System.Math.Min(sourceDepth, targetDepth);
            var lowerDepth = System.Math.Max(sourceDepth, targetDepth);
            if (upperDepth == lowerDepth)
            {
                if (!bandCounts.ContainsKey(upperDepth))
                {
                    bandCounts[upperDepth] = 0;
                }

                bandCounts[upperDepth]++;
                continue;
            }

            for (var depth = upperDepth; depth < lowerDepth; depth++)
            {
                if (!bandCounts.ContainsKey(depth))
                {
                    bandCounts[depth] = 0;
                }

                bandCounts[depth]++;
            }
        }

        var maxDepth = depths.Values.DefaultIfEmpty(0).Max();
        var offsets = new Dictionary<int, int> { [0] = 0 };
        var cumulativeOffset = 0;
        for (var depth = 0; depth <= maxDepth; depth++)
        {
            var requiredBandHeight = bandCounts.TryGetValue(depth, out var count)
                ? SafeAdd(SafeMultiply(count, EdgeSpacing), EdgePadding * 4)
                : 0;
            var extraBandHeight = System.Math.Max(0, SafeSubtract(requiredBandHeight, layout.VerticalSpacing));
            cumulativeOffset = SafeAdd(cumulativeOffset, extraBandHeight);
            offsets[SafeAdd(depth, 1)] = cumulativeOffset;
        }

        return offsets;
    }

    private static int NodeTop(
        int originTop,
        int depth,
        LayoutSettings layout,
        Dictionary<int, int> depthOffsets)
    {
        return SafeAdd(
            originTop,
            SafeMultiply(depth, layout.NodeHeight + layout.VerticalSpacing),
            depthOffsets.TryGetValue(depth, out var offset) ? offset : 0);
    }

    private static Dictionary<string, int> CalculatePortOffsets(Dictionary<string, List<DependencyEdge>> groups)
    {
        var offsets = new Dictionary<string, int>();
        foreach (var group in groups.Values)
        {
            var start = -((group.Count - 1) * EdgeSpacing) / 2;
            for (var i = 0; i < group.Count; i++)
            {
                offsets[group[i].Id] = SafeAdd(start, i * EdgeSpacing);
            }
        }

        return offsets;
    }

    private static int PortLocalX(Rect node, int offset)
    {
        var min = System.Math.Min(EdgePadding, node.Width / 2);
        var max = System.Math.Max(min, SafeSubtract(node.Width, min));
        return System.Math.Min(max, System.Math.Max(min, SafeAdd(node.Width / 2, offset)));
    }

    private static Dictionary<string, int> CalculateDepths(
        ArchitectureGraph graph,
        Dictionary<string, TypeNode> types,
        HashSet<string> nodeIds,
        List<string> orderedNodeIds,
        Dictionary<string, List<string>> outgoing)
    {
        var components = FindComponents(orderedNodeIds, outgoing);
        var componentByNode = new Dictionary<string, int>();
        for (var i = 0; i < components.Count; i++)
        {
            foreach (var id in components[i])
            {
                componentByNode[id] = i;
            }
        }

        var componentOutgoing = new Dictionary<int, List<int>>();
        var componentOutgoingSeen = new Dictionary<int, HashSet<int>>();
        foreach (var edge in outgoing)
        {
            var sourceComponent = componentByNode[edge.Key];
            foreach (var targetId in edge.Value)
            {
                var targetComponent = componentByNode[targetId];
                if (sourceComponent == targetComponent)
                {
                    continue;
                }

                if (!componentOutgoing.TryGetValue(sourceComponent, out var targets))
                {
                    targets = new List<int>();
                    componentOutgoing[sourceComponent] = targets;
                    componentOutgoingSeen[sourceComponent] = new HashSet<int>();
                }

                if (componentOutgoingSeen[sourceComponent].Add(targetComponent))
                {
                    targets.Add(targetComponent);
                }
            }
        }

        var selectedProject = graph.Projects.FirstOrDefault();
        var selectedTypeIds = selectedProject is null
            ? new HashSet<string>()
            : new HashSet<string>(selectedProject.Types
                .Where(t => nodeIds.Contains(t.Id))
                .Select(t => t.Id));
        var selectedIncoming = new HashSet<string>(outgoing
            .Where(kv => selectedTypeIds.Contains(kv.Key))
            .SelectMany(kv => kv.Value)
            .Where(selectedTypeIds.Contains));
        var roots = selectedTypeIds
            .Where(id => !selectedIncoming.Contains(id))
            .OrderBy(id => orderedNodeIds.IndexOf(id))
            .ToList();

        var topLevelRoots = SelectTopLevelRoots(roots, types, outgoing);
        if (topLevelRoots.Count > 0)
        {
            roots = topLevelRoots;
        }

        if (roots.Count == 0)
        {
            roots = orderedNodeIds.Where(selectedTypeIds.Contains).ToList();
        }

        if (roots.Count == 0)
        {
            roots = orderedNodeIds.Where(nodeIds.Contains).ToList();
        }

        var componentDepths = new Dictionary<int, int>();
        var rootComponents = new HashSet<int>(roots.Select(id => componentByNode[id]));
        var queue = new Queue<int>();
        foreach (var root in roots)
        {
            var component = componentByNode[root];
            componentDepths[component] = 0;
            queue.Enqueue(component);
        }

        while (queue.Count > 0)
        {
            var sourceComponent = queue.Dequeue();
            if (!componentOutgoing.TryGetValue(sourceComponent, out var targets))
            {
                continue;
            }

            foreach (var targetComponent in targets)
            {
                if (rootComponents.Contains(targetComponent))
                {
                    continue;
                }

                if (componentDepths.ContainsKey(targetComponent))
                {
                    continue;
                }

                var nextDepth = SafeAdd(componentDepths[sourceComponent], 1);
                componentDepths[targetComponent] = nextDepth;
                queue.Enqueue(targetComponent);
            }
        }

        var depths = new Dictionary<string, int>();
        foreach (var componentDepth in componentDepths)
        {
            foreach (var id in components[componentDepth.Key])
            {
                depths[id] = componentDepth.Value;
            }
        }

        var fallbackDepth = componentDepths.Count == 0 ? 0 : SafeAdd(componentDepths.Values.Max(), 1);
        foreach (var project in graph.Projects)
        {
            var projectTypeIds = project.Types
                .Where(t => nodeIds.Contains(t.Id))
                .Select(t => t.Id)
                .ToList();
            var isSelectedProject = selectedProject is not null && project.Id == selectedProject.Id;
            var projectDepth = isSelectedProject
                ? fallbackDepth
                : projectTypeIds
                    .Where(depths.ContainsKey)
                    .Select(id => depths[id])
                    .DefaultIfEmpty(fallbackDepth)
                    .Min();

            foreach (var id in projectTypeIds)
            {
                if (!depths.ContainsKey(id))
                {
                    depths[id] = projectDepth;
                }
            }
        }

        foreach (var id in nodeIds)
        {
            if (!depths.ContainsKey(id))
            {
                depths[id] = fallbackDepth;
            }
        }

        return depths;
    }

    private static List<string> SelectTopLevelRoots(
        List<string> roots,
        Dictionary<string, TypeNode> types,
        Dictionary<string, List<string>> outgoing)
    {
        var rootsWithDependencies = roots
            .Where(id => outgoing.TryGetValue(id, out var targets) && targets.Count > 0)
            .ToList();
        if (rootsWithDependencies.Count == 0)
        {
            return roots;
        }

        var bestPriority = rootsWithDependencies
            .Select(id => types.TryGetValue(id, out var type) ? ArchitecturalPriority(type) : int.MaxValue)
            .Min();
        return bestPriority == int.MaxValue
            ? rootsWithDependencies
            : rootsWithDependencies
                .Where(id => types.TryGetValue(id, out var type) && ArchitecturalPriority(type) == bestPriority)
                .ToList();
    }

    private static int ArchitecturalPriority(TypeNode type)
    {
        var text = $"{type.FullName}.{type.Name}";
        if (text.Contains("Coordination"))
        {
            return 0;
        }

        if (text.Contains("Orchestration"))
        {
            return 1;
        }

        if (text.Contains("Processing"))
        {
            return 2;
        }

        if (text.Contains("Foundation"))
        {
            return 3;
        }

        if (text.Contains("Broker"))
        {
            return 4;
        }

        return int.MaxValue;
    }

    private static List<List<string>> FindComponents(
        List<string> nodeIds,
        Dictionary<string, List<string>> outgoing)
    {
        var index = 0;
        var stack = new Stack<string>();
        var indices = new Dictionary<string, int>();
        var lowLinks = new Dictionary<string, int>();
        var onStack = new HashSet<string>();
        var components = new List<List<string>>();

        foreach (var id in nodeIds)
        {
            if (!indices.ContainsKey(id))
            {
                Visit(id);
            }
        }

        return components;

        void Visit(string id)
        {
            indices[id] = index;
            lowLinks[id] = index;
            index++;
            stack.Push(id);
            onStack.Add(id);

            if (outgoing.TryGetValue(id, out var targets))
            {
                foreach (var targetId in targets)
                {
                    if (!indices.ContainsKey(targetId))
                    {
                        Visit(targetId);
                        lowLinks[id] = System.Math.Min(lowLinks[id], lowLinks[targetId]);
                    }
                    else if (onStack.Contains(targetId))
                    {
                        lowLinks[id] = System.Math.Min(lowLinks[id], indices[targetId]);
                    }
                }
            }

            if (lowLinks[id] != indices[id])
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
            while (current != id);

            components.Add(component);
        }
    }

    private static int SafeAdd(params int[] values)
    {
        long total = 0;
        foreach (var value in values)
        {
            total += value;
            if (total > MaxGeometryValue)
            {
                return MaxGeometryValue;
            }

            if (total < -MaxGeometryValue)
            {
                return -MaxGeometryValue;
            }
        }

        return (int)total;
    }

    private static int SafeSubtract(int value, params int[] subtract)
    {
        long total = value;
        foreach (var item in subtract)
        {
            total -= item;
            if (total > MaxGeometryValue)
            {
                return MaxGeometryValue;
            }

            if (total < -MaxGeometryValue)
            {
                return -MaxGeometryValue;
            }
        }

        return (int)total;
    }

    private static int SafeMultiply(int value, int multiplier)
    {
        var result = (long)value * multiplier;
        if (result > MaxGeometryValue)
        {
            return MaxGeometryValue;
        }

        if (result < -MaxGeometryValue)
        {
            return -MaxGeometryValue;
        }

        return (int)result;
    }

    private static string BuildNodeStyle(NodeStyle style)
    {
        var shape = style.Shape switch
        {
            "rounded" => "rounded=1;whiteSpace=wrap;html=1;",
            "rectangle" => "rounded=0;whiteSpace=wrap;html=1;",
            "cylinder" => "shape=cylinder3d;boundedLbl=1;backgroundOutline=1;size=15;whiteSpace=wrap;html=1;",
            "rhombus" => "shape=rhombus;whiteSpace=wrap;html=1;",
            "ellipse" => "shape=ellipse;whiteSpace=wrap;html=1;",
            "swimlane" => "shape=swimlane;whiteSpace=wrap;html=1;",
            _ => $"{style.Shape};whiteSpace=wrap;html=1;"
        };

        return $"{shape}fillColor={style.FillColor};strokeColor={style.StrokeColor};fontColor={style.FontColor};shadow={(style.Shadow ? 1 : 0)};{style.ExtraStyle}";
    }

    private static string BuildConnectorStyle(ConnectorStyle style, EdgeRoute route)
    {
        if (string.IsNullOrWhiteSpace(route.ExitY) || string.IsNullOrWhiteSpace(route.EntryY))
        {
            route = new EdgeRoute(default, default, 0.5, 0.5, "1", "0", 0);
        }

        return $"edgeStyle=orthogonalEdgeStyle;rounded={(style.Rounded ? 1 : 0)};orthogonalLoop=1;jettySize=auto;html=1;exitX={FormatRatio(route.ExitX)};exitY={route.ExitY};exitDx=0;exitDy=0;exitPerimeter=0;entryX={FormatRatio(route.EntryX)};entryY={route.EntryY};entryDx=0;entryDy=0;entryPerimeter=0;endArrow=block;endFill=1;strokeColor={style.StrokeColor};strokeWidth={style.StrokeWidth};";
    }

    private static string FormatRatio(double value)
    {
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private static bool TryFindOverlappingRect(Rect rect, List<Rect> candidates, out Rect overlap)
    {
        foreach (var candidate in candidates)
        {
            if (rect.Intersects(candidate))
            {
                overlap = candidate;
                return true;
            }
        }

        overlap = default;
        return false;
    }

    private readonly record struct Rect(int X, int Y, int Width, int Height)
    {
        public int Right => SafeAdd(X, Width);
        public int Bottom => SafeAdd(Y, Height);
        public int CenterX => SafeAdd(X, Width / 2);

        public bool Intersects(Rect other)
        {
            return X < other.Right &&
                Right > other.X &&
                Y < other.Bottom &&
                Bottom > other.Y;
        }

        public Rect Expand(int amount)
        {
            return new Rect(
                SafeSubtract(X, amount),
                SafeSubtract(Y, amount),
                SafeAdd(Width, amount * 2),
                SafeAdd(Height, amount * 2));
        }
    }

    private readonly record struct Point(int X, int Y);
    private readonly record struct EdgeRoute(
        Point SourcePoint,
        Point TargetPoint,
        double ExitX,
        double EntryX,
        string ExitY,
        string EntryY,
        int LaneOffset);
    private sealed record ProjectLayout(ProjectContainer Project, List<ProjectLayer> Layers, int Width)
    {
        public int FirstDepth { get; } = Layers.Min(l => l.Depth);
        public int LastDepth { get; } = Layers.Max(l => l.Depth);

        public int NaturalTop(
            int originTop,
            LayoutSettings layout,
            Dictionary<int, int> depthOffsets)
        {
            return SafeSubtract(
                NodeTop(originTop, FirstDepth, layout, depthOffsets),
                layout.ContainerPadding);
        }

        public int NaturalBottom(
            int originTop,
            LayoutSettings layout,
            Dictionary<int, int> depthOffsets)
        {
            return SafeAdd(
                NodeTop(originTop, LastDepth, layout, depthOffsets),
                layout.NodeHeight,
                layout.ContainerPadding);
        }
    }

    private sealed record ProjectLayer(int Depth, List<PositionedNode> Nodes);
    private sealed record PositionedNode(TypeNode Type, int X, int Depth, int Width);
    private sealed record SubtreeLayout(int Width, List<PositionedNode> Nodes);
}
