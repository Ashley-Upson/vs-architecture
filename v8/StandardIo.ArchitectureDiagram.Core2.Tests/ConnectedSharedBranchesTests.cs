// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System.Linq;
using StandardIo.ArchitectureDiagram.Core2.Models;
using StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Rendering;
using Xunit;
namespace StandardIo.ArchitectureDiagram.Core2.Tests;
public sealed class ConnectedSharedBranchesTests
{
    [Fact]
    public void ShouldSettleAConnectedGraphOfSharedServices()
    {
        // Given: fourteen nodes reduced from the failing ContentManagement layout.
        var nodes = new[] {
            new RenderNode("tree-0-node-25", "cCoder.ContentManagement.Brokers.RoleBroker", "", "#123456", 0, 0, 180, 60, []),
            new RenderNode("tree-0-node-36", "cCoder.ContentManagement.Brokers.Storages.PageBroker", "", "#123456", 0, 0, 180, 60, []),
            new RenderNode("tree-0-node-54", "cCoder.ContentManagement.Exposures.AuthorizationManager", "", "#123456", 0, 0, 180, 60, []),
            new RenderNode("tree-0-node-127", "cCoder.ContentManagement.Services.Foundations.Rendering.ComponentRenderService", "", "#123456", 0, 0, 180, 60, []),
            new RenderNode("tree-0-node-133", "cCoder.ContentManagement.Services.Foundations.Storages.AppService", "", "#123456", 0, 0, 180, 60, []),
            new RenderNode("tree-0-node-135", "cCoder.ContentManagement.Services.Foundations.Storages.ComponentService", "", "#123456", 0, 0, 180, 60, []),
            new RenderNode("tree-0-node-145", "cCoder.ContentManagement.Services.Foundations.Storages.ResourceService", "", "#123456", 0, 0, 180, 60, []),
            new RenderNode("tree-0-node-146", "cCoder.ContentManagement.Services.Foundations.Storages.ScriptService", "", "#123456", 0, 0, 180, 60, []),
            new RenderNode("tree-0-node-184", "cCoder.ContentManagement.Services.Processings.ComponentRenderProcessingService", "", "#123456", 0, 0, 180, 60, []),
            new RenderNode("tree-0-node-189", "cCoder.ContentManagement.Services.Processings.CurrentAppProcessingService", "", "#123456", 0, 0, 180, 60, []),
            new RenderNode("tree-0-node-217", "cCoder.ContentManagement.Services.Processings.PageRoleImportLookupProcessingService", "", "#123456", 0, 0, 180, 60, []),
            new RenderNode("tree-0-node-219", "cCoder.ContentManagement.Services.Processings.PageRoleProcessingService", "", "#123456", 0, 0, 180, 60, []),
            new RenderNode("tree-0-node-221", "cCoder.ContentManagement.Services.Processings.ResourceProcessingService", "", "#123456", 0, 0, 180, 60, []),
            new RenderNode("tree-0-node-223", "cCoder.ContentManagement.Services.Processings.ScriptProcessingService", "", "#123456", 0, 0, 180, 60, [])
        };
        var edges = new[] { (8, 3), (8, 4), (8, 5), (8, 6), (8, 7), (9, 4), (10, 0), (10, 1), (11, 1), (11, 2), (12, 2), (12, 6), (13, 7) }
            .Select((pair, index) => new RenderConnection("edge-" + index, nodes[pair.Item1].Id, nodes[pair.Item2].Id,
                nodes[pair.Item1].TypeName, nodes[pair.Item2].TypeName, false, [])).ToArray();
        var model = new RenderModel(0, 0, [new RenderProject("project", "Project", 0, 0, 300, 200, nodes, edges)]);
        model.Configuration.MaxLayoutIterations = 1000;
        // When: execute every layout rule and validate the resulting diagram.
        TestServices.Get<IProjectModelLayoutService>().Layout(model);
        // Then: retain the graph and satisfy the rules in a bounded drawing.
        Assert.Equal(14, model.Projects[0].Nodes.Length);
        Assert.Equal(13, model.Projects[0].Connections.Length);
        Assert.InRange(model.Width, 0, 10000);
    }
}
