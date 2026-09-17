using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using StandardIo.ArchitectureDiagram.Core2.Models;
using StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Rendering;
using StandardIo.ArchitectureDiagram.Core2.Services.Processings.Layout;
using Xunit;
namespace StandardIo.ArchitectureDiagram.Core2.Tests;
public class LargeTreeConvergenceTests
{
    [Fact]
    public void ShouldSettleTheCombinedContentManagementGraph()
    {
        using var stream=typeof(LargeTreeConvergenceTests).Assembly.GetManifestResourceStream(
            "StandardIo.ArchitectureDiagram.Core2.Tests.Fixtures.ContentManagementCombined.layout.json")!;
        var project=JsonSerializer.Deserialize<RenderProject>(new StreamReader(stream).ReadToEnd())!;
        double originalWidth=project.Width;
        int originalNodes=project.Nodes.Length, originalConnections=project.Connections.Length;
        var model=new RenderModel(0,0,[project]);
        model.Configuration.NoDuplicates=true;
        model.Configuration.MaxLayoutIterations=25;
        var service=TestServices.Get<IProjectModelLayoutService>();
        service.Layout(model);
        var settled=LayoutState.Capture(model);
        service.Layout(model);
        Assert.Equal(settled,LayoutState.Capture(model));
        Assert.Equal(originalNodes,model.Projects[0].Nodes.Length);
        Assert.Equal(originalConnections,model.Projects[0].Connections.Length);
        Assert.InRange(model.Projects[0].Width,1,originalWidth);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ShouldSettleRealWebApplicationTreeWithoutAccumulatingSpace(bool noDuplicates)
    {
        // Given: the exact 159-node graph captured from the reported diagram.
        using var stream=typeof(LargeTreeConvergenceTests).Assembly.GetManifestResourceStream(
            "StandardIo.ArchitectureDiagram.Core2.Tests.Fixtures.WebApplicationExtensions.layout.json")!;
        var project=JsonSerializer.Deserialize<RenderProject>(new StreamReader(stream).ReadToEnd())!;
        var model=new RenderModel(0,0,[project]);
        model.Configuration.NoDuplicates=noDuplicates;
        model.Configuration.MaxLayoutIterations=25;
        var service=TestServices.Get<IProjectModelLayoutService>();
        // When: arrange, then arrange the same instance again.
        service.Layout(model);
        var settled=LayoutState.Capture(model);
        service.Layout(model);
        // Then: repeated execution neither moves nodes nor widens the diagram.
        Assert.Equal(settled,LayoutState.Capture(model));
        Assert.Equal(159,model.Projects[0].Nodes.Length);
        Assert.Equal(253,model.Projects[0].Connections.Length);
        Assert.InRange(model.Projects[0].Width,1,33453.12);
    }
}
