using System;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core2.Models;
using StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Rendering;
using Xunit;
namespace StandardIo.ArchitectureDiagram.Core2.Tests;
public sealed class ContextualLayoutTests
{
    [Fact]
    public void ShouldPositionCompositionConsumersAboveTheirReferencesRegardlessOfInputOrder()
    {
        var diagram = new ContextualDiagram([new("Child", "Project", ["Child"]), new("Root", "Project", ["Root"])], [new("Root", "Child", "references")]);
        var model = TestServices.Get<IContextualLayoutService>().Layout(new RenderModel([], diagramType: DiagramTypes.Composition), diagram);
        var nodes = model.Projects.Single().Nodes;
        Assert.True(nodes.Single(n => n.TypeName == "Root").Y + nodes.Single(n => n.TypeName == "Root").Height < nodes.Single(n => n.TypeName == "Child").Y);
    }
    [Fact]
    public void ShouldSizeEachDataRowFromItsOwnMembersRatherThanTheTallestEntityInTheProject()
    {
        var types = Enumerable.Range(0,9).Select(i => new ContextualType("T"+i,"Project", i == 8 ? Enumerable.Repeat("member",30).ToArray() : ["T"+i])).ToArray();
        var model = TestServices.Get<IContextualLayoutService>().Layout(new RenderModel([], diagramType: DiagramTypes.DataModel), new(types, []));
        var nodes = model.Projects.Single().Nodes;
        Assert.Equal(nodes[0].Y + nodes[0].Height + 80, nodes[4].Y);
    }
    [Fact]
    public void ShouldRouteAcrossDataRowsWithoutCuttingThroughTheInterveningEntity()
    {
        var types = Enumerable.Range(0,9).Select(i => new ContextualType("T"+i,"Project",["T"+i])).ToArray();
        var model = TestServices.Get<IContextualLayoutService>().Layout(new RenderModel([], diagramType: DiagramTypes.DataModel), new(types,[new("T0","T8","Items [many]")]));
        var project = model.Projects.Single(); var edge = Assert.Single(project.Connections);
        foreach (var node in project.Nodes.Where(n => n.TypeName is not "T0" and not "T8"))
        for (int i=1;i<edge.Points.Length;i++)
        {
            var a=edge.Points[i-1];var b=edge.Points[i];
            Assert.False(a.X==b.X && a.X>node.X && a.X<node.X+node.Width && Math.Max(a.Y,b.Y)>node.Y && Math.Min(a.Y,b.Y)<node.Y+node.Height);
            Assert.False(a.Y==b.Y && a.Y>node.Y && a.Y<node.Y+node.Height && Math.Max(a.X,b.X)>node.X && Math.Min(a.X,b.X)<node.X+node.Width);
        }
        Assert.Equal("Items [many]",edge.Label);
    }
}
