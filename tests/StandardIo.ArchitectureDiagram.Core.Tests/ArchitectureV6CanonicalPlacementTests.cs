using System;
using System.Collections.Generic;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core.Models.ArchitectureV6;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.ArchitectureV6;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core.Tests;

public sealed class ArchitectureV6CanonicalPlacementTests
{
    [Fact]
    public void Single_child_is_below_and_centred_on_parent()
    {
        var result = Build(new ArchitectureV6SemanticFixtureBuilder()
            .Project("p", "Project")
            .Node("parent", "ParentService", "p")
            .Node("child", "ChildService", "p")
            .Link("link", "parent", "child"));

        var parent = Node(result, "parent");
        var child = Node(result, "child");

        Assert.True(child.AnchorCellId.RowId.Value != parent.AnchorCellId.RowId.Value);
        Assert.True(Row(result, child) > Row(result, parent));
        Assert.Equal(parent.CentreColumnId.Value, child.CentreColumnId.Value);
    }

    [Fact]
    public void Multiple_children_use_direct_child_centres_and_fifo_sibling_order()
    {
        var result = Build(new ArchitectureV6SemanticFixtureBuilder()
            .Project("p", "Project")
            .Node("parent", "ParentService", "p")
            .Node("first", "FirstService", "p")
            .Node("second", "SecondService", "p")
            .Node("unrelated", "UnrelatedService", "p")
            .Link("first-link", "parent", "first")
            .Link("second-link", "parent", "second"));

        var parent = Node(result, "parent");
        var first = Node(result, "first");
        var second = Node(result, "second");
        var unrelated = Node(result, "unrelated");
        var expectedCentre = (Column(first) + Column(second)) / 2;

        Assert.Equal(expectedCentre, Column(parent));
        Assert.True(Column(first) < Column(second));
        Assert.True(Column(unrelated) > Column(second) || Column(unrelated) < Column(first));
        Assert.Equal(new[] { "physical:first", "physical:second" },
            result.Placement.NodeMetadata.Single(item => item.PhysicalNodeId == "physical:parent").PositionalChildIds);
    }

    [Fact]
    public void Sibling_units_have_one_logical_separation_column_and_no_footprint_overlap()
    {
        var result = Build(new ArchitectureV6SemanticFixtureBuilder()
            .Project("p", "Project")
            .Node("left", "LeftService", "p")
            .Node("right", "RightService", "p"));

        var left = Node(result, "left");
        var right = Node(result, "right");
        var leftColumns = left.Footprint.Select(cell => Column(cell.ColumnId)).ToArray();
        var rightColumns = right.Footprint.Select(cell => Column(cell.ColumnId)).ToArray();

        Assert.Equal(1, rightColumns.Min() - leftColumns.Max() - 1);
        Assert.Empty(result.Placement.NodePlacements.SelectMany(item => item.Footprint)
            .GroupBy(cell => cell).Where(group => group.Count() > 1));
    }

    [Fact]
    public void Detached_reserved_dependency_is_composed_outside_main_tree_in_fifo_order()
    {
        var rules = new[] { new ArchitectureV6RoleRule("Processing", "*ProcessingService", 0) };
        var result = Build(new ArchitectureV6SemanticFixtureBuilder()
            .Project("p", "Project")
            .Node("parent", "ParentProcessingService", "p")
            .Node("detached", "DetachedProcessingService", "p")
            .Link("link", "parent", "detached"), rules);

        var parent = Node(result, "parent");
        var detached = Node(result, "detached");
        Assert.True(Column(detached) > Column(parent));
        Assert.NotEqual(Column(parent), Column(detached));
        Assert.Empty(result.Placement.Diagnostics.Where(item => item.Code == "LogicalPlacementFootprintOverlap"));
    }

    [Fact]
    public void Project_grid_has_fixed_two_track_surround_and_capabilities()
    {
        var result = Build(new ArchitectureV6SemanticFixtureBuilder()
            .Project("p", "Project")
            .Node("node", "NodeService", "p")
            .Node("child", "ChildService", "p")
            .Link("link", "node", "child"));
        var grid = result.Placement.ProjectGrids.Single().Grid;

        Assert.Equal(new[] { -2, -1 }, grid.Rows.OrderBy(row => row.LogicalOrder).Take(2).Select(row => row.LogicalOrder));
        Assert.Equal(PlanningGridTrackRole.ProjectHeader, grid.Rows.Single(row => row.LogicalOrder == -2).Role);
        Assert.Equal(PlanningGridTrackRole.InterLayerRouting, grid.Rows.Single(row => row.LogicalOrder == -1).Role);
        Assert.Equal(PlanningGridTrackRole.ProjectBoundaryTransition, grid.Columns.MinBy(column => column.LogicalOrder)!.Role);
        Assert.Equal(PlanningGridTrackRole.ProjectBoundaryTransition, grid.Columns
            .OrderBy(column => column.LogicalOrder).ElementAt(^2).Role);

        var headerCell = grid.Cells.Single(cell => cell.Key.RowId.Value == "r-2" && cell.Key.ColumnId.Value == "c0").Value;
        var outerCell = grid.Cells.Single(cell => cell.Key.RowId.Value == "r-1" && cell.Key.ColumnId.Value == "c-1").Value;
        var nodeCell = grid.Cells.Single(cell => cell.Key.RowId.Value == "r1" && cell.Key.ColumnId.Value == "c0").Value;
        Assert.True(headerCell.Capabilities.HasFlag(CellCapability.ProjectBoundary));
        Assert.True(outerCell.Capabilities.HasFlag(CellCapability.RoutingAllowed));
        Assert.True(nodeCell.Capabilities.HasFlag(CellCapability.NodeAllowed));
    }

    [Fact]
    public void External_nodes_use_final_reserved_row_and_prefer_owner_centre()
    {
        var result = Build(new ArchitectureV6SemanticFixtureBuilder()
            .Project("p", "Project")
            .Node("owner", "OwnerService", "p")
            .External("external", "IMetadataCache")
            .Link("link", "owner", "external"));

        var owner = Node(result, "owner");
        var external = Node(result, "external");
        Assert.Equal(result.Placement.ProjectGrids.Single().ExternalNodeIds.Single(), external.PhysicalNodeId);
        Assert.Equal(result.Placement.ProjectGrids.Single().Grid.Rows.Max(row => row.LogicalOrder - 2), Row(result, external));
        Assert.Equal(Column(owner), Column(external));
    }

    [Fact]
    public void Standalone_nodes_are_below_external_and_pack_square_with_one_cell_gaps()
    {
        var fixture = new ArchitectureV6SemanticFixtureBuilder()
            .Project("p", "Project")
            .Node("owner", "OwnerService", "p")
            .External("external", "IExternal")
            .Link("external-link", "owner", "external");
        for (var index = 0; index < 4; index++)
            fixture.Node("standalone" + index, "Standalone" + index + "Service", "p");

        var result = Build(fixture);
        var grid = result.Placement.ProjectGrids.Single().Grid;
        var standalones = Enumerable.Range(0, 4).Select(index => Node(result, "standalone" + index)).ToArray();
        var external = Node(result, "external");

        Assert.All(standalones, node => Assert.True(Row(result, node) > Row(result, external)));
        Assert.Equal(2, standalones.Select(node => Row(result, node)).Distinct().Count());
        var rows = standalones.GroupBy(node => Row(result, node)).OrderBy(group => group.Key).ToArray();
        Assert.All(rows, row => Assert.Equal(2, row.Count()));
        Assert.Equal(2, rows[1].Key - rows[0].Key);
        foreach (var row in rows)
        {
            var nodes = row.OrderBy(node => Column(node)).ToArray();
            var rightEdge = nodes[1].Footprint.Min(cell => Column(cell.ColumnId));
            var leftEnd = nodes[0].Footprint.Max(cell => Column(cell.ColumnId));
            Assert.Equal(1, rightEdge - leftEnd - 1);
        }
        Assert.DoesNotContain(grid.Rows, row => row.Role == PlanningGridTrackRole.StandaloneRegion && row.LogicalOrder == Row(result, standalones[0]) - 1);
    }

    [Fact]
    public void Standalone_rows_use_cumulative_actual_spans_and_report_composition_metrics()
    {
        var result = Build(new ArchitectureV6SemanticFixtureBuilder()
            .Project("p", "Project")
            .Node("wide", new string('W', 100) + "Service", "p")
            .Node("narrow", "NarrowService", "p")
            .Node("third", "ThirdService", "p")
            .Node("fourth", "FourthService", "p"));

        var row = new[] { "wide", "narrow" }.Select(id => Node(result, id)).Select(item => Row(result, item)).Distinct().Single();
        var sameRow = new[] { "wide", "narrow" }.Select(id => Node(result, id)).OrderBy(item => Column(item)).ToArray();
        var leftEnd = sameRow[0].Footprint.Max(cell => Column(cell.ColumnId));
        var rightStart = sameRow[1].Footprint.Min(cell => Column(cell.ColumnId));

        Assert.Equal(1, rightStart - leftEnd - 1);
        Assert.Empty(result.Placement.Diagnostics.Where(item => item.Code == "LogicalPlacementFootprintOverlap"));
        var metrics = result.Placement.Diagnostics.Single(item => item.Code == "LogicalPlacementCompositionMetrics");
        Assert.Contains("standaloneRegionWidth=", metrics.Message, StringComparison.Ordinal);
        Assert.Contains("atomicUnitSeparation=1", metrics.Message, StringComparison.Ordinal);
        Assert.Contains("finalProjectGridWidth=", metrics.Message, StringComparison.Ordinal);
        Assert.Equal(2, sameRow.Length);
        Assert.All(sameRow, item => Assert.Equal(row, Row(result, item)));
    }

    [Fact]
    public void Independent_top_level_tree_units_have_one_logical_separation_column()
    {
        var result = Build(new ArchitectureV6SemanticFixtureBuilder()
            .Project("p", "Project")
            .Node("left-root", "LeftRootService", "p")
            .Node("left-child", "LeftChildService", "p")
            .Node("right-root", "RightRootService", "p")
            .Node("right-child", "RightChildService", "p")
            .Link("left-link", "left-root", "left-child")
            .Link("right-link", "right-root", "right-child"));

        var leftUnit = new[] { Node(result, "left-root"), Node(result, "left-child") };
        var rightUnit = new[] { Node(result, "right-root"), Node(result, "right-child") };
        var leftEnd = leftUnit.Max(item => item.Footprint.Max(cell => Column(cell.ColumnId)));
        var rightStart = rightUnit.Min(item => item.Footprint.Min(cell => Column(cell.ColumnId)));

        Assert.Equal(1, rightStart - leftEnd - 1);
        Assert.DoesNotContain(result.Placement.Diagnostics, item => item.Code == "LogicalPlacementFootprintOverlap");
        var metric = result.Placement.Diagnostics.Single(item => item.Code == "LogicalPlacementCompositionMetrics");
        Assert.Contains("topLevelTrees=2", metric.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Placement_composition_metrics_are_deterministic()
    {
        var fixture = new ArchitectureV6SemanticFixtureBuilder()
            .Project("p", "Project")
            .Node("root", "RootService", "p")
            .Node("child", "ChildService", "p")
            .Link("link", "root", "child");

        var first = Build(fixture.BuildRequest());
        var second = Build(fixture.BuildRequest());

        Assert.Equal(
            first.Placement.Diagnostics.Single(item => item.Code == "LogicalPlacementCompositionMetrics").Message,
            second.Placement.Diagnostics.Single(item => item.Code == "LogicalPlacementCompositionMetrics").Message);
    }

    [Fact]
    public void Placement_freeze_is_deterministic_and_exposes_immutable_records()
    {
        var fixture = new ArchitectureV6SemanticFixtureBuilder()
            .Project("first", "First")
            .Project("second", "Second")
            .Node("a", "AService", "first")
            .Node("b", "BService", "second");
        var first = Build(fixture.BuildRequest() with
        {
            SelectedScope = new ArchitectureSelectionScope("SelectedProjects", new[] { "first", "second" }, Array.Empty<string>())
        });
        var second = Build(new ArchitectureV6SemanticFixtureBuilder()
            .Project("first", "First")
            .Project("second", "Second")
            .Node("a", "AService", "first")
            .Node("b", "BService", "second")
            .BuildRequest() with
        {
            SelectedScope = new ArchitectureSelectionScope("SelectedProjects", new[] { "first", "second" }, Array.Empty<string>())
        });

        Assert.True(first.Freeze.IsFrozen);
        Assert.Equal(first.Freeze.Fingerprint, second.Freeze.Fingerprint);
        Assert.Equal(0, first.Placement.Diagnostics.Count(item => item.Code == "LogicalPlacementFootprintOverlap"));
        Assert.Equal(new[] { "first", "second" }, first.Freeze.Projects.Select(project => project.ProjectId));
        Assert.NotEqual(first.Freeze.Projects[0].Grid.Transform.Origin.X, first.Freeze.Projects[1].Grid.Transform.Origin.X);
        Assert.Equal(first.Freeze.Nodes.Count, first.Freeze.Nodes.Distinct().Count());
        Assert.IsAssignableFrom<IReadOnlyDictionary<PlanningGridCellId, PlanningGridCell>>(first.Freeze.Projects[0].Grid.Cells);
    }

    [Fact]
    public void Child_and_placement_order_follow_projection_order_not_physical_ids()
    {
        var first = Build(new ArchitectureV6SemanticFixtureBuilder()
            .Project("p", "Project")
            .Node("parent", "ParentService", "p")
            .Node("a", "AService", "p")
            .Node("b", "BService", "p")
            .Link("a-link", "parent", "a")
            .Link("b-link", "parent", "b"));
        var second = Build(new ArchitectureV6SemanticFixtureBuilder()
            .Project("p", "Project")
            .Node("parent", "ParentService", "p")
            .Node("a", "AService", "p")
            .Node("b", "BService", "p")
            .Link("b-link", "parent", "b")
            .Link("a-link", "parent", "a"));

        Assert.Equal(first.Freeze.Fingerprint, second.Freeze.Fingerprint);
        Assert.Equal(Column(Node(first, "a")), Column(Node(second, "a")));
        Assert.Equal(Column(Node(first, "b")), Column(Node(second, "b")));
    }

    [Fact]
    public void Active_planner_stops_at_placement_freeze_and_defers_downstream_stages()
    {
        var request = new ArchitectureV6SemanticFixtureBuilder()
            .Project("p", "Project")
            .Node("parent", "ParentService", "p")
            .Node("child", "ChildService", "p")
            .Link("link", "parent", "child")
            .BuildRequest();

        var expected = Build(request);
        var result = new ArchitectureDiagramV6Planner().Plan(request);

        Assert.True(result.PlacementFreeze!.IsFrozen);
        Assert.Equal(expected.Freeze.Fingerprint, result.PlacementFreeze.Fingerprint);
        Assert.Equal(expected.Placement.NodePlacements.Select(item => (item.PhysicalNodeId, item.AnchorCellId)),
            result.NodePlacements.Select(item => (item.PhysicalNodeId, item.AnchorCellId)));
        Assert.Equal(result.PhysicalLinks.Count, result.Routes.Count);
        Assert.All(result.Routes, route => Assert.NotEmpty(route.Steps));
        Assert.NotNull(result.RelativeGeometry);
        Assert.NotNull(result.PhysicalScene);
        Assert.True(result.StageStatus.LogicalPlacementCompleted);
        Assert.True(result.StageStatus.AbstractRoutingCompleted);
        Assert.False(result.StageStatus.LaneAllocationDeferred);
        Assert.False(result.StageStatus.SizingDeferred);
        Assert.False(result.StageStatus.AbsoluteGeometryDeferred);
        Assert.True(result.StageStatus.SizingCompleted);
        Assert.True(result.StageStatus.AbsoluteGeometryCompleted);
        Assert.DoesNotContain(result.Diagnostics.Findings, item => item.Code == "V6StageDeferred.LanesAndTerminals");
        Assert.DoesNotContain(result.Diagnostics.Findings, item => item.Code == "V6StageDeferred.PhysicalSizing");
        Assert.DoesNotContain(result.Diagnostics.Findings, item => item.Code == "V6StageDeferred.Validation");
        Assert.Contains(result.Diagnostics.Findings, item => item.Code == "V6StageDeferred.Rendering");
    }

    private static ArchitectureV6CanonicalPlacementResult Build(
        ArchitecturePlanningRequest request)
    {
        var projection = ArchitectureV6ProjectionStage.Build(request);
        var spans = new ArchitectureV6PreRoutingSpanSizer(request, projection).Build();
        var reservations = new ArchitectureV6ReservedDepthPlanner(request, projection);
        var table = reservations.BuildFrozenTable(reservations.Inspect());
        return new ArchitectureV6CanonicalPlacementBuilder(request, projection, spans, table).Build();
    }

    private static ArchitectureV6CanonicalPlacementResult Build(
        ArchitectureV6SemanticFixtureBuilder fixture,
        IReadOnlyList<ArchitectureV6RoleRule>? rules = null) => Build(fixture.BuildRequest(rules));

    private static PlannedNodePlacement Node(ArchitectureV6CanonicalPlacementResult result, string semanticId) =>
        result.Placement.NodePlacements.Single(item => item.PhysicalNodeId == "physical:" + semanticId);

    private static int Row(ArchitectureV6CanonicalPlacementResult result, PlannedNodePlacement node) =>
        result.Placement.ProjectGrids.Single(project => project.Grid.Id == node.GridId).Grid.Rows
            .Single(row => row.Id == node.AnchorCellId.RowId).LogicalOrder;

    private static int Column(PlannedNodePlacement node) => Column(node.CentreColumnId);

    private static int Column(PlanningGridColumnId id) => int.Parse(id.Value[1..]);

}
