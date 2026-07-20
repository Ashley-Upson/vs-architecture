using System.Xml.Linq;
using StandardIo.ArchitectureDiagram.Core.Models;
using StandardIo.ArchitectureDiagram.Core.Models.Architectures;
using StandardIo.ArchitectureDiagram.Core.Models.Drawios;
using StandardIo.ArchitectureDiagram.Core.Models.Generation;
using StandardIo.ArchitectureDiagram.Core.Services.Processings.Drawios;
using Xunit;
using ArchitectureDiagramModel = StandardIo.ArchitectureDiagram.Core.Models.Architectures.ArchitectureDiagram;

namespace StandardIo.ArchitectureDiagram.Core.Tests;

public sealed class ArchitectureGeometryAnalyserTests
{
    [Fact]
    public void Analyses_structured_page_and_route_geometry_deterministically()
    {
        var result = Result(Page(
            Node("a", 10, 10, 80, 40),
            Node("b", 140, 100, 80, 40),
            Edge("edge", "a", "b")),
            new GeneratedRoute("edge", [new ValidationPoint(50, 50), new ValidationPoint(50, 80), new ValidationPoint(180, 80), new ValidationPoint(180, 100)]));
        var analyser = new ArchitectureGeometryAnalyser();

        var first = analyser.Analyse(result);
        var second = analyser.Analyse(result);

        Assert.Equal(2, first.Summary.RenderedNodeCount);
        Assert.Equal(180, Assert.Single(first.Routes).Length);
        Assert.Equal(2, Assert.Single(first.Routes).BendCount);
        Assert.Equal(first.Summary.AnalysisSha256, second.Summary.AnalysisSha256);
        Assert.Empty(first.Findings);
        Assert.Contains("Hard findings: 0", analyser.ToMarkdown(first));
    }

    [Fact]
    public void Detects_node_overlap_link_node_intersection_shared_segment_and_diagonal()
    {
        var result = Result(Page(
                Node("a", 0, 0, 40, 40),
                Node("overlap", 20, 20, 40, 40),
                Node("obstacle", 80, 0, 40, 40),
                Node("target", 160, 80, 40, 40),
                Edge("one", "a", "target"), Edge("two", "overlap", "target")),
            new GeneratedRoute("one", [new ValidationPoint(20, 40), new ValidationPoint(100, 40), new ValidationPoint(100, 80), new ValidationPoint(180, 80)]),
            new GeneratedRoute("two", [new ValidationPoint(40, 60), new ValidationPoint(100, 60), new ValidationPoint(100, 80), new ValidationPoint(180, 90)]));

        var analysis = new ArchitectureGeometryAnalyser().Analyse(result);

        Assert.Contains(analysis.Findings, finding => finding.Code == "NodeOverlap");
        Assert.Contains(analysis.Findings, finding => finding.Code == "LinkNodeIntersection");
        Assert.Contains(analysis.Findings, finding => finding.Code == "SharedSegment");
        Assert.Contains(analysis.Findings, finding => finding.Code == "DiagonalSegment");
        Assert.True(analysis.Summary.HardFindingCount >= 4);
    }

    private static TypedArchitectureGenerationResult Result(DrawioPage page, params GeneratedRoute[] routes)
    {
        var diagram = new ArchitectureDiagramModel(
            [new ArchitectureProject("project", "Project", [
                new ArchitectureNode("a", "project", "A", "Fixture.A", "Class", "", []),
                new ArchitectureNode("target", "project", "Target", "Fixture.Target", "Class", "", [])], "")],
            [], routes.Select(route => new ArchitectureLink(route.LogicalRouteId, "a", "target", "internal")).ToArray(), null);
        return new TypedArchitectureGenerationResult(diagram, page, [], [], [], [], routes, [],
            new ArchitectureGenerationManifest(1, 2, routes.Length, routes.Length, 0, 0, "architecture"),
            new ArchitectureEligibilityResult(true, []),
            () => new DrawioDiagnosticExportResult("", "{}", new Dictionary<string, string>(), 0, 0), null, null);
    }

    private static DrawioPage Page(params XElement[] cells) => new("Architecture", "architecture",
        new XElement("mxGraphModel", new XElement("root", new XElement("mxCell", new XAttribute("id", "0")),
            new XElement("mxCell", new XAttribute("id", "1"), new XAttribute("parent", "0")), cells)), []);

    private static XElement Node(string id, int x, int y, int width, int height) => new("mxCell",
        new XAttribute("id", id), new XAttribute("value", id), new XAttribute("vertex", "1"), new XAttribute("parent", "1"),
        new XElement("mxGeometry", new XAttribute("x", x), new XAttribute("y", y), new XAttribute("width", width),
            new XAttribute("height", height), new XAttribute("as", "geometry")));

    private static XElement Edge(string id, string source, string target) => new("mxCell",
        new XAttribute("id", id), new XAttribute("logicalEdgeId", id), new XAttribute("segmentIndex", "0"),
        new XAttribute("edge", "1"), new XAttribute("parent", "1"), new XAttribute("source", source), new XAttribute("target", target),
        new XElement("mxGeometry", new XAttribute("relative", "1"), new XAttribute("as", "geometry")));
}
