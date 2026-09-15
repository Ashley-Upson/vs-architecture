// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using StandardIo.ArchitectureDiagram.Core2.Models;
using StandardIo.ArchitectureDiagram.Core2.Services.Processings.Rendering;
using StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Rendering;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core2.Tests;
public sealed partial class RoutingChannelTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ShouldNestFanOutByDestinationDistanceWithoutCrossings(bool reverse, bool duplicateView)
    {
        var drawing = CreateDrawing(count: 4);
        var nodes = drawing.Nodes.ToArray();
        nodes[0] = nodes[0] with { X = 500 };
        double[] positions = { 0, 250, 750, 1000 };
        for (int index = 0; index < 4; index++)
        {
            nodes[index + 4] = nodes[index + 4] with { X = positions[index] };
            drawing.Model.Dependencies![index].FromType = nodes[0].Type.Name;
        }

        if (reverse)
        {
            Array.Reverse(drawing.Model.Dependencies!);
        }

        var routes = DiagramRouting.CreateRoutes(drawing: drawing with { Nodes = nodes }, configuration: duplicateView ? new RenderConfiguration() : null)
            .OrderBy(route => route.Points[^1].X).ToArray();
        Assert.True(routes[0].Points[0].X < routes[1].Points[0].X);
        Assert.True(routes[1].Points[0].X < routes[2].Points[0].X);
        Assert.True(routes[2].Points[0].X < routes[3].Points[0].X);
        Assert.True(routes[0].Points[1].Y < routes[1].Points[1].Y);
        Assert.True(routes[3].Points[1].Y < routes[2].Points[1].Y);
        for (int first = 0; first < routes.Length; first++)
        for (int second = 0; second < routes.Length; second++)
        {
            if (first == second) continue;
            var horizontal = routes[first].Points;
            var vertical = routes[second].Points;
            foreach (int stem in new[] { 0, 2 })
            {
                bool crosses = vertical[stem].X > Math.Min(horizontal[1].X, horizontal[2].X)
                    && vertical[stem].X < Math.Max(horizontal[1].X, horizontal[2].X)
                    && horizontal[1].Y > Math.Min(vertical[stem].Y, vertical[stem + 1].Y)
                    && horizontal[1].Y < Math.Max(vertical[stem].Y, vertical[stem + 1].Y);
                Assert.False(crosses);
            }
        }
    }

    [Fact]
    public void ShouldPositionExitsBeforeAssigningChannelsForOppositeChildren()
    {
        var drawing = CreateDrawing(count: 2);
        var nodes = drawing.Nodes.ToArray();
        nodes[0] = nodes[0] with { X = 300 };
        nodes[2] = nodes[2] with { X = 0 };
        nodes[3] = nodes[3] with { X = 600 };
        drawing.Model.Dependencies![1].FromType = nodes[0].Type.Name;

        var routes = DiagramRouting.CreateRoutes(drawing: drawing with { Nodes = nodes });

        Assert.Equal(expected: 110, actual: routes[0].Points[1].Y);
        Assert.Equal(expected: 110, actual: routes[1].Points[1].Y);
        Assert.True(condition: routes[0].Points[1].X < routes[1].Points[1].X);
        Assert.Equal(expected: routes[0].Points[0].X, actual: routes[0].Points[1].X);
        Assert.Equal(expected: routes[1].Points[0].X, actual: routes[1].Points[1].X);
    }

    [Fact]
    public void ShouldReuseTheCenteredChannelAfterAnIndependentRunEnds()
    {
        var drawing = CreateDrawing(count: 3);
        var nodes = drawing.Nodes.ToArray();
        nodes[0] = nodes[0] with { X = 0 };
        nodes[3] = nodes[3] with { X = 200 };
        nodes[1] = nodes[1] with { X = 100 };
        nodes[4] = nodes[4] with { X = 400 };
        nodes[2] = nodes[2] with { X = 300 };
        nodes[5] = nodes[5] with { X = 500 };
        var routes = DiagramRouting.CreateRoutes(drawing: drawing with { Nodes = nodes });
        Assert.Equal(routes[0].Points[1].Y, routes[2].Points[1].Y);
        Assert.True(routes[1].Points[1].Y < routes[0].Points[1].Y);
    }

    [Fact]
    public void ShouldKeepSingleDestinationExitsCentered()
    {
        var drawing = CreateDrawing(count: 2);
        var routes = DiagramRouting.CreateRoutes(drawing: drawing);
        foreach (var route in routes)
        {
            var source = drawing.Nodes.Single(node => node.Type.Name == route.Relationship.FromType);
            Assert.Equal(source.X + 90, route.Points[0].X);
            Assert.Equal(route.Points[0].X, route.Points[1].X);
        }
    }

    [Fact]
    public void ShouldShareOneBusForTheSameDestinationAcrossSourceRows()
    {
        // Given
        var drawing = CreateDrawing(count: 2);
        var nodes = drawing.Nodes.ToArray();
        nodes[0] = nodes[0] with { Y = -160 };
        var links = drawing.Model.Dependencies!;
        links[1].ToType = links[0].ToType;
        // When
        var routes = DiagramRouting.CreateRoutes(drawing: drawing with { Nodes = nodes });
        // Then
        // An obstructed source may need an earlier detour; the final horizontal segment is the destination bus.
        Assert.Equal(expected: routes[0].Points[^3].Y, actual: routes[1].Points[^3].Y);
        Assert.Equal(expected: routes[0].Points[^2], actual: routes[1].Points[^2]);
        Assert.Equal(expected: 110, actual: routes[0].Points[^3].Y);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void ShouldGiveEachDestinationItsOwnGutterSlice(int count)
    {
        // Given
        var drawing = CreateDrawing(count: count);
        // When
        var routes = DiagramRouting.CreateRoutes(drawing: drawing);
        // Then
        Assert.Equal(expected: count, actual: routes.Select(route => route.Points[1].Y).Distinct().Count());
        Assert.All(collection: routes, action: route => Assert.InRange(actual: route.Points[1].Y, low: 61, high: 159));
    }

    [Fact]
    public void ShouldWriteTheSameExplicitWaypointsInBothFormats()
    {
        // Given
        var drawings = new[] { CreateDrawing(count: 2) };
        // When
        var html = XDocument.Parse(text: Encoding.UTF8.GetString(bytes: new HtmlDocumentService().Render(renderModel: PrepareRoutes(drawings))));
        var drawio = XDocument.Parse(text: Encoding.UTF8.GetString(bytes: new DrawIODocumentService().Render(renderModel: PrepareRoutes(drawings))));
        XNamespace svg = "http://www.w3.org/2000/svg";
        var lines = html.Descendants(name: svg + "polyline").ToArray();
        var edges = drawio.Descendants(name: "mxCell").Where(predicate: cell => (string?)cell.Attribute(name: "edge") == "1").ToArray();
        // Then
        for (int index = 0; index < lines.Length; index++)
        {
            string[] expected = ((string)lines[index].Attribute(name: "points")!).Split(' ').Skip(1).Take(2).ToArray();
            string[] actual = edges[index].Descendants(name: "mxPoint").Select(selector: point => $"{point.Attribute(name: "x")!.Value},{point.Attribute(name: "y")!.Value}").ToArray();
            Assert.Equal(expected: expected, actual: actual);
        }
    }

    [Fact]
    public void ShouldKeepIndependentRunsOnTheCenteredChannel()
    {
        // Given
        var drawing = CreateDrawing(count: 2);
        DrawingNode[] nodes = drawing.Nodes.ToArray();
        nodes[0] = nodes[0] with { X = 0 };
        nodes[2] = nodes[2] with { X = 200 };
        nodes[1] = nodes[1] with { X = 500 };
        nodes[3] = nodes[3] with { X = 700 };
        // When
        var routes = DiagramRouting.CreateRoutes(drawing: drawing with { Nodes = nodes });
        // Then
        Assert.Equal(expected: 110, actual: routes[0].Points[1].Y);
        Assert.Equal(expected: 110, actual: routes[1].Points[1].Y);
    }

    [Fact]
    public void ShouldKeepAlignedConnectionsStraightAndAnchorsUnchanged()
    {
        // Given
        var drawing = CreateDrawing(count: 1);
        DrawingNode[] nodes = drawing.Nodes.ToArray();
        nodes[1] = nodes[1] with { X = nodes[0].X };
        // When
        var route = Assert.Single(collection: DiagramRouting.CreateRoutes(drawing: drawing with { Nodes = nodes }));
        // Then
        Assert.All(collection: route.Points, action: point => Assert.Equal(expected: 90, actual: point.X));
        Assert.Equal(expected: new DrawingPoint(90, 60), actual: route.Points[0]);
        Assert.Equal(expected: new DrawingPoint(90, 160), actual: route.Points[^1]);
    }

    private static RenderModel PrepareRoutes(ProjectModelDrawing[] drawings)
    {
        var model = new RenderModel(1000, 1000, drawings.Select(drawing => new RenderProject(drawing.Id, "Fixture", drawing.X, 40, drawing.Width, drawing.Height,
            drawing.Nodes.Select(node => new RenderNode(node.Id, node.Type.Name!, node.Label, "#123456", node.X, node.Y, node.Width, node.Height, [new RenderText(node.Label, node.X + node.Width / 2, node.Y + node.Height / 2)])).ToArray(),
            drawing.Model.Dependencies!.Select((link, index) => new RenderConnection("edge" + index, drawing.Nodes.Single(node => node.Type.Name == link.FromType).Id, drawing.Nodes.Single(node => node.Type.Name == link.ToType).Id, link.FromType!, link.ToType!, false, [])).ToArray())).ToArray());
        new StandardIo.ArchitectureDiagram.Core2.Services.Processings.Layout.RoutingLayoutRuleProcessingService().ApplyRule(model);
        return model;
    }

    private static ProjectModelDrawing CreateDrawing(int count)
    {
        var types = Enumerable.Range(start: 0, count: count * 2).Select(selector: index => new DefinedType { Name = "Type" + index }).ToArray();
        var links = Enumerable.Range(start: 0, count: count).Select(selector: index => new TypeRelationship { FromType = types[index].Name, ToType = types[count + index].Name }).ToArray();
        var nodes = types.Select(selector: (type, index) => new DrawingNode("node" + index, type, type.Name!, index < count ? index * 20 : 400 + (index - count) * 20, index < count ? 0 : 160, 180, 60)).ToArray();
        return new ProjectModelDrawing("project", new ProjectModel { Types = types, Dependencies = links }, 40, 1000, 300, nodes);
    }
}
