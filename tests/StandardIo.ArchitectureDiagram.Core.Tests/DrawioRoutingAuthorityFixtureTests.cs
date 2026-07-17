using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core.Tests;

public sealed class DrawioRoutingAuthorityFixtureTests
{
    [Fact]
    public void Fixture_contains_equivalent_waypoints_for_each_routing_style()
    {
        var path = Path.Combine(
            System.AppContext.BaseDirectory,
            "Fixtures",
            "routing-authority.drawio");
        var document = XDocument.Load(path);
        var edges = document
            .Descendants("mxCell")
            .Where(cell => (string?)cell.Attribute("edge") == "1")
            .ToDictionary(cell => (string)cell.Attribute("id")!);

        Assert.Equal(3, edges.Count);
        Assert.Contains("edgeStyle=orthogonalEdgeStyle", Style(edges["edge-orthogonal"]));
        Assert.Contains("edgeStyle=segmentEdgeStyle", Style(edges["edge-segment"]));
        Assert.Contains("noEdgeStyle=1", Style(edges["edge-manual"]));

        var normalizedRoutes = edges.Values
            .Select(edge => edge
                .Descendants("mxPoint")
                .Select(point => new
                {
                    X = (int)point.Attribute("x")!,
                    Y = (int)point.Attribute("y")! - RouteOffset(edge)
                })
                .ToArray())
            .ToArray();

        Assert.Equal(normalizedRoutes[0], normalizedRoutes[1]);
        Assert.Equal(normalizedRoutes[0], normalizedRoutes[2]);
    }

    [Fact]
    public void Segmented_ownership_fixture_assigns_each_segment_to_its_coordinate_owner()
    {
        var path = Path.Combine(
            System.AppContext.BaseDirectory,
            "Fixtures",
            "segmented-ownership.drawio");
        var document = XDocument.Load(path);
        var cells = document.Descendants("mxCell")
            .Where(cell => cell.Attribute("id") is not null)
            .ToDictionary(cell => (string)cell.Attribute("id")!);

        Assert.Equal("project-a", (string?)cells["dep-internal"].Attribute("parent"));
        Assert.Equal("project-a", (string?)cells["dep-ab-source-anchor"].Attribute("parent"));
        Assert.Equal("project-b", (string?)cells["dep-ab-target-anchor"].Attribute("parent"));
        Assert.Equal("project-a", (string?)cells["dep-ab-source"].Attribute("parent"));
        Assert.Equal("1", (string?)cells["dep-ab-middle"].Attribute("parent"));
        Assert.Equal("project-b", (string?)cells["dep-ab-target"].Attribute("parent"));
        Assert.Equal("project-a", (string?)cells["dep-aext-source"].Attribute("parent"));
        Assert.Equal("1", (string?)cells["dep-aext-root"].Attribute("parent"));
        Assert.All(
            cells.Values.Where(cell => (string?)cell.Attribute("logicalEdgeId") == "dep-ab"),
            cell => Assert.NotNull(cell.Attribute("logicalEdgeId")));
        Assert.DoesNotContain("endArrow=block", Style(cells["dep-ab-source"]));
        Assert.DoesNotContain("endArrow=block", Style(cells["dep-ab-middle"]));
        Assert.Contains("endArrow=block", Style(cells["dep-ab-target"]));
    }

    private static string Style(XElement edge) =>
        (string?)edge.Attribute("style") ?? string.Empty;

    private static int RouteOffset(XElement edge) =>
        (string?)edge.Attribute("id") switch
        {
            "edge-orthogonal" => 0,
            "edge-segment" => 320,
            "edge-manual" => 640,
            _ => throw new InvalidDataException("Unknown routing-authority fixture edge.")
        };
}
