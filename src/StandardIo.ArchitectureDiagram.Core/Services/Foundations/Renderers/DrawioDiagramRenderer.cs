using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using StandardIo.ArchitectureDiagram.Core.Models;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Renderers;

// Generic DiagramModel rendering remains available for the non-Architecture diagram path.
// Architecture generation is wired directly to DrawioArchitectureV6Renderer.
public sealed class DrawioDiagramRenderer : IDiagramRenderer
{
    public string RendererId => DiagramRendererIds.Drawio;
    public string DisplayName => "Draw.io";
    public string FileExtension => ".drawio";
    public string FileFilter => "Draw.io file (*.drawio)|*.drawio|All files (*.*)|*.*";

    public string Render(DiagramModel diagram, DiagramSettings settings)
    {
        if (diagram is null) throw new ArgumentNullException(nameof(diagram));
        var cells = new List<XElement> { new("mxCell", new XAttribute("id", "0")), new("mxCell", new XAttribute("id", "1"), new XAttribute("parent", "0")) };
        var known = new HashSet<string>(StringComparer.Ordinal);
        var y = 40;
        foreach (var project in diagram.Projects ?? Array.Empty<ProjectContainer>())
        {
            foreach (var node in project.Types ?? Array.Empty<TypeNode>())
            {
                known.Add(node.Id);
                cells.Add(Node(node.Id, node.Name, y));
                y += 80;
            }
        }
        foreach (var external in diagram.ExternalDependencies ?? Array.Empty<ExternalDependencyNode>())
        {
            known.Add(external.Id);
            cells.Add(Node(external.Id, external.Name, y));
            y += 80;
        }
        foreach (var edge in diagram.Edges ?? Array.Empty<DependencyEdge>())
        {
            if (!known.Contains(edge.SourceId) || !known.Contains(edge.TargetId)) continue;
            cells.Add(new XElement("mxCell", new XAttribute("id", edge.Id), new XAttribute("edge", "1"),
                new XAttribute("source", edge.SourceId), new XAttribute("target", edge.TargetId), new XAttribute("parent", "1")));
        }
        return new XElement("mxGraphModel", new XElement("root", cells)).ToString(SaveOptions.DisableFormatting);
    }

    private static XElement Node(string id, string value, int y) => new("mxCell",
        new XAttribute("id", id), new XAttribute("value", value), new XAttribute("vertex", "1"), new XAttribute("parent", "1"),
        new XElement("mxGeometry", new XAttribute("x", "40"), new XAttribute("y", y), new XAttribute("width", "180"), new XAttribute("height", "60"), new XAttribute("as", "geometry")));
}
