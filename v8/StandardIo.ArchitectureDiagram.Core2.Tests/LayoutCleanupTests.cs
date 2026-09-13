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
