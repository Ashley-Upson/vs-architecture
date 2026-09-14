using System;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core2.Models;
using StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Rendering;
using Xunit;
namespace StandardIo.ArchitectureDiagram.Core2.Tests;
public sealed class ContextualLayoutTests
{
    [Fact]
    public void ShouldUseFacingEdgePositionsToAvoidBendsBetweenDifferentHeightEntities()
    {
        var model=TestServices.Get<IContextualLayoutService>().Layout(new RenderModel([],diagramType:DiagramTypes.DataModel),
            new([new("A","Project",Enumerable.Repeat("member",10).ToArray()),new("B","Project",["B"])],[new("A","B","item")]));
        Assert.Equal(2,Assert.Single(Assert.Single(model.Projects).Connections).Points.Length);
    }
    [Fact]
    public void ShouldUseSpaceBetweenAxesForLargeAggregateChildren()
    {
        var names=Enumerable.Range(0,12).Select(i=>"Child"+i).ToArray();
        var types=names.Prepend("Root").Select(n=>new ContextualType(n,"Project",[n])).ToArray();
        var model=TestServices.Get<IContextualLayoutService>().Layout(new RenderModel([],diagramType:DiagramTypes.DataModel),
            new(types,names.Select(n=>new ContextualLink("Root",n,"child")).ToArray()));
        var nodes=Assert.Single(model.Projects).Nodes;
        Assert.True(nodes.Max(n=>n.X)-nodes.Min(n=>n.X)<=4*(model.Configuration.DataModel.NodeWidth+model.Configuration.DataModel.NodeSpacing));
        Assert.True(nodes.Select(n=>n.Y).Distinct().Count()<=5);
    }
    [Fact]
    public void ShouldEnterAndLeaveEntityEdgesPerpendicularly()
    {
        var model=TestServices.Get<IContextualLayoutService>().Layout(new RenderModel([],diagramType:DiagramTypes.DataModel),
            new([new("A","Project",Enumerable.Repeat("member",10).ToArray()),new("B","Project",["B"])],[new("A","B","item")]));
        var project=Assert.Single(model.Projects);var edge=Assert.Single(project.Connections);
        var a=project.Nodes.Single(n=>n.Id==edge.SourceId);var b=project.Nodes.Single(n=>n.Id==edge.TargetId);
        Assert.True(Outside(a,edge.Points[0],edge.Points[1]));
        Assert.True(Outside(b,edge.Points[^1],edge.Points[^2]));
    }
    private static bool Outside(RenderNode node,DrawingPoint port,DrawingPoint outside) =>
        port.X==node.X && outside.X<port.X && outside.Y==port.Y ||
        port.X==node.X+node.Width && outside.X>port.X && outside.Y==port.Y ||
        port.Y==node.Y && outside.Y<port.Y && outside.X==port.X ||
        port.Y==node.Y+node.Height && outside.Y>port.Y && outside.X==port.X;
    [Fact]
    public void ShouldArrangeChildrenAroundRootAndSeparateUnattachedEntities()
    {
        var types=new[]{"Unattached","A","B","C","D","Root"}.Select(n=>new ContextualType(n,"Project",[n])).ToArray();
        var links=new[]{"A","B","C","D"}.Select(n=>new ContextualLink("Root",n,"child")).ToArray();
        var model=TestServices.Get<IContextualLayoutService>().Layout(new RenderModel([],diagramType:DiagramTypes.DataModel),new(types,links));
        var nodes=Assert.Single(model.Projects).Nodes;var root=nodes.Single(n=>n.TypeName=="Root");
        var children=nodes.Where(n=>new[]{"A","B","C","D"}.Contains(n.TypeName)).ToArray();
        Assert.True(children.Min(n=>n.X)<root.X && children.Max(n=>n.X)>root.X);
        Assert.True(children.Min(n=>n.Y)<root.Y && children.Max(n=>n.Y)>root.Y);
        Assert.True(nodes.Single(n=>n.TypeName=="Unattached").Y>=nodes.Where(n=>n.TypeName!="Unattached").Max(n=>n.Y+n.Height)+model.Configuration.DataModel.RowSpacing);
    }
    [Fact]
    public void ShouldRetainVisibleSelfRelationships()
    {
        var model=TestServices.Get<IContextualLayoutService>().Layout(new RenderModel([],diagramType:DiagramTypes.DataModel),
            new([new("Entity","Project",["Entity"])],[new("Entity","Entity","parent")]));
        var edge=Assert.Single(Assert.Single(model.Projects).Connections);
        Assert.True(edge.Points.Length>=4);
        Assert.NotEqual(edge.Points[0],edge.Points[^1]);
    }
    [Fact]
    public void ShouldGroupParentAndChildNamespacesAndExcludeBehaviourAndAnonymousTypes()
    {
        var project=new ProjectModel {Name="Project",Types=[
            new() {Name="Shop.Sales.Order",HasDeclaredBehaviour=false,IsInternal=true},
            new() {Name="Shop.Sales.Details.Line",HasDeclaredBehaviour=false,IsInternal=true},
            new() {Name="Shop.Stock.Item",HasDeclaredBehaviour=false,IsInternal=true},
            new() {Name="Shop.Service",HasDeclaredBehaviour=true,IsInternal=true},
            new() {Name="<anonymous type: string Name>",HasDeclaredBehaviour=false,IsInternal=true}]};
        var diagram=TestServices.Get<IContextualModelService>().Prepare(new RenderModel([project],diagramType:DiagramTypes.DataModel));
        Assert.Equal(3,diagram.Types.Length);
        var model=TestServices.Get<IContextualLayoutService>().Layout(new RenderModel([],diagramType:DiagramTypes.DataModel),diagram);
        Assert.Equal(2,model.Projects.Length);
        var sales=Assert.Single(model.Projects,p=>p.Nodes.Any(n=>n.TypeName=="Shop.Sales.Order"));
        Assert.Contains(sales.Nodes,n=>n.TypeName=="Shop.Sales.Details.Line");
    }
    [Fact]
    public void ShouldConnectAdjacentEntitiesDirectlyAcrossTheirFacingEdges()
    {
        var model=TestServices.Get<IContextualLayoutService>().Layout(new RenderModel([],diagramType:DiagramTypes.DataModel),
            new([new("A","Project",["A"]),new("B","Project",["B"])],[new("A","B","item")]));
        var project=Assert.Single(model.Projects);var a=project.Nodes[0];var b=project.Nodes[1];var edge=Assert.Single(project.Connections);
        Assert.Equal(2,edge.Points.Length);
        Assert.Equal(a.X+a.Width,edge.Points[0].X);
        Assert.Equal(b.X,edge.Points[1].X);
        Assert.Equal(edge.Points[0].Y,edge.Points[1].Y);
    }
    [Fact]
    public void ShouldPlaceRelatedEntitiesNextToEachOtherInsteadOfFollowingUnrelatedInputOrder()
    {
        var types = new[] { "A", "B", "C", "D", "AChild", "BChild", "CChild", "DChild" }
            .Select(n => new ContextualType(n,"Project",[n])).ToArray();
        var links = new[] { "A", "B", "C", "D" }.Select(n => new ContextualLink(n,n+"Child","child")).ToArray();
        var model = TestServices.Get<IContextualLayoutService>().Layout(new RenderModel([],diagramType:DiagramTypes.DataModel),new(types,links));
        foreach (var link in links)
        {
            var a = model.Projects[0].Nodes.Single(n=>n.TypeName==link.From);
            var b = model.Projects[0].Nodes.Single(n=>n.TypeName==link.To);
            Assert.Equal(a.Y,b.Y);
            Assert.Equal(a.Width + model.Configuration.DataModel.NodeSpacing, Math.Abs(a.X-b.X));
        }
    }
    [Fact]
    public void ShouldUseABroadCanvasForLargeDataModels()
    {
        var types = Enumerable.Range(0,100).Select(i => new ContextualType("T"+i,"Project",["T"+i])).ToArray();
        var model = TestServices.Get<IContextualLayoutService>().Layout(new RenderModel([], diagramType: DiagramTypes.DataModel), new(types, []));
        Assert.True(model.Projects[0].Nodes.GroupBy(n => n.Y).Max(row => row.Count()) > 4);
        Assert.True(model.Width / model.Height > 0.7);
        Assert.True(model.Width / model.Height < 2.5);
    }
    [Theory]
    [InlineData(DiagramTypes.Composition)]
    [InlineData(DiagramTypes.DataModel)]
    public void ShouldOnlyIncludeTypesOwnedBySelectedProjects(DiagramTypes kind)
    {
        DefinedType Type(string name, bool owned) => new() { Name = name, IsInternal = owned, HasDeclaredBehaviour = kind == DiagramTypes.Composition, Properties = [] };
        var first = new ProjectModel { Name = "First", Types = [Type("A",true), Type("B",false),Type("External",false)], Dependencies = [new() { FromType="A",ToType="B",IsComposition=true },new() { FromType="A",ToType="External",IsComposition=true }] };
        var second = new ProjectModel { Name = "Second", Types = [Type("B",true)] };
        var diagram = TestServices.Get<IContextualModelService>().Prepare(new RenderModel([first,second],diagramType:kind));
        Assert.DoesNotContain(diagram.Types,t => t.Name == "External");
        Assert.Contains(diagram.Types,t => t.Name == "B" && t.Project == "Second");
    }
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
