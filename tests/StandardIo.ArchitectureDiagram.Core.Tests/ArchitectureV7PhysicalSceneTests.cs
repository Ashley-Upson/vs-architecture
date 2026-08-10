using System;
using System.Collections.Generic;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core.Models.ArchitectureV7;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.ArchitectureV7;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core.Tests;

public sealed class ArchitectureV7PhysicalSceneTests
{
    [Fact]
    public void Vertical_route_uses_constant_x_and_exact_node_edges()
    {
        var route = Route("a", "s", "t", (1, 1), (2, 1), (3, 1), (4, 1), (5, 1));
        var scene = Compile(new[] { route }, Nodes(("s", 1, 1), ("t", 5, 1)));
        var physical = Assert.Single(scene.Routes);
        Assert.All(physical.Segments, segment => Assert.True(segment.Start.X == segment.End.X || segment.Start.Y == segment.End.Y));
        Assert.All(physical.Points, point => Assert.Equal(physical.Points[0].X, point.X));
        Assert.Equal(scene.Nodes.Single(x => x.PhysicalNodeId == "s").Bounds.Bottom, physical.Points[0].Y);
        Assert.Equal(scene.Nodes.Single(x => x.PhysicalNodeId == "t").Bounds.Top, physical.Points[^1].Y);
        Assert.All(physical.Segments, segment => Assert.NotEmpty(segment.RunId));
    }

    [Fact]
    public void Overlapping_horizontal_routes_use_distinct_lanes_and_increase_row_extent()
    {
        var routes = new[]
        {
            Route("a", "s1", "t1", (3, 1), (3, 2), (3, 3), (3, 4), (3, 5)),
            Route("b", "s2", "t2", (3, 1), (3, 2), (3, 3), (3, 4), (3, 5))
        };
        var scene = Compile(routes, Nodes(("s1", 3, 1), ("t1", 3, 5), ("s2", 3, 1), ("t2", 3, 5)), spacing: 6);
        Assert.True(scene.Rows[3].RequiredExtent >= 6);
        Assert.Equal(2, scene.Routes.SelectMany(x => x.Segments).Select(x => x.LaneId).Distinct().Count());
        Assert.True(scene.Routes[0].Points.Any(point => scene.Routes[1].Points.Any(other => other.Y != point.Y)));
    }

    [Fact]
    public void Endpoint_lane_mismatch_materialises_allocated_orthogonal_handoff_without_diagonal_or_clamping()
    {
        var route = Route("a", "s", "t", (1, 1), (1, 2), (1, 3));
        var scene = Compile(new[] { route }, Nodes(("s", 1, 1), ("t", 1, 3)), spacing: 2);
        var physical = Assert.Single(scene.Routes);
        Assert.DoesNotContain(physical.Segments, segment => segment.Start.X != segment.End.X && segment.Start.Y != segment.End.Y);
        Assert.Contains(physical.Points, point => point.Provenance.Contains("frozen-handoff: a:SourceDeparture".Replace(" ", ""), StringComparison.Ordinal));
        Assert.Contains(physical.Points, point => point.Provenance.Contains("frozen-handoff:a:DestinationArrival", StringComparison.Ordinal));
        Assert.DoesNotContain(scene.Diagnostics, diagnostic => diagnostic.Code == "TERMINAL-CLAMPED");
        Assert.Equal(scene.Terminals.Single(x => x.EndpointKind == ArchitectureV7EndpointKind.SourceDeparture).Position, physical.Points[0]);
        Assert.Equal(scene.Terminals.Single(x => x.EndpointKind == ArchitectureV7EndpointKind.DestinationArrival).Position, physical.Points[physical.Points.Count - 1]);
    }

    [Fact]
    public void Clean_perpendicular_crossing_remains_straight_for_both_routes()
    {
        var routes = new[]
        {
            Route("h", "h1", "h2", (3, 1), (3, 2), (3, 3), (3, 4), (3, 5)),
            Route("v", "v1", "v2", (1, 3), (2, 3), (3, 3), (4, 3), (5, 3))
        };
        var scene = Compile(routes, Nodes(("h1", 3, 1), ("h2", 3, 5), ("v1", 1, 3), ("v2", 5, 3)));
        Assert.DoesNotContain(scene.Diagnostics, x => x.Code == "DIAGONAL-COMPILER-OUTPUT");
        Assert.All(scene.Routes, route => Assert.DoesNotContain(route.Points.Zip(route.Points.Skip(1), (a, b) => (a, b)), pair => pair.Item1.X != pair.Item2.X && pair.Item1.Y != pair.Item2.Y));
        Assert.Contains(scene.Routes.SelectMany(x => x.Points), point => point.X == scene.Routes[0].Points[2].X && point.Y == scene.Routes[1].Points[2].Y);
    }

    [Fact]
    public void Scene_preserves_all_three_freeze_fingerprints_and_unsimplified_route_cells()
    {
        var route = Route("a", "s", "t", (1, 1), (2, 1), (3, 1));
        var scene = Compile(new[] { route }, Nodes(("s", 1, 1), ("t", 3, 1)));
        Assert.Equal("placement", scene.PlacementFingerprint);
        Assert.Equal("routes", scene.RouteFingerprint);
        Assert.StartsWith("placement|routes|", scene.AllocationFingerprint, StringComparison.Ordinal);
        Assert.Equal(3, scene.Routes.Single().Points.Count);
        Assert.All(scene.Routes.SelectMany(x => x.Segments), segment =>
        {
            Assert.NotEmpty(segment.LogicalCells);
            Assert.NotEmpty(segment.RouteCellIndices);
            Assert.NotEmpty(segment.LaneId);
        });
    }

    [Fact]
    public void Physical_track_sizing_does_not_mutate_frozen_logical_dimensions()
    {
        var route = Route("a", "s", "t", (1, 1), (2, 1), (3, 1));
        var scene = Compile(new[] { route }, Nodes(("s", 1, 1), ("t", 3, 1)), spacing: 20);
        Assert.Equal(6, scene.Rows.Count);
        Assert.Equal(6, scene.Columns.Count);
        Assert.True(scene.Rows[2].RequiredExtent >= 20);
        Assert.Equal(new[] { (1, 1), (2, 1), (3, 1) }, route.Cells.Select(x => (x.Row, x.Column)));
    }

    [Fact]
    public void Bend_resource_demand_expands_only_affected_physical_tracks()
    {
        var route = Route("turn", "s", "t", (1, 1), (2, 1), (2, 3));
        var scene = CompileWithResourceClearance(new[] { route }, Nodes(("s", 1, 1), ("t", 2, 3)), resourceClearance: 30);

        Assert.True(scene.Rows[2].RequiredExtent >= 60);
        Assert.True(scene.Columns[1].RequiredExtent >= 60);
        Assert.Equal(new[] { (1, 1), (2, 1), (2, 3) }, route.Cells.Select(x => (x.Row, x.Column)));
        Assert.Equal("placement", scene.PlacementFingerprint);
        Assert.Equal("routes", scene.RouteFingerprint);
    }

    [Fact]
    public void Crossing_resource_demand_expands_only_affected_physical_tracks()
    {
        var routes = new[]
        {
            Route("h", "h1", "h2", (3, 1), (3, 2), (3, 3), (3, 4), (3, 5)),
            Route("v", "v1", "v2", (1, 3), (2, 3), (3, 3), (4, 3), (5, 3))
        };
        var scene = CompileWithResourceClearance(routes, Nodes(("h1", 3, 1), ("h2", 3, 5), ("v1", 1, 3), ("v2", 5, 3)), resourceClearance: 30);

        Assert.True(scene.Rows[3].RequiredExtent >= 60);
        Assert.True(scene.Columns[3].RequiredExtent >= 60);
        Assert.Equal(new[] { "h", "v" }, routes.Select(x => x.PhysicalLinkId).ToArray());
    }

    [Fact]
    public void Dense_lane_demand_expands_independent_row_and_column_tracks_without_allocation_failure()
    {
        var routes = new List<ArchitectureV7LogicalRoute>();
        var nodeSpecs = new List<(string Id, int Row, int Column)>();
        for (var horizontal = 0; horizontal < 15; horizontal++)
        {
            routes.Add(Route("h" + horizontal, "hs" + horizontal, "ht" + horizontal,
                (3, 0), (3, 1), (3, 2), (3, 3), (3, 4), (3, 5), (3, 6)));
            nodeSpecs.Add(("hs" + horizontal, 3, 0));
            nodeSpecs.Add(("ht" + horizontal, 3, 6));
        }
        for (var vertical = 0; vertical < 27; vertical++)
        {
            routes.Add(Route("v" + vertical, "vs" + vertical, "vt" + vertical,
                (0, 3), (1, 3), (2, 3), (3, 3), (4, 3), (5, 3), (6, 3)));
            nodeSpecs.Add(("vs" + vertical, 0, 3));
            nodeSpecs.Add(("vt" + vertical, 6, 3));
        }

        var placement = new ArchitectureV7PlacementFreeze(
            Nodes(nodeSpecs.ToArray()), Array.Empty<ArchitectureV7ProjectRegion>(),
            new ArchitectureV7ExternalRegion(0, Array.Empty<string>(), Array.Empty<ArchitectureV7FrozenNodePlacement>()),
            new ArchitectureV7StandaloneRegion(0, 0, 0, Array.Empty<string>(), Array.Empty<ArchitectureV7FrozenNodePlacement>()),
            new ArchitectureV7CommonDiagramGrid(7, 7, Array.Empty<ArchitectureV7LogicalCell>()), Array.Empty<ArchitectureV7ProjectTransform>(),
            "placement", "ownership", "sizing", "reservation", "placement");
        var routeFreeze = new ArchitectureV7LogicalRouteFreeze(routes, Array.Empty<ArchitectureV7RouteDiagnostic>(), "placement", "projection", "routes");
        var allocation = new ArchitectureV7CollectivePostRoutingAllocationStage().Allocate(placement, routeFreeze,
            new ArchitectureV7AllocationConfiguration(12, 25, 20, 100, 10));

        Assert.DoesNotContain(allocation.Diagnostics, diagnostic => diagnostic.IsHardFailure);
        Assert.Contains(allocation.TrackDemands, demand => demand.LogicalRow == 3 && demand.RequiredRowExtent >= 188);
        Assert.Contains(allocation.TrackDemands, demand => demand.LogicalColumn == 3 && demand.RequiredColumnExtent >= 332);

        var scene = new ArchitectureV7PhysicalSceneCompilationStage().Compile(placement, routeFreeze, allocation,
            new ArchitectureV7PhysicalSceneConfiguration(100, 20, 20, 10, 20, 34, 1, 20, 10, 10, 12, 25, 20));

        Assert.True(scene.Rows[3].RequiredExtent >= 188);
        Assert.True(scene.Columns[3].RequiredExtent >= 332);
    }

    [Fact]
    public void Endpoint_handoff_resource_demand_expands_its_authoritative_track()
    {
        var route = Route("a", "s", "t", (1, 1), (1, 2), (1, 3));
        var scene = CompileWithResourceClearance(new[] { route }, Nodes(("s", 1, 1), ("t", 1, 3)), resourceClearance: 30);

        Assert.True(scene.Rows[1].RequiredExtent >= 60);
        Assert.Equal("routes", scene.RouteFingerprint);
    }

    [Fact]
    public void Allocated_bend_is_materialised_at_frozen_resource_position_with_provenance()
    {
        var route = Route("turn", "s", "t", (1, 1), (2, 1), (2, 3));
        var scene = Compile(new[] { route }, Nodes(("s", 1, 1), ("t", 2, 3)), spacing: 4);
        var physical = Assert.Single(scene.Routes);

        Assert.Contains(physical.Points, point => point.Provenance.Contains("bend-resource=bend:turn:1", StringComparison.Ordinal));
        Assert.DoesNotContain(scene.Diagnostics, diagnostic => diagnostic.Code == "BEND-RESOURCE-MISSING");
        Assert.All(physical.Segments, segment => Assert.True(segment.Start.X == segment.End.X || segment.Start.Y == segment.End.Y));
    }

    [Fact]
    public void F8_bend_uses_the_single_frozen_lane_intersection()
    {
        var route = Route("f8-bend", "s", "t", (1, 1), (2, 1), (2, 3));
        var scene = Compile(new[] { route }, Nodes(("s", 1, 1), ("t", 2, 3)), spacing: 4);
        var physical = Assert.Single(scene.Routes);
        var bend = Assert.Single(physical.Points.Where(point => point.Provenance.Contains("bend-resource=", StringComparison.Ordinal)));

        Assert.Equal((scene.Columns[1].Start + scene.Columns[1].End) / 2d, bend.X);
        Assert.Equal((scene.Rows[2].Start + scene.Rows[2].End) / 2d, bend.Y);
        Assert.DoesNotContain(scene.Diagnostics, diagnostic => diagnostic.Code == "DIAGONAL-COMPILER-OUTPUT");
        Assert.All(physical.Segments, segment => Assert.True(segment.Start.X == segment.End.X || segment.Start.Y == segment.End.Y));
    }

    [Fact]
    public void Allocated_crossing_is_materialised_at_frozen_resource_with_provenance()
    {
        var routes = new[]
        {
            Route("h", "h1", "h2", (3, 1), (3, 2), (3, 3), (3, 4), (3, 5)),
            Route("v", "v1", "v2", (1, 3), (2, 3), (3, 3), (4, 3), (5, 3))
        };
        var scene = Compile(routes, Nodes(("h1", 3, 1), ("h2", 3, 5), ("v1", 1, 3), ("v2", 5, 3)));

        Assert.Contains(scene.Routes.SelectMany(route => route.Points), point => point.Provenance.Contains("crossing-resource=", StringComparison.Ordinal));
        Assert.DoesNotContain(scene.Diagnostics, diagnostic => diagnostic.Code == "CROSSING-RESOURCE-MISSING");
    }

    [Fact]
    public void F8_clean_crossing_uses_the_existing_horizontal_vertical_lane_intersection()
    {
        var routes = new[]
        {
            Route("h8", "h1", "h2", (3, 1), (3, 2), (3, 3), (3, 4), (3, 5)),
            Route("v8", "v1", "v2", (1, 3), (2, 3), (3, 3), (4, 3), (5, 3))
        };
        var scene = Compile(routes, Nodes(("h1", 3, 1), ("h2", 3, 5), ("v1", 1, 3), ("v2", 5, 3)));
        var crossingPoints = scene.Routes.SelectMany(route => route.Points)
            .Where(point => point.Provenance.Contains("crossing-resource=", StringComparison.Ordinal)).ToArray();
        var expectedX = (scene.Columns[3].Start + scene.Columns[3].End) / 2d;
        var expectedY = (scene.Rows[3].Start + scene.Rows[3].End) / 2d;

        Assert.NotEmpty(crossingPoints);
        Assert.All(crossingPoints, point => { Assert.Equal(expectedX, point.X); Assert.Equal(expectedY, point.Y); });
        Assert.DoesNotContain(scene.Diagnostics, diagnostic => diagnostic.Code == "DIAGONAL-COMPILER-OUTPUT");
    }

    [Fact]
    public void Missing_required_handoff_is_an_explicit_compiler_failure_without_synthesis()
    {
        var route = Route("a", "s", "t", (1, 1), (1, 2), (1, 3));
        var placement = Placement(Nodes(("s", 1, 1), ("t", 1, 3)));
        var routeFreeze = new ArchitectureV7LogicalRouteFreeze(new[] { route }, Array.Empty<ArchitectureV7RouteDiagnostic>(), "placement", "projection", "routes");
        var allocated = new ArchitectureV7CollectivePostRoutingAllocationStage().Allocate(placement, routeFreeze, new ArchitectureV7AllocationConfiguration(2, 2, 0));
        var missing = WithoutResources(allocated, handoffs: true);
        var scene = new ArchitectureV7PhysicalSceneCompilationStage().Compile(placement, routeFreeze, missing,
            new ArchitectureV7PhysicalSceneConfiguration(10, 20, 20, 10, 20, 20, 1, 0, 2, 1, 2, 2, 0));

        Assert.Contains(scene.Diagnostics, diagnostic => diagnostic.Code == "ENDPOINT-HANDOFF-MISSING");
        Assert.Empty(scene.Routes);
    }

    [Fact]
    public void Missing_required_bend_is_an_explicit_compiler_failure_without_synthesised_turn()
    {
        var route = Route("turn", "s", "t", (1, 1), (2, 1), (2, 3));
        var placement = Placement(Nodes(("s", 1, 1), ("t", 2, 3)));
        var routeFreeze = new ArchitectureV7LogicalRouteFreeze(new[] { route }, Array.Empty<ArchitectureV7RouteDiagnostic>(), "placement", "projection", "routes");
        var allocated = new ArchitectureV7CollectivePostRoutingAllocationStage().Allocate(placement, routeFreeze, new ArchitectureV7AllocationConfiguration(4, 4, 0));
        var missing = WithoutResources(allocated, bends: true);
        var scene = new ArchitectureV7PhysicalSceneCompilationStage().Compile(placement, routeFreeze, missing,
            new ArchitectureV7PhysicalSceneConfiguration(10, 20, 20, 10, 20, 20, 1, 0, 2, 1, 4, 4, 0));

        Assert.Contains(scene.Diagnostics, diagnostic => diagnostic.Code == "BEND-RESOURCE-MISSING");
        Assert.Empty(scene.Routes);
    }

    [Fact]
    public void Missing_required_crossing_is_an_explicit_compiler_failure_without_synthesised_intersection()
    {
        var routes = new[]
        {
            Route("h", "h1", "h2", (3, 1), (3, 2), (3, 3), (3, 4), (3, 5)),
            Route("v", "v1", "v2", (1, 3), (2, 3), (3, 3), (4, 3), (5, 3))
        };
        var placement = Placement(Nodes(("h1", 3, 1), ("h2", 3, 5), ("v1", 1, 3), ("v2", 5, 3)));
        var routeFreeze = new ArchitectureV7LogicalRouteFreeze(routes, Array.Empty<ArchitectureV7RouteDiagnostic>(), "placement", "projection", "routes");
        var allocated = new ArchitectureV7CollectivePostRoutingAllocationStage().Allocate(placement, routeFreeze, new ArchitectureV7AllocationConfiguration(4, 4, 0));
        var missing = WithoutResources(allocated, crossings: true);
        var scene = new ArchitectureV7PhysicalSceneCompilationStage().Compile(placement, routeFreeze, missing,
            new ArchitectureV7PhysicalSceneConfiguration(10, 20, 20, 10, 20, 20, 1, 0, 2, 1, 4, 4, 0));

        Assert.Contains(scene.Diagnostics, diagnostic => diagnostic.Code == "CROSSING-RESOURCE-MISSING");
        Assert.Empty(scene.Routes);
    }

    [Fact]
    public void Physical_node_width_preserves_the_frozen_pre_routing_requirement()
    {
        var node = new ArchitectureV7FrozenNodePlacement("wide", "wide", "p", 1, 0, 9, 4,
            Enumerable.Range(0, 9).Select(column => (1, column)).ToArray(), false, false, false, "tree",
            "ContentManagementMigrationAggregationService : IContentManagementMigrationAggregationService", "wide", "test");
        var placement = new ArchitectureV7PlacementFreeze(
            new[] { node }, Array.Empty<ArchitectureV7ProjectRegion>(),
            new ArchitectureV7ExternalRegion(0, Array.Empty<string>(), Array.Empty<ArchitectureV7FrozenNodePlacement>()),
            new ArchitectureV7StandaloneRegion(0, 0, 0, Array.Empty<string>(), Array.Empty<ArchitectureV7FrozenNodePlacement>()),
            new ArchitectureV7CommonDiagramGrid(3, 9, Enumerable.Range(0, 9).Select(column =>
                new ArchitectureV7LogicalCell(1, column, ArchitectureV7CellCapability.NodeAllowed, "wide")).ToArray()),
            Array.Empty<ArchitectureV7ProjectTransform>(), "projection", "ownership", "sizing", "reservation", "placement");
        var routes = new ArchitectureV7LogicalRouteFreeze(Array.Empty<ArchitectureV7LogicalRoute>(), Array.Empty<ArchitectureV7RouteDiagnostic>(), "placement", "projection", "routes");
        var allocation = new ArchitectureV7CollectiveAllocationFreeze(Array.Empty<ArchitectureV7StraightRun>(), Array.Empty<ArchitectureV7PhysicalLane>(),
            Array.Empty<ArchitectureV7RunLaneAssignment>(), Array.Empty<ArchitectureV7TerminalSlotAssignment>(), Array.Empty<ArchitectureV7EndpointApproachReservation>(),
            Array.Empty<ArchitectureV7EndpointHandoff>(), Array.Empty<ArchitectureV7BendAllocation>(), Array.Empty<ArchitectureV7CrossingAllocation>(),
            Array.Empty<ArchitectureV7AllocationDiagnostic>(), "placement", "routes", "allocation");
        var configuration = new ArchitectureV7PhysicalSceneConfiguration(100, 20, 20, 200, 80, 34, 8, 20, 10, 10, 12, 25, 20);
        var scene = new ArchitectureV7PhysicalSceneCompilationStage().Compile(placement, routes, allocation, configuration,
            new Dictionary<string, int> { ["wide"] = 756 });

        var bounds = Assert.Single(scene.Nodes).Bounds;
        Assert.Equal(900, bounds.Right - bounds.Left);
        Assert.True(bounds.Right - bounds.Left >= 756);
    }

    [Fact]
    public void F8A_multi_span_endpoint_gets_a_frozen_handoff_when_shared_columns_shift_its_physical_centre()
    {
        var source = new ArchitectureV7FrozenNodePlacement("wide-source", "wide-source", "p", 1, 0, 3, 1,
            new[] { (1, 0), (1, 1), (1, 2) }, false, false, false, "tree", "wide-source", "wide-source", "test");
        var target = new ArchitectureV7FrozenNodePlacement("target", "target", "p", 3, 1, 1, 1,
            new[] { (3, 1) }, false, false, false, "tree", "target", "target", "test");
        var unrelated = new ArchitectureV7FrozenNodePlacement("wide-left", "wide-left", "p", 0, 0, 1, 0,
            new[] { (0, 0) }, false, false, false, "tree", "wide-left", "wide-left", "test");
        var placement = new ArchitectureV7PlacementFreeze(
            new[] { source, target, unrelated }, Array.Empty<ArchitectureV7ProjectRegion>(),
            new ArchitectureV7ExternalRegion(0, Array.Empty<string>(), Array.Empty<ArchitectureV7FrozenNodePlacement>()),
            new ArchitectureV7StandaloneRegion(0, 0, 0, Array.Empty<string>(), Array.Empty<ArchitectureV7FrozenNodePlacement>()),
            new ArchitectureV7CommonDiagramGrid(4, 4, Array.Empty<ArchitectureV7LogicalCell>()), Array.Empty<ArchitectureV7ProjectTransform>(),
            "projection", "ownership", "sizing", "reservation", "placement");
        var route = Route("f8a-wide", "wide-source", "target", (1, 1), (2, 1), (3, 1));
        var routeFreeze = new ArchitectureV7LogicalRouteFreeze(new[] { route }, Array.Empty<ArchitectureV7RouteDiagnostic>(), "placement", "projection", "routes");
        var allocation = new ArchitectureV7CollectivePostRoutingAllocationStage().Allocate(placement, routeFreeze,
            new ArchitectureV7AllocationConfiguration(4, 4, 0));
        var scene = new ArchitectureV7PhysicalSceneCompilationStage().Compile(placement, routeFreeze, allocation,
            new ArchitectureV7PhysicalSceneConfiguration(100, 20, 20, 200, 80, 34, 8, 20, 10, 10, 4, 4, 0),
            new Dictionary<string, int> { ["wide-source"] = 350, ["wide-left"] = 500, ["target"] = 100 });

        Assert.Contains(allocation.Handoffs, handoff => handoff.PhysicalLinkId == "f8a-wide" && handoff.EndpointKind == ArchitectureV7EndpointKind.SourceDeparture);
        Assert.Single(scene.Routes);
        Assert.DoesNotContain(scene.Diagnostics, diagnostic => diagnostic.Code == "ENDPOINT-HANDOFF-MISSING");
        Assert.DoesNotContain(scene.Diagnostics, diagnostic => diagnostic.Code == "DIAGONAL-COMPILER-OUTPUT");
        Assert.All(scene.Routes.Single().Segments, segment => Assert.True(segment.Start.X == segment.End.X || segment.Start.Y == segment.End.Y));
    }

    private static ArchitectureV7PhysicalSceneFreeze Compile(IReadOnlyList<ArchitectureV7LogicalRoute> routes,
        IReadOnlyList<ArchitectureV7FrozenNodePlacement> nodes, double spacing = 4)
    {
        var placement = Placement(nodes);
        var routeFreeze = new ArchitectureV7LogicalRouteFreeze(routes, Array.Empty<ArchitectureV7RouteDiagnostic>(), "placement", "projection", "routes");
        var allocation = new ArchitectureV7CollectivePostRoutingAllocationStage().Allocate(placement, routeFreeze,
            new ArchitectureV7AllocationConfiguration((int)spacing, (int)spacing, 0));
        var scene = new ArchitectureV7PhysicalSceneCompilationStage().Compile(placement, routeFreeze, allocation,
            new ArchitectureV7PhysicalSceneConfiguration(10, 20, 20, 10, 20, 20, 1, 0, 2, 1, spacing, spacing, 0));
        return scene;
    }

    private static ArchitectureV7PlacementFreeze Placement(IReadOnlyList<ArchitectureV7FrozenNodePlacement> nodes) =>
        new(nodes, Array.Empty<ArchitectureV7ProjectRegion>(),
            new ArchitectureV7ExternalRegion(0, Array.Empty<string>(), Array.Empty<ArchitectureV7FrozenNodePlacement>()),
            new ArchitectureV7StandaloneRegion(0, 0, 0, Array.Empty<string>(), Array.Empty<ArchitectureV7FrozenNodePlacement>()),
            new ArchitectureV7CommonDiagramGrid(6, 6, Array.Empty<ArchitectureV7LogicalCell>()), Array.Empty<ArchitectureV7ProjectTransform>(),
            "projection", "ownership", "sizing", "reservation", "placement");

    private static ArchitectureV7CollectiveAllocationFreeze WithoutResources(ArchitectureV7CollectiveAllocationFreeze allocation,
        bool handoffs = false, bool bends = false, bool crossings = false)
    {
        var interactionEvidence = allocation.CrossingInteractions.Count != 0
            ? allocation.CrossingInteractions
            : allocation.Crossings.Select(crossing => new ArchitectureV7CrossingInteraction(
                "test-interaction-" + crossing.CrossingId, crossing.Cell, crossing.Classification,
                crossing.HorizontalPhysicalLinkId, crossing.VerticalPhysicalLinkId, crossing.HorizontalRunId, crossing.VerticalRunId,
                crossing.HorizontalLaneId, crossing.VerticalLaneId, crossing.HorizontalRouteIndex, crossing.VerticalRouteIndex,
                "test-frozen-crossing-evidence", crossing.CrossingId)).ToArray();
        return new(
            allocation.Runs, allocation.Lanes, allocation.RunAssignments, allocation.Terminals, allocation.Approaches,
            handoffs ? Array.Empty<ArchitectureV7EndpointHandoff>() : allocation.Handoffs,
            bends ? Array.Empty<ArchitectureV7BendAllocation>() : allocation.Bends,
            crossings ? Array.Empty<ArchitectureV7CrossingAllocation>() : allocation.Crossings,
            allocation.Diagnostics, allocation.PlacementFingerprint, allocation.RouteFingerprint, allocation.AllocationFingerprint,
            interactionEvidence);
    }

    private static ArchitectureV7PhysicalSceneFreeze CompileWithResourceClearance(IReadOnlyList<ArchitectureV7LogicalRoute> routes,
        IReadOnlyList<ArchitectureV7FrozenNodePlacement> nodes, int resourceClearance)
    {
        var placement = new ArchitectureV7PlacementFreeze(nodes, Array.Empty<ArchitectureV7ProjectRegion>(),
            new ArchitectureV7ExternalRegion(0, Array.Empty<string>(), Array.Empty<ArchitectureV7FrozenNodePlacement>()),
            new ArchitectureV7StandaloneRegion(0, 0, 0, Array.Empty<string>(), Array.Empty<ArchitectureV7FrozenNodePlacement>()),
            new ArchitectureV7CommonDiagramGrid(6, 6, Array.Empty<ArchitectureV7LogicalCell>()), Array.Empty<ArchitectureV7ProjectTransform>(),
            "projection", "ownership", "sizing", "reservation", "placement");
        var routeFreeze = new ArchitectureV7LogicalRouteFreeze(routes, Array.Empty<ArchitectureV7RouteDiagnostic>(), "placement", "projection", "routes");
        var allocation = new ArchitectureV7CollectivePostRoutingAllocationStage().Allocate(placement, routeFreeze,
            new ArchitectureV7AllocationConfiguration(4, 4, 0, 100, resourceClearance));
        return new ArchitectureV7PhysicalSceneCompilationStage().Compile(placement, routeFreeze, allocation,
            new ArchitectureV7PhysicalSceneConfiguration(10, 20, 20, 10, 20, 20, 1, 0, 2, 1, 4, 4, 0));
    }

    private static ArchitectureV7FrozenNodePlacement[] Nodes(params (string Id, int Row, int Column)[] nodes) => nodes.Select(node =>
        new ArchitectureV7FrozenNodePlacement(node.Id, node.Id, "p", node.Row, node.Column, 1, node.Column,
            new[] { (node.Row, node.Column) }, false, false, false, "tree", node.Id, node.Id, "test")).ToArray();

    private static ArchitectureV7LogicalRoute Route(string id, string source, string destination, params (int Row, int Column)[] cells) =>
        new(id, id, source, destination, cells.Select(x => new ArchitectureV7RouteCell(x.Row, x.Column)).ToArray(), true,
            Array.Empty<ArchitectureV7RouteDiagnostic>(), "test");
}
