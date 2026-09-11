// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System.Linq;
using System.Text.Json;
using StandardIo.ArchitectureDiagram.Core2.Exposures;
using StandardIo.ArchitectureDiagram.Core2.Models;
using Xunit;
namespace StandardIo.ArchitectureDiagram.Core2.Tests;
public sealed class TreeExternalScopeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ShouldScopeExternalDependenciesToTheSelectedView(bool noDuplicates)
    {
        // Given: two roots share one external type; only the second uses another type in that assembly.
        var project = RenderConfigurationTests.Project("Example", "First", "Second", "External.Hub", "External.Utility");
        foreach (var type in project.Types!.Skip(2)) { type.IsInternal = false; type.AssemblyName = "External"; }
        project.Dependencies = [RenderConfigurationTests.Link("First", "External.Hub"),
            RenderConfigurationTests.Link("Second", "External.Hub"), RenderConfigurationTests.Link("Second", "External.Utility")];
        string before = JsonSerializer.Serialize(project);
        var inputs = noDuplicates ? new[] { project } : TestServices.Get<ProjectModelSplitter>().Split(project);

        // When: prepare and lay out the same graph in either supported mode.
        var model = TestServices.Get<LayoutModelBuilder>().BuildRenderModel(new RenderModel(inputs, new RenderConfiguration { NoDuplicates = noDuplicates }));

        // Then: the combined view has one external box; split trees each have their own complete boundary.
        var external = model.Projects.Where(box => box.Name == "External (external)").ToArray();
        Assert.Equal(noDuplicates ? 1 : 2, external.Length);
        Assert.Equal(noDuplicates ? 2 : 3, external.Sum(box => box.Nodes.Length));
        var firstEdge = Assert.Single(model.CrossProjectConnections, edge => edge.FromType == "First");
        var secondEdges = model.CrossProjectConnections.Where(edge => edge.FromType == "Second").ToArray();
        Assert.Equal(2, secondEdges.Length);
        var firstBox = Assert.Single(external, box => box.Nodes.Any(node => node.Id == firstEdge.TargetId));
        var secondBox = Assert.Single(external, box => box.Nodes.Any(node => node.Id == secondEdges[0].TargetId));
        Assert.All(secondEdges, edge => Assert.Contains(secondBox.Nodes, node => node.Id == edge.TargetId));
        if (noDuplicates) Assert.Equal(firstBox.Id, secondBox.Id);
        else { Assert.NotEqual(firstBox.Id, secondBox.Id); Assert.Single(firstBox.Nodes); }
        Assert.Equal(before, JsonSerializer.Serialize(project));
    }
}
