using System;
using System.Collections.Generic;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core.Models;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

public sealed class ProjectOwnershipBoundsCompilerTests
{
    [Fact]
    public void Compile_includes_owned_route_geometry_beyond_owned_nodes()
    {
        var projects = Projects(Project("a", new Rect(0, 0, 300, 200)));
        var nodes = Nodes(Node("source", "a", new Rect(20, 40, 20, 20)), Node("target", "a", new Rect(40, 80, 20, 20)));
        var link = Link("edge", "source", "target", new Point(30, 60), new[] { new Point(220, 60), new Point(220, 80) }, new Point(50, 80));
        var links = Links(link);

        var semantic = RouteSemanticOwnershipCompiler.Compile(nodes, links);
        var result = ProjectOwnershipBoundsCompiler.Compile(projects, nodes, links, semantic, 10, 20);

        Assert.Equal(230, result["a"].Rect.Right);
        Assert.True(result["a"].Rect.Right > nodes.Values.Max(node => node.Rect.Right) + 10);
    }

    [Fact]
    public void Compile_ignores_root_owned_cross_boundary_geometry()
    {
        var projects = Projects(
            Project("a", new Rect(0, 0, 100, 120)),
            Project("b", new Rect(300, 0, 100, 120)));
        var nodes = Nodes(
            Node("source", "a", new Rect(20, 40, 20, 20)),
            Node("target", "b", new Rect(360, 40, 20, 20)));
        var link = Link("edge", "source", "target", new Point(30, 60), new[] { new Point(100, 60), new Point(250, 60), new Point(300, 60) }, new Point(370, 60));
        var links = Links(link);

        var semantic = RouteSemanticOwnershipCompiler.Compile(nodes, links);
        var result = ProjectOwnershipBoundsCompiler.Compile(projects, nodes, links, semantic, 10, 20);

        Assert.Equal(50, result["a"].Rect.Right);
        Assert.Equal(350, result["b"].Rect.X);
        Assert.True(result["a"].Rect.Right < 100);
        Assert.True(result["b"].Rect.X > 300);
    }

    [Fact]
    public void Compile_contains_both_exterior_sides_with_padding_and_is_idempotent()
    {
        var projects = Projects(Project("a", new Rect(0, 0, 100, 120)));
        var nodes = Nodes(
            Node("source", "a", new Rect(20, 40, 20, 20)),
            Node("target", "a", new Rect(60, 80, 20, 20)));
        var links = Links(Link("edge", "source", "target", new Point(30, 60),
            new[] { new Point(-50, 60), new Point(-50, 120), new Point(150, 120), new Point(150, 80) },
            new Point(70, 80)));
        var semantic = RouteSemanticOwnershipCompiler.Compile(nodes, links);

        var first = ProjectOwnershipBoundsCompiler.Compile(projects, nodes, links, semantic, 10, 20);
        var second = ProjectOwnershipBoundsCompiler.Compile(first, nodes, links, semantic, 10, 20);

        Assert.Equal(-60, first["a"].Rect.X);
        Assert.Equal(160, first["a"].Rect.Right);
        Assert.Equal(first["a"].Rect, second["a"].Rect);
        Assert.All(links["edge"].Points, point => Assert.True(first["a"].Rect.Contains(point)));
    }

    private static IReadOnlyDictionary<string, ProjectLayout> Projects(params ProjectLayout[] projects) =>
        projects.ToDictionary(project => project.Project.Id, StringComparer.Ordinal);

    private static IReadOnlyDictionary<string, NodeLayout> Nodes(params NodeLayout[] nodes) =>
        nodes.ToDictionary(node => node.Node.Id, StringComparer.Ordinal);

    private static IReadOnlyDictionary<string, LinkLayout> Links(LinkLayout link) =>
        new Dictionary<string, LinkLayout>(StringComparer.Ordinal) { [link.Link.Id] = link };

    private static ProjectLayout Project(string id, Rect rect) => new(new RenderProject(id, id, 0), rect);

    private static NodeLayout Node(string id, string projectId, Rect rect) =>
        new(new RenderNode(id, projectId, id, id, "Class", false, string.Empty, 0, Array.Empty<string>(), Array.Empty<TypeProperty>(), 0), rect, 0, false);

    private static LinkLayout Link(string id, string sourceId, string targetId, Point source, IReadOnlyList<Point> points, Point target) =>
        new(new RenderLink(id, sourceId, targetId, "internal", 0), source, target, points, 0.5, 0.5);
}
