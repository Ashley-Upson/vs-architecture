using System;
using System.Collections.Generic;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core.Models;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core.Tests;

public sealed class InterLayerSpacingConstraintProducerTests
{
    [Fact]
    public void Complete_vertical_delta_moves_lower_layers_once_and_keeps_upper_fixed()
    {
        var fixture = Fixture(
            new[] { Node("a", 0, 0), Node("b", 0, 1), Node("x", 1, 0), Node("y", 1, 1), Node("z", 2, 0) },
            Link("one", "a", "x", 0, 10, 80), Link("two", "b", "y", 1, 20, 70));
        fixture.Settings.Layout.ParallelLaneSpacing = 40;
        fixture.Settings.Layout.LinkPadding = 20;
        var report = InterLayerDemandDiscovery.Observe(fixture.Placement, fixture.Routes, fixture.Settings);
        var originalUpper = fixture.Placement.Nodes["a"].Rect;
        var originalLower = fixture.Placement.Nodes["x"].Rect;
        var missing = report.InterLayers[0].MissingExtent;

        var result = InterLayerSpacingConstraintProducer.Apply(fixture.Placement, fixture.Routes, report, fixture.Settings);

        Assert.Equal(originalUpper, result.Placement.Nodes["a"].Rect);
        Assert.Equal(originalLower.Y + missing, result.Placement.Nodes["x"].Rect.Y);
        Assert.Equal(fixture.Placement.Nodes["z"].Rect.Y + missing, result.Placement.Nodes["z"].Rect.Y);
        Assert.Equal(1, result.Iterations);
    }

    [Fact]
    public void Moved_endpoints_and_band_routes_are_regenerated_with_new_revisions()
    {
        var fixture = Fixture(new[] { Node("a", 0, 0), Node("x", 1, 0) },
            Link("edge", "a", "x", 0, 10, 80));
        fixture.Settings.Layout.ParallelLaneSpacing = 100;
        var report = InterLayerDemandDiscovery.Observe(fixture.Placement, fixture.Routes, fixture.Settings);

        var result = InterLayerSpacingConstraintProducer.Apply(fixture.Placement, fixture.Routes, report, fixture.Settings);

        Assert.Equal(fixture.Routes.Revision.Next(), result.Routes.Revision);
        Assert.Contains("edge", result.Plan.InvalidatedRoutes);
        Assert.Equal(result.Placement.Nodes["a"].Rect.Bottom, result.Routes.Links["edge"].SourcePoint.Y);
        Assert.Equal(result.Placement.Nodes["x"].Rect.Y, result.Routes.Links["edge"].TargetPoint.Y);
        Assert.All(Segments(result.Routes.Links["edge"]), segment => Assert.True(segment.IsOrthogonal));
    }

    [Fact]
    public void Unsupported_skipped_layer_graph_uses_explicit_fallback_boundary()
    {
        var fixture = Fixture(new[] { Node("a", 0, 0), Node("middle", 1, 0), Node("x", 2, 0) },
            Link("edge", "a", "x", 0, 10, 80));
        var report = InterLayerDemandDiscovery.Observe(fixture.Placement, fixture.Routes, fixture.Settings);

        Assert.False(InterLayerSpacingConstraintProducer.Supports(fixture.Placement, fixture.Routes, report));
        Assert.Throws<InvalidOperationException>(() =>
            InterLayerSpacingConstraintProducer.Plan(fixture.Placement, fixture.Routes, report, fixture.Settings));
    }

    [Fact]
    public void Reversed_enumeration_produces_identical_constraints_and_routes()
    {
        var nodes = new[] { Node("a", 0, 0), Node("b", 0, 1), Node("x", 1, 0), Node("y", 1, 1) };
        var links = new[] { Link("one", "a", "x", 0, 10, 80), Link("two", "b", "y", 1, 20, 70) };
        var forward = Fixture(nodes, links);
        var reverse = Fixture(nodes.AsEnumerable().Reverse(), links.AsEnumerable().Reverse().ToArray());
        var first = Apply(forward);
        var second = Apply(reverse);

        Assert.Equal(first.Plan.Constraints, second.Plan.Constraints);
        Assert.Equal(first.Routes.Links.OrderBy(item => item.Key).SelectMany(item => Complete(item.Value)),
            second.Routes.Links.OrderBy(item => item.Key).SelectMany(item => Complete(item.Value)));
    }

    [Fact]
    public void Eligible_under_sized_adjacent_graph_uses_grouped_pipeline_as_one_authority()
    {
        var model = new DiagramModel(
            new[]
            {
                new ProjectContainer("project", "Project", new[]
                {
                    new TypeNode("source", "project", "Source", "App.Source", "Class"),
                    new TypeNode("left", "project", "Left", "App.Left", "Class"),
                    new TypeNode("right", "project", "Right", "App.Right", "Class")
                })
            },
            Array.Empty<ExternalDependencyNode>(),
            new[]
            {
                new DependencyEdge("edge-left", "source", "left", "internal"),
                new DependencyEdge("edge-right", "source", "right", "internal")
            });
        var settings = DiagramSettings.CreateDefault();
        settings.Layout.ParallelLaneSpacing = 300;

        var layout = RenderLayout.Build(RenderGraph.From(model, settings), settings);

        Assert.Equal("GroupedVerticalBandConverged", layout.RepairRunReason);
        Assert.Empty(layout.Corridors.Corridors);
        Assert.NotNull(layout.GroupedSpacingPlan);
        Assert.True(layout.GroupedSpacingIterations > 0);
        Assert.Equal(2, layout.GroupedSpacingPlan!.InvalidatedRoutes.Count);
        Assert.All(layout.Links.Values.SelectMany(Segments), segment => Assert.True(segment.IsOrthogonal));
    }

    private static InterLayerSpacingConstraintResult Apply(FixtureData fixture)
    {
        var report = InterLayerDemandDiscovery.Observe(fixture.Placement, fixture.Routes, fixture.Settings);
        return InterLayerSpacingConstraintProducer.Apply(fixture.Placement, fixture.Routes, report, fixture.Settings);
    }

    private static FixtureData Fixture(IEnumerable<NodeLayout> nodes, params LinkLayout[] links)
    {
        var nodeMap = nodes.ToDictionary(node => node.Node.Id, StringComparer.Ordinal);
        var graph = RenderGraph.From(new DiagramModel(Array.Empty<ProjectContainer>(), Array.Empty<ExternalDependencyNode>(), Array.Empty<DependencyEdge>()));
        var placement = new PlacedGraph(graph, nodeMap, new Dictionary<string, ProjectLayout>(), new LayoutRevision(1));
        var settings = DiagramSettings.CreateDefault();
        settings.Layout.ParallelLaneSpacing = 100;
        return new FixtureData(placement, new GeneratedLogicalRoutes(placement,
            links.ToDictionary(link => link.Link.Id, StringComparer.Ordinal), new RouteRevision(2)),
            settings);
    }

    private static NodeLayout Node(string id, int depth, int order) => new(
        new RenderNode(id, null, id, id, "Class", false, string.Empty, order,
            Array.Empty<string>(), Array.Empty<TypeProperty>(), 0),
        new Rect(order * 120, depth * 100, 20, 20), depth, false);

    private static LinkLayout Link(string id, string source, string target, int order, int sourceX, int targetX) =>
        new(new RenderLink(id, source, target, "internal", order),
            new Point(sourceX, 20), new Point(targetX, 100),
            new[] { new Point(sourceX, 50), new Point(targetX, 50) }, .5, .5);

    private static IEnumerable<Segment> Segments(LinkLayout link) =>
        Complete(link).Zip(Complete(link).Skip(1), (start, end) => new Segment(start, end));

    private static Point[] Complete(LinkLayout link) =>
        new[] { link.SourcePoint }.Concat(link.Points).Concat(new[] { link.TargetPoint }).ToArray();

    private sealed record FixtureData(PlacedGraph Placement, GeneratedLogicalRoutes Routes, DiagramSettings Settings);
}
