using System;
using System.Linq;
using System.Xml.Linq;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core.Tests;

public sealed class DrawioDiagnosticReportBuilderTests
{
    [Fact]
    public void Annotate_adds_diagnostic_overlay_and_page_without_changing_edges()
    {
        const string content =
            "<mxfile><diagram name=\"Architecture\"><mxGraphModel><root>" +
            "<mxCell id=\"0\"/><mxCell id=\"1\" parent=\"0\"/>" +
            "<mxCell id=\"edge\" edge=\"1\" parent=\"1\"><mxGeometry relative=\"1\" as=\"geometry\"/></mxCell>" +
            "</root></mxGraphModel></diagram></mxfile>";
        var violations = new[]
        {
            new TraceabilityViolation(
                TraceabilityViolationCode.NodeCollision,
                "edge",
                null,
                1,
                "Edge intersects node.",
                "node",
                new[] { new Point(50, 60) },
                new[] { new Segment(new Point(20, 60), new Point(80, 60)) })
        };

        var annotated = DrawioDiagnosticReportBuilder.Annotate(content, violations, "Node intersections");
        var document = XDocument.Parse(annotated);

        Assert.Equal("true", (string?)document.Root!.Attribute("diagnostic"));
        Assert.Equal(2, document.Root.Elements("diagram").Count());
        Assert.Single(document.Descendants("mxCell"), cell => (string?)cell.Attribute("id") == "edge");
        Assert.Equal(2, document.Descendants("mxCell").Count(cell =>
            ((string?)cell.Attribute("id"))?.StartsWith("diagnostic_marker_", StringComparison.Ordinal) == true));
    }
}
