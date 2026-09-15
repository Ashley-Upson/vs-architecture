using System;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using StandardIo.ArchitectureDiagram.Core2.Exposures;
using StandardIo.ArchitectureDiagram.Core2.Models;
using StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Rendering;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core2.Tests;

public sealed class LayoutCleanupTests
{
    [Fact]
    public void ShouldFitShallowSiblingAboveWideDeepDescendants()
    {
        RenderNode Node(string id,double x,double y)=>new(id,id,id,"blue",x,y,210,60,[]);
        RenderConnection Edge(string a,string b)=>new(a+b,a,b,a,b,false,[]);
        var project=new RenderProject("p","P",0,0,2500,800,
            [Node("root",900,0),Node("deep",540,160),Node("shallow",1600,160),Node("fork",540,320),Node("shortLeaf",1600,320),
             Node("one",0,480),Node("two",540,480),Node("three",1080,480)],
            [Edge("root","deep"),Edge("root","shallow"),Edge("deep","fork"),Edge("fork","one"),Edge("fork","two"),Edge("fork","three"),Edge("shallow","shortLeaf")]);
        var model=new RenderModel(2500,800,[project]);
        var cleanup=new StandardIo.ArchitectureDiagram.Core2.Services.Processings.Layout.LayoutCleanupRuleProcessingService();
        cleanup.ApplyRule(model);
        RenderNode Find(string id)=>project.Nodes.Single(n=>n.Id==id);
        Assert.Equal(270,Find("shallow").X-Find("deep").X,5);
        Assert.Equal(Find("shallow").X,Find("shortLeaf").X);
        var positions=project.Nodes.Select(n=>n.X).ToArray();cleanup.ApplyRule(model);
        Assert.Equal(positions,project.Nodes.Select(n=>n.X).ToArray());
    }

    [Fact]
    public void ShouldPackBranchesBeforePlacingSharedLeafInVacantBottomSlot()
    {
        RenderNode Node(string id,double x,double y)=>new(id,id,id,"orange",x,y,210,60,[]);
        RenderConnection Edge(string a,string b)=>new(a+b,a,b,a,b,false,[]);
        var project=new RenderProject("p","P",0,0,1700,700,
            [Node("root",985,0),Node("authorization",580,160),Node("events",1120,160),Node("component",1390,160),
             Node("hub",1120,320),Node("context",1390,320),Node("factory",850,320)],
            [Edge("root","authorization"),Edge("root","events"),Edge("root","component"),Edge("authorization","factory"),
             Edge("events","hub"),Edge("component","context"),Edge("component","factory")]);
        var model=new RenderModel(1700,700,[project]);
        var cleanup=new StandardIo.ArchitectureDiagram.Core2.Services.Processings.Layout.LayoutCleanupRuleProcessingService();
        cleanup.ApplyRule(model);
        RenderNode Find(string id)=>project.Nodes.Single(n=>n.Id==id);
        var branchPositions = new[] { Find("authorization").X, Find("events").X, Find("component").X }.OrderBy(x => x).ToArray();
        Assert.Equal(270,branchPositions[1]-branchPositions[0],5);
        Assert.Equal(270,branchPositions[2]-branchPositions[1],5);
        Assert.Equal(Find("authorization").X,Find("factory").X,5);
        var positions=project.Nodes.Select(n=>n.X).ToArray();
        cleanup.ApplyRule(model);
        Assert.Equal(positions,project.Nodes.Select(n=>n.X).ToArray());
    }

    [Fact]
    public void ShouldReclaimUnusedSpaceBetweenCompletedBranchesAndRemainStable()
    {
        var nodes = new[] {
            new RenderNode("a", "A", "A", "orange", 40, 60, 180, 60, []),
            new RenderNode("b", "B", "B", "orange", 1040, 60, 180, 60, []),
            new RenderNode("c", "C", "C", "orange", 40, 220, 180, 60, []),
            new RenderNode("d", "D", "D", "orange", 1040, 220, 180, 60, []) };
        var edges = new[] {
            new RenderConnection("ac", "a", "c", "A", "C", false, []),
            new RenderConnection("bd", "b", "d", "B", "D", false, []) };
        var model = new RenderModel(1400, 400, [new RenderProject("p", "P", 40, 40, 1300, 350, nodes, edges)])
        { LayoutInitialized = true, SharedParentLayoutInitialized = true };
        var service = TestServices.Get<IProjectModelLayoutService>();
        service.Layout(model);
        Assert.Equal(60, model.Projects[0].Nodes[1].X - model.Projects[0].Nodes[0].X - 180, 5);
        Assert.Equal(model.Projects[0].Nodes[0].X, model.Projects[0].Nodes[2].X);
        Assert.Equal(model.Projects[0].Nodes[1].X, model.Projects[0].Nodes[3].X);
        var positions = model.Projects[0].Nodes.Select(n => (n.X, n.Y)).ToArray();
        service.Layout(model);
        Assert.Equal(positions, model.Projects[0].Nodes.Select(n => (n.X, n.Y)).ToArray());
    }

    [Fact]
    public void ShouldKeepFrameworkAndApplicationBehaviourVisible()
    {
        var project = RenderConfigurationTests.Project("App", "App.Service", "System.Text.RegularExpressions.Regex", "Systematic.Service");
        project.Dependencies = [new() { DependencyType = DependencyType.Consumed, FromType = "App.Service", ToType = "System.Text.RegularExpressions.Regex" }, new() { DependencyType = DependencyType.Consumed, FromType = "App.Service", ToType = "Systematic.Service" }];
        var result = TestServices.Get<IProjectModelPresentationService>().Prepare(project);
        Assert.Equal(new[] { "App.Service", "System.Text.RegularExpressions.Regex", "Systematic.Service" }, result.Model.Types!.Select(t => t.Name));
        Assert.Equal(2, result.Model.Dependencies!.Length);
    }

    [Fact]
    public void ShouldDefaultToWiderNodesAndMoreProjectClearance()
    {
        var configuration = new ArchitectureRenderConfiguration();
        Assert.Equal(210, configuration.NodeWidth);
        Assert.Equal(150, configuration.ProjectSpacing);
    }

    [Fact]
    public void ShouldDisableDrawIOGridAndPageView()
    {
        var project = RenderConfigurationTests.Project("App", "App.Service");
        var bytes = TestServices.Get<DrawIODiagramRenderer>().Render(new RenderModel([project]));
        var graph = XDocument.Parse(Encoding.UTF8.GetString(bytes)).Descendants("mxGraphModel").Single();
        Assert.Equal("0", (string?)graph.Attribute("grid"));
        Assert.Equal("0", (string?)graph.Attribute("page"));
    }
}
