// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System;
using System.Linq;
using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.Core2.Exposures;
using StandardIo.ArchitectureDiagram.Core2.Models;
using Xunit;
namespace StandardIo.ArchitectureDiagram.Core2.Tests;
public sealed partial class SampleProjectExtractionTests
{
    [Fact]
    public async Task ShouldPositionSharedReportBrokerInsideItsAncestorTreeWithoutOverlappingItsNeighboursAsync()
    {
        // Given: two processing services share a broker; one also owns a deeper foundation/broker branch.
        var project = await TestServices.Get<ProjectModelBuilder>().BuildAsync(FindSampleProject());
        var tree = TestServices.Get<ProjectModelSplitter>().Split(project).Single(model => model.Types![0].Name!.EndsWith(".SchoolReportManager"));
        var configuration = new RenderConfiguration { MaxLayoutIterations = 100 };
        // When: use the actual extraction, splitter and complete layout rule pipeline.
        var drawing = TestServices.Get<LayoutModelBuilder>().BuildRenderModel(new RenderModel(new[] { tree }, configuration));
        var box = Assert.Single(drawing.Projects);
        // Then: all six nodes survive, settle in a compact finite layout and retain the shared links.
        Assert.Equal(6, box.Nodes.Length);
        Assert.Equal(6, box.Connections.Length);
        Assert.InRange(box.Width, 0, 2000);
        Assert.All(box.Nodes, node => Assert.True(double.IsFinite(node.X) && double.IsFinite(node.Y)));
        var broker = box.Nodes.Single(node => node.TypeName.EndsWith(".SchoolReportBroker"));
        var parents = box.Connections.Where(edge => edge.TargetId == broker.Id).Select(edge => box.Nodes.Single(node => node.Id == edge.SourceId)).ToArray();
        Assert.Equal(2, parents.Length);
        // Shared centring is now a preference: keep this broker between its consumers,
        // while the ordinary root/child centring and spacing assertions remain strict.
        Assert.InRange(broker.X + broker.Width / 2,
            parents.Min(node => node.X + node.Width / 2), parents.Max(node => node.X + node.Width / 2));
        foreach (var row in box.Nodes.GroupBy(node => node.Y))
        {
            var nodes = row.OrderBy(node => node.X).ToArray();
            for (int i = 1; i < nodes.Length; i++)
                Assert.True(nodes[i].X - nodes[i-1].X - nodes[i-1].Width >= configuration.Architecture.NodeSpacing - 0.01);
        }
        var root = box.Nodes.Single(node => node.TypeName.EndsWith(".SchoolReportManager"));
        Assert.InRange(Math.Abs((parents.Min(node => node.X) + parents.Max(node => node.X + node.Width)) / 2 - root.X - root.Width / 2), 0, 0.01);
    }
}
