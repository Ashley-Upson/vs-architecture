// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using StandardIo.ArchitectureDiagram.Core2.Models;
namespace StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Rendering;
internal sealed class DrawIODocumentService : IDrawIODocumentService
{
    public byte[] Render(IReadOnlyList<ProjectModelDrawing> drawings)
    {
        var root = new XElement("root", new XElement("mxCell", new XAttribute("id", "0")),
            new XElement("mxCell", new XAttribute("id", "1"), new XAttribute("parent", "0")));
        foreach (ProjectModelDrawing drawing in drawings)
        {
            root.Add(new XElement("mxCell", new XAttribute("id", drawing.Id), new XAttribute("parent", "1"),
                new XAttribute("value", drawing.Model.Name ?? "Project"), new XAttribute("vertex", "1"),
                new XAttribute("style", "rounded=0;html=0;container=1;collapsible=0;fillColor=#374151;fontColor=#ffffff;align=left;verticalAlign=top;spacingLeft=12;spacingTop=10;fontSize=16;"),
                Geometry(drawing.X, 40, drawing.Width, drawing.Height)));
            var byName = drawing.Nodes.ToDictionary(keySelector: node => node.Type.Name!);
            foreach (DrawingNode node in drawing.Nodes)
            {
                string fill = DiagramStyles.GetRoleColour(name: node.Type.Name!);
                root.Add(new XElement("mxCell", new XAttribute("id", node.Id), new XAttribute("parent", drawing.Id),
                    new XAttribute("value", node.Label), new XAttribute("typeName", node.Type.Name!), new XAttribute("vertex", "1"),
                    new XAttribute("style", "rounded=0;html=0;whiteSpace=wrap;fontColor=#ffffff;fontSize=12;fillColor=" + fill + ";"),
                    Geometry(node.X, node.Y, node.Width, node.Height)));
            }
            int index = 0;
            foreach (Dependency link in drawing.Model.Dependencies ?? System.Array.Empty<Dependency>())
            {
                bool inheritance = link.DependencyType == DependencyType.Inheritance;
                root.Add(new XElement("mxCell", new XAttribute("id", drawing.Id + "-edge-" + index++),
                    new XAttribute("parent", drawing.Id), new XAttribute("edge", "1"), new XAttribute("value", ""),
                    new XAttribute("source", byName[link.FromType!].Id), new XAttribute("target", byName[link.ToType!].Id),
                    new XAttribute("style", "edgeStyle=orthogonalEdgeStyle;rounded=0;html=0;strokeColor=#d1d5db;fontColor=#ffffff;labelBackgroundColor=#374151;exitX=0.5;exitY=1;entryX=0.5;entryY=0;exitPerimeter=0;entryPerimeter=0;"
                        + (inheritance ? "endArrow=block;endFill=0;dashed=1;" : "endArrow=classic;")),
                    new XElement("mxGeometry", new XAttribute("relative", "1"), new XAttribute("as", "geometry"))));
            }
        }
        var file = new XDocument(new XElement("mxfile", new XAttribute("host", "app.diagrams.net"),
            new XElement("diagram", new XAttribute("id", "architecture"), new XAttribute("name", "Architecture"),
                new XElement("mxGraphModel", new XAttribute("grid", "1"), new XAttribute("gridSize", "10"), root))));
        return Encoding.UTF8.GetBytes(s: file.ToString(options: SaveOptions.DisableFormatting));
    }
    private static XElement Geometry(double x, double y, double width, double height) => new("mxGeometry",
        new XAttribute("x", x), new XAttribute("y", y), new XAttribute("width", width), new XAttribute("height", height), new XAttribute("as", "geometry"));
}
