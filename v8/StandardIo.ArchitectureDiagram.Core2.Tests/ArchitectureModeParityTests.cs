using System.Linq;
using StandardIo.ArchitectureDiagram.Core2.Models;
using StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Rendering;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core2.Tests;

public sealed class ArchitectureModeParityTests
{
    [Fact]
    public void ShouldKeepSharedLeafGroupsTogetherDespiteLargeInitialGaps()
    {
        RenderNode Node(string id,double x,double y)=>new(id,id,id,"blue",x,y,180,60,[]);
        RenderConnection Edge(string a,string b)=>new(a+b,a,b,a,b,false,[]);
        var project=new RenderProject("p","P",0,0,2500,400,
            [Node("A",0,0),Node("B",240,0),Node("C",600,0),Node("D",840,0),
             Node("Left",0,160),Node("Right",2000,160),Node("Middle1",1000,160),Node("Middle2",1240,160)],
            [Edge("A","Left"),Edge("A","Right"),Edge("B","Left"),Edge("B","Right"),
             Edge("C","Middle1"),Edge("C","Middle2"),Edge("D","Middle1"),Edge("D","Middle2")]);
        new StandardIo.ArchitectureDiagram.Core2.Services.Processings.Layout.LayoutCleanupRuleProcessingService().ApplyRule(new RenderModel(2500,400,[project]));
        var ordered=project.Nodes.Where(n=>n.Y==160).OrderBy(n=>n.X).Select(n=>n.Id).ToArray();
        Assert.Equal(1,System.Math.Abs(System.Array.IndexOf(ordered,"Left")-System.Array.IndexOf(ordered,"Right")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ShouldClearPassageWithoutRejectingAnUnrelatedUnfinishedSiblingGroup(bool sameParent)
    {
        RenderNode Node(string id,double x,double y)=>new(id,id,id,"blue",x,y,180,60,[]);
        RenderConnection Edge(string a,string b)=>new(a+b,a,b,a,b,false,[]);
        var model=new RenderModel(2000,700,[new RenderProject("p","P",0,0,2000,700,
            [Node("Source",0,0),Node("Middle",0,160),Node("Deep",500,480),
             Node("Other",1000,0),Node("Left",1000,320),Node("Right",1000,320)],
            [Edge("Source","Middle"),Edge("Source","Deep"),Edge(sameParent ? "Source" : "Other","Left"),Edge(sameParent ? "Source" : "Other","Right")])]);
        new StandardIo.ArchitectureDiagram.Core2.Services.Processings.Layout.VerticalPassageLayoutRuleProcessingService().ApplyRule(model);
        Assert.NotEqual(0,model.Projects[0].Nodes.Single(n=>n.Id=="Middle").X);
    }

    [Fact]
    public void ShouldArrangeAndRouteTheSameGraphIdenticallyRegardlessOfDuplicateOption()
    {
        RenderModel Build(bool noDuplicates)
        {
            RenderNode Node(string id,double x,double y)=>new(id,id,id,"blue",x,y,180,60,[]);
            RenderConnection Edge(string a,string b)=>new(a+b,a,b,a,b,false,[]);
            var model=new RenderModel(1500,700,[new RenderProject("p","P",0,0,1500,700,
                [Node("A",0,0),Node("B",300,0),Node("Middle",150,160),Node("Lower",150,320),Node("Logging",150,480)],
                [Edge("A","Middle"),Edge("B","Middle"),Edge("Middle","Lower"),Edge("Lower","Logging"),Edge("A","Logging"),Edge("B","Logging")])]);
            model.Configuration.NoDuplicates=noDuplicates;
            TestServices.Get<IProjectModelLayoutService>().Layout(model);
            return model;
        }
        var split=Build(false);
        var combined=Build(true);
        Assert.Equal(split.Projects[0].Nodes.Select(n=>(n.Id,n.X,n.Y)),combined.Projects[0].Nodes.Select(n=>(n.Id,n.X,n.Y)));
        Assert.Equal(split.Projects[0].Connections.SelectMany(e=>e.Points),combined.Projects[0].Connections.SelectMany(e=>e.Points));
    }
}
