using StandardIo.ArchitectureDiagram.Core.Models;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core.Tests;

public sealed class GenerationPhaseResultTests
{
    [Fact]
    public void PlacedGraph_snapshots_collections_and_increments_layout_revision()
    {
        var nodes = new Dictionary<string, NodeLayout> { ["node"] = Node("node") };
        var placed = Placement(nodes, new LayoutRevision(3));

        nodes.Clear();
        var revised = placed.Revise(new Dictionary<string, NodeLayout>(), new Dictionary<string, ProjectLayout>());

        Assert.Single(placed.Nodes);
        Assert.Equal(new LayoutRevision(4), revised.Revision);
    }

    [Fact]
    public void Generated_routes_reject_nodes_from_another_layout_revision()
    {
        var first = Placement(new Dictionary<string, NodeLayout>(), new LayoutRevision(0));
        var second = first.Revise(new Dictionary<string, NodeLayout>(), new Dictionary<string, ProjectLayout>());
        var generated = new GeneratedLogicalRoutes(first, new Dictionary<string, LinkLayout>(), new RouteRevision(0));

        var exception = Assert.Throws<InvalidOperationException>(() => generated.EnsureCompatible(second));

        Assert.Contains("layout revision 0, not 1", exception.Message);
    }

    [Fact]
    public void Validation_rejects_a_newer_route_revision()
    {
        var placed = Placement(new Dictionary<string, NodeLayout>(), new LayoutRevision(0));
        var generated = new GeneratedLogicalRoutes(placed, new Dictionary<string, LinkLayout>(), new RouteRevision(0));
        var normalized = new NormalizedLogicalRoutes(generated, generated.Links, generated.Revision.Next());
        var validated = new ValidatedLogicalRoutes(normalized, new TraceabilityValidationResult(Array.Empty<TraceabilityViolation>()));
        var newer = new NormalizedLogicalRoutes(generated, generated.Links, normalized.Revision.Next());

        Assert.Throws<InvalidOperationException>(() => validated.EnsureCompatible(newer));
    }

    [Fact]
    public void Repair_decision_rejects_validation_from_a_previous_layout_revision()
    {
        var first = Placement(new Dictionary<string, NodeLayout>(), new LayoutRevision(0));
        var moved = first.Revise(new Dictionary<string, NodeLayout>(), new Dictionary<string, ProjectLayout>());
        var generated = new GeneratedLogicalRoutes(first, new Dictionary<string, LinkLayout>(), new RouteRevision(0));
        var normalized = new NormalizedLogicalRoutes(generated, generated.Links, generated.Revision.Next());
        var stale = new ValidatedLogicalRoutes(normalized, new TraceabilityValidationResult(Array.Empty<TraceabilityViolation>()));

        Assert.Throws<InvalidOperationException>(() =>
            RenderLayout.LegacyRoutingPipeline.RequiresDuplicateRepair(moved, stale, DiagramSettings.CreateDefault()));
    }

    private static PlacedGraph Placement(
        IReadOnlyDictionary<string, NodeLayout> nodes,
        LayoutRevision revision) =>
        new(
            RenderGraph.From(
                new DiagramModel(Array.Empty<ProjectContainer>(), Array.Empty<ExternalDependencyNode>(), Array.Empty<DependencyEdge>()),
                DiagramSettings.CreateDefault()),
            nodes,
            new Dictionary<string, ProjectLayout>(),
            revision);

    private static NodeLayout Node(string id) =>
        new(
            new RenderNode(id, null, id, id, "Class", false, string.Empty, 0,
                Array.Empty<string>(), Array.Empty<TypeProperty>(), 0),
            new Rect(0, 0, 10, 10),
            0,
            false);
}
