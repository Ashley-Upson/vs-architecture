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

        foreach (var edge in graph.Edges)
        {
            root.Add(new XElement("mxCell",
                new XAttribute("id", edge.Id),
                new XAttribute("style", BuildConnectorStyle(settings.Connector)),
                new XAttribute("edge", "1"),
                new XAttribute("parent", "1"),
                new XAttribute("source", edge.SourceId),
                new XAttribute("target", edge.TargetId),
                new XElement("mxGeometry",
                    new XAttribute("relative", "1"),
                    new XAttribute("as", "geometry"))));
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
        var nodeIds = new HashSet<string>(types.Keys.Concat(externals.Keys));
        var displayNames = types.ToDictionary(kv => kv.Key, kv => kv.Value.Name);
        foreach (var external in externals.Values)
        {
            displayNames[external.Id] = external.Name;
        }

        var outgoing = graph.Edges
            .Where(e => nodeIds.Contains(e.SourceId) && nodeIds.Contains(e.TargetId))
            .GroupBy(e => e.SourceId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(e => e.TargetId)
                    .Distinct()
                    .OrderBy(id => displayNames.TryGetValue(id, out var name) ? name : id)
                    .ToList());
        var depths = CalculateDepths(graph, nodeIds, outgoing);
        var originTop = layout.ContainerPadding * 2 + 40;
        var originLeft = layout.ContainerPadding * 2;
        var projectLeft = originLeft;

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
                .ToList();
            var maxLayerCount = projectLayers.Max(g => g.Count());
            var projectWidth = SafeAdd(
                SafeMultiply(layout.ContainerPadding, 2),
                SafeMultiply(maxLayerCount, layout.NodeWidth),
                SafeMultiply(System.Math.Max(0, maxLayerCount - 1), layout.HorizontalSpacing));
            var firstDepth = projectLayers.Min(g => g.Key);
            var lastDepth = projectLayers.Max(g => g.Key);
            var projectTop = SafeSubtract(
                SafeAdd(originTop, SafeMultiply(firstDepth, layout.NodeHeight + layout.VerticalSpacing)),
                layout.ContainerPadding,
                40);
            var projectBottom = SafeAdd(
                originTop,
                SafeMultiply(lastDepth, layout.NodeHeight + layout.VerticalSpacing),
                layout.NodeHeight,
                layout.ContainerPadding);
            result[project.Id] = new Rect(projectLeft, projectTop, projectWidth, SafeSubtract(projectBottom, projectTop));

            foreach (var layerGroup in projectLayers)
            {
                var layer = layerGroup
                    .OrderBy(t => displayNames.TryGetValue(t.Id, out var name) ? name : t.Id)
                    .ToList();
                var y = SafeAdd(originTop, SafeMultiply(layerGroup.Key, layout.NodeHeight + layout.VerticalSpacing));

                for (var i = 0; i < layer.Count; i++)
                {
                    var x = SafeAdd(projectLeft, layout.ContainerPadding, SafeMultiply(i, layout.NodeWidth + layout.HorizontalSpacing));
                    result[layer[i].Id] = new Rect(x, y, layout.NodeWidth, layout.NodeHeight);
                }
            }

            projectLeft = SafeAdd(projectLeft, projectWidth, layout.HorizontalSpacing);
        }

        var externalLeft = projectLeft;
        foreach (var externalLayer in graph.ExternalDependencies
            .Where(e => nodeIds.Contains(e.Id))
            .GroupBy(e => depths.TryGetValue(e.Id, out var depth) ? depth : 0)
            .OrderBy(g => g.Key))
        {
            var layer = externalLayer
                .OrderBy(e => e.Name)
                .ToList();
            var y = SafeAdd(originTop, SafeMultiply(externalLayer.Key, layout.NodeHeight + layout.VerticalSpacing));
            for (var i = 0; i < layer.Count; i++)
            {
                var x = SafeAdd(externalLeft, SafeMultiply(i, layout.NodeWidth + layout.HorizontalSpacing));
                result[layer[i].Id] = new Rect(x, y, layout.NodeWidth, layout.NodeHeight);
            }
        }

        return result;
    }

    private static Dictionary<string, int> CalculateDepths(
        ArchitectureGraph graph,
        HashSet<string> nodeIds,
        Dictionary<string, List<string>> outgoing)
    {
        var components = FindComponents(nodeIds, outgoing);
        var componentByNode = new Dictionary<string, int>();
        for (var i = 0; i < components.Count; i++)
        {
            foreach (var id in components[i])
            {
                componentByNode[id] = i;
            }
        }

        var componentOutgoing = new Dictionary<int, HashSet<int>>();
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
                    targets = new HashSet<int>();
                    componentOutgoing[sourceComponent] = targets;
                }

                targets.Add(targetComponent);
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
            .OrderBy(id => id)
            .ToList();

        if (roots.Count == 0)
        {
            roots = selectedTypeIds.OrderBy(id => id).ToList();
        }

        if (roots.Count == 0)
        {
            roots = nodeIds.OrderBy(id => id).ToList();
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

        var maxDepth = System.Math.Max(0, components.Count - 1);
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

                var nextDepth = System.Math.Min(maxDepth, SafeAdd(componentDepths[sourceComponent], 1));
                if (componentDepths.TryGetValue(targetComponent, out var existingDepth) && nextDepth <= existingDepth)
                {
                    continue;
                }

                componentDepths[targetComponent] = nextDepth;
                if (nextDepth < maxDepth)
                {
                    queue.Enqueue(targetComponent);
                }
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
        HashSet<string> nodeIds,
        Dictionary<string, List<string>> outgoing)
    {
        var index = 0;
        var stack = new Stack<string>();
        var indices = new Dictionary<string, int>();
        var lowLinks = new Dictionary<string, int>();
        var onStack = new HashSet<string>();
        var components = new List<List<string>>();

        foreach (var id in nodeIds.OrderBy(id => id))
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
        return $"edgeStyle=orthogonalEdgeStyle;rounded={(style.Rounded ? 1 : 0)};orthogonalLoop=1;jettySize=auto;html=1;endArrow=block;endFill=1;strokeColor={style.StrokeColor};strokeWidth={style.StrokeWidth};";
    }

    private readonly record struct Rect(int X, int Y, int Width, int Height);
}
