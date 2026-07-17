using System;
using System.Collections.Generic;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core.Tests;

public sealed class CoordinateOwnershipCompilerTests
{
    [Fact]
    public void Compile_keeps_same_project_dependency_in_one_project_owned_segment()
    {
        var context = Context(
            new[] { Project("a", new Rect(0, 0, 200, 200)) },
            new[] { Node("source", "a"), Node("target", "a") },
            Link("edge", "source", "target", new Point(20, 20), new[] { new Point(100, 20) }, new Point(180, 20)));

        var result = Compile(context);

        var segment = Assert.Single(result.Segments);
        Assert.Equal("a", segment.ParentId);
        Assert.Equal(PhysicalEdgeSegmentRole.Complete, segment.Role);
        Assert.Empty(result.Anchors);
        Assert.Equal(new[] { new Point(100, 20) }, segment.RelativeWaypoints);
    }

    [Fact]
    public void Compile_splits_project_to_project_dependency_into_owned_root_owned_segments()
    {
        var context = TwoProjectContext(new[] { new Point(200, 50) });

        var result = Compile(context);

        Assert.Equal(new[] { "a", "1", "b" }, result.Segments.Select(segment => segment.ParentId));
        Assert.Equal(new[] { "edge__segment__000", "edge__segment__001", "edge__segment__002" }, result.Segments.Select(segment => segment.Id));
        Assert.Equal(2, result.Anchors.Count);
        Assert.Equal(new[] { new Point(100, 50), new Point(300, 50) }, result.Anchors.Select(anchor => anchor.AbsolutePoint));
        Assert.Equal(CompletePoints(context.Link), Normalize(CoordinateOwnershipCompiler.ReconstructAbsolutePoints(result, "edge")));
    }

    [Fact]
    public void Compile_splits_project_to_external_dependency_at_project_boundary()
    {
        var context = Context(
            new[] { Project("a", new Rect(0, 0, 100, 100)) },
            new[] { Node("source", "a"), Node("external", null, external: true) },
            Link("edge", "source", "external", new Point(20, 40), new[] { new Point(180, 40) }, new Point(240, 40)));

        var result = Compile(context);

        Assert.Equal(new[] { "a", "1" }, result.Segments.Select(segment => segment.ParentId));
        Assert.Equal(new Point(100, 40), Assert.Single(result.Anchors).AbsolutePoint);
    }

    [Fact]
    public void Compile_supports_route_leaving_and_reentering_same_project()
    {
        var context = Context(
            new[] { Project("a", new Rect(0, 0, 100, 100)) },
            new[] { Node("source", "a"), Node("target", "a") },
            Link("edge", "source", "target", new Point(20, 20), new[]
            {
                new Point(150, 20),
                new Point(150, 80),
                new Point(20, 80)
            }, new Point(20, 60)));

        var result = Compile(context);

        Assert.Equal(new[] { "a", "1", "a" }, result.Segments.Select(segment => segment.ParentId));
        Assert.Equal(2, result.Anchors.Count);
        Assert.Equal(CompletePoints(context.Link), Normalize(CoordinateOwnershipCompiler.ReconstructAbsolutePoints(result, "edge")));
    }

    [Fact]
    public void Compile_does_not_assign_unrelated_project_ownership()
    {
        var context = Context(
            new[]
            {
                Project("a", new Rect(0, 0, 100, 100)),
                Project("unrelated", new Rect(150, 0, 100, 100))
            },
            new[]
            {
                Node("source", "a"),
                Node("external", null, external: true),
                Node("unrelated-node", "unrelated")
            },
            Link("edge", "source", "external", new Point(20, 50), new[] { new Point(300, 50) }, new Point(350, 50)));

        var result = Compile(context);

        Assert.Equal(new[] { "a", "1" }, result.Segments.Select(segment => segment.ParentId));
        Assert.DoesNotContain(result.Segments, segment => segment.OwnerProjectId == "unrelated");
    }

    [Fact]
    public void Compile_is_deterministic_when_edge_enumeration_is_reversed()
    {
        var context = TwoProjectContext(new[] { new Point(200, 50) });
        var second = Link("edge_second", "source", "target", new Point(20, 60), new[] { new Point(200, 60) }, new Point(380, 60), order: 1);
        var linksForward = new Dictionary<string, LinkLayout>
        {
            [context.Link.Link.Id] = context.Link,
            [second.Link.Id] = second
        };
        var linksReverse = linksForward.Reverse().ToDictionary(item => item.Key, item => item.Value);

        var forward = CoordinateOwnershipCompiler.Compile(context.Nodes, context.Projects, linksForward, true);
        var reverse = CoordinateOwnershipCompiler.Compile(context.Nodes, context.Projects, linksReverse, true);

        Assert.Equal(forward.Anchors.Select(anchor => anchor.Id), reverse.Anchors.Select(anchor => anchor.Id));
        Assert.Equal(forward.Segments.Select(segment => segment.Id), reverse.Segments.Select(segment => segment.Id));
    }

    [Fact]
    public void Compile_assigns_markers_and_arrow_only_to_outer_segments()
    {
        var result = Compile(TwoProjectContext(new[] { new Point(200, 50) }));

        Assert.True(result.Segments[0].HasSourceMarker);
        Assert.False(result.Segments[0].HasTargetArrow);
        Assert.False(result.Segments[1].HasSourceMarker);
        Assert.False(result.Segments[1].HasTargetArrow);
        Assert.False(result.Segments[2].HasSourceMarker);
        Assert.True(result.Segments[2].HasTargetArrow);
        Assert.True(result.Segments[1].OwnsLabel);
    }

    [Fact]
    public void Compile_suppresses_zero_length_segments_and_duplicate_points()
    {
        var context = Context(
            new[] { Project("a", new Rect(0, 0, 100, 100)) },
            new[] { Node("source", "a"), Node("external", null, external: true) },
            Link("edge", "source", "external", new Point(20, 50), new[]
            {
                new Point(100, 50),
                new Point(100, 50),
                new Point(200, 50)
            }, new Point(240, 50)));

        var result = Compile(context);

        Assert.Equal(2, result.Segments.Count);
        Assert.All(result.Segments, segment => Assert.NotEqual(segment.AbsoluteStart, segment.AbsoluteEnd));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Compile_handles_boundary_at_existing_waypoint_or_inside_segment(bool existingWaypoint)
    {
        var waypoints = existingWaypoint
            ? new[] { new Point(100, 50), new Point(200, 50) }
            : new[] { new Point(200, 50) };
        var context = Context(
            new[] { Project("a", new Rect(0, 0, 100, 100)) },
            new[] { Node("source", "a"), Node("external", null, external: true) },
            Link("edge", "source", "external", new Point(20, 50), waypoints, new Point(240, 50)));

        var result = Compile(context);

        Assert.Equal(new Point(100, 50), Assert.Single(result.Anchors).AbsolutePoint);
        Assert.Equal(CompletePoints(context.Link), Normalize(CoordinateOwnershipCompiler.ReconstructAbsolutePoints(result, "edge")));
    }

    [Fact]
    public void Compile_translates_negative_project_origins()
    {
        var context = Context(
            new[] { Project("a", new Rect(-200, -100, 200, 200)) },
            new[] { Node("source", "a"), Node("external", null, external: true) },
            Link("edge", "source", "external", new Point(-150, -50), new[] { new Point(100, -50) }, new Point(200, -50)));

        var result = Compile(context);

        var anchor = Assert.Single(result.Anchors);
        Assert.Equal(new Point(0, -50), anchor.AbsolutePoint);
        Assert.Equal(new Point(200, 50), anchor.RelativePoint);
    }

    [Fact]
    public void Compile_reconstructs_globally_selected_logical_geometry_exactly()
    {
        var selectedPoints = new[]
        {
            new Point(20, 50),
            new Point(100, 50),
            new Point(200, 50),
            new Point(300, 50),
            new Point(380, 50)
        };
        var candidates = new Dictionary<string, IReadOnlyList<CorridorPathCandidate>>(StringComparer.Ordinal)
        {
            ["edge"] = new[]
            {
                new CorridorPathCandidate(
                    "edge",
                    new[] { "source", "root", "target" },
                    new[] { "source-boundary", "target-boundary" },
                    new CorridorPathSignature("source-root-target"),
                    new CorridorPathLocalCost(360, 0, 0),
                    selectedPoints,
                    IsAcceptedPath: true)
            }
        };
        var selection = GlobalCorridorPathSelector.Select(
            candidates,
            new Dictionary<string, int>(StringComparer.Ordinal),
            10,
            4);
        var selected = selection.Selected["edge"].Points;
        var context = Context(
            new[]
            {
                Project("a", new Rect(0, 0, 100, 100)),
                Project("b", new Rect(300, 0, 100, 100))
            },
            new[] { Node("source", "a"), Node("target", "b") },
            Link("edge", "source", "target", selected[0], selected.Skip(1).Take(selected.Count - 2).ToArray(), selected[^1]));

        var ownership = Compile(context);

        Assert.Equal(Normalize(selected), Normalize(CoordinateOwnershipCompiler.ReconstructAbsolutePoints(ownership, "edge")));
    }

    private static CoordinateOwnershipCompilation Compile(TestContext context) =>
        CoordinateOwnershipCompiler.Compile(
            context.Nodes,
            context.Projects,
            new Dictionary<string, LinkLayout> { [context.Link.Link.Id] = context.Link },
            true);

    private static TestContext TwoProjectContext(IReadOnlyList<Point> waypoints) => Context(
        new[]
        {
            Project("a", new Rect(0, 0, 100, 100)),
            Project("b", new Rect(300, 0, 100, 100))
        },
        new[] { Node("source", "a"), Node("target", "b") },
        Link("edge", "source", "target", new Point(20, 50), waypoints, new Point(380, 50)));

    private static TestContext Context(
        IEnumerable<ProjectLayout> projects,
        IEnumerable<NodeLayout> nodes,
        LinkLayout link) => new(
            projects.ToDictionary(project => project.Project.Id),
            nodes.ToDictionary(node => node.Node.Id),
            link);

    private static ProjectLayout Project(string id, Rect rect) =>
        new(new RenderProject(id, id, 0), rect);

    private static NodeLayout Node(string id, string? projectId, bool external = false) =>
        new(new RenderNode(
            id,
            projectId,
            id,
            id,
            "Class",
            external,
            external ? "[External]" : string.Empty,
            0,
            Array.Empty<string>(),
            Array.Empty<StandardIo.ArchitectureDiagram.Core.Models.TypeProperty>(),
            0), new Rect(0, 0, 20, 20), 0, false);

    private static LinkLayout Link(
        string id,
        string sourceId,
        string targetId,
        Point source,
        IReadOnlyList<Point> waypoints,
        Point target,
        int order = 0) =>
        new(new RenderLink(id, sourceId, targetId, "internal", order), source, target, waypoints, 0.5, 0.5);

    private static IReadOnlyList<Point> CompletePoints(LinkLayout link) =>
        Normalize(new[] { link.SourcePoint }.Concat(link.Points).Concat(new[] { link.TargetPoint }).ToArray());

    private static IReadOnlyList<Point> Normalize(IReadOnlyList<Point> points)
    {
        var result = new List<Point>();
        foreach (var point in points)
        {
            if (result.Count == 0 || result[result.Count - 1] != point)
            {
                result.Add(point);
            }

            while (result.Count >= 3 &&
                (result[^3].X == result[^2].X && result[^2].X == result[^1].X ||
                 result[^3].Y == result[^2].Y && result[^2].Y == result[^1].Y))
            {
                result.RemoveAt(result.Count - 2);
            }
        }

        return result;
    }

    private sealed record TestContext(
        IReadOnlyDictionary<string, ProjectLayout> Projects,
        IReadOnlyDictionary<string, NodeLayout> Nodes,
        LinkLayout Link);
}
