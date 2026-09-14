using System.Linq;
using StandardIo.ArchitectureDiagram.Core2.Models;
using StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Rendering;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core2.Tests;

public sealed class LayerGapRoutingTests
{
    [Fact]
    public void ShouldTurnBelowParentAndDropDirectlyToChildInDuplicateView()
    {
        DrawingNode Node(string name,double x,double y,double width) =>
            new(name,new DefinedType { Name=name },name,x,y,width,60);
        var link=new TypeRelationship { FromType="Source",ToType="Target",DependencyType=DependencyType.Consumed };
        var drawing=new ProjectModelDrawing("p",new ProjectModel { Dependencies=[link] },0,700,600,
            [Node("Source",250,0,100),Node("First",100,160,300),Node("Second",200,320,300),Node("Target",550,480,100)]);
        var points=Assert.Single(DiagramRouting.CreateRoutes(drawing,new RenderConfiguration { NoDuplicates=false })).Points;
        Assert.Equal(4,points.Length);
        Assert.InRange(points[1].Y,61,159);
        Assert.Equal(600,points[2].X);
        Assert.Equal(points[2].X,points[3].X);
        Assert.Equal(480,points[3].Y);
    }

    [Fact]
    public void ShouldPreferOneClearCorridorOverShorterZigzagOnlyWithDuplicates()
    {
        DrawingNode Node(string name,double x,double y,double width) =>
            new(name,new DefinedType { Name=name },name,x,y,width,60);
        var nodes=new[] { Node("Source",250,0,100),Node("First",100,160,300),
            Node("Second",300,320,300),Node("Target",250,480,100) };
        var link=new TypeRelationship { FromType="Source",ToType="Target",DependencyType=DependencyType.Consumed };
        var drawing=new ProjectModelDrawing("p",new ProjectModel { Dependencies=[link] },0,700,600,nodes);
        var split=Assert.Single(DiagramRouting.CreateRoutes(drawing,new RenderConfiguration { NoDuplicates=false })).Points;
        var combined=Assert.Single(DiagramRouting.CreateRoutes(drawing,new RenderConfiguration { NoDuplicates=true })).Points;
        Assert.Equal(6,split.Length);
        Assert.Equal(split[2].X,split[3].X);
        Assert.True(combined.Length>split.Length);
        foreach(var segment in split.Zip(split.Skip(1)))
        foreach(var obstacle in nodes.Skip(1).Take(2))
            Assert.False(segment.First.X==segment.Second.X
                ? segment.First.X>obstacle.X && segment.First.X<obstacle.X+obstacle.Width && System.Math.Max(segment.First.Y,segment.Second.Y)>obstacle.Y && System.Math.Min(segment.First.Y,segment.Second.Y)<obstacle.Y+obstacle.Height
                : segment.First.Y>obstacle.Y && segment.First.Y<obstacle.Y+obstacle.Height && System.Math.Max(segment.First.X,segment.Second.X)>obstacle.X && System.Math.Min(segment.First.X,segment.Second.X)<obstacle.X+obstacle.Width);
    }

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
