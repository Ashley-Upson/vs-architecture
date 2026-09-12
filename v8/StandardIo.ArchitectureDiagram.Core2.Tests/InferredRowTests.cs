using System.Linq;
using StandardIo.ArchitectureDiagram.Core2.Exposures;
using StandardIo.ArchitectureDiagram.Core2.Models;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core2.Tests;

public sealed class InferredRowTests
{
    [Fact]
    public void ShouldRetainDepthInsideAGroupAndFallBackForContradictoryGroups()
    {
        var project = RenderConfigurationTests.Project("Example", "AlphaQuartz", "BetaQuartz", "GammaQuartz", "AlphaMarble", "BetaMarble");
        project.Dependencies = [RenderConfigurationTests.Link("AlphaQuartz", "BetaQuartz"),
            RenderConfigurationTests.Link("BetaQuartz", "AlphaMarble"),
            RenderConfigurationTests.Link("BetaMarble", "GammaQuartz")];
        var result = TestServices.Get<LayoutModelBuilder>().BuildRenderModel(new RenderModel([project]));
        var nodes = result.Projects.SelectMany(p => p.Nodes).ToDictionary(n => n.TypeName);
        foreach (var link in project.Dependencies)
            Assert.True(nodes[link.ToType!].Y > nodes[link.FromType!].Y);
    }

    [Theory]
    [InlineData("AlphaQuartz", "BetaQuartz", "AlphaMarble", "BetaMarble")]
    [InlineData("QuartzAlpha", "QuartzBeta", "MarbleAlpha", "MarbleBeta")]
    public void ShouldInferArbitraryAffixesAndAlignRowsAcrossProjects(string a, string b, string c, string d)
    {
        var first = RenderConfigurationTests.Project("First", "Entry", a, c);
        first.Dependencies = [RenderConfigurationTests.Link("Entry", a), RenderConfigurationTests.Link(a, c)];
        var second = RenderConfigurationTests.Project("Second", b, d);
        second.Dependencies = [RenderConfigurationTests.Link(b, d)];
        var result = TestServices.Get<LayoutModelBuilder>().BuildRenderModel(new RenderModel([first, second]));
        var nodes = result.Projects.SelectMany(p => p.Nodes).ToDictionary(n => n.TypeName);
        Assert.Equal(nodes[a].Y, nodes[b].Y);
        Assert.Equal(nodes[c].Y, nodes[d].Y);
        Assert.True(nodes[c].Y > nodes[a].Y);
    }
}
