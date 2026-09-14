// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using StandardIo.ArchitectureDiagram.Core2.Models;

namespace StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Rendering;
internal sealed partial class HtmlDocumentService : IHtmlDocumentService
{
    public byte[] Render(RenderModel renderModel)
    {
        XNamespace svg = "http://www.w3.org/2000/svg";

        double width = renderModel.Width, height = renderModel.Height;

        var canvas = new XElement(svg + "svg", new XAttribute("width", width), new XAttribute("height", height), new XAttribute("viewBox", $"0 0 {Number(value: width)} {Number(value: height)}"), new XAttribute("role", "img"), new XElement(svg + "title", renderModel.DiagramType == DiagramTypes.DataModel ? "Entity Relationship" : renderModel.DiagramType.ToString()), new XElement(svg + "defs", new XElement(svg + "marker", new XAttribute("id", "arrow"), new XAttribute("viewBox", "0 0 10 10"), new XAttribute("refX", "10"), new XAttribute("refY", "5"), new XAttribute("markerWidth", "8"), new XAttribute("markerHeight", "8"), new XAttribute("orient", "auto"), new XElement(svg + "path", new XAttribute("d", "M 0 0 L 10 5 L 0 10 z"), new XAttribute("fill", "context-stroke"))), new XElement(svg + "marker", new XAttribute("id", "inheritance"), new XAttribute("viewBox", "0 0 10 10"), new XAttribute("refX", "10"), new XAttribute("refY", "5"), new XAttribute("markerWidth", "10"), new XAttribute("markerHeight", "10"), new XAttribute("orient", "auto"), new XElement(svg + "path", new XAttribute("d", "M 0 0 L 10 5 L 0 10 z"), new XAttribute("fill", "#263242"), new XAttribute("stroke", "context-stroke")))));

        foreach (RenderProject drawing in renderModel.Projects)
        {
            var group = new XElement(svg + "g", new XAttribute("id", drawing.Id), new XAttribute("transform", $"translate({Number(value: drawing.X)} {Number(value: drawing.Y)})"), Rectangle(svg: svg, x: 0, y: 0, width: drawing.Width, height: drawing.Height, style: "scope"), new XElement(svg + "text", new XAttribute("x", "12"), new XAttribute("y", "26"), new XAttribute("class", "heading"), drawing.Name));
            foreach (RenderConnection route in drawing.Connections)
            {
                string points = string.Join(separator: " ", values: route.Points.Select(point => $"{Number(value: point.X)},{Number(value: point.Y)}"));
                bool inherited = route.Inheritance;
                group.Add(content: new XElement(svg + "polyline", new XElement(svg + "title", (route.IsComposition ? "Composition: " : "") + route.FromType + " → " + route.ToType), new XAttribute("points", points), new XAttribute("stroke", route.Stroke), new XAttribute("class", route.IsTree ? "link tree" : inherited ? "link inherited" : route.IsComposition ? "link composition" : "link"), new XAttribute("data-from", route.FromType), new XAttribute("data-to", route.ToType), new XAttribute("marker-end", route.IsTree ? "none" : inherited ? "url(#inheritance)" : "url(#arrow)")));
            }

            foreach (RenderNode node in drawing.Nodes)
            {
                XElement rectangle = Rectangle(svg: svg, x: node.X, y: node.Y, width: node.Width, height: node.Height, style: "node");
                rectangle.SetAttributeValue(name: "fill", value: node.Fill);
                var text = new XElement(svg + "text", new XAttribute("x", node.TextLines[0].X), new XAttribute("class", "label"));

                foreach (RenderText line in node.TextLines)
                {
                    text.Add(content: new XElement(svg + "tspan", new XAttribute("x", line.X), new XAttribute("y", line.Y), new XAttribute("text-anchor", node.HasHeader && !line.Bold ? "start" : "middle"), new XAttribute("font-weight", line.Bold ? "bold" : "normal"), new XAttribute("font-size", line.FontSize), line.Text));
                }

                group.Add(content: new XElement(svg + "g", new XAttribute("data-type", node.TypeName), new XElement(svg + "title", node.TypeName), rectangle, node.HasHeader ? new XElement(svg + "line", new XAttribute("x1",node.X),new XAttribute("x2",node.X+node.Width),new XAttribute("y1",node.Y+34),new XAttribute("y2",node.Y+34),new XAttribute("stroke","#94a3b8")) : null, text));
            }

            canvas.Add(content: group);
        }

        foreach (RenderConnection route in renderModel.CrossProjectConnections)
        {
            canvas.Add(new XElement(svg + "polyline", new XElement(svg + "title", (route.IsComposition ? "Composition: " : "") + route.FromType + " → " + route.ToType), new XAttribute("points", string.Join(" ", route.Points.Select(point => $"{Number(point.X)},{Number(point.Y)}"))),
                new XAttribute("stroke", route.Stroke), new XAttribute("class", route.Inheritance ? "link inherited" : route.IsComposition ? "link composition" : "link"),
                new XAttribute("data-from", route.FromType), new XAttribute("data-to", route.ToType),
                new XAttribute("marker-end", route.Inheritance ? "url(#inheritance)" : "url(#arrow)")));
        }

        foreach (RenderConnection route in renderModel.Projects.SelectMany(p => p.Connections).Concat(renderModel.CrossProjectConnections).Where(c => c.Label is not null))
        {
            var owner = renderModel.Projects.FirstOrDefault(p => p.Connections.Contains(route));
            var point = route.Points[1];
            canvas.Add(new XElement(svg + "text", new XAttribute("x", point.X + (owner?.X ?? 0) + 5), new XAttribute("y", point.Y + (owner?.Y ?? 0) - 5), new XAttribute("fill", "#ffffff"), new XAttribute("font-size", 11), route.Label));
        }
        const string css = "html,body{margin:0;background:#111827;color:#fff;font-family:Arial,sans-serif}body{height:100vh;display:flex;flex-direction:column}nav{display:flex;align-items:center;gap:8px;padding:10px;background:#1f2937;flex-wrap:wrap}button{background:#374151;color:white;border:1px solid #9ca3af;border-radius:4px;padding:6px 12px;cursor:pointer}button:focus-visible{outline:2px solid #60a5fa}main{overflow:auto;flex:1;min-height:0;cursor:grab;touch-action:none}main:active{cursor:grabbing}svg{display:block;max-width:none;user-select:none}.scope{fill:#263242;stroke:#6b7280}.node{stroke:#111827}.heading{fill:#fff;font-size:16px}.label{fill:#fff;font-size:12px;text-anchor:middle;dominant-baseline:middle}.link{fill:none;stroke-width:1.5}.inherited{stroke-dasharray:5 4}.composition{stroke-dasharray:2 4}";
        var document = new XDocument(new XDocumentType("html", null, null, null), new XElement("html", new XAttribute("lang", "en"), new XElement("head", new XElement("meta", new XAttribute("charset", "utf-8")), new XElement("meta", new XAttribute("name", "viewport"), new XAttribute("content", "width=device-width,initial-scale=1")), new XElement("title", renderModel.DiagramType == DiagramTypes.DataModel ? "Entity Relationship" : renderModel.DiagramType.ToString()), new XElement("style", css)), new XElement("body", new XElement("nav", new XAttribute("aria-label", "Diagram navigation"), Button("zoom-out", "−", "Zoom out"), Button("zoom-in", "+", "Zoom in"), Button("zoom-fit", "Fit", "Fit entire diagram"), Button("zoom-reset", "100%", "Actual size"), new XElement("output", new XAttribute("id", "zoom-level"), "100%"), new XElement("span", "Drag to pan · Ctrl + wheel to zoom · Hover a line for its endpoints")), new XElement("main", new XAttribute("id", "viewport"), new XAttribute("tabindex", "0"), new XAttribute("aria-label", "Diagram canvas"), canvas), new XElement("script", NavigationScript))));
        return Encoding.UTF8.GetBytes(s: document.ToString(options: SaveOptions.DisableFormatting));
    }

    private static string Number(double value) =>
        value.ToString(provider: CultureInfo.InvariantCulture);

    private static XElement Rectangle(XNamespace svg, double x, double y, double width, double height, string style) =>
        new(svg + "rect", new XAttribute("x", x), new XAttribute("y", y), new XAttribute("width", width), new XAttribute("height", height), new XAttribute("class", style));
}