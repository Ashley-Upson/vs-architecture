// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core2.Exposures;
using StandardIo.ArchitectureDiagram.Core2.Models;
using StandardIo.ArchitectureDiagram.Core2.Services.Processings.Layout;
using Xunit;
namespace StandardIo.ArchitectureDiagram.Core2.Tests;
public sealed class ProjectPositioningTests
{
    [Theory]
    [InlineData(100)]
    [InlineData(35)]
    [InlineData(175)]
    public void ShouldCentreUnequalProjectBoxesAndTheirSharedChild(double projectSpacing)
    {
        // Given: one parent, two differently sized children, and their shared dependency.
        RenderProject Project(string id, double width, double height) => new(id, id, 40, 40, width, height,
            new[] { new RenderNode(id + "-node", id, id, "#123456", 40, 60, 180, 60, Array.Empty<RenderText>()) }, Array.Empty<RenderConnection>());
        RenderConnection Edge(string from, string to) => new(from + to, from + "-node", to + "-node", from, to, false, Array.Empty<DrawingPoint>());
        var model = new RenderModel(0, 0, new[] { Project("A", 1000, 400), Project("B", 300, 200), Project("C", 700, 600), Project("D", 500, 200) })
        { Configuration = new RenderConfiguration { Architecture = new ArchitectureRenderConfiguration { ProjectSpacing = projectSpacing } }, CrossProjectConnections = new[] { Edge("A", "B"), Edge("A", "C"), Edge("B", "D"), Edge("C", "D") } };
        var rule = TestServices.Get<ILayoutRuleFactory>().GetLayoutRuleServices().OfType<ProjectPositioningLayoutRuleProcessingService>().Single();

        // When: apply the project rule as the iterative layout pipeline does.
        for (int iteration = 0; iteration < 100; iteration++) rule.ApplyRule(model);

        // Then: spacing uses actual widths, and centring uses the outer group edges.
        var a = model.Projects[0]; var b = model.Projects[1]; var c = model.Projects[2]; var d = model.Projects[3];
        double midpoint = (Math.Min(b.X, c.X) + Math.Max(b.X + b.Width, c.X + c.Width)) / 2;
        Assert.InRange(Math.Abs(a.X + a.Width / 2 - midpoint), 0, 0.01);
        Assert.InRange(Math.Abs(d.X + d.Width / 2 - midpoint), 0, 0.01);
        Assert.True(c.X >= b.X + b.Width + projectSpacing - 0.01 || b.X >= c.X + c.Width + projectSpacing - 0.01);
        Assert.Equal(b.Y, c.Y);
        Assert.Equal(projectSpacing, b.Y - a.Y - a.Height, 2);
        Assert.Equal(projectSpacing, d.Y - Math.Max(b.Y + b.Height, c.Y + c.Height), 2);
    }
}
