// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core2.Models;
using StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Rendering;
using StandardIo.ArchitectureDiagram.Core2.Services.Processings.Layout;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core2.Tests;
public sealed class HorizontalDetourCollisionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ShouldSeparateHorizontalDetoursToDifferentDestinations(bool reverse)
    {
        var links = new[] { Link("Source", "First"), Link("Source", "Second") };
        if (reverse) Array.Reverse(links);
        var drawing = new ProjectModelDrawing("project", new ProjectModel { Dependencies = links }, 0, 1000, 400,
        [Node("Source", 0, 0), Node("Obstacle", 0, 160), Node("First", 400, 320), Node("Second", 600, 320)]);

        var routes = DiagramRouting.CreateRoutes(drawing);

        Assert.All(routes, route => Assert.Equal(6, route.Points.Length));
        AssertNoHorizontalCollision(routes[0].Points, routes[1].Points);
        Assert.All(routes, route => Assert.InRange(route.Points[1].Y, 61, 159));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ShouldSeparateCrossProjectDetoursFromExistingInternalBuses(bool noDuplicates)
    {
        var owner = new RenderProject("owner", "Owner", 40, 40, 800, 300,
            [RenderNode("Source", 0, 0), RenderNode("Child", 400, 160), RenderNode("Obstacle", 0, 160)],
            [new RenderConnection("local", "Source", "Child", "Source", "Child", false, [])]);
        var external = new RenderProject("external", "External", 40, 360, 800, 200,
            [RenderNode("External", 600, 0)], []);
        var model = new RenderModel(1000, 600, [owner, external])
        {
            CrossProjectConnections = [new RenderConnection("cross", "Source", "External", "Source", "External", false, [])]
        };
        model.Configuration.NoDuplicates = noDuplicates;
        new RoutingLayoutRuleProcessingService().ApplyRule(model);

        new CrossProjectRoutingLayoutRuleProcessingService().ApplyRule(model);

        var local = owner.Connections[0].Points.Select(p => new DrawingPoint(p.X + owner.X, p.Y + owner.Y)).ToArray();
        var cross = model.CrossProjectConnections[0].Points;
        Assert.Equal(noDuplicates ? 6 : 4, cross.Length);
        AssertNoHorizontalCollision(local, cross);
        Assert.Equal(new DrawingPoint(130, 100), cross[0]);
        Assert.Equal(new DrawingPoint(730, 360), cross[^1]);
    }

    private static TypeRelationship Link(string from, string to) => new()
    {
        FromType = from, ToType = to, DependencyType = DependencyType.Consumed
    };

    private static DrawingNode Node(string name, double x, double y) =>
        new(name, new DefinedType { Name = name }, name, x, y, 180, 60);

    private static RenderNode RenderNode(string name, double x, double y) =>
        new(name, name, name, "#123456", x, y, 180, 60, []);

    private static void AssertNoHorizontalCollision(DrawingPoint[] first, DrawingPoint[] second)
    {
        foreach (var a in first.Zip(first.Skip(1)))
        foreach (var b in second.Zip(second.Skip(1)))
        {
            bool overlaps = Math.Abs(a.First.Y - a.Second.Y) < 0.001
                && Math.Abs(b.First.Y - b.Second.Y) < 0.001
                && Math.Abs(a.First.Y - b.First.Y) < 0.001
                && Math.Min(Math.Max(a.First.X, a.Second.X), Math.Max(b.First.X, b.Second.X))
                    - Math.Max(Math.Min(a.First.X, a.Second.X), Math.Min(b.First.X, b.Second.X)) > 0.001;
            Assert.False(overlaps, "Horizontal segments to different destinations overlap.");
        }
    }
}
