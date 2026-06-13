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
        var incoming = new HashSet<string>(outgoing.Values.SelectMany(targets => targets));
        var roots = nodeIds
            .Where(id => !incoming.Contains(id))
            .OrderBy(id => displayNames.TryGetValue(id, out var name) ? name : id)
            .ToList();

        var placed = new HashSet<string>();
        var measuring = new HashSet<string>();
        var measured = new Dictionary<string, Size>();
        var x = layout.ContainerPadding * 2;
        var y = layout.ContainerPadding * 2 + 40;

        foreach (var root in roots)
        {
            var size = MeasureSubgraph(root);
            PlaceSubgraph(root, x, y);
            x = SafeAdd(x, size.Width, layout.HorizontalSpacing);
        }

        foreach (var id in nodeIds.OrderBy(id => displayNames.TryGetValue(id, out var name) ? name : id))
        {
            if (placed.Contains(id))
            {
                continue;
            }

            var size = MeasureSubgraph(id);
            PlaceSubgraph(id, x, y);
            x = SafeAdd(x, size.Width, layout.HorizontalSpacing);
        }

        foreach (var project in graph.Projects)
        {
            var typeRects = project.Types
                .Where(t => result.ContainsKey(t.Id))
                .Select(t => result[t.Id])
                .ToList();
            if (typeRects.Count == 0)
            {
                continue;
            }

            var left = SafeSubtract(typeRects.Min(r => r.X), layout.ContainerPadding);
            var top = SafeSubtract(typeRects.Min(r => r.Y), layout.ContainerPadding, 40);
            var right = SafeAdd(typeRects.Max(r => SafeAdd(r.X, r.Width)), layout.ContainerPadding);
            var bottom = SafeAdd(typeRects.Max(r => SafeAdd(r.Y, r.Height)), layout.ContainerPadding);
            result[project.Id] = new Rect(left, top, SafeSubtract(right, left), SafeSubtract(bottom, top));
        }

        return result;

        Size MeasureSubgraph(string id)
        {
            if (placed.Contains(id))
            {
                return new Size(layout.NodeWidth, layout.NodeHeight);
            }

            if (measured.TryGetValue(id, out var cached))
            {
                return cached;
            }

            if (!measuring.Add(id))
            {
                return new Size(layout.NodeWidth, layout.NodeHeight);
            }

            if (!outgoing.TryGetValue(id, out var targets) || targets.Count == 0)
            {
                measuring.Remove(id);
                var leaf = new Size(layout.NodeWidth, layout.NodeHeight);
                measured[id] = leaf;
                return leaf;
            }

            var childSizes = targets.Select(MeasureSubgraph).ToList();
            var childrenWidth = SafeAdd(
                SafeSum(childSizes.Select(s => s.Width)),
                SafeMultiply(layout.HorizontalSpacing, childSizes.Count - 1));
            var childrenHeight = childSizes.Max(s => s.Height);

            measuring.Remove(id);
            var size = new Size(
                System.Math.Max(layout.NodeWidth, childrenWidth),
                SafeAdd(layout.NodeHeight, layout.VerticalSpacing, childrenHeight));
            measured[id] = size;
            return size;
        }

        void PlaceSubgraph(string id, int left, int top)
        {
            if (placed.Contains(id))
            {
                return;
            }

            var size = MeasureSubgraph(id);
            var nodeX = SafeAdd(left, (size.Width - layout.NodeWidth) / 2);
            result[id] = new Rect(nodeX, top, layout.NodeWidth, layout.NodeHeight);
            placed.Add(id);

            if (!outgoing.TryGetValue(id, out var targets) || targets.Count == 0)
            {
                return;
            }

            var childSizes = targets.Select(MeasureSubgraph).ToList();
            var childrenWidth = SafeAdd(
                SafeSum(childSizes.Select(s => s.Width)),
                SafeMultiply(layout.HorizontalSpacing, childSizes.Count - 1));
            var childX = SafeAdd(left, (size.Width - childrenWidth) / 2);
            var childY = SafeAdd(top, layout.NodeHeight, layout.VerticalSpacing);

            for (var i = 0; i < targets.Count; i++)
            {
                PlaceSubgraph(targets[i], childX, childY);
                childX = SafeAdd(childX, childSizes[i].Width, layout.HorizontalSpacing);
            }
        }
    }

    private static int SafeSum(IEnumerable<int> values)
    {
        long total = 0;
        foreach (var value in values)
        {
            total += value;
            if (total > MaxGeometryValue)
            {
                return MaxGeometryValue;
            }
        }

        return (int)total;
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

    private readonly record struct Size(int Width, int Height);
}
