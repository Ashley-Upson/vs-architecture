using StandardIo.ArchitectureDiagram.Core.Models;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core.Tests;

public sealed class ProjectInterLayerSlotCompilerTests
{
    [Fact]
    public void Project_local_bands_do_not_share_physical_coordinates_for_equal_local_depths()
    {
        var first = CompileOffsetProjects(reverseProjects: false);
        var reversed = CompileOffsetProjects(reverseProjects: true);

        Assert.All(first.Demands.Where(demand => demand.CoordinateFrameId == "project-a"), demand =>
        {
            Assert.StartsWith("project:project-a:depth:", demand.MovementScope!.Value.Id);
            Assert.Contains("project-project-a", demand.BandId);
            Assert.Equal("ProjectInternal", demand.DemandCategory);
            Assert.InRange(first.Assignments[demand.Id].AxisCoordinate,
                demand.AllowedAxisRange.Minimum, demand.AllowedAxisRange.Maximum);
            Assert.True(first.Assignments[demand.Id].AxisCoordinate > 1900);
        });
        Assert.All(first.Demands.Where(demand => demand.CoordinateFrameId == "project-b"), demand =>
        {
            Assert.StartsWith("project:project-b:depth:", demand.MovementScope!.Value.Id);
            Assert.Contains("project-project-b", demand.BandId);
            Assert.InRange(first.Assignments[demand.Id].AxisCoordinate,
                demand.AllowedAxisRange.Minimum, demand.AllowedAxisRange.Maximum);
            Assert.True(first.Assignments[demand.Id].AxisCoordinate < 1000);
        });
        Assert.Equal(first.Links.Keys.OrderBy(item => item), reversed.Links.Keys.OrderBy(item => item));
        foreach (var routeId in first.Links.Keys)
            Assert.Equal(first.Links[routeId].Points, reversed.Links[routeId].Points);
    }

    [Fact]
    public void Cross_project_routes_use_a_distinct_root_transition_demand_category()
    {
        var graph = Graph(new[] { "project-a", "project-b" },
            new[] { ("cross", "a0", "b1") });
        var nodes = Layouts(graph);
        var revision = new LayoutRevision(1);
        var plans = CanonicalTopologyFamilySelector.Select(graph, nodes, revision).Plans;
        var compiled = ProjectInterLayerSlotCompiler.Compile(plans, nodes, Routes(graph, nodes),
            new Dictionary<string, ProjectLabelGeometry>(), revision, 12, 10);

        var demand = Assert.Single(compiled.Demands);
        Assert.Null(demand.CoordinateFrameId);
        Assert.Equal("RootTransition", demand.DemandCategory);
        Assert.StartsWith("root-transition:depth:", demand.MovementScope!.Value.Id);
        Assert.Contains("RootTransition", demand.BandId);
    }

    [Fact]
    public void Single_project_adjacent_route_retains_the_existing_slot_order_and_coordinate()
    {
        var graph = RenderGraph.From(new DiagramModel(
            new[] { new ProjectContainer("project", "Project", new[] { Node("upper"), Node("lower") }) },
            Array.Empty<ExternalDependencyNode>(),
            new[] { new DependencyEdge("route", "upper", "lower", "Dependency") }));
        var nodes = new Dictionary<string, NodeLayout>(StringComparer.Ordinal)
        {
            ["upper"] = Layout(graph, "upper", new Rect(0, 0, 120, 60), 0),
            ["lower"] = Layout(graph, "lower", new Rect(240, 180, 120, 60), 1)
        };
        var route = graph.Links.Single();
        var links = new Dictionary<string, LinkLayout>(StringComparer.Ordinal)
        {
            [route.Id] = new(route, new Point(60, 60), new Point(300, 180), Array.Empty<Point>(), 0.5, 0.5)
        };
        var revision = new LayoutRevision(1);
        var plans = CanonicalTopologyFamilySelector.Select(graph, nodes, revision).Plans;

        var compiled = ProjectInterLayerSlotCompiler.Compile(plans, nodes, links,
            new Dictionary<string, ProjectLabelGeometry>(), revision, 12, 10);

        Assert.Equal(70, compiled.Assignments["route:horizontal:0"].AxisCoordinate);
        Assert.Equal(new[] { new Point(60, 70), new Point(300, 70) }, compiled.Links["route"].Points);
    }

    [Fact]
    public void Project_slot_skips_a_node_intersecting_only_the_requested_horizontal_span()
    {
        var compiled = CompileObstacleFixture(new Rect(180, 65, 120, 60));
        var assignment = compiled.Assignments["route:horizontal:0"];

        Assert.True(assignment.AxisCoordinate > 135);
        Assert.Empty(compiled.RequiredLayerExpansion);
    }

    [Fact]
    public void Node_outside_the_requested_horizontal_span_does_not_block_the_preferred_slot()
    {
        var compiled = CompileObstacleFixture(new Rect(700, 65, 120, 60));

        Assert.Equal(70, compiled.Assignments["route:horizontal:0"].AxisCoordinate);
    }

    [Fact]
    public void Project_slot_requests_project_scoped_expansion_when_every_existing_slot_is_blocked()
    {
        var compiled = CompileObstacleFixture(new Rect(180, 55, 120, 150));

        var expansion = Assert.Single(compiled.RequiredLayerExpansion);
        Assert.Equal(new ProjectLayerExpansionIdentity("project", 1), expansion.Key);
        Assert.True(expansion.Value > 0);
    }

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

    private static ProjectSlotCompilation CompileOffsetProjects(bool reverseProjects)
    {
        var projectIds = reverseProjects ? new[] { "project-b", "project-a" } : new[] { "project-a", "project-b" };
        var links = new[]
        {
            ("a-adjacent", "a0", "a1"), ("a-long", "a0", "a2"),
            ("a-up", "a2", "a0"), ("a-same", "a1", "a1b"),
            ("b-adjacent", "b0", "b1"), ("b-long", "b0", "b2"),
            ("b-up", "b2", "b0"), ("b-same", "b1", "b1b")
        };
        var graph = Graph(projectIds, links);
        var nodes = Layouts(graph);
        var revision = new LayoutRevision(1);
        return ProjectInterLayerSlotCompiler.Compile(
            CanonicalTopologyFamilySelector.Select(graph, nodes, revision).Plans,
            nodes, Routes(graph, nodes), new Dictionary<string, ProjectLabelGeometry>(), revision, 12, 10);
    }

    private static ProjectSlotCompilation CompileObstacleFixture(Rect obstacleRect)
    {
        var graph = RenderGraph.From(new DiagramModel(
            new[] { new ProjectContainer("project", "Project", new[]
            {
                Node("source"), Node("target"), Node("obstacle")
            }) }, Array.Empty<ExternalDependencyNode>(),
            new[] { new DependencyEdge("route", "source", "target", "Dependency") }));
        var nodes = new Dictionary<string, NodeLayout>(StringComparer.Ordinal)
        {
            ["source"] = Layout(graph, "source", new Rect(0, 0, 120, 60), 0),
            ["target"] = Layout(graph, "target", new Rect(440, 180, 120, 60), 1),
            ["obstacle"] = Layout(graph, "obstacle", obstacleRect, 2)
        };
        var link = graph.Links.Single();
        var routes = new Dictionary<string, LinkLayout>(StringComparer.Ordinal)
        {
            [link.Id] = new(link, new Point(60, 60), new Point(500, 180), Array.Empty<Point>(), 0.5, 0.5)
        };
        var revision = new LayoutRevision(1);
        return ProjectInterLayerSlotCompiler.Compile(
            CanonicalTopologyFamilySelector.Select(graph, nodes, revision).Plans,
            nodes, routes, new Dictionary<string, ProjectLabelGeometry>(), revision, 12, 10);
    }

    private static RenderGraph Graph(IEnumerable<string> projectIds, IEnumerable<(string Id, string Source, string Target)> links)
    {
        var projects = projectIds.Select(id =>
        {
            var prefix = id == "project-a" ? "a" : "b";
            return new ProjectContainer(id, id, new[]
            {
                Node(prefix + "0", id), Node(prefix + "1", id), Node(prefix + "1b", id), Node(prefix + "2", id)
            });
        }).ToArray();
        return RenderGraph.From(new DiagramModel(projects, Array.Empty<ExternalDependencyNode>(),
            links.Select(link => new DependencyEdge(link.Id, link.Source, link.Target, "Dependency")).ToArray()));
    }

    private static IReadOnlyDictionary<string, NodeLayout> Layouts(RenderGraph graph) =>
        graph.Nodes.ToDictionary(node => node.Id, node =>
        {
            var projectA = node.ProjectId == "project-a";
            var origin = projectA ? 1900 : 100;
            var depth = node.Id.EndsWith("0", StringComparison.Ordinal) ? 0 :
                node.Id.EndsWith("2", StringComparison.Ordinal) ? 2 : 1;
            var x = node.Id.EndsWith("b", StringComparison.Ordinal) ? 500 : depth * 200;
            return new NodeLayout(node, new Rect(x, origin + depth * 250, 120, 60), depth, false);
        }, StringComparer.Ordinal);

    private static IReadOnlyDictionary<string, LinkLayout> Routes(
        RenderGraph graph, IReadOnlyDictionary<string, NodeLayout> nodes) =>
        graph.Links.ToDictionary(link => link.Id, link => new LinkLayout(link,
            new Point(nodes[link.SourceId].Rect.CenterX, nodes[link.SourceId].Rect.Bottom),
            new Point(nodes[link.TargetId].Rect.CenterX, nodes[link.TargetId].Rect.Y),
            Array.Empty<Point>(), 0.5, 0.5), StringComparer.Ordinal);

    private static TypeNode Node(string id, string projectId = "project") =>
        new(id, projectId, id, $"Fixture.{id}", "Class");

    private static NodeLayout Layout(RenderGraph graph, string id, Rect rect, int depth) =>
        new(graph.Nodes.Single(node => node.Id == id), rect, depth, false);

    private static LinkSegmentDemand Demand(string id, string routeId, int turnOrder) => new(
        id, routeId, LinkSegmentOrientation.Horizontal, new AxisInterval(0, 600), new AxisInterval(0, 600),
        null, LinkSegmentRole.Through, 0, turnOrder,
        new MovementScopeIdentity(MovementScopeKind.LayerAndLowerSuffix, "depth:1"),
        new LayoutRevision(1), new RouteRevision(0));
}
