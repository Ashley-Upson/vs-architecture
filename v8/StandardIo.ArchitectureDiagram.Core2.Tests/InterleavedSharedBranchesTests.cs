// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System.Linq;
using StandardIo.ArchitectureDiagram.Core2.Models;
using StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Rendering;
using Xunit;
namespace StandardIo.ArchitectureDiagram.Core2.Tests;
public sealed class InterleavedSharedBranchesTests
{
    [Fact]
    public void SharedBranches_WhenTheyHaveDifferentParents_DoNotOverlap()
    {
        // Given: two shared branches own descendants and are pulled towards different parent groups.
        string[] names = ["Root", "LeftParent", "RightParent", "FarParent", "CommonReader", "MetadataReader", "CommonCache", "Storage", "MetadataCache"];
        double[] x = [0, 0, 0, 0, 827, 1005, 827, 827, 1005];
        double[] y = [0, 120, 120, 120, 480, 240, 600, 720, 360];
        RenderNode[] nodes = names.Select((name, index) =>
            new RenderNode("node-" + index, name, name, "#123456", x[index], y[index], 180, 60, [])).ToArray();
        (int From, int To)[] links = [(0, 1), (0, 2), (0, 3), (1, 4), (2, 5), (3, 5), (4, 6), (6, 7), (5, 8), (8, 4)];
        RenderConnection[] edges = links.Select((link, index) =>
            new RenderConnection("edge-" + index, nodes[link.From].Id, nodes[link.To].Id,
                nodes[link.From].TypeName, nodes[link.To].TypeName, false, [])).ToArray();
        var model = new RenderModel(0, 0, [new RenderProject("project", "Project", 0, 0, 300, 200, nodes, edges)]);
        model.Configuration.MaxLayoutIterations = 100;

        // When: the branch-spacing rule arranges the related shared branches.
        new StandardIo.ArchitectureDiagram.Core2.Services.Processings.Layout.BranchSpacingLayoutRuleProcessingService()
            .ApplyRule(model);

        // Then: the complete branch bounds settle with the configured clearance.
        RenderProject project = Assert.Single(model.Projects);
        RenderNode commonReader = project.Nodes.Single(node => node.TypeName == "CommonReader");
        RenderNode metadataReader = project.Nodes.Single(node => node.TypeName == "MetadataReader");
        RenderNode[][] branches = [
            StandardIo.ArchitectureDiagram.Core2.Services.Processings.Layout.LayoutGraph.OwnedBranch(project, commonReader.Id),
            StandardIo.ArchitectureDiagram.Core2.Services.Processings.Layout.LayoutGraph.OwnedBranch(project, metadataReader.Id)];
        branches = branches.OrderBy(branch => branch.Min(node => node.X)).ToArray();
        Assert.True(branches[1].Min(node => node.X) - branches[0].Max(node => node.X + node.Width) >=
            model.Configuration.Architecture.NodeSpacing - 0.01);
    }

    [Fact]
    public void ShouldSettleInterleavedSharedBranchesWithoutExpandingIndefinitely()
    {
        // Given: seventeen nodes reduced from the failing ContentManagement layout.
        var nodes = new[] {
            new RenderNode("tree-0-node-22", "cCoder.ContentManagement.Brokers.JsonBroker", "", "#123456", 0, 0, 180, 60, []),
            new RenderNode("tree-0-node-25", "cCoder.ContentManagement.Brokers.RoleBroker", "", "#123456", 0, 0, 180, 60, []),
            new RenderNode("tree-0-node-36", "cCoder.ContentManagement.Brokers.Storages.PageBroker", "", "#123456", 0, 0, 180, 60, []),
            new RenderNode("tree-0-node-54", "cCoder.ContentManagement.Exposures.AuthorizationManager", "", "#123456", 0, 0, 180, 60, []),
            new RenderNode("tree-0-node-135", "cCoder.ContentManagement.Services.Foundations.Storages.ComponentService", "", "#123456", 0, 0, 180, 60, []),
            new RenderNode("tree-0-node-139", "cCoder.ContentManagement.Services.Foundations.Storages.PackageItemService", "", "#123456", 0, 0, 180, 60, []),
            new RenderNode("tree-0-node-140", "cCoder.ContentManagement.Services.Foundations.Storages.PackageService", "", "#123456", 0, 0, 180, 60, []),
            new RenderNode("tree-0-node-143", "cCoder.ContentManagement.Services.Foundations.Storages.PageRoleService", "", "#123456", 0, 0, 180, 60, []),
            new RenderNode("tree-0-node-146", "cCoder.ContentManagement.Services.Foundations.Storages.ScriptService", "", "#123456", 0, 0, 180, 60, []),
            new RenderNode("tree-0-node-181", "cCoder.ContentManagement.Services.Processings.CommonObjectProcessingService", "", "#123456", 0, 0, 180, 60, []),
            new RenderNode("tree-0-node-183", "cCoder.ContentManagement.Services.Processings.ComponentProcessingService", "", "#123456", 0, 0, 180, 60, []),
            new RenderNode("tree-0-node-184", "cCoder.ContentManagement.Services.Processings.ComponentRenderProcessingService", "", "#123456", 0, 0, 180, 60, []),
            new RenderNode("tree-0-node-196", "cCoder.ContentManagement.Services.Processings.PackageItemProcessingService", "", "#123456", 0, 0, 180, 60, []),
            new RenderNode("tree-0-node-197", "cCoder.ContentManagement.Services.Processings.PackageProcessingService", "", "#123456", 0, 0, 180, 60, []),
            new RenderNode("tree-0-node-217", "cCoder.ContentManagement.Services.Processings.PageRoleImportLookupProcessingService", "", "#123456", 0, 0, 180, 60, []),
            new RenderNode("tree-0-node-219", "cCoder.ContentManagement.Services.Processings.PageRoleProcessingService", "", "#123456", 0, 0, 180, 60, []),
            new RenderNode("tree-0-node-228", "cCoder.ContentManagement.Services.Processings.TemplateRenderProcessingService", "", "#123456", 0, 0, 180, 60, [])
        };
        var edges = new[] { (9, 0), (9, 3), (10, 4), (11, 0), (11, 8), (12, 5), (13, 6), (14, 2), (15, 1), (15, 2), (15, 3), (15, 7), (16, 4), (16, 8) }
            .Select((pair, index) => new RenderConnection("edge-" + index, nodes[pair.Item1].Id, nodes[pair.Item2].Id,
                nodes[pair.Item1].TypeName, nodes[pair.Item2].TypeName, false, [])).ToArray();
        var model = new RenderModel(0, 0, [new RenderProject("project", "Project", 0, 0, 300, 200, nodes, edges)]);
        model.Configuration.MaxLayoutIterations = 1000;
        // When: execute every layout rule and validate the resulting diagram.
        TestServices.Get<IProjectModelLayoutService>().Layout(model);
        // Then: retain the graph and satisfy the rules in a bounded drawing.
        Assert.Equal(17, model.Projects[0].Nodes.Length);
        Assert.Equal(14, model.Projects[0].Connections.Length);
        Assert.InRange(model.Width, 0, 10000);
    }
}
