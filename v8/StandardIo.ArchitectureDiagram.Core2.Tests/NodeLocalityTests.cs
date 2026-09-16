using System.Linq;
using StandardIo.ArchitectureDiagram.Core2.Models;
using StandardIo.ArchitectureDiagram.Core2.Services.Processings.Layout;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core2.Tests;

public sealed class NodeLocalityTests
{
    [Fact]
    public void ShouldMoveSharedChildrenTowardsConsumersAndPushNeighbourAside()
    {
        RenderNode Node(string id, double x, double y) => new(id,id,id,"blue",x,y,180,60,[]);
        RenderConnection Edge(string a,string b) => new(a+b,a,b,a,b,false,[]);
        var project = new RenderProject("p","P",0,0,1500,600,
            [Node("A",0,0),Node("B",240,0),Node("C",720,0),Node("D",960,0),
             Node("LeftChild",840,160),Node("RightChild",120,160)],
            [Edge("A","LeftChild"),Edge("B","LeftChild"),Edge("C","RightChild"),Edge("D","RightChild")]);
        var model = new RenderModel(1500,600,[project]);
        model.Configuration.NoDuplicates=true;
        new NodeLocalityLayoutRuleProcessingService().ApplyRule(model);
        Assert.True(project.Nodes.Single(n=>n.Id=="LeftChild").X < project.Nodes.Single(n=>n.Id=="RightChild").X);
        Assert.True(project.Nodes.Single(n=>n.Id=="RightChild").X-project.Nodes.Single(n=>n.Id=="LeftChild").X>=240);
        var positions=project.Nodes.Select(n=>n.X).ToArray();
        new NodeLocalityLayoutRuleProcessingService().ApplyRule(model);
        Assert.Equal(positions,project.Nodes.Select(n=>n.X));
        Assert.Equal(6,project.Nodes.Length);
        Assert.Equal(4,project.Connections.Length);
    }

    [Fact]
    public void ShouldKeepCrossProjectPassageOpenWhenPullingChildTowardsParent()
    {
        RenderNode Node(string id,double x,double y)=>new(id,id,id,"blue",x,y,180,60,[]);
        RenderConnection Edge(string a,string b)=>new(a+b,a,b,a,b,false,[]);
        var project=new RenderProject("p","P",0,0,1000,400,
            [Node("Source",0,0),Node("Child",240,160)],[Edge("Source","Child")]);
        var model=new RenderModel(1000,700,[project,new RenderProject("q","External",0,500,1000,200,[Node("External",500,0)],[])])
            {CrossProjectConnections=[Edge("Source","External")]};
        model.Configuration.NoDuplicates=true;
        model.PassageOffsetParents.Add("Source");
        new NodeLocalityLayoutRuleProcessingService().ApplyRule(model);
        Assert.True(project.Nodes.Single(n=>n.Id=="Child").X>=180);
    }

    [Fact]
    public void ShouldMeasureSharedBranchesUsingTheirOccupiedRows()
    {
        RenderNode Node(string id,double x,double y)=>new(id,id,id,"blue",x,y,180,60,[]);
        RenderConnection Edge(string a,string b)=>new(a+b,a,b,a,b,false,[]);
        var project=new RenderProject("p","P",0,0,1000,600,
            [Node("A",0,0),Node("B",480,0),Node("AChild",480,160),Node("BChild",0,320)],
            [Edge("A","AChild"),Edge("B","BChild")]);
        Assert.Equal(300,LayoutGraph.BranchClearance(project,"A","B",true,respectBranchOrder:false));
        Assert.True(LayoutGraph.BranchClearance(project,"A","B",false)<0);
    }
}
