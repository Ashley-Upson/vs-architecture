using System;
using System.Collections.Generic;
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
        var diagnostics = new List<DiagramDiagnostic>();
        foreach (var finding in diagram.Diagnostics.Findings)
            diagnostics.Add(new DiagramDiagnostic(finding.Code, finding.Message, finding.SubjectId));
        return new DrawioPage("Architecture", "architecture", new XElement("mxGraphModel",
            new XAttribute("grid", "0"), new XAttribute("page", "0"),
            new XElement("root", new XElement("mxCell", new XAttribute("id", "0")),
                new XElement("mxCell", new XAttribute("id", "1"), new XAttribute("parent", "0")))), diagnostics);
    }
}
