// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System.Linq;
using StandardIo.ArchitectureDiagram.Core2.Exposures;
using StandardIo.ArchitectureDiagram.Core2.Models;
using StandardIo.ArchitectureDiagram.Core2.Services.Processings.Layout;
using StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Rendering;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core2.Tests;

public sealed class DirectAndIndirectDependencyLayoutTests
{
    [Fact]
    public void ShouldKeepTheEntryDependencyIntoACycle()
    {
        // Given: the only path from Root to B passes through the destination A.
        RenderConnection Edge(string source, string target) => new(source + target, source, target, source, target, false, []);
        var connections = new[] { Edge("Root", "A"), Edge("A", "B"), Edge("B", "A") };

        // When: remove redundant placement relationships.
        var topology = new LayoutTopology(connections);

        // Then: the entry link is not redundant; deleting it would detach the whole cycle.
        Assert.Contains("Root", topology.Parents["A"]);
    }

    [Fact]
    public void ShouldSettleSharedBranchesAlongsideIndependentTrees()
    {
        // Given: a reduced ContentManagement graph with two shared services and two independent chains.
        var names = new[] { "PageBroker", "AuthorizationManager", "PackageItemService", "PackageService",
            "PageRoleService", "ScriptService", "ComponentRenderProcessingService", "PackageItemProcessingService",
            "PackageProcessingService", "PageRoleImportLookupProcessingService", "PageRoleProcessingService", "TemplateRenderProcessingService" };
        var ids = new[] { 36, 54, 139, 140, 143, 146, 184, 196, 197, 217, 219, 228 };
        var nodes = names.Select((name, index) => new RenderNode("tree-0-node-" + ids[index], name, name,
            "#123456", 0, 0, 180, 60, [])).ToArray();
        var edges = new[] { (6, 5), (7, 2), (8, 3), (9, 0), (10, 0), (10, 1), (10, 4), (11, 5) }
            .Select((pair, index) => new RenderConnection("edge-" + index, nodes[pair.Item1].Id, nodes[pair.Item2].Id,
                names[pair.Item1], names[pair.Item2], false, [])).ToArray();
        var renderModel = new RenderModel(0, 0, [new RenderProject("project", "Project", 0, 0, 300, 200, nodes, edges)]);
        renderModel.Configuration.MaxLayoutIterations = 1000;

        // When: apply the full rule and validation pipeline.
        TestServices.Get<IProjectModelLayoutService>().Layout(renderModel);

        // Then: preserve the graph in a bounded diagram satisfying the layout validator.
        Assert.Equal(12, renderModel.Projects[0].Nodes.Length);
        Assert.Equal(8, renderModel.Projects[0].Connections.Length);
        Assert.InRange(renderModel.Width, 0, 10000);
    }

    [Fact]
    public void ShouldUseCurrentPositionsWhenAnEarlierSharedGroupMovesALaterGroup()
    {
        // Given: A and B share S. S's two children P and Q share T, already centred.
        RenderNode Node(string id, double x, double y) => new(id, id, id, "#123456", x, y, 180, 60, []);
        RenderConnection Edge(string source, string target) => new(source + target, source, target, source, target, false, []);
        var project = new RenderProject("project", "Project", 0, 0, 1500, 800,
            [Node("A", 0, 0), Node("B", 240, 0), Node("S", 1000, 160),
             Node("P", 880, 320), Node("Q", 1120, 320), Node("T", 1000, 480)],
            [Edge("A", "S"), Edge("B", "S"), Edge("S", "P"), Edge("S", "Q"), Edge("P", "T"), Edge("Q", "T")]);

        // When: aligning S moves its whole branch, including the later shared group T.
        new SharedParentCentringLayoutRuleProcessingService().ApplyRule(new RenderModel(1500, 800, [project]));

        // Then: T remains centred using the new positions, not the pre-pass snapshot.
        var target = project.Nodes.Single(node => node.Id == "T");
        var parents = project.Nodes.Where(node => node.Id is "P" or "Q").ToArray();
        Assert.Equal(LayoutGraph.Midpoint(parents), LayoutGraph.Centre(target), 6);
    }

    [Fact]
    public void ShouldRetainUnrelatedParentsAndParentsWithinACycle()
    {
        // Given: two mutually reachable parents and one independent consumer.
        RenderConnection Edge(string source, string target) => new(
            source + target, source, target, source, target, false, []);
        var connections = new[] { Edge("A", "B"), Edge("B", "A"),
            Edge("A", "Target"), Edge("B", "Target"), Edge("C", "Target") };

        // When: derive placement relationships without discarding cycle members.
        var topology = new LayoutTopology(connections);

        // Then: none of these three consumers is a redundant ancestor.
        Assert.Equal(new[] { "A", "B", "C" }, topology.Parents["Target"]);
        Assert.Equal(5, connections.Length);
    }

    [Fact]
    public void ShouldUseTheNearestParentAcrossSeveralDirectShortcuts()
    {
        // Given: A -> B -> C -> D, with additional direct calls A -> D and B -> D.
        RenderConnection Edge(string source, string target) => new(
            source + target, source, target, source, target, false, []);
        var connections = new[] { Edge("A", "B"), Edge("B", "C"), Edge("C", "D"),
            Edge("A", "D"), Edge("B", "D") };

        // When: select placement parents.
        var topology = new LayoutTopology(connections);

        // Then: C places D, while all five displayed connections remain available.
        Assert.Equal(new[] { "C" }, topology.Parents["D"]);
        Assert.Equal(new[] { "B" }, topology.Children["A"]);
        Assert.Equal(5, connections.Length);
    }

    [Fact]
    public void ShouldLayoutDirectAndIndirectCallsToTheSameBroker()
    {
        // PageRoleProcessingService calls PageBroker directly and through PageRoleService.
        // PageRoleService also calls PageRoleBroker. All four links must remain visible.
        var projectModel = RenderConfigurationTests.Project(
            "DirectAndIndirectCalls", "ProcessingService", "FoundationService", "SharedBroker", "OtherBroker");
        projectModel.Dependencies = new[] { (0, 1), (0, 2), (1, 2), (1, 3) }
            .Select(pair => RenderConfigurationTests.Link(
                projectModel.Types![pair.Item1].Name!, projectModel.Types[pair.Item2].Name!))
            .ToArray();
        var renderConfiguration = new RenderConfiguration { MaxLayoutIterations = 100 };

        var renderModel = TestServices.Get<LayoutModelBuilder>().BuildRenderModel(
            new RenderModel(new[] { projectModel }, renderConfiguration));

        var renderProject = Assert.Single(renderModel.Projects);
        Assert.Equal(4, renderProject.Nodes.Length);
        Assert.Equal(4, renderProject.Connections.Length);
        foreach (var row in renderProject.Nodes.GroupBy(node => node.Y))
        {
            var nodes = row.OrderBy(node => node.X).ToArray();
            for (int index = 1; index < nodes.Length; index++)
                Assert.True(nodes[index].X - nodes[index - 1].X - nodes[index - 1].Width
                    >= renderConfiguration.Architecture.NodeSpacing - 0.01);
        }
    }
}
