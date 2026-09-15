using System.Linq;
using StandardIo.ArchitectureDiagram.Core2.Models;
using StandardIo.ArchitectureDiagram.Core2.Services.Processings.Layout;
using Xunit;
namespace StandardIo.ArchitectureDiagram.Core2.Tests;
public class CombinedLayoutTests
{
    [Fact]
    public void ShouldPositionRootAfterSharedServiceBranchHasFinishedMoving()
    {
        RenderNode Node(string id,double x,double y)=>new(id,id,id,"blue",x,y,210,60,[]);
        RenderConnection Edge(string a,string b)=>new(a+b,a,b,a,b,false,[]);
        var project=new RenderProject("p","P",0,0,6000,800,
            [Node("root",0,0),Node("other",400,0),Node("service",1000,160),Node("logging",0,640),Node("wide",400,160),
             Node("one",0,320),Node("two",2000,320),Node("three",4000,320),Node("serviceChild",1000,320)],
            [Edge("root","service"),Edge("root","logging"),Edge("other","service"),Edge("other","logging"),Edge("other","wide"),
             Edge("wide","one"),Edge("wide","two"),Edge("wide","three"),Edge("service","serviceChild")]);
        var model=new RenderModel(6000,800,[project]);model.Configuration.NoDuplicates=true;
        new LayoutCleanupRuleProcessingService().ApplyRule(model);
        Assert.Equal(project.Nodes.Single(n=>n.Id=="service").X,project.Nodes.Single(n=>n.Id=="root").X,5);
    }

    [Fact]
    public void ShouldGrowCrowdedGutterAndKeepRepeatedLayoutStable()
    {
        var nodes=Enumerable.Range(0,21).Select(i=>new RenderNode(i.ToString(),i.ToString(),i.ToString(),"blue",i*270,i==0?0:160,210,60,[])).ToArray();
        var edges=Enumerable.Range(1,20).Select(i=>new RenderConnection(i.ToString(),"0",i.ToString(),"0",i.ToString(),false,[])).ToArray();
        var model=new RenderModel(6000,500,[new RenderProject("p","P",0,0,6000,500,nodes,edges)]) { LayoutInitialized=true, SharedParentLayoutInitialized=true };
        model.Configuration.NoDuplicates=true;
        var layout=TestServices.Get<StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Rendering.IProjectModelLayoutService>();
        layout.Layout(model);
        var project=model.Projects[0];
        double gap=project.Nodes[1].Y-project.Nodes[0].Y-project.Nodes[0].Height;
        Assert.True(gap>100);
        var positions=project.Nodes.Select(n=>(n.X,n.Y)).ToArray();layout.Layout(model);
        Assert.Equal(positions,model.Projects[0].Nodes.Select(n=>(n.X,n.Y)).ToArray());
    }

    [Fact]
    public void ShouldAlignRootWithSharedNearestChildDespiteDeeperSharedDependency()
    {
        RenderNode Node(string id,double x,double y)=>new(id,id,id,"blue",x,y,210,60,[]);
        RenderConnection Edge(string a,string b)=>new(a+b,a,b,a,b,false,[]);
        var project=new RenderProject("p","P",0,0,2000,800,
            [Node("root",0,0),Node("other",400,0),Node("service",1000,160),Node("logging",0,480)],
            [Edge("root","service"),Edge("root","logging"),Edge("other","service"),Edge("other","logging")]);
        var model=new RenderModel(2000,800,[project]);model.Configuration.NoDuplicates=true;
        new LayoutCleanupRuleProcessingService().ApplyRule(model);
        var roots=project.Nodes.Where(n=>n.Y==0).OrderBy(n=>n.X).ToArray();
        Assert.True(roots.All(n=>System.Math.Abs(n.X-project.Nodes.Single(s=>s.Id=="service").X)<=270));
    }
}
