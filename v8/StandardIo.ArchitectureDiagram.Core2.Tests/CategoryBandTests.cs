using System.Linq;
using StandardIo.ArchitectureDiagram.Core2.Exposures;
using StandardIo.ArchitectureDiagram.Core2.Models;
using Xunit;
namespace StandardIo.ArchitectureDiagram.Core2.Tests;

public sealed class CategoryBandTests
{
    [Fact]
    public void ShouldAlignExposurePeersAndRepeatOnlyOccupiedCategories()
    {
        var project = RenderConfigurationTests.Project("Example", "SchoolManager", "SchoolController", "Example.Exposures.Events",
            "SchoolProcessingService", "SchoolBroker", "RemoteClient", "RemoteBroker");
        project.Dependencies = [RenderConfigurationTests.Link("SchoolManager", "SchoolProcessingService"),
            RenderConfigurationTests.Link("SchoolController", "SchoolProcessingService"),
            RenderConfigurationTests.Link("Example.Exposures.Events", "SchoolProcessingService"),
            RenderConfigurationTests.Link("SchoolProcessingService", "SchoolBroker"),
            RenderConfigurationTests.Link("SchoolBroker", "RemoteClient"),
            RenderConfigurationTests.Link("RemoteClient", "RemoteBroker")];
        var result = TestServices.Get<LayoutModelBuilder>().BuildRenderModel(new RenderModel([project]));
        var nodes = result.Projects.Single().Nodes.ToDictionary(n => n.TypeName);
        Assert.Equal(nodes["SchoolManager"].Y, nodes["SchoolController"].Y);
        Assert.Equal(nodes["SchoolManager"].Y, nodes["Example.Exposures.Events"].Y);
        var expected = new[] { "SchoolManager", "SchoolProcessingService", "SchoolBroker", "RemoteClient", "RemoteBroker" };
        for (int i = 0; i < expected.Length; i++) Assert.Equal(60 + i * 160, nodes[expected[i]].Y);
        Assert.Equal(nodes["SchoolManager"].Fill, nodes["RemoteClient"].Fill);
    }

    [Theory]
    [InlineData("MessageHub")]
    [InlineData("DataContext")]
    [InlineData("RemoteClient")]
    [InlineData("ForeignComponent")]
    public void ShouldTreatExternalComponentsAsExposures(string name)
    {
        var project = RenderConfigurationTests.Project("Example", "ApplicationManager", name);
        project.Types![1].IsInternal = false;
        var result = TestServices.Get<LayoutModelBuilder>().BuildRenderModel(new RenderModel([project]));
        var nodes = result.Projects.SelectMany(p => p.Nodes).ToArray();
        Assert.Single(nodes.Select(n => n.Y).Distinct());
        Assert.Single(nodes.Select(n => n.Fill).Distinct());
    }
}
