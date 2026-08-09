using System;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core.Models.ArchitectureV6;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.ArchitectureV6;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core.Tests;

public sealed class ArchitectureV6LogicalRoutingTests
{
    [Fact]
    public void Downward_route_reaches_target_from_the_routing_row_above_it()
    {
        var request = new ArchitectureV6SemanticFixtureBuilder()
            .Project("p", "Project")
            .Node("source", "SourceService", "p")
            .Node("target", "TargetService", "p")
            .Link("link", "source", "target")
            .BuildRequest();

        var plan = new ArchitectureDiagramV6Planner().Plan(request);
        var route = Assert.Single(plan.Routes);
        var source = plan.NodePlacements.Single(item => item.PhysicalNodeId == route.Source.PhysicalNodeId);
        var target = plan.NodePlacements.Single(item => item.PhysicalNodeId == route.Destination.PhysicalNodeId);

        Assert.True(Row(route.Steps[0]) < Row(route.Steps[^1]));
        Assert.Equal(target.AnchorCellId, route.Steps[^1].CellId);
        Assert.Contains(route.Steps, step => step.Role == RouteStepRole.VerticalPassThrough && Row(step) < Row(target));
        AssertOrthogonallyAdjacent(route);
        Assert.Equal(source.GridId, route.Steps[0].GridId);
    }

    [Fact]
    public void Upward_route_departs_downward_and_escapes_source_footprint_before_ascending()
    {
        var request = new ArchitectureV6SemanticFixtureBuilder()
            .Project("p", "Project")
            .Node("source", "SourceService", "p")
            .Node("target", "TargetService", "p")
            .Link("down", "source", "target")
            .Link("up", "target", "source")
            .BuildRequest();

        var plan = new ArchitectureDiagramV6Planner().Plan(request);
        var route = plan.Routes.Single(item => item.PhysicalLinkId.Contains("up", StringComparison.Ordinal));
        var source = plan.NodePlacements.Single(item => item.PhysicalNodeId == route.Source.PhysicalNodeId);
        var sourceColumns = source.Footprint.Select(cell => Column(cell.ColumnId)).ToHashSet();
        Assert.True(route.Steps.Count > 1,
            $"Upward route was not materialised: supported={route.IsStructurallySupported}, reason={route.UnsupportedReason}, source={source.AnchorCellId}, steps={string.Join(" -> ", route.Steps.Select(step => step.CellId))}");
        var firstMove = route.Steps[1];

        Assert.True(Row(firstMove) > Row(source));
        Assert.Contains(route.Steps, step => Row(step) < Row(source) && !sourceColumns.Contains(Column(step.CellId.ColumnId)));
        Assert.DoesNotContain(route.Steps.Skip(1), step => step.CellId.Equals(source.AnchorCellId));
        AssertOrthogonallyAdjacent(route);
    }

    [Fact]
    public void Logical_routes_use_capabilities_and_never_skip_cells_or_enter_unrelated_footprints()
    {
        var request = new ArchitectureV6SemanticFixtureBuilder()
            .Project("p", "Project")
            .Node("root", "RootService", "p")
            .Node("left", "LeftService", "p")
            .Node("right", "RightService", "p")
            .Node("target", "TargetService", "p")
            .Link("left-target", "left", "target")
            .Link("right-target", "right", "target")
            .BuildRequest();

        var plan = new ArchitectureDiagramV6Planner().Plan(request);
        foreach (var route in plan.Routes)
        {
            Assert.True(route.IsStructurallySupported, route.UnsupportedReason);
            AssertOrthogonallyAdjacent(route);
            var endpoints = new[] { route.Source.PhysicalNodeId, route.Destination.PhysicalNodeId };
            var occupied = plan.NodePlacements.Where(item => !endpoints.Contains(item.PhysicalNodeId))
                .SelectMany(item => item.Footprint).ToHashSet();
            Assert.DoesNotContain(route.Steps.Skip(1).SkipLast(1), step => occupied.Contains(step.CellId));
        }
    }

    [Fact]
    public void Cross_project_route_uses_explicit_grid_provenance_without_a_second_routing_algorithm()
    {
        var request = new ArchitectureV6SemanticFixtureBuilder()
            .Project("a", "A")
            .Project("b", "B")
            .Node("source", "SourceService", "a")
            .Node("target", "TargetService", "b")
            .Link("cross", "source", "target", "cross-project")
            .BuildRequest() with
        {
            SelectedScope = new ArchitectureSelectionScope("SelectedProjects", new[] { "a", "b" }, Array.Empty<string>())
        };

        var plan = new ArchitectureDiagramV6Planner().Plan(request);
        var route = Assert.Single(plan.Routes);

        Assert.Equal(RouteTopologyFamily.CrossProject, route.TopologyFamily);
        Assert.Equal(2, route.Transitions.Count);
        Assert.Contains(route.Steps, step => step.Role == RouteStepRole.DiagramGridPassage);
        Assert.Equal(plan.PhysicalLinks.Count, plan.Routes.Count);
        AssertOrthogonallyAdjacentWithinGrid(route);
    }

    [Fact]
    public void Logical_route_freeze_is_deterministic_and_later_stages_are_deferred()
    {
        var request = new ArchitectureV6SemanticFixtureBuilder()
            .Project("p", "Project")
            .Node("source", "SourceService", "p")
            .Node("target", "TargetService", "p")
            .Link("link", "source", "target")
            .BuildRequest();

        var first = new ArchitectureDiagramV6Planner().Plan(request);
        var second = new ArchitectureDiagramV6Planner().Plan(request);

        Assert.Equal(RouteFingerprint(first), RouteFingerprint(second));
        Assert.True(first.StageStatus.AbstractRoutingCompleted);
        Assert.True(first.StageStatus.LaneAllocationDeferred);
        Assert.True(first.StageStatus.SizingDeferred);
        Assert.Null(first.RelativeGeometry);
        Assert.Null(first.PhysicalScene);
    }

    private static string RouteFingerprint(PlannedArchitectureDiagram plan) => string.Join("|",
        plan.Routes.Select(route => route.PhysicalLinkId + ":" + string.Join(",", route.Steps.Select(step => step.CellId.ToString()))));

    private static void AssertOrthogonallyAdjacent(PlannedGridRoute route) => AssertOrthogonallyAdjacentWithinGrid(route);

    private static void AssertOrthogonallyAdjacentWithinGrid(PlannedGridRoute route)
    {
        foreach (var pair in route.Steps.Zip(route.Steps.Skip(1), (first, second) => (first, second)))
        {
            if (pair.first.GridId != pair.second.GridId) continue;
            var rowDelta = Math.Abs(Row(pair.second) - Row(pair.first));
            var columnDelta = Math.Abs(Column(pair.second.CellId.ColumnId) - Column(pair.first.CellId.ColumnId));
            Assert.True(rowDelta + columnDelta == 1,
                $"Route skipped or diagonally joined {pair.first.CellId} to {pair.second.CellId}.");
        }
    }

    private static int Row(PlannedGridRouteStep step) => Row(step.CellId.RowId);
    private static int Row(PlannedNodePlacement placement) => Row(placement.AnchorCellId.RowId);
    private static int Row(PlanningGridRowId id) => int.Parse(id.Value.TrimStart('r'));
    private static int Column(PlanningGridCellId cell) => Column(cell.ColumnId);
    private static int Column(PlanningGridColumnId id) => int.Parse(id.Value.TrimStart('c'));
}
