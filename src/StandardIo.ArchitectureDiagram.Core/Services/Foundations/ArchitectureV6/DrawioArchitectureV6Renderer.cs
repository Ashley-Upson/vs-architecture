using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using StandardIo.ArchitectureDiagram.Core.Models.ArchitectureV6;
using StandardIo.ArchitectureDiagram.Core.Models.Drawios;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.ArchitectureV6;

public sealed class DrawioArchitectureV6Renderer : IArchitectureDiagramRenderer<DrawioPage>
{
    public DrawioPage Render(PlannedArchitectureDiagram diagram, ArchitectureRenderRequest request)
    {
        if (diagram is null) throw new ArgumentNullException(nameof(diagram));
        if (request is null) throw new ArgumentNullException(nameof(request));

        var diagnostics = diagram.Diagnostics.Findings
            .Select(finding => new DiagramDiagnostic(finding.Code, finding.Message, finding.SubjectId))
            .ToList();
        diagnostics.Add(new DiagramDiagnostic("V6PlacementDeferred", "The V6 renderer received no placed physical nodes; node emission is deferred.", null));
        diagnostics.Add(new DiagramDiagnostic("V6RoutingDeferred", "The V6 renderer received no abstract routes; edge emission is deferred.", null));
        diagnostics.Add(new DiagramDiagnostic("V6MinimalPage", "Only the minimal valid Draw.io page shell was emitted.", null));

        var root = new XElement("root",
            new XElement("mxCell", new XAttribute("id", "0")),
            new XElement("mxCell", new XAttribute("id", "1"), new XAttribute("parent", "0")));
        var graph = new XElement("mxGraphModel",
            new XAttribute("dx", "1200"),
            new XAttribute("dy", "900"),
            new XAttribute("grid", "0"),
            new XAttribute("gridSize", "10"),
            new XAttribute("page", "1"),
            new XAttribute("pageScale", "1"),
            new XAttribute("pageWidth", "1200"),
            new XAttribute("pageHeight", "900"),
            root);
        return new DrawioPage("Architecture", "architecture", graph, diagnostics);
    }
}
