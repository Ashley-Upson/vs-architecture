using System.Linq;
using StandardIo.ArchitectureDiagram.Core2.Models;
using StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Rendering;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core2.Tests;

public sealed class DeepDestinationRoutingTests
{
    private static ProjectModelDrawing Drawing()
    {
        DrawingNode Node(string id,double x,double y)=>new(id,new DefinedType {Name=id},id,x,y,180,60);
        return new ProjectModelDrawing("project",new ProjectModel
        {
            Dependencies=[new TypeRelationship {FromType="Source",ToType="Near"},
                new TypeRelationship {FromType="Source",ToType="Deep"}]
        },0,2000,600,[Node("Source",500,0),Node("Near",700,160),Node("Deep",1500,480)]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ShouldKeepSharedRootAnchoringFromClosingReservedPassages(bool noDuplicates)
    {
        RenderNode Node(string id,double x,double y)=>new(id,id,id,"blue",x,y,180,60,[]);
        RenderConnection Edge(string a,string b)=>new(a+b,a,b,a,b,false,[]);
        var model=new RenderModel(1500,700,[new RenderProject("p","P",0,0,1500,700,
            [Node("A",0,0),Node("B",300,0),Node("Middle",150,160),Node("Lower",150,320),Node("Logging",150,480)],
            [Edge("A","Middle"),Edge("B","Middle"),Edge("Middle","Lower"),Edge("Lower","Logging"),Edge("A","Logging"),Edge("B","Logging")])]);
        model.Configuration.NoDuplicates=noDuplicates;
        TestServices.Get<IProjectModelLayoutService>().Layout(model);
        var project=model.Projects[0];var lower=project.Nodes.Single(n=>n.Id=="Lower");
        foreach(var edge in project.Connections.Where(e=>(e.SourceId=="A"||e.SourceId=="B")&&e.TargetId=="Logging"))
            Assert.True(edge.Points[1].Y>lower.Y+lower.Height);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ShouldUseDepthPriorityAcrossProjectBoundary(bool noDuplicates)
    {
        RenderNode Node(string id,double x,double y)=>new(id,id,id,"blue",x,y,180,60,[]);
        RenderConnection Edge(string target)=>new(target,"Source",target,"Source",target,false,[]);
        var model=new RenderModel(2000,700,
            [new RenderProject("p","P",0,0,2000,300,[Node("Source",500,0),Node("Near",700,160)],[Edge("Near")]),
             new RenderProject("q","Q",0,480,2000,100,[Node("Deep",1500,0)],[])])
            {CrossProjectConnections=[Edge("Deep")]};
        model.Configuration.NoDuplicates=noDuplicates;
        new StandardIo.ArchitectureDiagram.Core2.Services.Processings.Layout.RoutingLayoutRuleProcessingService().ApplyRule(model);
        new StandardIo.ArchitectureDiagram.Core2.Services.Processings.Layout.CrossProjectRoutingLayoutRuleProcessingService().ApplyRule(model);
        var deep=model.CrossProjectConnections[0];var near=model.Projects[0].Connections[0];
        Assert.True(deep.Points[0].X<near.Points[0].X);
        Assert.InRange(deep.Points[1].Y,221,479);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ShouldMoveBlockingChildBranchToClearDeepConnection(bool noDuplicates)
    {
        RenderNode Node(string id,double y)=>new(id,id,id,"blue",500,y,180,60,[]);
        RenderConnection Edge(string a,string b)=>new(a+b,a,b,a,b,false,[]);
        var model=new RenderModel(1000,700,[new RenderProject("p","P",0,0,1000,700,
            [Node("Source",0),Node("Middle",160),Node("Lower",320),Node("Deep",480)],
            [Edge("Source","Middle"),Edge("Middle","Lower"),Edge("Lower","Deep"),Edge("Source","Deep")])]);
        model.Configuration.NoDuplicates=noDuplicates;
        TestServices.Get<IProjectModelLayoutService>().Layout(model);
        var project=model.Projects[0];
        var route=project.Connections.Single(e=>e.SourceId=="Source"&&e.TargetId=="Deep");
        var middle=project.Nodes.Single(n=>n.Id=="Middle");
        var lower=project.Nodes.Single(n=>n.Id=="Lower");
        Assert.True(route.Points[0].X<middle.X || route.Points[0].X>middle.X+middle.Width);
        Assert.Equal(middle.X,lower.X);
        Assert.True(route.Points[1].Y>lower.Y+lower.Height);
        Assert.Equal(route.Points[0].X,route.Points[1].X);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ShouldNotSendNearCentreChildBackAcrossDeeperExit(bool noDuplicates)
    {
        var drawing=Drawing();
        drawing=drawing with {Nodes=drawing.Nodes.Select(n=>n.Type.Name=="Near"?n with {X=505}:n).ToArray()};
        var routes=DiagramRouting.CreateRoutes(drawing,new RenderConfiguration {NoDuplicates=noDuplicates});
        var near=routes.Single(r=>r.Relationship.ToType=="Near");
        var deep=routes.Single(r=>r.Relationship.ToType=="Deep");
        Assert.True(near.Points[0].X<=595);
        Assert.True(deep.Points[0].X<near.Points[0].X);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ShouldAllocateDeepDestinationExitBeforeNearLayerExits(bool noDuplicates)
    {
        var routes=DiagramRouting.CreateRoutes(Drawing(),new RenderConfiguration {NoDuplicates=noDuplicates});
        var near=routes.Single(r=>r.Relationship.ToType=="Near");
        var deep=routes.Single(r=>r.Relationship.ToType=="Deep");
        Assert.True(deep.Points[0].X>590);
        Assert.True(deep.Points[0].X<near.Points[0].X);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ShouldDescendThroughClearLayersBeforeBendingToDeepDestination(bool noDuplicates)
    {
        var route=DiagramRouting.CreateRoutes(Drawing(),new RenderConfiguration {NoDuplicates=noDuplicates})
            .Single(r=>r.Relationship.ToType=="Deep");
        Assert.Equal(route.Points[0].X,route.Points[1].X);
        Assert.InRange(route.Points[1].Y,221,479);
    }
}
