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
