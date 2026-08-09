using System;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core.Models.ArchitectureV6;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.ArchitectureV6;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core.Tests;

public sealed class ArchitectureV6PostRoutingTests
{
    [Fact]
    public void Completed_scene_has_orthogonal_segments_with_route_cell_provenance()
    {
        var request = new ArchitectureV6SemanticFixtureBuilder()
            .Project("p", "Project")
            .Node("source", "SourceService", "p")
            .Node("target", "TargetService", "p")
            .Link("link", "source", "target")
            .BuildRequest();

        var plan = new ArchitectureDiagramV6Planner().Plan(request);
        Assert.NotNull(plan.PhysicalScene);
        var scene = plan.PhysicalScene!;
        var route = Assert.Single(scene.Geometry.Routes);

        Assert.NotEmpty(route.Segments);
        Assert.All(route.Segments, segment =>
        {
            Assert.True(segment.Start.X == segment.End.X || segment.Start.Y == segment.End.Y);
            Assert.NotEmpty(segment.AllocatedCells!);
        });
        Assert.Equal(0, scene.Metrics.DiagonalSegmentCount);
    }

    [Fact]
    public void Terminals_are_on_node_edges_and_ordered_for_multiple_demands()
    {
        var fixture = new ArchitectureV6SemanticFixtureBuilder()
            .Project("p", "Project")
            .Node("source", "SourceService", "p")
            .Node("left", "LeftService", "p")
            .Node("right", "RightService", "p")
            .Link("left-link", "source", "left")
            .Link("right-link", "source", "right");

        var plan = new ArchitectureDiagramV6Planner().Plan(fixture.BuildRequest());
        Assert.NotNull(plan.PhysicalScene);
        var scene = plan.PhysicalScene!;
        var source = scene.Geometry.Nodes.Single(node => node.PhysicalNodeId == "physical:source");
        var terminals = scene.Terminals.Where(terminal => terminal.PhysicalNodeId == source.PhysicalNodeId)
            .OrderBy(terminal => terminal.Ordinal).ToArray();

        Assert.Equal(2, terminals.Length);
        Assert.All(terminals, terminal => Assert.Equal(GridSide.Bottom, terminal.Side));
        Assert.True(terminals[0].Point.X < terminals[1].Point.X);
        Assert.All(terminals, terminal => Assert.Equal(source.AbsoluteBounds.Y + source.AbsoluteBounds.Height, terminal.Point.Y));
    }

    [Fact]
    public void Absolute_transforms_preserve_local_geometry_dimensions_for_two_projects()
    {
        var fixture = new ArchitectureV6SemanticFixtureBuilder()
            .Project("a", "A")
            .Project("b", "B")
            .Node("source", "SourceService", "a")
            .Node("target", "TargetService", "b")
            .Link("cross", "source", "target", "cross-project");

        var plan = new ArchitectureDiagramV6Planner().Plan(fixture.BuildRequest() with
        {
            SelectedScope = new ArchitectureSelectionScope("SelectedProjects", new[] { "a", "b" }, Array.Empty<string>())
        });
        Assert.NotNull(plan.PhysicalScene);
        var scene = plan.PhysicalScene!;

        Assert.Equal(plan.RelativeGeometry!.Nodes.Count, scene.Geometry.Nodes.Count);
        foreach (var relative in plan.RelativeGeometry.Nodes)
        {
            var absolute = scene.Geometry.Nodes.Single(node => node.PhysicalNodeId == relative.PhysicalNodeId);
            Assert.Equal(relative.Bounds.Width, absolute.AbsoluteBounds.Width);
            Assert.Equal(relative.Bounds.Height, absolute.AbsoluteBounds.Height);
        }
        var projectTransforms = scene.Transforms.Where(transform => transform.GridId.Value.StartsWith("project:", StringComparison.Ordinal)).ToArray();
        Assert.Equal(2, projectTransforms.Length);
        Assert.NotEqual(projectTransforms[0].Origin, projectTransforms[1].Origin);
    }

    [Fact]
    public void Post_routing_completes_sizing_but_leaves_rendering_deferred()
    {
        var request = new ArchitectureV6SemanticFixtureBuilder()
            .Project("p", "Project")
            .Node("source", "SourceService", "p")
            .Node("target", "TargetService", "p")
            .Link("link", "source", "target")
            .BuildRequest();

        var plan = new ArchitectureDiagramV6Planner().Plan(request);

        Assert.True(plan.StageStatus.SizingCompleted);
        Assert.True(plan.StageStatus.AbsoluteGeometryCompleted);
        Assert.False(plan.StageStatus.SizingDeferred);
        Assert.Contains(plan.Diagnostics.Findings, finding => finding.Code == "V6StageDeferred.Rendering");
        Assert.DoesNotContain(plan.Diagnostics.Findings, finding => finding.Code == "V6StageDeferred.PhysicalSizing");
    }
}
