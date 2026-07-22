using StandardIo.ArchitectureDiagram.Core.Models;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core.Tests;

public sealed class ProjectLayerExpansionReconcilerTests
{
    private static readonly ProjectLayerExpansionIdentity First = new("project", 1);
    private static readonly ProjectLayerExpansionIdentity Second = new("project", 2);

    [Fact]
    public void Final_gap_is_the_larger_of_immutable_gap_and_latest_required_extent()
    {
        var basis = Map((First, 80), (Second, 80));
        var required = Map((First, 440), (Second, 32));

        var desired = ProjectLayerExpansionReconciler.DesiredExpansions(basis, required);
        var gaps = ProjectLayerExpansionReconciler.ActualGaps(basis, desired);

        Assert.Equal(360, desired[First]);
        Assert.False(desired.ContainsKey(Second));
        Assert.Equal(440, gaps[First]);
        Assert.Equal(80, gaps[Second]);
        Assert.True(ProjectLayerExpansionReconciler.Fits(gaps, required));
    }

    [Fact]
    public void Latest_required_extent_replaces_a_larger_earlier_expansion()
    {
        var basis = Map((First, 80));
        var earlier = ProjectLayerExpansionReconciler.DesiredExpansions(basis, Map((First, 1040)));
        var latest = ProjectLayerExpansionReconciler.DesiredExpansions(basis, Map((First, 440)));

        Assert.Equal(960, earlier[First]);
        Assert.Equal(360, latest[First]);
        Assert.NotEqual(1320, latest[First]);
    }

    [Fact]
    public void Earlier_expansion_disappears_when_latest_assignment_fits_the_base()
    {
        var basis = Map((First, 80));
        var latest = ProjectLayerExpansionReconciler.DesiredExpansions(basis, Map((First, 68)));

        Assert.Empty(latest);
        Assert.Equal(80, ProjectLayerExpansionReconciler.ActualGaps(basis, latest)[First]);
    }

    [Fact]
    public void Independent_bands_grow_and_shrink_independently()
    {
        var basis = Map((First, 80), (Second, 100));
        var desired = ProjectLayerExpansionReconciler.DesiredExpansions(
            basis, Map((First, 200), (Second, 60)));

        Assert.Equal(120, Assert.Single(desired).Value);
        Assert.Equal(First, Assert.Single(desired).Key);
    }

    [Fact]
    public void Safe_cycle_map_is_no_smaller_than_every_observed_state_and_selected_requirement()
    {
        var basis = Map((First, 80), (Second, 80));
        var firstState = Map((First, 120));
        var secondState = Map((Second, 240));
        var safe = ProjectLayerExpansionReconciler.SafeCycleMap(
            new[] { firstState, secondState }, Map((First, 260), (Second, 200)), basis);
        var gaps = ProjectLayerExpansionReconciler.ActualGaps(basis, safe);

        Assert.Equal(180, safe[First]);
        Assert.Equal(240, safe[Second]);
        Assert.True(ProjectLayerExpansionReconciler.Fits(gaps, Map((First, 260), (Second, 200))));
    }

    [Fact]
    public void Synthetic_two_state_cycle_is_detected_deterministically()
    {
        var first = Map((First, 120));
        var second = Map((Second, 240));
        var seen = new HashSet<string>(StringComparer.Ordinal);

        Assert.True(ProjectLayerExpansionReconciler.TryAddState(seen, first));
        Assert.True(ProjectLayerExpansionReconciler.TryAddState(seen, second));
        Assert.False(ProjectLayerExpansionReconciler.TryAddState(seen, first));
    }

    [Fact]
    public void Expansion_map_hash_is_stable_under_reversed_enumeration()
    {
        var forward = Map((First, 360), (Second, 84));
        var reversed = Map((Second, 84), (First, 360));

        Assert.Equal(ProjectLayerExpansionReconciler.Hash(forward),
            ProjectLayerExpansionReconciler.Hash(reversed));
        Assert.True(ProjectLayerExpansionReconciler.Same(forward, reversed));
    }

    [Fact]
    public void Required_extent_is_reported_even_when_the_band_already_fits()
    {
        var graph = RenderGraph.From(new DiagramModel(
            [new ProjectContainer("project", "Project",
                [Node("source"), Node("target")])],
            Array.Empty<ExternalDependencyNode>(),
            [new DependencyEdge("route", "source", "target", "Dependency")]));
        var nodes = new Dictionary<string, NodeLayout>(StringComparer.Ordinal)
        {
            ["source"] = Layout(graph, "source", new Rect(0, 0, 120, 60), 0),
            ["target"] = Layout(graph, "target", new Rect(240, 180, 120, 60), 1)
        };
        var link = graph.Links.Single();
        var routes = new Dictionary<string, LinkLayout>(StringComparer.Ordinal)
        {
            [link.Id] = new(link, new Point(60, 60), new Point(300, 180), Array.Empty<Point>(), 0.5, 0.5)
        };
        var revision = new LayoutRevision(1);
        var compiled = ProjectInterLayerSlotCompiler.Compile(
            CanonicalTopologyFamilySelector.Select(graph, nodes, revision).Plans,
            nodes, routes, new Dictionary<string, ProjectLabelGeometry>(), revision, 12, 10);
        var extent = compiled.RequiredExtentByBand.Single(item => item.Key.LowerDepth == 1);

        Assert.Equal(new ProjectLayerExpansionIdentity("project", 1), extent.Key);
        Assert.Equal(32, extent.Value);
        Assert.Equal(0, compiled.RequiredExtentByBand[new ProjectLayerExpansionIdentity("project", 2)]);
        Assert.Empty(compiled.RequiredLayerExpansion);
    }

    private static Dictionary<ProjectLayerExpansionIdentity, int> Map(
        params (ProjectLayerExpansionIdentity Key, int Value)[] values) =>
        values.ToDictionary(item => item.Key, item => item.Value);

    private static TypeNode Node(string id) => new(
        id, "project", id, $"Fixture.{id}", "Class", string.Empty,
        Array.Empty<string>(), Array.Empty<TypeProperty>(), 0);

    private static NodeLayout Layout(RenderGraph graph, string id, Rect rect, int depth) =>
        new(graph.Nodes.Single(node => node.Id == id), rect, depth, false);
}
