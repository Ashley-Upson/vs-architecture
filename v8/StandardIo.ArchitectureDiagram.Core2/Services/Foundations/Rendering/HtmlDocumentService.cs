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
internal sealed class HtmlDocumentService : IHtmlDocumentService
{
    public byte[] Render(RenderModel renderModel)
    {
        XNamespace svg = "http://www.w3.org/2000/svg";

        double width = renderModel.Width, height = renderModel.Height;

        var canvas = new XElement(svg + "svg", new XAttribute("width", width), new XAttribute("height", height), new XAttribute("viewBox", $"0 0 {Number(value: width)} {Number(value: height)}"), new XAttribute("role", "img"), new XElement(svg + "title", "Architecture diagram"), new XElement(svg + "defs", new XElement(svg + "marker", new XAttribute("id", "arrow"), new XAttribute("viewBox", "0 0 10 10"), new XAttribute("refX", "10"), new XAttribute("refY", "5"), new XAttribute("markerWidth", "8"), new XAttribute("markerHeight", "8"), new XAttribute("orient", "auto"), new XElement(svg + "path", new XAttribute("d", "M 0 0 L 10 5 L 0 10 z"), new XAttribute("fill", "context-stroke"))), new XElement(svg + "marker", new XAttribute("id", "inheritance"), new XAttribute("viewBox", "0 0 10 10"), new XAttribute("refX", "10"), new XAttribute("refY", "5"), new XAttribute("markerWidth", "10"), new XAttribute("markerHeight", "10"), new XAttribute("orient", "auto"), new XElement(svg + "path", new XAttribute("d", "M 0 0 L 10 5 L 0 10 z"), new XAttribute("fill", "#263242"), new XAttribute("stroke", "context-stroke")))));

        foreach (RenderProject drawing in renderModel.Projects)
        {
            var group = new XElement(svg + "g", new XAttribute("id", drawing.Id), new XAttribute("transform", $"translate({Number(value: drawing.X)} {Number(value: drawing.Y)})"), Rectangle(svg: svg, x: 0, y: 0, width: drawing.Width, height: drawing.Height, style: "scope"), new XElement(svg + "text", new XAttribute("x", "12"), new XAttribute("y", "26"), new XAttribute("class", "heading"), drawing.Name));
            foreach (RenderConnection route in drawing.Connections)
            {
                string points = string.Join(separator: " ", values: route.Points.Select(point => $"{Number(value: point.X)},{Number(value: point.Y)}"));
                bool inherited = route.Inheritance;
                group.Add(content: new XElement(svg + "polyline", new XAttribute("points", points), new XAttribute("stroke", route.Stroke), new XAttribute("class", inherited ? "link inherited" : route.IsDeferred ? "link deferred" : "link"), new XAttribute("data-from", route.FromType), new XAttribute("data-to", route.ToType), new XAttribute("marker-end", inherited ? "url(#inheritance)" : "url(#arrow)")));
            }

            foreach (RenderNode node in drawing.Nodes)
            {
                XElement rectangle = Rectangle(svg: svg, x: node.X, y: node.Y, width: node.Width, height: node.Height, style: "node");
                rectangle.SetAttributeValue(name: "fill", value: node.Fill);
                var text = new XElement(svg + "text", new XAttribute("x", node.TextLines[0].X), new XAttribute("class", "label"));

                foreach (RenderText line in node.TextLines)
                {
                    text.Add(content: new XElement(svg + "tspan", new XAttribute("x", line.X), new XAttribute("y", line.Y), new XAttribute("font-weight", line.Bold ? "bold" : "normal"), new XAttribute("font-size", line.FontSize), line.Text));
                }

                group.Add(content: new XElement(svg + "g", new XAttribute("data-type", node.TypeName), new XElement(svg + "title", node.TypeName), rectangle, text));
            }

            canvas.Add(content: group);
        }

        foreach (RenderConnection route in renderModel.CrossProjectConnections)
        {
            canvas.Add(new XElement(svg + "polyline", new XAttribute("points", string.Join(" ", route.Points.Select(point => $"{Number(point.X)},{Number(point.Y)}"))),
                new XAttribute("stroke", route.Stroke), new XAttribute("class", route.Inheritance ? "link inherited" : route.IsDeferred ? "link deferred" : "link"),
                new XAttribute("data-from", route.FromType), new XAttribute("data-to", route.ToType),
                new XAttribute("marker-end", route.Inheritance ? "url(#inheritance)" : "url(#arrow)")));
        }

        const string css = "html,body{margin:0;background:#111827;color:#fff;font-family:Arial,sans-serif}main{overflow:auto;min-height:100vh}svg{display:block}.scope{fill:#263242;stroke:#6b7280}.node{stroke:#111827}.heading{fill:#fff;font-size:16px}.label{fill:#fff;font-size:12px;text-anchor:middle;dominant-baseline:middle}.link{fill:none;stroke-width:1.5}.inherited,.deferred{stroke-dasharray:5 4}";
        var document = new XDocument(new XDocumentType("html", null, null, null), new XElement("html", new XAttribute("lang", "en"), new XElement("head", new XElement("meta", new XAttribute("charset", "utf-8")), new XElement("meta", new XAttribute("name", "viewport"), new XAttribute("content", "width=device-width,initial-scale=1")), new XElement("title", "Architecture diagram"), new XElement("style", css)), new XElement("body", new XElement("main", canvas))));
        return Encoding.UTF8.GetBytes(s: document.ToString(options: SaveOptions.DisableFormatting));
    }

    private static string Number(double value) =>
        value.ToString(provider: CultureInfo.InvariantCulture);

    private static XElement Rectangle(XNamespace svg, double x, double y, double width, double height, string style) =>
        new(svg + "rect", new XAttribute("x", x), new XAttribute("y", y), new XAttribute("width", width), new XAttribute("height", height), new XAttribute("class", style));
}