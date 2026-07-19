using StandardIo.ArchitectureDiagram.Core.Models;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core.Tests;

public sealed class PreAssignmentConstraintDemandProducerTests
{
    [Fact]
    public void Overlapping_destination_aligned_columns_produce_a_separation_demand()
    {
        var placement = Placement();
        var report = Detect(placement, new[]
        {
            Plan("a", "target-a", 120, new AxisInterval(40, 300)),
            Plan("b", "target-b", 120, new AxisInterval(60, 320))
        });

        Assert.Equal(1, report.DestinationColumnConflicts);
        Assert.Contains(report.Demands, item => item.Reason == PositionalConstraintReason.DestinationColumnSeparation);
    }

    [Fact]
    public void Columns_with_disjoint_vertical_ranges_may_reuse_the_same_x()
    {
        var placement = Placement();
        var report = Detect(placement, new[]
        {
            Plan("a", "target-a", 120, new AxisInterval(40, 100)),
            Plan("b", "target-b", 120, new AxisInterval(100, 160))
        });

        Assert.Equal(0, report.DestinationColumnConflicts);
    }

    [Fact]
    public void Column_clear_near_source_but_blocked_at_a_deeper_layer_produces_clearance_demand()
    {
        var placement = Placement();
        var report = Detect(placement, new[]
        {
            Plan("a", "target-a", placement.Nodes["target-a"].Rect.CenterX, new AxisInterval(40, 300))
        });

        Assert.True(report.VerticalColumnObstacles > 0);
        Assert.Contains(report.Demands, item => item.Reason == PositionalConstraintReason.VerticalColumnClearance &&
            (item.LeftStructureId == "blocker" || item.RightStructureId == "blocker"));
    }

    private static PreAssignmentConstraintDemandReport Detect(PlacedGraph placement, IReadOnlyList<GeneralDownwardLinkPlan> plans) =>
        PreAssignmentConstraintDemandProducer.Detect(placement,
            new GeneralDownwardObservationReport(plans, 0), Array.Empty<AdjacentDownwardLinkContext>(), 12, 8);

    private static GeneralDownwardLinkPlan Plan(string id, string target, int x, AxisInterval vertical)
    {
        var observation = new AdjacentDownwardLinkObservation(id, true, null, Array.Empty<LinkSegmentDemand>(),
            Array.Empty<ExistingSegmentMapping>(), Array.Empty<AssignedLinkSegment>(), Array.Empty<LinkTransition>(),
            Array.Empty<Point>(), ObservationalLinkPathParity.UnableToMap, Array.Empty<Point>(), Array.Empty<string>());
        var demand = new VerticalLinkColumnDemand($"{id}:column", id, x, new AxisInterval(x, x), 0, 3,
            vertical, 0, "source", target, "project", new MovementScopeIdentity(MovementScopeKind.LayoutSubtree, target),
            new LayoutRevision(0), new RouteRevision(0));
        return new GeneralDownwardLinkPlan(observation, new[] { demand }, "source", target);
    }

    private static PlacedGraph Placement()
    {
        var ids = new[] { "source", "target-a", "target-b", "blocker" };
        var edges = new[]
        {
            Edge("a", "source", "target-a"), Edge("b", "source", "target-b"),
            Edge("blocker-edge", "source", "blocker")
        };
        var graph = RenderGraph.From(new DiagramModel(
            new[] { new ProjectContainer("project", "Project", ids.Select(Type).ToArray()) },
            Array.Empty<ExternalDependencyNode>(), edges));
        var nodes = graph.Nodes.ToDictionary(item => item.Id, StringComparer.Ordinal);
        var layouts = new Dictionary<string, NodeLayout>
        {
            ["source"] = Layout(nodes["source"], 100, 0, 0),
            ["target-a"] = Layout(nodes["target-a"], 100, 300, 3),
            ["target-b"] = Layout(nodes["target-b"], 100, 320, 3),
            ["blocker"] = Layout(nodes["blocker"], 105, 180, 2)
        };
        return new PlacedGraph(graph, layouts, new Dictionary<string, ProjectLayout>(), new LayoutRevision(0));
    }

    private static TypeNode Type(string id) => new(id, "project", id, id, "Class");
    private static DependencyEdge Edge(string id, string source, string target) => new(id, source, target, "Dependency");
    private static NodeLayout Layout(RenderNode node, int x, int y, int depth) => new(node, new Rect(x, y, 40, 40), depth, false);
}
