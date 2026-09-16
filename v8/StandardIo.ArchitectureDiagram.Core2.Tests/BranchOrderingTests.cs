// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System.Linq;
using StandardIo.ArchitectureDiagram.Core2.Models;
using StandardIo.ArchitectureDiagram.Core2.Services.Processings.Layout;
using Xunit;
namespace StandardIo.ArchitectureDiagram.Core2.Tests;
public sealed class BranchOrderingTests
{
    [Fact]
    public void ShouldOrderSharedLeafGroupsByTheirConsumersInCombinedView()
    {
        RenderNode Node(string id,double x,double y)=>new(id,id,id,"blue",x,y,180,60,[]);
        RenderConnection Edge(string a,string b)=>new(a+b,a,b,a,b,false,[]);
        var project=new RenderProject("p","P",0,0,2000,600,
            [Node("a",0,0),Node("b",240,0),Node("c",720,0),Node("d",960,0),
             Node("aa",0,160),Node("bb",240,160),Node("cc",720,160),Node("dd",960,160),
             Node("right1",0,320),Node("right2",240,320),Node("left1",720,320),Node("left2",960,320)],
            [Edge("a","aa"),Edge("b","bb"),Edge("c","cc"),Edge("d","dd"),
             Edge("aa","left1"),Edge("bb","left1"),Edge("aa","left2"),Edge("bb","left2"),
             Edge("cc","right1"),Edge("dd","right1"),Edge("cc","right2"),Edge("dd","right2")]);
        var model=new RenderModel(2000,600,[project]);model.Configuration.NoDuplicates=true;
        new LayoutCleanupRuleProcessingService().ApplyRule(model);
        double X(string id)=>project.Nodes.Single(n=>n.Id==id).X;
        Assert.True(System.Math.Max(X("left1"),X("left2"))<System.Math.Min(X("right1"),X("right2")));
    }

    [Fact]
    public void ShouldKeepBranchesSharingFactoryAdjacentInsteadOfStraddlingEventBranch()
    {
        RenderNode Node(string id,double x,double y)=>new(id,id,id,"blue",x,y,180,60,[]);
        RenderConnection Edge(string a,string b)=>new(a+b,a,b,a,b,false,[]);
        var project=new RenderProject("p","P",0,0,1000,600,
            [Node("root",240,0),Node("authorization",0,160),Node("event",240,160),Node("page",480,160),
             Node("factory",0,320),Node("hub",240,320),Node("context",480,320)],
            [Edge("root","authorization"),Edge("root","event"),Edge("root","page"),Edge("authorization","factory"),
             Edge("event","hub"),Edge("page","context"),Edge("page","factory")]);
        var model=new RenderModel(1000,600,[project]);
        new LayoutCleanupRuleProcessingService().ApplyRule(model);
        var ordered=project.Nodes.Where(n=>n.Y==160).OrderBy(n=>n.X).Select(n=>n.Id).ToArray();
        Assert.Equal(1,System.Math.Abs(System.Array.IndexOf(ordered,"authorization")-System.Array.IndexOf(ordered,"page")));
    }

    [Fact]
    public void ShouldPlaceSharedOrchestrationBetweenItsCoordinationBranches()
    {
        RenderNode Node(string id,double x,double y)=>new(id,id,id,"blue",x,y,180,60,[]);
        RenderConnection Edge(string a,string b)=>new(a+b,a,b,a,b,false,[]);
        var project=new RenderProject("p","P",0,0,2000,800,
            [Node("aggregate",700,0),Node("lifecycle",600,160),Node("manager",1200,160),
             Node("app",0,320),Node("bootstrap",480,320),Node("role",720,320),Node("page",1200,320),Node("appChild",0,480)],
            [Edge("aggregate","lifecycle"),Edge("aggregate","manager"),Edge("lifecycle","app"),Edge("lifecycle","bootstrap"),
             Edge("lifecycle","role"),Edge("manager","app"),Edge("manager","page"),Edge("app","appChild")]);
        var model=new RenderModel(2000,800,[project]);
        var cleanup=new LayoutCleanupRuleProcessingService();cleanup.ApplyRule(model);
        double X(string id)=>project.Nodes.Single(n=>n.Id==id).X;
        Assert.True(X("bootstrap")<X("role") && X("role")<X("app") && X("app")<X("page"));
        Assert.Equal(X("app"),X("appChild"));
        var positions=project.Nodes.Select(n=>n.X).ToArray();cleanup.ApplyRule(model);
        Assert.Equal(positions,project.Nodes.Select(n=>n.X).ToArray());
    }

    [Fact]
    public void ShouldFinishDescendantSpacingBeforePlacingNeighbouringTrees()
    {
        // Given: many consumers each own a deep branch and share another dependency.
        var nodes = Enumerable.Range(0, 40).SelectMany(index => new[]
        {
            new RenderNode($"parent-{index}", $"Parent{index}", "", "#123456", 0, 0, 180, 60, []),
            new RenderNode($"child-{index}", $"Child{index}", "", "#123456", 0, 160, 180, 60, []),
            new RenderNode($"shared-{index}", $"Shared{index}", "", "#123456", 0, 160, 180, 60, [])
        }).ToArray();
        var edges = Enumerable.Range(0, 40).SelectMany(index => new[]
        {
            ($"parent-{index}", $"child-{index}"),
            ($"parent-{index}", $"shared-{index}"),
            ($"parent-{(index + 1) % 40}", $"shared-{index}")
        }).Select((pair, index) => new RenderConnection($"edge-{index}", pair.Item1, pair.Item2, nodes.Single(node => node.Id == pair.Item1).TypeName, nodes.Single(node => node.Id == pair.Item2).TypeName, false, [])).ToArray();
        var model = new RenderModel(0, 0, [new RenderProject("project", "Project", 0, 0, 100, 100, nodes, edges)]);
        model.Configuration.MaxLayoutIterations = 5;

        // When: use the complete rule pipeline with a small bounded pass budget.
        TestServices.Get<StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Rendering.IProjectModelLayoutService>().Layout(model);

        // Then: each owned child remains centred and every row has the configured clearance.
        foreach (var parent in model.Projects[0].Nodes.Where(node => node.Id.StartsWith("parent-")))
        {
            var child = model.Projects[0].Nodes.Single(node => node.Id == parent.Id.Replace("parent-", "child-"));
            Assert.Equal(parent.X, child.X);
        }
        foreach (var row in model.Projects[0].Nodes.GroupBy(node => node.Y))
        {
            var ordered = row.OrderBy(node => node.X).ToArray();
            for (int index = 1; index < ordered.Length; index++)
                Assert.True(ordered[index].X - ordered[index - 1].X - ordered[index - 1].Width >= 60 - 0.01);
        }
        Assert.Equal(120, model.Projects[0].Nodes.Length);
        Assert.Equal(120, model.Projects[0].Connections.Length);
    }

    [Fact]
    public void ShouldOrderAnInternalSharedBranchWithItsOwningTree()
    {
        // Given: A owns a diamond ending at Shared; B owns an independent chain.
        // Shared initially sits beyond B, but still belongs to A's reserved space.
        RenderNode Node(string id, double x, double y) => new(id, id, id, "#123456", x, y, 180, 60, []);
        RenderConnection Edge(string source, string target) => new(source + target, source, target, source, target, false, []);
        var project = new RenderProject("project", "Project", 0, 0, 2000, 600,
            [Node("A", 0, 0), Node("P", 0, 160), Node("Q", 240, 160), Node("Shared", 1000, 320),
             Node("B", 600, 0), Node("T", 600, 160), Node("R", 600, 320)],
            [Edge("A", "P"), Edge("A", "Q"), Edge("P", "Shared"), Edge("Q", "Shared"), Edge("B", "T"), Edge("T", "R")]);

        // When: space all overlapping branch groups in one rule pass.
        new BranchSpacingLayoutRuleProcessingService().ApplyRule(new RenderModel(2000, 600, [project]));

        // Then: ordering the internal shared branch cannot reverse its owner's order against B.
        var left = LayoutGraph.OwnedBranch(project, "A");
        var right = LayoutGraph.OwnedBranch(project, "B");
        Assert.True(right.Min(node => node.X) - left.Max(node => node.X + node.Width) >= 60 - 0.01);
    }
}
