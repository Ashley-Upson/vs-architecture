using StandardIo.ArchitectureDiagram.Core.Models;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core.Tests;

public sealed class ProjectInterLayerSlotCompilerTests
{
    [Fact]
    public void Destination_columns_do_not_occupy_another_routes_fixed_target_entry_column()
    {
        var graph = RenderGraph.From(new DiagramModel(
            new[] { new ProjectContainer("project", "Project", new[] { Node("upper"), Node("lower"), Node("target") }) },
            Array.Empty<ExternalDependencyNode>(),
            new[]
            {
                new DependencyEdge("lower-target", "lower", "target", "Dependency"),
                new DependencyEdge("upper-target", "upper", "target", "Dependency")
            }));
        var nodes = new Dictionary<string, NodeLayout>(StringComparer.Ordinal)
        {
            ["upper"] = Layout(graph, "upper", new Rect(240, 0, 120, 60), 0),
            ["lower"] = Layout(graph, "lower", new Rect(440, 180, 120, 60), 1),
            ["target"] = Layout(graph, "target", new Rect(60, 600, 120, 60), 3)
        };
        var links = graph.Links.ToDictionary(link => link.Id, link => link.Id == "upper-target"
            ? new LinkLayout(link, new Point(300, 60), new Point(132, 600), Array.Empty<Point>(), 0.5, 0.6)
            : new LinkLayout(link, new Point(500, 240), new Point(120, 600), Array.Empty<Point>(), 0.5, 0.4),
            StringComparer.Ordinal);
        var revision = new LayoutRevision(1);
        var plans = CanonicalTopologyFamilySelector.Select(graph, nodes, revision).Plans;

        var compiled = ProjectInterLayerSlotCompiler.Compile(
            plans, nodes, links, new Dictionary<string, ProjectLabelGeometry>(), revision, 12, 10);
        var validation = TraceabilityValidator.Validate(nodes, compiled.Links, 12);

        Assert.DoesNotContain(validation.Violations, violation =>
            violation.Code is TraceabilityViolationCode.SharedSegment or TraceabilityViolationCode.ParallelSpacing);
    }

    [Fact]
    public void Destination_column_exclusions_include_other_routes_fixed_arrival_segment()
    {
        var graph = RenderGraph.From(new DiagramModel(
            new[] { new ProjectContainer("project", "Project", new[] { Node("source"), Node("other"), Node("target") }) },
            Array.Empty<ExternalDependencyNode>(),
            new[]
            {
                new DependencyEdge("current", "source", "target", "Dependency"),
                new DependencyEdge("other", "other", "target", "Dependency")
            }));
        var nodes = new Dictionary<string, NodeLayout>(StringComparer.Ordinal)
        {
            ["source"] = Layout(graph, "source", new Rect(300, 0, 120, 60), 0),
            ["other"] = Layout(graph, "other", new Rect(500, 180, 120, 60), 1),
            ["target"] = Layout(graph, "target", new Rect(60, 600, 120, 60), 3)
        };
        var revision = new LayoutRevision(1);
        var plans = CanonicalTopologyFamilySelector.Select(graph, nodes, revision).Plans;
        var routes = graph.Links.ToDictionary(link => link.Id, link => new LinkLayout(
            link, new Point(link.Id == "current" ? 360 : 560, link.Id == "current" ? 60 : 240),
            new Point(link.Id == "current" ? 132 : 120, 600), Array.Empty<Point>(), 0.5, 0.5), StringComparer.Ordinal);
        var demands = plans.Values.SelectMany(plan => new[]
        {
            Demand($"{plan.LogicalRouteId}:departure", plan.LogicalRouteId, 0),
            Demand($"{plan.LogicalRouteId}:arrival", plan.LogicalRouteId, 1)
        }).ToArray();
        var assignments = demands.ToDictionary(demand => demand.Id, demand => new AssignedLinkSegment(
            demand.Id + ":assigned", demand.Id, demand.LogicalRouteId, LinkSegmentOrientation.Horizontal,
            demand.TurnOrder == 0 ? 100 : 300, 0, demand.OccupiedInterval, demand.Role,
            demand.PlacementRevision, demand.RouteRevision), StringComparer.Ordinal);

        var exclusions = ProjectInterLayerSlotCompiler.FixedColumnExclusions(
            plans["current"], plans, routes, demands, assignments, new AxisInterval(100, 400), 12).ToArray();

        Assert.Contains(exclusions, interval => interval == new AxisInterval(108, 132));
    }

    private static TypeNode Node(string id) => new(id, "project", id, $"Fixture.{id}", "Class");

    private static NodeLayout Layout(RenderGraph graph, string id, Rect rect, int depth) =>
        new(graph.Nodes.Single(node => node.Id == id), rect, depth, false);

    private static LinkSegmentDemand Demand(string id, string routeId, int turnOrder) => new(
        id, routeId, LinkSegmentOrientation.Horizontal, new AxisInterval(0, 600), new AxisInterval(0, 600),
        null, LinkSegmentRole.Through, 0, turnOrder,
        new MovementScopeIdentity(MovementScopeKind.LayerAndLowerSuffix, "depth:1"),
        new LayoutRevision(1), new RouteRevision(0));
}
