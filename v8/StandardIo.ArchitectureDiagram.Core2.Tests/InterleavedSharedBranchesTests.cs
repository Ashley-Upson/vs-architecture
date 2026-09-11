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
