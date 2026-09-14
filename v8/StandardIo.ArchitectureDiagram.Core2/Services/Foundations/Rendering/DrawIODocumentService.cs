// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using StandardIo.ArchitectureDiagram.Core2.Models;

namespace StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Rendering;
internal sealed class DrawIODocumentService : IDrawIODocumentService
{
    public byte[] Render(RenderModel renderModel)
    {
        var root = new XElement("root", new XElement("mxCell", new XAttribute("id", "0")), new XElement("mxCell", new XAttribute("id", "1"), new XAttribute("parent", "0")));

        var locations=renderModel.Projects.SelectMany(p=>p.Nodes.Select(n=>(Project:p,Node:n))).ToDictionary(x=>x.Node.Id);
        foreach (RenderProject drawing in renderModel.Projects)
        {
            root.Add(content: new XElement("mxCell", new XAttribute("id", drawing.Id), new XAttribute("parent", "1"), new XAttribute("value", drawing.Name), new XAttribute("vertex", "1"), new XAttribute("style", "rounded=0;html=0;container=1;collapsible=0;fillColor=#263242;fontColor=#ffffff;align=left;verticalAlign=top;spacingLeft=12;spacingTop=10;fontSize=16;"), Geometry(x: drawing.X, y: drawing.Y, width: drawing.Width, height: drawing.Height)));

            foreach (RenderNode node in drawing.Nodes)
            {
                string fill = node.Fill;
                root.Add(content: new XElement("mxCell", new XAttribute("id", node.Id), new XAttribute("parent", drawing.Id), new XAttribute("value", NodeLabel(node)), new XAttribute("typeName", node.TypeName), new XAttribute("vertex", "1"), new XAttribute("style", "rounded=0;html=1;whiteSpace=wrap;fontColor=#ffffff;fontSize=12;fillColor=" + fill + ";"), Geometry(x: node.X, y: node.Y, width: node.Width, height: node.Height)));
            }


            foreach (RenderConnection route in drawing.Connections)
            {
                bool inheritance = route.Inheritance;
                var source = locations[route.SourceId].Node;
                var target = locations[route.TargetId].Node;
                string exitY=((route.Points[0].Y-source.Y)/source.Height).ToString(CultureInfo.InvariantCulture);
                string entryX=((route.Points[^1].X-target.X)/target.Width).ToString(CultureInfo.InvariantCulture);
                string entryY=((route.Points[^1].Y-target.Y)/target.Height).ToString(CultureInfo.InvariantCulture);
                string exitX = ((route.Points[0].X - source.X) / source.Width).ToString(CultureInfo.InvariantCulture);
                root.Add(content: new XElement("mxCell", new XAttribute("id", route.Id), new XAttribute("parent", drawing.Id), new XAttribute("edge", "1"), new XAttribute("value", route.Label ?? ""), new XAttribute("source", route.SourceId), new XAttribute("target", route.TargetId), new XAttribute("style", "edgeStyle=none;noEdgeStyle=1;rounded=0;html=0;strokeColor=" + route.Stroke + ";fontColor=#ffffff;labelBackgroundColor=#263242;exitX=" + exitX + (";exitY="+exitY+";entryX="+entryX+";entryY="+entryY+";") + "exitPerimeter=0;entryPerimeter=0;" + (route.IsTree ? "endArrow=none;" : inheritance ? "endArrow=block;endFill=0;dashed=1;" : route.IsComposition ? "endArrow=classic;dashed=1;dashPattern=2 4;" : "endArrow=classic;")), new XElement("mxGeometry", new XAttribute("relative", "1"), new XAttribute("as", "geometry"), new XElement("Array", new XAttribute("as", "points"), route.Points.Skip(count: 1).Take(count: route.Points.Length - 2).Select(point => new XElement("mxPoint", new XAttribute("x", point.X), new XAttribute("y", point.Y)))))));
            }
        }

        foreach (RenderConnection route in renderModel.CrossProjectConnections)
        {
            var owner = locations[route.SourceId].Project;
            var source = locations[route.SourceId].Node;
            string exitX = ((route.Points[0].X - owner.X - source.X) / source.Width).ToString(CultureInfo.InvariantCulture);
            var targetOwner = locations[route.TargetId].Project;
            var target = locations[route.TargetId].Node;
            string exitY = ((route.Points[0].Y-owner.Y-source.Y)/source.Height).ToString(CultureInfo.InvariantCulture);
            string entryX = ((route.Points[^1].X-targetOwner.X-target.X)/target.Width).ToString(CultureInfo.InvariantCulture);
            string entryY = ((route.Points[^1].Y-targetOwner.Y-target.Y)/target.Height).ToString(CultureInfo.InvariantCulture);
            root.Add(new XElement("mxCell", new XAttribute("id", route.Id), new XAttribute("parent", "1"),
                new XAttribute("edge", "1"), new XAttribute("value", route.Label ?? ""), new XAttribute("source", route.SourceId), new XAttribute("target", route.TargetId),
                new XAttribute("style", "edgeStyle=none;noEdgeStyle=1;rounded=0;strokeColor=" + route.Stroke + ";exitX=" + exitX + ";exitY=" + exitY + ";entryX=" + entryX + ";entryY=" + entryY + ";exitPerimeter=0;entryPerimeter=0;" + (route.IsTree ? "endArrow=none;" : route.Inheritance ? "endArrow=block;endFill=0;dashed=1;" : route.IsComposition ? "endArrow=classic;dashed=1;dashPattern=2 4;" : "endArrow=classic;")),
                new XElement("mxGeometry", new XAttribute("relative", "1"), new XAttribute("as", "geometry"),
                    new XElement("Array", new XAttribute("as", "points"), route.Points.Skip(1).Take(route.Points.Length - 2)
                        .Select(point => new XElement("mxPoint", new XAttribute("x", point.X), new XAttribute("y", point.Y)))))));
        }

        var file = new XDocument(new XElement("mxfile", new XAttribute("host", "app.diagrams.net"), new XElement("diagram", new XAttribute("id", renderModel.DiagramType.ToString()), new XAttribute("name", renderModel.DiagramType == DiagramTypes.DataModel ? "Entity Relationship" : renderModel.DiagramType.ToString()), new XElement("mxGraphModel", new XAttribute("grid", "0"), new XAttribute("page", "0"), new XAttribute("gridSize", "10"), root))));
        return Encoding.UTF8.GetBytes(s: file.ToString(options: SaveOptions.DisableFormatting));
    }

    private static string NodeLabel(RenderNode node)
    {
        string Span(RenderText line) => "<span style=\"font-size:" + line.FontSize.ToString(CultureInfo.InvariantCulture) + "px;font-weight:" + (line.Bold ? "bold" : "normal") + "\">" + System.Net.WebUtility.HtmlEncode(line.Text) + "</span>";
        if (!node.HasHeader) return string.Join("<br>",node.TextLines.Select(Span));
        return "<div style=\"text-align:left\"><div style=\"text-align:center;border-bottom:1px solid #94a3b8;padding-bottom:8px;margin-bottom:8px\">" + Span(node.TextLines[0]) + "</div>" + string.Join("<br>",node.TextLines.Skip(1).Select(Span)) + "</div>";
    }

    private static XElement Geometry(double x, double y, double width, double height) =>
        new("mxGeometry", new XAttribute("x", x), new XAttribute("y", y), new XAttribute("width", width), new XAttribute("height", height), new XAttribute("as", "geometry"));
}