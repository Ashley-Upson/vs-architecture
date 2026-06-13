using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using StandardIo.ArchitectureDiagram.Core.Graph;
using StandardIo.ArchitectureDiagram.Core.Settings;

namespace StandardIo.ArchitectureDiagram.Core.Drawio;

public sealed class DrawioExporter
{
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
        var projectX = layout.ContainerPadding;
        var top = layout.ContainerPadding;

        foreach (var project in graph.Projects)
        {
            var ranks = RankTypes(project.Types, graph.Edges);
            var columns = project.Types
                .GroupBy(t => ranks.TryGetValue(t.Id, out var rank) ? rank : 0)
                .OrderBy(g => g.Key)
                .ToList();

            var columnCount = columns.Count == 0 ? 1 : columns.Count;
            var maxRows = columns.Count == 0 ? 1 : columns.Max(c => c.Count());
            var projectWidth = layout.ContainerPadding * 2
                + columnCount * layout.NodeWidth
                + (columnCount - 1) * layout.HorizontalSpacing;
            var projectHeight = layout.ContainerPadding * 2 + 40
                + maxRows * layout.NodeHeight
                + (maxRows - 1) * layout.VerticalSpacing;

            result[project.Id] = new Rect(projectX, top, projectWidth, projectHeight);

            foreach (var column in columns)
            {
                var colIndex = columns.IndexOf(column);
                var row = 0;
                foreach (var type in column.OrderBy(t => t.Name))
                {
                    var x = projectX + layout.ContainerPadding + colIndex * (layout.NodeWidth + layout.HorizontalSpacing);
                    var y = top + layout.ContainerPadding + 40 + row * (layout.NodeHeight + layout.VerticalSpacing);
                    result[type.Id] = new Rect(x, y, layout.NodeWidth, layout.NodeHeight);
                    row++;
                }
            }

            projectX += projectWidth + layout.HorizontalSpacing;
        }

        var externalTop = graph.Projects.Count == 0
            ? top
            : result.Values.Max(r => r.Y + r.Height) + layout.VerticalSpacing;
        var externalX = layout.ContainerPadding;

        foreach (var external in graph.ExternalDependencies.OrderBy(e => e.Name))
        {
            result[external.Id] = new Rect(externalX, externalTop, layout.NodeWidth, layout.NodeHeight);
            externalX += layout.NodeWidth + layout.HorizontalSpacing;
        }

        return result;
    }

    private static Dictionary<string, int> RankTypes(IReadOnlyList<TypeNode> types, IReadOnlyList<DependencyEdge> edges)
    {
        var ids = new HashSet<string>(types.Select(t => t.Id));
        var outgoing = edges
            .Where(e => ids.Contains(e.SourceId) && ids.Contains(e.TargetId))
            .GroupBy(e => e.SourceId)
            .ToDictionary(g => g.Key, g => g.Select(e => e.TargetId).Distinct().ToList());
        var memo = new Dictionary<string, int>();

        int Rank(string id, HashSet<string> visiting)
        {
            if (memo.TryGetValue(id, out var value))
            {
                return value;
            }

            if (!visiting.Add(id))
            {
                return 0;
            }

            var rank = outgoing.TryGetValue(id, out var targets) && targets.Count > 0
                ? 1 + targets.Max(target => Rank(target, visiting))
                : 0;

            visiting.Remove(id);
            memo[id] = rank;
            return rank;
        }

        foreach (var id in ids)
        {
            Rank(id, new HashSet<string>());
        }

        var maxRank = memo.Count == 0 ? 0 : memo.Values.Max();
        return memo.ToDictionary(kv => kv.Key, kv => maxRank - kv.Value);
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
