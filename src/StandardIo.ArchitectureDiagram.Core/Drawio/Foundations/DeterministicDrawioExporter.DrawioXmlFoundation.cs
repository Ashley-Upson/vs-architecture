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
    private sealed partial class DiagramFileBuilder
    {
        private XElement GraphModel(XElement root)
        {
            return new XElement("mxGraphModel",
                new XAttribute("dx", "1200"),
                new XAttribute("dy", "900"),
                new XAttribute("grid", "0"),
                new XAttribute("gridSize", "10"),
                new XAttribute("guides", "1"),
                new XAttribute("tooltips", "1"),
                new XAttribute("connect", "1"),
                new XAttribute("arrows", "1"),
                new XAttribute("fold", "1"),
                new XAttribute("page", "0"),
                new XAttribute("pageScale", "1"),
                new XAttribute("pageWidth", "1600"),
                new XAttribute("pageHeight", "1200"),
                new XAttribute("background", _settings.Canvas.BackgroundColor),
                root);
        }

        private static string NodeLabel(RenderNode node)
        {
            return node.Interfaces.Count == 0
                ? node.Name
                : $"{node.Name} ({string.Join(", ", node.Interfaces)})";
        }

        private static string DataModelPropertyLabel(TypeProperty property)
        {
            return string.IsNullOrWhiteSpace(property.TypeName)
                ? property.Name
                : $"{property.TypeName}: {property.Name}";
        }

        private static string DataModelNodeId(string id) => $"data_model_{id}";

        private static string DataModelContainerStyle()
        {
            return "shape=rectangle;whiteSpace=wrap;html=1;fillColor=none;strokeColor=#9fb7d5;strokeWidth=1;";
        }

        private static string DataModelHeaderStyle()
        {
            return "shape=rectangle;whiteSpace=wrap;html=1;align=left;verticalAlign=middle;spacingLeft=8;fontStyle=1;fontColor=#ffffff;fillColor=#2f5f97;strokeColor=#9fb7d5;";
        }

        private static string DataModelRowStyle(int index)
        {
            var fill = index % 2 == 0 ? "#f7fbff" : "#e6f0fb";
            return $"shape=rectangle;whiteSpace=wrap;html=1;align=left;verticalAlign=middle;spacingLeft=8;fontColor=#111111;fillColor={fill};strokeColor=#9fb7d5;";
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
            return new TypeNode(node.Id, node.ProjectId ?? string.Empty, node.Name, node.FullName, node.Kind, Interfaces: node.Interfaces, Properties: node.Properties, MethodCount: node.MethodCount);
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
            return $"edgeStyle=orthogonalEdgeStyle;html=1;{rounded}orthogonalLoop=1;jettySize=auto;endArrow=block;endFill=1;strokeColor={style.StrokeColor};strokeWidth={style.StrokeWidth};exitX={FormatRatio(link.ExitX)};exitY={FormatRatio(link.ExitY)};entryX={FormatRatio(link.EntryX)};entryY={FormatRatio(link.EntryY)};";
        }

        private static string FormatRatio(double value)
        {
            return value.ToString("0.######", CultureInfo.InvariantCulture);
        }
    }
}
