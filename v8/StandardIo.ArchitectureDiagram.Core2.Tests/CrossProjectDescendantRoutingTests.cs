// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System.Linq;
using StandardIo.ArchitectureDiagram.Core2.Exposures;
using StandardIo.ArchitectureDiagram.Core2.Models;
using Xunit;
namespace StandardIo.ArchitectureDiagram.Core2.Tests;
public sealed class CrossProjectDescendantRoutingTests
{
    [Fact]
    public void ShouldRouteAnExternalCallAroundTheSourcesInternalChild()
    {
        // Given: a source calls an internal child and an external dependency.
        var project = RenderConfigurationTests.Project("SourceProject", "Source", "Child", "External");
        project.Types![2].IsInternal = false;
        project.Types[2].AssemblyName = "ExternalAssembly";
        project.Dependencies = [RenderConfigurationTests.Link("Source", "Child"), RenderConfigurationTests.Link("Source", "External")];

        // When: use the real preparation, layout and routing pipeline.
        var model = TestServices.Get<LayoutModelBuilder>().BuildRenderModel(new RenderModel([project]));
        var owner = model.Projects.Single(box => box.Name == "SourceProject");
        var child = owner.Nodes.Single(node => node.TypeName == "Child");
        var route = Assert.Single(model.CrossProjectConnections);

        // Then: the external line avoids the internal child by reserving the same vertical passage used in both views.
        var source = owner.Nodes.Single(node => node.TypeName == "Source");
        Assert.Contains(source.Id, model.PassageOffsetParents);
        for (int index = 1; index < route.Points.Length; index++)
        {
            var a = route.Points[index - 1]; var b = route.Points[index];
            double left = owner.X + child.X, right = left + child.Width;
            double top = owner.Y + child.Y, bottom = top + child.Height;
            bool crosses = a.X == b.X
                ? a.X > left && a.X < right && System.Math.Max(a.Y, b.Y) > top && System.Math.Min(a.Y, b.Y) < bottom
                : a.Y > top && a.Y < bottom && System.Math.Max(a.X, b.X) > left && System.Math.Min(a.X, b.X) < right;
            Assert.False(crosses, "The external connection crosses the source's internal child.");
        }
    }
}
