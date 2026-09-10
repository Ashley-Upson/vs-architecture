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
    public byte[] Render(IReadOnlyList<ProjectModelDrawing> drawings)
    {
        XNamespace svg = "http://www.w3.org/2000/svg";
        double width = drawings.Select(d => d.X + d.Width + 40).DefaultIfEmpty(300).Max();
        double height = drawings.Select(d => d.Height + 80).DefaultIfEmpty(200).Max();
        var canvas = new XElement(svg + "svg", new XAttribute("width", width), new XAttribute("height", height),
            new XAttribute("viewBox", $"0 0 {Number(width)} {Number(height)}"), new XAttribute("role", "img"),
            new XElement(svg + "title", "Architecture diagram"),
            new XElement(svg + "defs",
                new XElement(svg + "marker", new XAttribute("id", "arrow"), new XAttribute("viewBox", "0 0 10 10"),
                    new XAttribute("refX", "10"), new XAttribute("refY", "5"), new XAttribute("markerWidth", "8"), new XAttribute("markerHeight", "8"), new XAttribute("orient", "auto"),
                    new XElement(svg + "path", new XAttribute("d", "M 0 0 L 10 5 L 0 10 z"), new XAttribute("fill", "#d1d5db"))),
                new XElement(svg + "marker", new XAttribute("id", "inheritance"), new XAttribute("viewBox", "0 0 10 10"),
                    new XAttribute("refX", "10"), new XAttribute("refY", "5"), new XAttribute("markerWidth", "10"), new XAttribute("markerHeight", "10"), new XAttribute("orient", "auto"),
                    new XElement(svg + "path", new XAttribute("d", "M 0 0 L 10 5 L 0 10 z"), new XAttribute("fill", "#374151"), new XAttribute("stroke", "#d1d5db")))));
        foreach (ProjectModelDrawing drawing in drawings)
        {
            var group = new XElement(svg + "g", new XAttribute("id", drawing.Id), new XAttribute("transform", $"translate({Number(drawing.X)} 40)"),
                Rectangle(svg, 0, 0, drawing.Width, drawing.Height, "scope"),
                new XElement(svg + "text", new XAttribute("x", "12"), new XAttribute("y", "26"), new XAttribute("class", "heading"), drawing.Model.Name ?? "Project"));
            var byName = drawing.Nodes.ToDictionary(n => n.Type.Name!);
            foreach (Dependency link in drawing.Model.Dependencies!)
            {
                DrawingNode from = byName[link.FromType!], to = byName[link.ToType!];
                double x1 = from.X + from.Width / 2, y1 = from.Y + from.Height;
                double x2 = to.X + to.Width / 2, y2 = to.Y, middle = (y1 + y2) / 2;
                string points = $"{Number(x1)},{Number(y1)} {Number(x1)},{Number(middle)} {Number(x2)},{Number(middle)} {Number(x2)},{Number(y2)}";
                bool inherited = link.DependencyType == DependencyType.Inheritance;
                group.Add(new XElement(svg + "polyline", new XAttribute("points", points), new XAttribute("class", inherited ? "link inherited" : "link"),
                    new XAttribute("data-from", link.FromType!), new XAttribute("data-to", link.ToType!),
                    new XAttribute("marker-end", inherited ? "url(#inheritance)" : "url(#arrow)")));
            }
            foreach (DrawingNode node in drawing.Nodes)
            {
                XElement rectangle = Rectangle(svg, node.X, node.Y, node.Width, node.Height, "node");
                rectangle.SetAttributeValue("fill", DiagramStyles.GetRoleColour(node.Type.Name!));
                var text = new XElement(svg + "text", new XAttribute("x", node.X + node.Width / 2), new XAttribute("class", "label"));
                string[] lines = node.Label.Split('\n');
                for (int index = 0; index < lines.Length; index++) text.Add(new XElement(svg + "tspan",
                    new XAttribute("x", node.X + node.Width / 2), new XAttribute("y", node.Y + node.Height / 2 + (index - (lines.Length - 1) / 2d) * 16), lines[index]));
                group.Add(new XElement(svg + "g", new XAttribute("data-type", node.Type.Name!), new XElement(svg + "title", node.Type.Name!), rectangle, text));
            }
            canvas.Add(group);
        }
        const string css = "html,body{margin:0;background:#111827;color:#fff;font-family:Arial,sans-serif}main{overflow:auto;min-height:100vh}svg{display:block}.scope{fill:#374151;stroke:#6b7280}.node{stroke:#111827}.heading{fill:#fff;font-size:16px}.label{fill:#fff;font-size:12px;text-anchor:middle;dominant-baseline:middle}.link{fill:none;stroke:#d1d5db;stroke-width:1.5}.inherited{stroke-dasharray:5 4}";
        var document = new XDocument(new XDocumentType("html", null, null, null),
            new XElement("html", new XAttribute("lang", "en"), new XElement("head",
                new XElement("meta", new XAttribute("charset", "utf-8")),
                new XElement("meta", new XAttribute("name", "viewport"), new XAttribute("content", "width=device-width,initial-scale=1")),
                new XElement("title", "Architecture diagram"), new XElement("style", css)), new XElement("body", new XElement("main", canvas))));
        return Encoding.UTF8.GetBytes(document.ToString(SaveOptions.DisableFormatting));
    }
    private static string Number(double value) => value.ToString(CultureInfo.InvariantCulture);
    private static XElement Rectangle(XNamespace svg, double x, double y, double width, double height, string style) => new(svg + "rect",
        new XAttribute("x", x), new XAttribute("y", y), new XAttribute("width", width), new XAttribute("height", height), new XAttribute("class", style));
}
