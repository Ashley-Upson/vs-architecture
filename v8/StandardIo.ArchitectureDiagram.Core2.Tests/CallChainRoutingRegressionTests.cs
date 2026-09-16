using System;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core2.Models;
using StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Rendering;
using Xunit;
namespace StandardIo.ArchitectureDiagram.Core2.Tests;
public class CallChainRoutingRegressionTests
{
    private static RenderNode Node(string id,double x,double y)=>new(id,id,id,"blue",x,y,300,60,[]);
    private static RenderConnection Edge(string a,string b,bool tree=false)=>new(a+b,a,b,a,b,false,[]){IsTree=tree};
    private static RenderConnection[] Calls(RenderModel model)=>model.CrossProjectConnections.Concat(model.Projects.SelectMany(p=>p.Connections.Where(e=>!e.IsTree).Select(e=>e with {Points=e.Points.Select(q=>q with {X=q.X+p.X,Y=q.Y+p.Y}).ToArray()}))).ToArray();
    [Fact]
    public void ShouldNotCrossChildMethodOnWayOutOfMergedContainer()
    {
        var model=new RenderModel(1800,800,[
            new("p","P",40,40,1000,700,[Node("a",40,100),Node("am",68,200),Node("b",518,100),Node("bm",546,200)],
                [Edge("a","am",true),Edge("b","bm",true),Edge("am","bm")]),
            new("q","Q",1200,40,500,700,[Node("c",40,260),Node("cm",68,360)], [Edge("c","cm",true)])])
            {DiagramType=DiagramTypes.CallChain,CrossProjectConnections=[Edge("am","cm")]};
        TestServices.Get<ICallChainRegionService>().Compact(model);
        var nodes=model.Projects.SelectMany(p=>p.Nodes.Select(n=>n with {X=n.X+p.X,Y=n.Y+p.Y})).ToArray();
        Assert.True(nodes.Single(n=>n.Id=="bm").Y>nodes.Single(n=>n.Id=="am").Y);
        Assert.Equal(100,nodes.Single(n=>n.Id=="bm").Y-nodes.Single(n=>n.Id=="b").Y);
        Assert.All(Calls(model),edge=>Assert.True(edge.Points.Length<=4));
        foreach(var edge in Calls(model))
        foreach(var n in nodes.Where(n=>n.Id!=edge.SourceId&&n.Id!=edge.TargetId))
        foreach(var segment in edge.Points.Zip(edge.Points.Skip(1)))
        {
            var a=segment.First;var b=segment.Second;
            Assert.False(a.X==b.X?a.X>n.X&&a.X<n.X+n.Width&&Math.Max(a.Y,b.Y)>n.Y&&Math.Min(a.Y,b.Y)<n.Y+n.Height:
                a.Y>n.Y&&a.Y<n.Y+n.Height&&Math.Max(a.X,b.X)>n.X&&Math.Min(a.X,b.X)<n.X+n.Width);
        }
    }
    [Fact]
    public void ShouldSeparateVerticalTracksForDifferentCalledMethods()
    {
        var model=new RenderModel(1200,900,[
            new("p","P",40,40,400,800,[Node("a",40,100),Node("am",68,200)],[Edge("a","am",true)]),
            new("q","Q",700,40,400,800,[Node("b",40,200),Node("bm",68,300),Node("cm",68,500)], [Edge("b","bm",true),Edge("b","cm",true)])])
            {DiagramType=DiagramTypes.CallChain,CrossProjectConnections=[Edge("am","bm"),Edge("am","cm")]};
        TestServices.Get<ICallChainRegionService>().Compact(model);
        var calls=Calls(model);
        foreach(var a in calls[0].Points.Zip(calls[0].Points.Skip(1)))
        foreach(var b in calls[1].Points.Zip(calls[1].Points.Skip(1)))
            Assert.False(a.First.X==a.Second.X&&b.First.X==b.Second.X&&a.First.X==b.First.X&&
                Math.Min(Math.Max(a.First.Y,a.Second.Y),Math.Max(b.First.Y,b.Second.Y))>Math.Max(Math.Min(a.First.Y,a.Second.Y),Math.Min(b.First.Y,b.Second.Y)));
    }
    [Fact]
    public void ShouldKeepExitClearWhenChildRegionMovesBeyondMergedParentColumns()
    {
        var model=new RenderModel(1800,1400,[new("p","P",40,40,1600,1300,
            [Node("a",40,100),Node("am",68,200),Node("b",518,1000),Node("bm",546,1100),
             Node("c",518,100),Node("cm",546,200),Node("d",996,100),Node("dm",1024,200)],
            [Edge("a","am",true),Edge("b","bm",true),Edge("c","cm",true),Edge("d","dm",true),
             Edge("am","bm"),Edge("am","cm"),Edge("cm","dm")])]){DiagramType=DiagramTypes.CallChain};
        TestServices.Get<ICallChainRegionService>().Compact(model);
        var nodes=model.Projects.SelectMany(p=>p.Nodes.Select(n=>n with {X=n.X+p.X,Y=n.Y+p.Y})).ToDictionary(n=>n.Id);
        var exit=Calls(model).Single(e=>e.SourceId=="am"&&e.TargetId=="bm").Points[0].Y;
        Assert.False(exit>nodes["dm"].Y&&exit<nodes["dm"].Y+nodes["dm"].Height);
    }
    [Fact]
    public void ShouldMakeRoomForAllDestinationTracksBeforePlacingChildTrees()
    {
        var children=Enumerable.Range(0,30).Select(i=>Node("child"+i,68,300+i*80)).Prepend(Node("b",40,200)).ToArray();
        var model=new RenderModel(1200,3000,[
            new("p","P",40,40,400,800,[Node("a",40,100),Node("am",68,200)],[Edge("a","am",true)]),
            new("q","Q",700,40,400,2800,children,children.Skip(1).Select(n=>Edge("b",n.Id,true)).ToArray())])
            {DiagramType=DiagramTypes.CallChain,CrossProjectConnections=children.Where(n=>n.Id!="b").Select(n=>Edge("am",n.Id)).ToArray()};
        TestServices.Get<ICallChainRegionService>().Compact(model);
        Assert.All(Calls(model),edge=>Assert.All(edge.Points.Zip(edge.Points.Skip(1)),pair=>Assert.True(pair.Second.X>=pair.First.X)));
    }
    [Fact]
    public void ShouldReclaimVerticalSlackBetweenIndependentCallTrees()
    {
        var model=new RenderModel(600,1600,[new("p","P",40,40,500,1500,
            [Node("a",40,100),Node("am",68,200),Node("b",40,1000),Node("bm",68,1100)],
            [Edge("a","am",true),Edge("b","bm",true)])]){DiagramType=DiagramTypes.CallChain};
        TestServices.Get<ICallChainRegionService>().Compact(model);
        var nodes=model.Projects.SelectMany(p=>p.Nodes.Select(n=>n with {Y=n.Y+p.Y})).ToDictionary(n=>n.Id);
        Assert.Equal(model.Configuration.Composition.ProjectSpacing,nodes["b"].Y-nodes["am"].Y-nodes["am"].Height);
    }
}
