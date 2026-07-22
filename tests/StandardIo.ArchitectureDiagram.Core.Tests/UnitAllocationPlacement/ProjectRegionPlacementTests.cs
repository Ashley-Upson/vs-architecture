using StandardIo.ArchitectureDiagram.Core.Models;
using StandardIo.ArchitectureDiagram.Core.Models.Architectures;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core.Tests;

public sealed class ProjectRegionPlacementTests
{
    [Fact]
    public void Independent_projects_are_placed_in_non_overlapping_regions()
    {
        var placed = Place(Graph(Project("a", "a1"), Project("b", "b1")));

        AssertNoProjectOverlap(placed);
    }

    [Fact]
    public void Dependent_project_is_below_its_source_project()
    {
        var placed = Place(Graph([Project("a", "a1"), Project("b", "b1")],
            [Link("a_b", "a1", "b1")]));

        Assert.True(placed.Projects["b"].Rect.Y > placed.Projects["a"].Rect.Y);
        AssertNoProjectOverlap(placed);
    }

    [Fact]
    public void Strongly_connected_projects_form_a_compact_non_overlapping_group()
    {
        var placed = Place(Graph([Project("a", "a1"), Project("b", "b1"), Project("c", "c1")],
            [Link("a_b", "a1", "b1"), Link("b_c", "b1", "c1"), Link("c_a", "c1", "a1")]));

        AssertNoProjectOverlap(placed);
        Assert.Equal(3, placed.Projects.Count);
    }

    [Fact]
    public void Project_region_placement_is_stable_when_project_enumeration_is_reversed()
    {
        var graph = Graph([Project("a", "a1"), Project("b", "b1"), Project("c", "c1")],
            [Link("a_c", "a1", "c1"), Link("b_c", "b1", "c1")]);
        var reversed = graph with { Projects = graph.Projects.Reverse().ToArray() };

        Assert.Equal(Signature(Place(graph)), Signature(Place(reversed)));
    }

    [Fact]
    public void Shared_root_external_region_is_outside_all_project_regions()
    {
        var graph = Graph([Project("a", "a1"), Project("b", "b1")],
            [Link("a_e", "a1", "external"), Link("b_e", "b1", "external")],
            [new ArchitectureRenderNode("external", "external", null, "ILogger", "Logging.ILogger",
                "External", true, "[External]", InterfaceResolutionStatus.NotApplicable, null, null, 0,
                ArchitectureRenderNodeOccurrence.Canonical, ArchitectureDuplicationReason.None, null, 2)]);

        var placed = Place(graph);
        var external = placed.Nodes["external"].Rect;

        Assert.All(placed.Projects.Values, project => Assert.True(external.X > project.Rect.Right));
    }

    [Fact]
    public void Empty_and_single_node_projects_receive_distinct_regions()
    {
        var placed = Place(Graph(Project("empty"), Project("single", "node")));

        Assert.Equal(2, placed.Projects.Count);
        AssertNoProjectOverlap(placed);
    }

    private static PlacedGraph Place(ArchitectureRenderGraph graph)
    {
        var settings = DiagramSettings.CreateDefault();
        return ProjectRegionPlacement.Place(RenderGraph.From(graph), settings, new LayoutRevision(0));
    }

    private static ArchitectureRenderGraph Graph(params ArchitectureRenderProject[] projects) =>
        Graph(projects, Array.Empty<ArchitectureRenderLink>());

    private static ArchitectureRenderGraph Graph(
        ArchitectureRenderProject[] projects,
        ArchitectureRenderLink[] links,
        ArchitectureRenderNode[]? extraNodes = null)
    {
        var nodes = projects.SelectMany(project => project.Name.Split(',').Where(id => id.Length > 0)
            .Select(id => new ArchitectureRenderNode(id, id, project.Id, id, "Fixture." + id,
                "Class", false, string.Empty, InterfaceResolutionStatus.NotApplicable, null, null, 0,
                ArchitectureRenderNodeOccurrence.Canonical, ArchitectureDuplicationReason.None, null, 0)))
            .Concat(extraNodes ?? Array.Empty<ArchitectureRenderNode>()).Select((node, order) => node with { Order = order }).ToArray();
        return new ArchitectureRenderGraph(projects.OrderBy(project => project.Id).Select((project, order) => project with { Order = order }).ToArray(),
            nodes, links.Select((link, order) => link with { Order = order }).ToArray(), [],
            nodes.ToDictionary(node => node.SemanticNodeId, node => (IReadOnlyList<string>)new[] { node.Id }, StringComparer.Ordinal));
    }

    private static ArchitectureRenderProject Project(string id, params string[] nodeIds) =>
        new(id, string.Join(",", nodeIds), 0);

    private static ArchitectureRenderLink Link(string id, string source, string target) =>
        new(id, id, source, target, source, target, "internal", 0);

    private static void AssertNoProjectOverlap(PlacedGraph placed)
    {
        var projects = placed.Projects.Values.OrderBy(project => project.Project.Id).ToArray();
        for (var left = 0; left < projects.Length; left++)
        for (var right = left + 1; right < projects.Length; right++)
            Assert.False(Overlaps(projects[left].Rect, projects[right].Rect),
                $"{projects[left].Project.Id} overlaps {projects[right].Project.Id}");
    }

    private static bool Overlaps(Rect left, Rect right) =>
        left.X < right.Right && left.Right > right.X && left.Y < right.Bottom && left.Bottom > right.Y;

    private static string[] Signature(PlacedGraph placed) => placed.Projects.OrderBy(item => item.Key)
        .Select(item => $"{item.Key}:{item.Value.Rect.X},{item.Value.Rect.Y},{item.Value.Rect.Width},{item.Value.Rect.Height}")
        .Concat(placed.Nodes.OrderBy(item => item.Key).Select(item =>
            $"{item.Key}:{item.Value.Rect.X},{item.Value.Rect.Y},{item.Value.Rect.Width},{item.Value.Rect.Height}"))
        .ToArray();
}
