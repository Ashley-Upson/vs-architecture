using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using StandardIo.ArchitectureDiagram.Core.Graph;
using StandardIo.ArchitectureDiagram.Core.Settings;

namespace StandardIo.ArchitectureDiagram.Core.Drawio;

public sealed class DrawioExporter
{
    private const int MaxGeometryValue = 1_000_000_000;

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

        foreach (var edge in graph.Edges)
        {
            root.Add(new XElement("mxCell",
                new XAttribute("id", edge.Id),
                new XAttribute("style", BuildConnectorStyle(settings.Connector)),
                new XAttribute("edge", "1"),
                new XAttribute("parent", "1"),
                new XAttribute("source", edge.SourceId),
                new XAttribute("target", edge.TargetId),
                BuildEdgeGeometry(edge, positions, routeObstacleIds, settings.Layout)));
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

        var outgoing = graph.Edges
            .Where(e => nodeIds.Contains(e.SourceId) && nodeIds.Contains(e.TargetId))
            .GroupBy(e => e.SourceId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(e => e.TargetId)
                    .Distinct()
                    .OrderBy(id => nodeOrder.TryGetValue(id, out var index) ? index : int.MaxValue)
                    .ToList());
        var depths = CalculateDepths(graph, nodeIds, orderedNodeIds, outgoing);
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

            var projectLayers = projectTypes
                .GroupBy(t => depths.TryGetValue(t.Id, out var depth) ? depth : 0)
                .OrderBy(g => g.Key)
                .Select(g => new ProjectLayer(g.Key, g.ToList()))
                .ToList();
            var maxLayerCount = projectLayers.Max(g => g.Nodes.Count);
            var projectWidth = SafeAdd(
                SafeMultiply(layout.ContainerPadding, 2),
                SafeMultiply(maxLayerCount, layout.NodeWidth),
                SafeMultiply(System.Math.Max(0, maxLayerCount - 1), layout.HorizontalSpacing));
            projectLayouts.Add(new ProjectLayout(project, projectLayers, projectWidth));
        }

        var rowNextLeft = new Dictionary<int, int>();
        var rowVerticalOffsets = new Dictionary<int, int>();
        var previousProjectRowBottom = int.MinValue;
        foreach (var projectRow in projectLayouts
            .GroupBy(p => p.FirstDepth)
            .OrderBy(g => g.Key))
        {
            var naturalRowTop = projectRow.Min(p => p.NaturalTop(originTop, layout));
            var rowTop = previousProjectRowBottom == int.MinValue
                ? naturalRowTop
                : System.Math.Max(naturalRowTop, SafeAdd(previousProjectRowBottom, layout.VerticalSpacing));
            var rowVerticalOffset = SafeSubtract(rowTop, naturalRowTop);
            var projectLeft = originLeft;
            var projectRowBottom = rowTop;

            foreach (var projectLayout in projectRow)
            {
                var projectTop = SafeAdd(projectLayout.NaturalTop(originTop, layout), rowVerticalOffset);
                var projectBottom = SafeAdd(projectLayout.NaturalBottom(originTop, layout), rowVerticalOffset);
                result[projectLayout.Project.Id] = new Rect(
                    projectLeft,
                    projectTop,
                    projectLayout.Width,
                    SafeSubtract(projectBottom, projectTop));
                projectRowBottom = System.Math.Max(projectRowBottom, projectBottom);

                foreach (var layerGroup in projectLayout.Layers)
                {
                    var y = SafeAdd(
                        originTop,
                        SafeMultiply(layerGroup.Depth, layout.NodeHeight + layout.VerticalSpacing),
                        rowVerticalOffset);

                    for (var i = 0; i < layerGroup.Nodes.Count; i++)
                    {
                        var x = SafeAdd(projectLeft, layout.ContainerPadding, SafeMultiply(i, layout.NodeWidth + layout.HorizontalSpacing));
                        result[layerGroup.Nodes[i].Id] = new Rect(x, y, layout.NodeWidth, layout.NodeHeight);
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
                originTop,
                SafeMultiply(externalLayer.Key, layout.NodeHeight + layout.VerticalSpacing),
                rowVerticalOffsets.TryGetValue(externalLayer.Key, out var rowOffset) ? rowOffset : 0);
            var externalLeft = rowNextLeft.TryGetValue(externalLayer.Key, out var rowLeft)
                ? rowLeft
                : originLeft;
            var nextExternalLeft = externalLeft;
            for (var i = 0; i < layer.Count; i++)
            {
                var position = new Rect(nextExternalLeft, y, layout.NodeWidth, layout.NodeHeight);
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

    private static XElement BuildEdgeGeometry(
        DependencyEdge edge,
        Dictionary<string, Rect> positions,
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
        var points = RouteEdge(source, target, obstacles, layout);
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
        List<Rect> obstacles,
        LayoutSettings layout)
    {
        var sourcePoint = new Point(source.CenterX, source.Bottom);
        var targetPoint = new Point(target.CenterX, target.Y);
        var gap = System.Math.Max(20, layout.VerticalSpacing / 2);
        var midY = target.Y >= source.Bottom
            ? SafeAdd(source.Bottom, System.Math.Max(gap, SafeSubtract(target.Y, source.Bottom) / 2))
            : SafeAdd(System.Math.Max(source.Bottom, target.Bottom), gap);
        var directPoints = new List<Point>
        {
            new(sourcePoint.X, midY),
            new(targetPoint.X, midY)
        };

        if (!RouteIntersects(sourcePoint, targetPoint, directPoints, obstacles))
        {
            return directPoints;
        }

        var bandTop = System.Math.Min(source.Y, target.Y);
        var bandBottom = System.Math.Max(source.Bottom, target.Bottom);
        var laneX = obstacles
            .Where(o => o.Bottom >= bandTop && o.Y <= bandBottom)
            .Select(o => o.Right)
            .DefaultIfEmpty(System.Math.Max(source.Right, target.Right))
            .Max();
        laneX = SafeAdd(laneX, layout.HorizontalSpacing);

        var sourceLaneY = SafeAdd(source.Bottom, gap);
        var targetLaneY = SafeSubtract(target.Y, gap);
        return new List<Point>
        {
            new(sourcePoint.X, sourceLaneY),
            new(laneX, sourceLaneY),
            new(laneX, targetLaneY),
            new(targetPoint.X, targetLaneY)
        };
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

    private static Dictionary<string, int> CalculateDepths(
        ArchitectureGraph graph,
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
            var projectDepth = projectTypeIds
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

    private static string BuildConnectorStyle(ConnectorStyle style)
    {
        return $"edgeStyle=orthogonalEdgeStyle;rounded={(style.Rounded ? 1 : 0)};orthogonalLoop=1;jettySize=auto;html=1;exitX=0.5;exitY=1;exitDx=0;exitDy=0;exitPerimeter=0;entryX=0.5;entryY=0;entryDx=0;entryDy=0;entryPerimeter=0;endArrow=block;endFill=1;strokeColor={style.StrokeColor};strokeWidth={style.StrokeWidth};";
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
    private sealed record ProjectLayout(ProjectContainer Project, List<ProjectLayer> Layers, int Width)
    {
        public int FirstDepth { get; } = Layers.Min(l => l.Depth);
        public int LastDepth { get; } = Layers.Max(l => l.Depth);

        public int NaturalTop(int originTop, LayoutSettings layout)
        {
            return SafeSubtract(
                SafeAdd(originTop, SafeMultiply(FirstDepth, layout.NodeHeight + layout.VerticalSpacing)),
                layout.ContainerPadding);
        }

        public int NaturalBottom(int originTop, LayoutSettings layout)
        {
            return SafeAdd(
                originTop,
                SafeMultiply(LastDepth, layout.NodeHeight + layout.VerticalSpacing),
                layout.NodeHeight,
                layout.ContainerPadding);
        }
    }

    private sealed record ProjectLayer(int Depth, List<TypeNode> Nodes);
}
