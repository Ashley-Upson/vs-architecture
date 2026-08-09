using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.Core.Models.ArchitectureV7;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.ArchitectureV7;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core.Tests;

public sealed class ArchitectureV7LogicalRoutingTests
{
    [Fact]
    public void Direct_immediate_child_is_exactly_three_cells()
    {
        var route = Route(new[] { PlacementNode("s", 1, 2), PlacementNode("t", 3, 2) }, Link("l", "s", "t"), 5, 5, GeneralGrid(5, 5));
        Assert.Equal(new[] { (1, 2), (2, 2), (3, 2) }, route.Cells.Select(cell => (cell.Row, cell.Column)));
    }

    [Fact]
    public void RoutingAllowed_permits_vertical_down_entry_and_down_exit()
    {
        var route = Route(new[] { PlacementNode("s", 1, 2), PlacementNode("t", 3, 2) }, Link("routing-allowed-down", "s", "t"), 5, 5,
            Grid(5, 5, (2, 2, ArchitectureV7CellCapability.RoutingAllowed)));
        Assert.True(route.IsComplete, string.Join(";", route.Diagnostics.Select(item => item.Code)));
        Assert.Equal(new[] { (1, 2), (2, 2), (3, 2) }, route.Cells.Select(item => (item.Row, item.Column)));
    }

    [Fact]
    public void RoutingAllowed_rejects_horizontal_exit_and_bend_at_upward_escape()
    {
        var route = Route(new[] { PlacementNode("s", 5, 2), PlacementNode("t", 1, 5) }, Link("routing-allowed-bend", "s", "t"), 9, 9,
            Grid(9, 9, (6, 2, ArchitectureV7CellCapability.RoutingAllowed)));
        Assert.False(route.IsComplete);
        Assert.Contains(route.Diagnostics, item => item.IsHardFailure);
    }

    [Fact]
    public void Downward_route_aligns_on_row_above_destination_and_enters_downward()
    {
        var route = Route(new[] { PlacementNode("s", 1, 1), PlacementNode("t", 7, 5) }, Link("l", "s", "t"), 9, 9, GeneralGrid(9, 9));
        Assert.True(route.IsComplete, string.Join(";", route.Diagnostics.Select(diagnostic => diagnostic.Code)));
        Assert.Equal((1, 1), (route.Cells.First().Row, route.Cells.First().Column));
        Assert.Equal((2, 1), (route.Cells[1].Row, route.Cells[1].Column));
        Assert.Equal((6, 5), (route.Cells[^2].Row, route.Cells[^2].Column));
        Assert.Equal((7, 5), (route.Cells.Last().Row, route.Cells.Last().Column));
        Assert.All(route.Cells.Zip(route.Cells.Skip(1), (a, b) => (a, b)), pair => Assert.Equal(1, Math.Abs(pair.a.Row - pair.b.Row) + Math.Abs(pair.a.Column - pair.b.Column)));
    }

    [Fact]
    public void GeneralRouting_permits_horizontal_traversal_on_an_ordinary_routing_row()
    {
        var route = Route(new[] { PlacementNode("s", 3, 1), PlacementNode("t", 3, 7) }, Link("horizontal", "s", "t"), 7, 9, GeneralGrid(7, 9));
        Assert.True(route.IsComplete, string.Join(";", route.Diagnostics.Select(diagnostic => diagnostic.Code + ":" + diagnostic.Message)));
        Assert.Contains(route.Cells.Zip(route.Cells.Skip(1), (a, b) => (a, b)), pair => pair.a.Row == pair.b.Row && pair.a.Column != pair.b.Column);
    }

    [Fact]
    public void GeneralRouting_permits_horizontal_to_vertical_bend_on_an_ordinary_routing_row()
    {
        var route = Route(new[] { PlacementNode("s", 1, 1), PlacementNode("t", 7, 5) }, Link("down-bend", "s", "t"), 9, 9, GeneralGrid(9, 9));
        Assert.True(route.IsComplete, string.Join(";", route.Diagnostics.Select(diagnostic => diagnostic.Code)));
        Assert.Contains(route.Cells.Zip(route.Cells.Skip(1), (a, b) => (a, b)), pair => pair.a.Row == pair.b.Row);
        Assert.Contains(route.Cells.Zip(route.Cells.Skip(1), (a, b) => (a, b)), pair => pair.a.Column == pair.b.Column);
    }

    [Fact]
    public void GeneralRouting_permits_vertical_to_horizontal_bend_on_an_ordinary_routing_row()
    {
        var route = Route(new[] { PlacementNode("s", 7, 5), PlacementNode("t", 1, 1) }, Link("up-bend", "s", "t"), 9, 9, GeneralGrid(9, 9));
        Assert.True(route.IsComplete, string.Join(";", route.Diagnostics.Select(diagnostic => diagnostic.Code)));
        Assert.Contains(route.Cells.Zip(route.Cells.Skip(1), (a, b) => (a, b)), pair => pair.a.Row == pair.b.Row);
        Assert.Contains(route.Cells.Zip(route.Cells.Skip(1), (a, b) => (a, b)), pair => pair.a.Column == pair.b.Column);
    }

    [Fact]
    public void Upward_route_starts_downward_then_escapes_source_before_ascending()
    {
        var route = Route(new[] { PlacementNode("s", 5, 3, 3), PlacementNode("t", 1, 7) }, Link("l", "s", "t"), 7, 11, GeneralGrid(7, 11));
        Assert.True(route.IsComplete, string.Join(";", route.Diagnostics.Select(diagnostic => diagnostic.Code + ":" + diagnostic.Message)));
        Assert.Equal((6, 4), (route.Cells[1].Row, route.Cells[1].Column));
        Assert.Contains(route.Cells, cell => cell.Row == 6 && cell.Column == 2);
        Assert.DoesNotContain(route.Cells.Skip(2).TakeWhile(cell => cell.Row > 1), cell => cell.Column is >= 3 and <= 5 && cell.Row == 5);
        Assert.Equal((0, 7), (route.Cells[^2].Row, route.Cells[^2].Column));
        Assert.Equal((1, 7), (route.Cells.Last().Row, route.Cells.Last().Column));
    }

    [Fact]
    public void Blocked_downward_continuation_uses_minus_plus_two_and_equal_distance_prefers_left()
    {
        var nodes = new[] { PlacementNode("s", 1, 3), PlacementNode("t", 7, 3), PlacementNode("obstacle", 3, 3) };
        var route = Route(nodes, Link("l", "s", "t"), 9, 9, GeneralGrid(9, 9));
        Assert.True(route.IsComplete, string.Join(";", route.Diagnostics.Select(diagnostic => diagnostic.Code + ":" + diagnostic.Message)));
        Assert.Equal(new[] { 3, 2, 1 }, route.Cells.Where(cell => cell.Row == 2).Select(cell => cell.Column));
        Assert.Contains(route.Cells, cell => cell.Row == 3 && cell.Column == 1);
        Assert.DoesNotContain(route.Cells, cell => cell.Row == 3 && cell.Column == 5);
        Assert.DoesNotContain(route.Cells.Zip(route.Cells.Skip(1), (a, b) => (a, b)), pair => pair.a.Row == pair.b.Row && Math.Abs(pair.a.Column - pair.b.Column) > 1);
        Assert.Equal(3, route.OperationMetrics.ContinuationCandidatesEvaluated);
    }

    [Fact]
    public void Continuation_selection_does_not_prefer_the_destination_column_over_nearest_left_candidate()
    {
        var nodes = new[] { PlacementNode("s", 1, 3), PlacementNode("t", 7, 7), PlacementNode("obstacle", 3, 3) };
        var route = Route(nodes, Link("nearest-not-destination", "s", "t"), 9, 11, GeneralGrid(9, 11));
        Assert.True(route.IsComplete, string.Join(";", route.Diagnostics.Select(diagnostic => diagnostic.Code + ":" + diagnostic.Message)));
        Assert.Equal(new[] { 3, 2, 1 }, route.Cells.Where(cell => cell.Row == 2).Select(cell => cell.Column));
        Assert.DoesNotContain(route.Cells, cell => cell.Row == 2 && cell.Column == 7);
    }

    [Fact]
    public void Wide_grid_continuation_evaluates_only_candidates_until_first_legal_column()
    {
        const int width = 201;
        var nodes = new[] { PlacementNode("s", 1, 100), PlacementNode("t", 7, 150), PlacementNode("obstacle", 3, 100) };
        var route = Route(nodes, Link("wide-grid", "s", "t"), 9, width, GeneralGrid(9, width));
        Assert.True(route.IsComplete, string.Join(";", route.Diagnostics.Select(diagnostic => diagnostic.Code + ":" + diagnostic.Message)));
        Assert.Equal(3, route.OperationMetrics.ContinuationCandidatesEvaluated);
        Assert.True(route.OperationMetrics.ContinuationCandidatesEvaluated < width / 10);
    }

    [Fact]
    public void Upward_escape_evaluates_nearest_outside_footprint_and_does_not_retain_probe_cells()
    {
        var route = Route(new[] { PlacementNode("s", 5, 3, 3), PlacementNode("t", 1, 7) }, Link("nearest-escape", "s", "t"), 7, 11, GeneralGrid(7, 11));
        Assert.True(route.IsComplete, string.Join(";", route.Diagnostics.Select(diagnostic => diagnostic.Code + ":" + diagnostic.Message)));
        Assert.Equal(1, route.OperationMetrics.UpwardEscapeCandidatesEvaluated);
        Assert.DoesNotContain(route.Cells.Zip(route.Cells.Skip(2), (a, b) => (a, b)), pair => pair.a == pair.b);
        Assert.DoesNotContain(route.Cells.Zip(route.Cells.Skip(2), (a, b) => (a, b)), pair => pair.a.Row == pair.b.Row && pair.a.Column == pair.b.Column);
    }

    [Fact]
    public void Blocked_upward_continuation_uses_plus_two_after_left_escape_is_blocked()
    {
        var nodes = new[] { PlacementNode("s", 7, 3), PlacementNode("t", 1, 3), PlacementNode("obstacle", 5, 1) };
        var route = Route(nodes, Link("l", "s", "t"), 9, 9, GeneralGrid(9, 9));
        Assert.True(route.IsComplete, string.Join(";", route.Diagnostics.Select(diagnostic => diagnostic.Code + ":" + diagnostic.Message)));
        Assert.Contains(route.Cells, cell => cell.Row == 5 && cell.Column == 3);
        Assert.DoesNotContain(route.Cells, cell => cell.Row == 5 && cell.Column == 1);
    }

    [Fact]
    public void Empty_node_rows_allow_vertical_passthrough_but_not_horizontal_or_bends()
    {
        var route = Route(new[] { PlacementNode("s", 1, 2), PlacementNode("t", 5, 2) }, Link("l", "s", "t"), 7, 7, GeneralGrid(7, 7));
        Assert.True(route.IsComplete);
        var emptyNodeCell = route.Cells.Single(cell => cell.Row == 3 && cell.Column == 2);
        Assert.Equal((3, 2), (emptyNodeCell.Row, emptyNodeCell.Column));
        var blocked = Route(new[] { PlacementNode("s", 1, 2), PlacementNode("t", 5, 2) }, Link("l", "s", "t"), 7, 7,
            Grid(7, 7, (3, 2, ArchitectureV7CellCapability.NodeAllowed), (2, 2, ArchitectureV7CellCapability.NodeAllowed)));
        Assert.True(blocked.IsComplete);
        Assert.DoesNotContain(blocked.Cells, cell => cell.Row == 3 && cell.Column != 2);
    }

    [Fact]
    public void Project_boundary_and_non_text_header_allow_straight_only_while_header_text_blocks()
    {
        var boundary = Route(new[] { PlacementNode("s", 1, 2), PlacementNode("t", 5, 2) }, Link("l", "s", "t"), 7, 7,
            Grid(7, 7, (2, 2, ArchitectureV7CellCapability.RoutingAllowed | ArchitectureV7CellCapability.ProjectBoundary | ArchitectureV7CellCapability.StraightPassthroughOnly)));
        Assert.True(boundary.IsComplete);
        var header = Route(new[] { PlacementNode("s", 1, 2), PlacementNode("t", 5, 2) }, Link("l", "s", "t"), 7, 7,
            Grid(7, 7, (2, 2, ArchitectureV7CellCapability.RoutingAllowed | ArchitectureV7CellCapability.HeaderBlocked)));
        Assert.False(header.IsComplete);
        Assert.Contains(header.Diagnostics, diagnostic => diagnostic.IsHardFailure);
    }

    [Fact]
    public void General_routing_allows_bends_and_other_routes_do_not_block_pathfinding()
    {
        var grid = GeneralGrid(9, 9);
        var projection = Projection(new[] { PhysicalNode("s", 1, 1), PhysicalNode("t", 7, 5), PhysicalNode("s2", 1, 1), PhysicalNode("t2", 7, 5) }, Link("a", "s", "t"), Link("b", "s2", "t2"));
        var freeze = Freeze(projection.PhysicalNodes.Select(node => PlacementNode(node.PhysicalNodeId.Substring("physical:".Length), node.PhysicalNodeId == "physical:s" || node.PhysicalNodeId == "physical:s2" ? 1 : 7, node.PhysicalNodeId == "physical:s" || node.PhysicalNodeId == "physical:s2" ? 1 : 5)).ToArray(), grid);
        var result = new ArchitectureV7LogicalRelationshipRoutingStage().Route(freeze, projection);
        Assert.All(result.Routes, route => Assert.True(route.IsComplete));
        Assert.Contains(result.Routes, route => route.Cells.Zip(route.Cells.Skip(1), (a, b) => a.Row != b.Row && a.Column != b.Column).Any(value => !value));
    }

    [Fact]
    public void Same_layer_and_cross_project_relationships_use_the_same_common_authority()
    {
        var projection = Projection(new[] { PhysicalNode("s", 3, 1, project: "p1"), PhysicalNode("t", 3, 7, project: "p2") }, Link("cross", "s", "t", "p1", "p2"));
        var result = new ArchitectureV7LogicalRelationshipRoutingStage().Route(Freeze(projection.PhysicalNodes.Select(node => PlacementNode(node.PhysicalNodeId.Substring("physical:".Length), 3, node.PhysicalNodeId == "physical:s" ? 1 : 7, project: node.ProjectId)).ToArray(), GeneralGrid(7, 9)), projection);
        var route = Assert.Single(result.Routes);
        Assert.True(route.IsComplete, string.Join(";", route.Diagnostics.Select(diagnostic => diagnostic.Code + ":" + diagnostic.Message)));
        Assert.Contains("same-authority", route.Provenance);
        Assert.Contains("cross-project=true", route.Provenance);
    }

    [Fact]
    public void Failed_relationship_remains_accounted_with_attempted_route_evidence()
    {
        var projection = Projection(new[] { PhysicalNode("s", 1, 1), PhysicalNode("t", 5, 1) }, Link("l", "s", "t"));
        var blocked = Grid(7, 5, (2, 1, ArchitectureV7CellCapability.HeaderBlocked), (3, 1, ArchitectureV7CellCapability.HeaderBlocked), (4, 1, ArchitectureV7CellCapability.HeaderBlocked));
        var result = new ArchitectureV7LogicalRelationshipRoutingStage().Route(Freeze(projection.PhysicalNodes.Select(node => PlacementNode(node.PhysicalNodeId.Substring("physical:".Length), node.PhysicalNodeId == "physical:s" ? 1 : 5, 1)).ToArray(), blocked), projection);
        var route = Assert.Single(result.Routes);
        Assert.False(route.IsComplete);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.IsHardFailure);
        Assert.NotEmpty(route.Diagnostics.Single().AttemptedCells);
    }

    [Fact]
    public void Routes_are_deterministic_under_shuffled_relationship_order_and_are_orthogonally_adjacent()
    {
        var links = new[] { Link("z", "s", "t"), Link("a", "s2", "t2") };
        var projectionLeft = Projection(new[] { PhysicalNode("s", 1, 1), PhysicalNode("t", 7, 5), PhysicalNode("s2", 1, 7), PhysicalNode("t2", 7, 1) }, links);
        var reversedLinks = links.ToArray();
        Array.Reverse(reversedLinks);
        var projectionRight = Projection(new[] { PhysicalNode("s", 1, 1), PhysicalNode("t", 7, 5), PhysicalNode("s2", 1, 7), PhysicalNode("t2", 7, 1) }, reversedLinks);
        var left = new ArchitectureV7LogicalRelationshipRoutingStage().Route(Freeze(projectionLeft.PhysicalNodes.Select(node => PlacementNode(node.PhysicalNodeId.Substring("physical:".Length), node.PhysicalNodeId.EndsWith("s") || node.PhysicalNodeId.EndsWith("s2") ? 1 : 7, node.PhysicalNodeId.EndsWith("s") ? 1 : node.PhysicalNodeId.EndsWith("s2") ? 7 : node.PhysicalNodeId.EndsWith("t") ? 5 : 1)).ToArray(), GeneralGrid(9, 9)), projectionLeft);
        var right = new ArchitectureV7LogicalRelationshipRoutingStage().Route(Freeze(projectionRight.PhysicalNodes.Select(node => PlacementNode(node.PhysicalNodeId.Substring("physical:".Length), node.PhysicalNodeId.EndsWith("s") || node.PhysicalNodeId.EndsWith("s2") ? 1 : 7, node.PhysicalNodeId.EndsWith("s") ? 1 : node.PhysicalNodeId.EndsWith("s2") ? 7 : node.PhysicalNodeId.EndsWith("t") ? 5 : 1)).ToArray(), GeneralGrid(9, 9)), projectionRight);
        Assert.Equal(left.RouteFingerprint, right.RouteFingerprint);
        Assert.All(left.Routes, route =>
        {
            Assert.All(route.Cells.Zip(route.Cells.Skip(1), (a, b) => Math.Abs(a.Row - b.Row) + Math.Abs(a.Column - b.Column)), distance => Assert.Equal(1, distance));
            Assert.DoesNotContain(route.Cells.Zip(route.Cells.Skip(2), (a, b) => (a, b)), pair => pair.a == pair.b);
            for (var index = 2; index < route.Cells.Count; index++)
                if (route.Cells[index - 2].Row == route.Cells[index - 1].Row && route.Cells[index - 1].Row == route.Cells[index].Row)
                    Assert.Equal(Math.Sign(route.Cells[index - 1].Column - route.Cells[index - 2].Column), Math.Sign(route.Cells[index].Column - route.Cells[index - 1].Column));
        });
    }

    [Fact]
    public async Task Parallel_route_completion_reduces_to_the_same_freeze()
    {
        var projection = Projection(new[] { PhysicalNode("s", 1, 1), PhysicalNode("t", 7, 5) }, Link("l", "s", "t"));
        var freezes = Enumerable.Range(0, 6).Select(_ => Freeze(projection.PhysicalNodes.Select(node => PlacementNode(node.PhysicalNodeId.Substring("physical:".Length), node.PhysicalNodeId == "physical:s" ? 1 : 7, node.PhysicalNodeId == "physical:s" ? 1 : 5)).ToArray(), GeneralGrid(9, 9))).ToArray();
        var results = await Task.WhenAll(freezes.Select(freeze => Task.Run(() => new ArchitectureV7LogicalRelationshipRoutingStage().Route(freeze, projection))));
        Assert.All(results, result => Assert.Equal(results[0].RouteFingerprint, result.RouteFingerprint));
    }

    private static ArchitectureV7LogicalRoute Route(ArchitectureV7FrozenNodePlacement[] nodes, ArchitectureV7PhysicalLink link, int rows, int columns, IReadOnlyList<ArchitectureV7LogicalCell> grid) =>
        Assert.Single(new ArchitectureV7LogicalRelationshipRoutingStage().Route(Freeze(nodes, grid), Projection(nodes.Select(node => PhysicalNode(node.PhysicalNodeId.Substring("physical:".Length), node.DiagramRow, node.CentreCell, node.LogicalSpan)).ToArray(), link)).Routes);

    private static ArchitectureV7PhysicalLink Link(string id, string source, string target, string sourceProject = "p", string targetProject = "p") =>
        new(id, id, "physical:" + source, "physical:" + target, sourceProject, targetProject, "dependency");

    private static ArchitectureV7PhysicalNode PhysicalNode(string id, int row, int column, int span = 1, string project = "p") =>
        new("physical:" + id, id, project, false, false, id, "P." + id, "Class", ArchitectureV7ProjectionMode.Canonical, null);

    private static ArchitectureV7FrozenNodePlacement PlacementNode(string id, int row, int column, int span = 1, string project = "p") =>
        new("physical:" + id, id, project, row, column, span, column + (span - 1) / 2, Enumerable.Range(column, span).Select(value => (row, value)).ToArray(), false, false, false, "tree", id, "P." + id, "test");

    private static ArchitectureV7PhysicalProjectionResult Projection(ArchitectureV7FrozenNodePlacement[] nodes, params ArchitectureV7PhysicalLink[] links) =>
        Projection(nodes.Select(node => PhysicalNode(node.PhysicalNodeId.Substring("physical:".Length), node.DiagramRow, node.CentreCell, node.LogicalSpan, node.ProjectId ?? "p")).ToArray(), links);

    private static ArchitectureV7PhysicalProjectionResult Projection(ArchitectureV7PhysicalNode[] nodes, params ArchitectureV7PhysicalLink[] links) =>
        new(nodes, links, new Dictionary<string, IReadOnlyList<string>>(), new Dictionary<string, IReadOnlyList<string>>(), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<ArchitectureV7ProjectionDiagnostic>(), "projection");

    private static ArchitectureV7PlacementFreeze Freeze(ArchitectureV7FrozenNodePlacement[] nodes, IReadOnlyList<ArchitectureV7LogicalCell> cells)
    {
        var occupied = cells.ToDictionary(cell => (cell.Row, cell.Column), cell => cell);
        foreach (var node in nodes)
            foreach (var footprint in node.LogicalFootprint)
                if (occupied.TryGetValue(footprint, out var cell)) occupied[footprint] = cell with { OccupantId = node.PhysicalNodeId };
        return new(nodes, Array.Empty<ArchitectureV7ProjectRegion>(), new ArchitectureV7ExternalRegion(0, Array.Empty<string>(), Array.Empty<ArchitectureV7FrozenNodePlacement>()), new ArchitectureV7StandaloneRegion(0, 0, 0, Array.Empty<string>(), Array.Empty<ArchitectureV7FrozenNodePlacement>()), new ArchitectureV7CommonDiagramGrid(cells.Max(cell => cell.Row) + 1, cells.Max(cell => cell.Column) + 1, occupied.Values.ToArray()), Array.Empty<ArchitectureV7ProjectTransform>(), "projection", "ownership", "sizing", "reservation", "placement");
    }

    private static IReadOnlyList<ArchitectureV7LogicalCell> GeneralGrid(int rows, int columns) => Grid(rows, columns);

    private static IReadOnlyList<ArchitectureV7LogicalCell> Grid(int rows, int columns, params (int Row, int Column, ArchitectureV7CellCapability Capability)[] overrides)
    {
        var map = overrides.ToDictionary(item => (item.Row, item.Column), item => item.Capability);
        return Enumerable.Range(0, rows).SelectMany(row => Enumerable.Range(0, columns).Select(column =>
            new ArchitectureV7LogicalCell(row, column, map.TryGetValue((row, column), out var capability) ? capability : ArchitectureV7CellCapability.RoutingAllowed | ArchitectureV7CellCapability.GeneralRouting))).ToArray();
    }
}
