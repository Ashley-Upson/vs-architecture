using System.Linq;
using StandardIo.ArchitectureDiagram.Core2.Models;
using StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Rendering;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core2.Tests;

public sealed class LayerGapRoutingTests
{
    [Fact]
    public void ShouldUseNearbyGapsInSuccessiveLayersInsteadOfOneOutsideColumn()
    {
        // Given: staggered obstacles have nearby gaps, but no single shared column.
        DrawingNode Node(string name, double x, double y, double width) =>
            new(name, new DefinedType { Name = name }, name, x, y, width, 60);
        var nodes = new[] { Node("Source", 250, 0, 100), Node("First", 100, 160, 300),
            Node("Second", 300, 320, 300), Node("Target", 250, 480, 100) };
        var link = new TypeRelationship { FromType = "Source", ToType = "Target", DependencyType = DependencyType.Consumed };
        var drawing = new ProjectModelDrawing("p", new ProjectModel { Dependencies = [link] }, 0, 700, 600, nodes);
        // When
        var route = Assert.Single(DiagramRouting.CreateRoutes(drawing));
        // Then: use the right gap of the first obstacle and left gap of the second.
        Assert.Contains(route.Points, p => p.X == 410);
        Assert.Contains(route.Points, p => p.X == 290);
        Assert.All(route.Points, p => Assert.InRange(p.X, 290, 410));
        foreach (var segment in route.Points.Zip(route.Points.Skip(1)))
        {
            Assert.True(segment.First.X == segment.Second.X || segment.First.Y == segment.Second.Y);
            Assert.True(segment.Second.Y >= segment.First.Y);
        }
    }
}
