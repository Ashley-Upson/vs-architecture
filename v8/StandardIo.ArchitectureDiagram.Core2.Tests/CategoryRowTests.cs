using System.Linq;
using StandardIo.ArchitectureDiagram.Core2.Exposures;
using StandardIo.ArchitectureDiagram.Core2.Models;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core2.Tests;

public sealed class CategoryRowTests
{
    [Fact]
    public void ShouldReserveExclusiveRowsForCategories()
    {
        var project = RenderConfigurationTests.Project("Example", "EntryManager", "AlphaProcessingService", "BetaProcessingService", "AlphaBroker", "BetaBroker", "Oddity");
        project.Dependencies = [RenderConfigurationTests.Link("EntryManager", "AlphaProcessingService"),
            RenderConfigurationTests.Link("EntryManager", "AlphaBroker"), RenderConfigurationTests.Link("EntryManager", "Oddity")];
        var result = TestServices.Get<LayoutModelBuilder>().BuildRenderModel(new RenderModel([project]));
        var nodes = result.Projects.SelectMany(p => p.Nodes).ToArray();
        var quartzRows = nodes.Where(n => n.TypeName.EndsWith("ProcessingService")).Select(n => n.Y).ToHashSet();
        Assert.All(nodes.Where(n => !n.TypeName.EndsWith("ProcessingService")), n => Assert.DoesNotContain(n.Y, quartzRows));
        Assert.Single(quartzRows);
    }

    [Fact]
    public void ShouldRetainDependencyDepthAcrossRepeatedCategories()
    {
        var project = RenderConfigurationTests.Project("Example", "AlphaProcessingService", "BetaProcessingService", "GammaProcessingService", "AlphaBroker", "BetaBroker");
        project.Dependencies = [RenderConfigurationTests.Link("AlphaProcessingService", "BetaProcessingService"),
            RenderConfigurationTests.Link("BetaProcessingService", "AlphaBroker"),
            RenderConfigurationTests.Link("BetaBroker", "GammaProcessingService")];
        var result = TestServices.Get<LayoutModelBuilder>().BuildRenderModel(new RenderModel([project]));
        var nodes = result.Projects.SelectMany(p => p.Nodes).ToDictionary(n => n.TypeName);
        foreach (var link in project.Dependencies)
            Assert.True(nodes[link.ToType!].Y > nodes[link.FromType!].Y);
    }

    [Theory]
    [InlineData("AlphaProcessingService", "BetaProcessingService", "AlphaBroker", "BetaBroker")]
    [InlineData("Example.Processings.Alpha", "Example.Processings.Beta", "Example.Brokers.Alpha", "Example.Brokers.Beta")]
    public void ShouldUseSharedCategoriesAndAlignRowsAcrossProjects(string a, string b, string c, string d)
    {
        var first = RenderConfigurationTests.Project("First", "EntryManager", a, c);
        first.Dependencies = [RenderConfigurationTests.Link("EntryManager", a), RenderConfigurationTests.Link(a, c)];
        var second = RenderConfigurationTests.Project("Second", b, d);
        second.Dependencies = [RenderConfigurationTests.Link(b, d)];
        var result = TestServices.Get<LayoutModelBuilder>().BuildRenderModel(new RenderModel([first, second]));
        var nodes = result.Projects.SelectMany(p => p.Nodes).ToDictionary(n => n.TypeName);
        Assert.Equal(nodes[a].Y, nodes[b].Y);
        Assert.Equal(nodes[c].Y, nodes[d].Y);
        Assert.True(nodes[c].Y > nodes[a].Y);
    }
}
