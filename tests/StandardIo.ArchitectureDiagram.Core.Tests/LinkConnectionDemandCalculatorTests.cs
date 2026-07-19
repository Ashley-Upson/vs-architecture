using System.Linq;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core.Tests;

public sealed class LinkConnectionDemandCalculatorTests
{
    [Fact]
    public void Retains_a_larger_current_width()
    {
        var result = Measure(current: 300, text: 100, incoming: 2, outgoing: 2);
        Assert.Equal(300, result.RequiredWidth);
    }

    [Fact]
    public void Text_plus_total_configured_padding_expands_width()
    {
        var result = Measure(current: 80, text: 100, incoming: 0, outgoing: 0);
        Assert.Equal(120, result.RequiredWidth);
        Assert.Equal(120, result.TextSpaceRequirement);
    }

    [Fact]
    public void Incoming_attachment_demand_expands_width()
    {
        var result = Measure(current: 40, text: 10, incoming: 4, outgoing: 0);
        Assert.Equal(92, result.RequiredWidth);
    }

    [Fact]
    public void Outgoing_attachment_demand_expands_width()
    {
        var result = Measure(current: 40, text: 10, incoming: 0, outgoing: 4);
        Assert.Equal(92, result.RequiredWidth);
    }

    [Fact]
    public void Incoming_and_outgoing_use_max_not_sum()
    {
        var result = Measure(current: 40, text: 10, incoming: 4, outgoing: 4);
        Assert.Equal(92, result.RequiredWidth);
    }

    [Fact]
    public void Terminal_positions_are_deterministic_separated_and_inside_padding()
    {
        var node = new Rect(100, 20, 92, 80);
        var requests = new[]
        {
            new LinkConnectionRequest("c", 300, LinkConnectionSide.OutgoingBottom),
            new LinkConnectionRequest("a", 100, LinkConnectionSide.OutgoingBottom),
            new LinkConnectionRequest("b", 200, LinkConnectionSide.OutgoingBottom)
        };

        var result = LinkConnectionDemandCalculator.Allocate(node, requests.AsEnumerable().Reverse(), 20, 24);

        Assert.Equal(new[] { "a", "b", "c" }, result.Select(item => item.RouteId));
        Assert.Equal(new[] { 122, 146, 170 }, result.Select(item => item.AxisCoordinate));
        Assert.All(result, item => Assert.InRange(item.AxisCoordinate, node.X + 10, node.Right - 10));
    }

    [Fact]
    public void Incoming_and_outgoing_sides_reuse_the_same_span()
    {
        var node = new Rect(0, 0, 92, 80);
        var requests = new[]
        {
            new LinkConnectionRequest("in-a", 0, LinkConnectionSide.IncomingTop),
            new LinkConnectionRequest("in-b", 1, LinkConnectionSide.IncomingTop),
            new LinkConnectionRequest("out-a", 0, LinkConnectionSide.OutgoingBottom),
            new LinkConnectionRequest("out-b", 1, LinkConnectionSide.OutgoingBottom)
        };

        var result = LinkConnectionDemandCalculator.Allocate(node, requests, 20, 24);

        Assert.Equal(
            result.Where(item => item.Side == LinkConnectionSide.IncomingTop).Select(item => item.AxisCoordinate),
            result.Where(item => item.Side == LinkConnectionSide.OutgoingBottom).Select(item => item.AxisCoordinate));
    }

    private static NodeConnectionDemand Measure(int current, int text, int incoming, int outgoing) =>
        LinkConnectionDemandCalculator.Measure("node", current, text, incoming, outgoing, 24, 20);
}
