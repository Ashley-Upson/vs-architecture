using StandardIo.ArchitectureDiagram.Core.Models;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core.Tests;

public sealed class LayerSuffixConstraintMaterializerTests
{
    [Fact]
    public void Deficient_band_moves_only_lower_suffix_by_exact_persistent_delta_and_invalidates_routes()
    {
        var basis = Placement();
        var scope = new MovementScopeIdentity(MovementScopeKind.LayerAndLowerSuffix, "depth:1");
        var store = new GenerationConstraintStore();
        Assert.True(store.Merge(new GenerationConstraint(
            new GenerationConstraintKey(scope, GenerationConstraintKind.MinimumY), 230, "first")));

        var first = LayerSuffixConstraintMaterializer.Materialize(basis, store.Snapshot(), Settings(), Routes(basis));
        Assert.Equal(0, first.Placement.Nodes["upper"].Rect.Y);
        Assert.Equal(230, first.Placement.Nodes["lower"].Rect.Y);
        Assert.Equal(330, first.Placement.Nodes["deeper"].Rect.Y);
        Assert.Equal(30, first.MaximumDelta);
        Assert.Equal(new[] { 1, 2 }, first.LayersMoved);
        Assert.Equal(new[] { "crossed", "incident" }, first.InvalidatedRouteIds);

        Assert.True(store.Merge(new GenerationConstraint(
            new GenerationConstraintKey(new MovementScopeIdentity(MovementScopeKind.LayerAndLowerSuffix, "depth:2"),
                GenerationConstraintKind.MinimumY), 350, "second")));
        var second = LayerSuffixConstraintMaterializer.Materialize(basis, store.Snapshot(), Settings(), Routes(basis));
        Assert.Equal(230, second.Placement.Nodes["lower"].Rect.Y);
        Assert.Equal(350, second.Placement.Nodes["deeper"].Rect.Y);

        var repeated = LayerSuffixConstraintMaterializer.Materialize(basis, store.Snapshot(), Settings(), Routes(basis));
        Assert.Equal(second.Placement.Nodes.Select(item => item.Value.Rect),
            repeated.Placement.Nodes.Select(item => item.Value.Rect));
    }

    [Fact]
    public void Minimum_y_proposal_uses_only_missing_extent()
    {
        var region = new RailAllocationRegionIdentity(
            RailOrientation.Horizontal, new AxisInterval(100, 140), "band",
            new MovementScopeIdentity(MovementScopeKind.LayerAndLowerSuffix, "depth:1"), new LayoutRevision(1));

        var proposal = LayerSuffixConstraintMaterializer.ProposeMinimumY(region, 64);

        Assert.Equal(164, proposal.Minimum);
        Assert.Equal(GenerationConstraintKind.MinimumY, proposal.Key.Kind);
    }

    private static DiagramSettings Settings()
    {
        var settings = DiagramSettings.CreateDefault();
        settings.ShowProjectContainers = false;
        return settings;
    }

    private static PlacedGraph Placement()
    {
        var types = new[]
        {
            Type("upper"), Type("lower"), Type("deeper"), Type("other")
        };
        var edges = new[]
        {
            new DependencyEdge("incident", "lower", "deeper", "Dependency"),
            new DependencyEdge("crossed", "upper", "deeper", "Dependency"),
            new DependencyEdge("unrelated", "upper", "other", "Dependency")
        };
        var graph = RenderGraph.From(new DiagramModel(
            new[] { new ProjectContainer("project", "Project", types) },
            Array.Empty<ExternalDependencyNode>(), edges));
        var nodes = graph.Nodes.ToDictionary(item => item.Id, StringComparer.Ordinal);
        var layouts = new Dictionary<string, NodeLayout>
        {
            ["upper"] = Layout(nodes["upper"], 0, 0),
            ["lower"] = Layout(nodes["lower"], 200, 1),
            ["deeper"] = Layout(nodes["deeper"], 300, 2),
            ["other"] = Layout(nodes["other"], 0, 0)
        };
        return new PlacedGraph(graph, layouts, new Dictionary<string, ProjectLayout>(), new LayoutRevision(1));
    }

    private static IReadOnlyDictionary<string, LinkLayout> Routes(PlacedGraph placement) =>
        placement.Graph.Links.ToDictionary(link => link.Id, link => new LinkLayout(
            link,
            new Point(10, placement.Nodes[link.SourceId].Rect.Bottom),
            new Point(10, placement.Nodes[link.TargetId].Rect.Y),
            new[] { new Point(10, 100) }, .5, .5), StringComparer.Ordinal);

    private static TypeNode Type(string id) => new(id, "project", id, id, "Class");

    private static NodeLayout Layout(RenderNode node, int y, int depth) =>
        new(node, new Rect(node.Id == "other" ? 200 : 0, y, 40, 40), depth, false);
}
