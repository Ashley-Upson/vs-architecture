using System;
using System.Collections.Generic;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core.Models.ArchitectureV7;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.ArchitectureV7;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core.Tests;

public sealed class ArchitectureV7CollectiveAllocationTests
{
    [Fact]
    public void Maximal_runs_get_stable_lanes_and_do_not_share_overlapping_intervals()
    {
        var routes = new[] { Route("b", "s", "t", (1, 1), (1, 4), (1, 6)), Route("a", "u", "v", (3, 1), (3, 4), (3, 6)) };
        var result = Allocate(routes, Nodes("s", "t", "u", "v"));
        Assert.Equal(2, result.Runs.Count);
        Assert.Equal(2, result.Lanes.Count);
        Assert.Equal("run:a:0", result.Runs[0].RunId);
        var reversedRoutes = routes.ToList();
        reversedRoutes.Reverse();
        var reversed = Allocate(reversedRoutes, Nodes("s", "t", "u", "v"));
        Assert.Equal(result.AllocationFingerprint, reversed.AllocationFingerprint);
    }

    [Fact]
    public void Non_overlapping_runs_can_reuse_a_lane_and_spacing_is_preserved_as_demand()
    {
        var routes = new[] { Route("a", "s", "t", (1, 1), (1, 2)), Route("b", "u", "v", (1, 5), (1, 6)) };
        var result = Allocate(routes, Nodes("s", "t", "u", "v"), spacing: 7);
        Assert.Single(result.Lanes);
        Assert.Equal(7, result.Lanes[0].ParallelLaneSpacing);
    }

    [Fact]
    public void Fan_out_terminals_are_symmetric_and_have_one_assignment_per_endpoint()
    {
        var routes = new[] { Route("a", "s", "t", (1, 3), (2, 3), (3, 1), (4, 1), (5, 1)), Route("b", "s", "u", (1, 3), (2, 3), (3, 5), (4, 5), (5, 5)) };
        var result = Allocate(routes, Nodes("s", "t", "u"), spacing: 2);
        var source = result.Terminals.Where(x => x.PhysicalNodeId == "s").OrderBy(x => x.SlotOrdinal).ToArray();
        Assert.Equal(2, source.Length);
        Assert.Equal(-1, source[0].RelativeOffset);
        Assert.Equal(1, source[1].RelativeOffset);
        Assert.Equal(4, result.Approaches.Count);
        Assert.Equal(2, result.Handoffs.Count);
    }

    [Fact]
    public void Top_and_bottom_terminal_capacity_is_independent_and_overflow_is_hard_failure()
    {
        var routes = new[] { Route("a", "s", "t", (1, 2), (2, 2), (3, 1)), Route("b", "u", "t", (1, 4), (2, 4), (3, 1)) };
        var result = Allocate(routes, Nodes("s", "u", "t"), spacing: 2, span: 1, baseCellWidth: 1);
        Assert.Contains(result.Diagnostics, x => x.Code == "TERMINAL-OVERFLOW" && x.IsHardFailure);
        var overflow = Assert.Single(result.Diagnostics.Where(x => x.Code == "TERMINAL-OVERFLOW"));
        Assert.Equal("t", overflow.PhysicalNodeId);
        Assert.Equal(2, overflow.RequiredWidth);
        Assert.Equal(1, overflow.AvailableWidth);
        Assert.Equal(2, result.Terminals.Count(x => x.PhysicalNodeId == "t"));
        Assert.DoesNotContain(result.Diagnostics, x => x.Code == "TOTAL-DEGREE-CAPACITY");
    }

    [Fact]
    public void Handoff_uses_only_authoritative_endpoint_adjacent_cells()
    {
        var route = Route("a", "s", "t", (1, 3), (2, 3), (3, 5), (4, 5));
        var result = Allocate(new[] { route }, Nodes("s", "t"), spacing: 0);
        Assert.Empty(result.Handoffs);
        Assert.All(result.Runs, run => Assert.Equal(run.Cells.Count, run.EndRouteIndex - run.StartRouteIndex + 1));
    }

    [Fact]
    public void Endpoint_handoff_resource_contains_frozen_run_lane_offsets_and_route_indices()
    {
        var routes = new[]
        {
            Route("a", "s", "t", (1, 3), (2, 3), (3, 1)),
            Route("b", "s", "u", (1, 3), (2, 3), (3, 5))
        };

        var result = Allocate(routes, Nodes("s", "t", "u"), spacing: 4);

        Assert.NotEmpty(result.Handoffs);
        Assert.All(result.Handoffs, handoff =>
        {
            Assert.StartsWith("handoff:", handoff.ResourceId, StringComparison.Ordinal);
            Assert.NotEmpty(handoff.AdjacentRunId);
            Assert.NotEmpty(handoff.AdjacentLaneId);
            Assert.NotNull(handoff.LogicalCell);
            Assert.Equal(2, handoff.AuthoritativeCells.Count);
            Assert.Equal(Math.Abs(handoff.RelativePhysicalOffset), handoff.RequiredClearance);
            Assert.NotEqual(handoff.TerminalAxisOffset, handoff.LaneAxisOffset);
            Assert.True(handoff.StartRouteIndex >= 0);
            Assert.True(handoff.EndRouteIndex > handoff.StartRouteIndex);
        });
    }

    [Fact]
    public void Endpoint_handoff_resource_order_is_deterministic_under_route_shuffle()
    {
        var routes = new[]
        {
            Route("b", "s", "u", (1, 3), (2, 3), (3, 5)),
            Route("a", "s", "t", (1, 3), (2, 3), (3, 1))
        };

        var result = Allocate(routes, Nodes("s", "t", "u"), spacing: 4);
        var reversed = Allocate(routes.AsEnumerable().Reverse().ToArray(), Nodes("s", "t", "u"), spacing: 4);

        Assert.Equal(result.AllocationFingerprint, reversed.AllocationFingerprint);
        Assert.Equal(result.Handoffs.Select(x => x.ResourceId), reversed.Handoffs.Select(x => x.ResourceId));
    }

    [Fact]
    public void Allocation_preserves_frozen_placement_and_route_fingerprints()
    {
        var routes = new[] { Route("a", "s", "t", (1, 1), (2, 1), (3, 1)) };
        var result = Allocate(routes, Nodes("s", "t"));

        Assert.Equal("placement", result.PlacementFingerprint);
        Assert.Equal("routes", result.RouteFingerprint);
    }

    [Fact]
    public void Bends_are_transitions_between_runs_and_perpendicular_crossings_are_clean()
    {
        var routes = new[]
        {
            Route("h", "h1", "h2", (3, 1), (3, 2), (3, 3), (3, 4), (3, 5)),
            Route("v", "v1", "v2", (1, 3), (2, 3), (3, 3), (4, 3), (5, 3))
        };
        var result = Allocate(routes, Nodes("h1", "h2", "v1", "v2"));
        Assert.Empty(result.Bends);
        Assert.Single(result.Crossings);
        Assert.True(result.IsComplete);
    }

    [Fact]
    public void Shared_bend_and_crossing_turn_get_distinct_post_route_resources_without_rerouting()
    {
        var routes = new[]
        {
            Route("a", "a1", "a2", (1, 2), (2, 2), (2, 3)),
            Route("b", "b1", "b2", (2, 1), (2, 2), (3, 2)),
            Route("h", "h1", "h2", (2, 0), (2, 1), (2, 2), (2, 3)),
            Route("v", "v1", "v2", (1, 2), (2, 2), (3, 2))
        };
        var result = Allocate(routes, Nodes("a1", "a2", "b1", "b2", "h1", "h2", "v1", "v2"));
        Assert.DoesNotContain(result.Diagnostics, x => x.Code == "SHARED-BEND-CONFLICT");
        Assert.DoesNotContain(result.Diagnostics, x => x.Code == "CROSSING-TURN-CONFLICT");
        Assert.NotEmpty(result.Bends);
        Assert.NotEmpty(result.Crossings);
        Assert.Equal(result.Bends.Count, result.Bends.Select(x => x.BendId).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(result.Crossings.Count, result.Crossings.Select(x => x.CrossingId).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal("routes", result.RouteFingerprint);
    }

    [Fact]
    public void Single_turn_allocates_frozen_incoming_and_outgoing_lane_resource()
    {
        var result = Allocate(new[] { Route("turn", "s", "t", (1, 1), (2, 1), (2, 3)) }, Nodes("s", "t"));
        var bend = Assert.Single(result.Bends);

        Assert.Equal("turn", bend.PhysicalLinkId);
        Assert.NotEmpty(bend.IncomingRunId);
        Assert.NotEmpty(bend.OutgoingRunId);
        Assert.NotEmpty(bend.IncomingLaneId);
        Assert.NotEmpty(bend.OutgoingLaneId);
        Assert.Equal(new ArchitectureV7RouteCell(2, 1), bend.Cell);
        Assert.NotNull(bend.EffectiveRelativePosition);
        Assert.DoesNotContain(result.Diagnostics, x => x.Code == "BEND-RESOURCE-MISSING");
    }

    [Fact]
    public void Bend_envelope_demand_is_frozen_without_rejecting_for_provisional_cell_width()
    {
        var routes = new[]
        {
            Route("a", "a1", "a2", (1, 1), (2, 1), (2, 2)),
            Route("b", "b1", "b2", (2, 0), (2, 1), (3, 1))
        };
        var result = Allocate(routes, Nodes("a1", "a2", "b1", "b2"), spacing: 10, baseCellWidth: 1);
        Assert.DoesNotContain(result.Diagnostics, x => x.Code == "BEND-SLOT-CAPACITY-EXCEEDED");
        Assert.Contains(result.TrackDemands, demand => demand.ResourceIds.Any(id => id.StartsWith("bend:", StringComparison.Ordinal)));
    }

    [Fact]
    public void Clean_crossing_allocates_horizontal_and_vertical_run_lane_resource()
    {
        var routes = new[]
        {
            Route("h", "h1", "h2", (3, 1), (3, 2), (3, 3), (3, 4), (3, 5)),
            Route("v", "v1", "v2", (1, 3), (2, 3), (3, 3), (4, 3), (5, 3))
        };
        var result = Allocate(routes, Nodes("h1", "h2", "v1", "v2"));
        var crossing = Assert.Single(result.Crossings);

        Assert.Equal("h", crossing.HorizontalPhysicalLinkId);
        Assert.Equal("v", crossing.VerticalPhysicalLinkId);
        Assert.NotEmpty(crossing.HorizontalRunId);
        Assert.NotEmpty(crossing.VerticalRunId);
        Assert.NotEmpty(crossing.HorizontalLaneId);
        Assert.NotEmpty(crossing.VerticalLaneId);
        Assert.Equal("clean-crossing", crossing.Classification);
        var interaction = Assert.Single(result.CrossingInteractions);
        Assert.Equal("clean-crossing", interaction.Classification);
        Assert.Equal(crossing.CrossingId, interaction.ResourceId);
        Assert.Empty(interaction.BendResourceId);
        Assert.Single(result.CrossingResources);
        Assert.DoesNotContain(result.Diagnostics, x => x.Code == "CROSSING-TURN-CONFLICT");
    }

    [Fact]
    public void Turn_pass_interaction_references_the_existing_bend_resource()
    {
        var routes = new[]
        {
            Route("turn", "s", "t", (1, 1), (2, 1), (2, 3)),
            Route("pass", "p", "q", (2, 0), (2, 1), (2, 2), (2, 3))
        };
        var result = Allocate(routes, Nodes("s", "t", "p", "q"));
        var interaction = Assert.Single(result.CrossingInteractions);

        Assert.Equal("turn-pass", interaction.Classification);
        Assert.NotEmpty(interaction.BendResourceId);
        Assert.Contains(result.Bends, bend => bend.BendId == interaction.BendResourceId);
        Assert.Single(result.CrossingResources);
    }

    [Fact]
    public void Crossing_interaction_and_resource_order_is_deterministic_under_route_shuffle()
    {
        var routes = new[]
        {
            Route("v", "v1", "v2", (1, 3), (2, 3), (3, 3), (4, 3), (5, 3)),
            Route("h", "h1", "h2", (3, 1), (3, 2), (3, 3), (3, 4), (3, 5))
        };
        var result = Allocate(routes, Nodes("h1", "h2", "v1", "v2"));
        var reversed = Allocate(routes.AsEnumerable().Reverse().ToArray(), Nodes("h1", "h2", "v1", "v2"));

        Assert.Equal(result.RouteFingerprint, reversed.RouteFingerprint);
        Assert.Equal(result.CrossingInteractions, reversed.CrossingInteractions);
        Assert.Equal(result.CrossingResources.Select(resource => resource.ResourceId), reversed.CrossingResources.Select(resource => resource.ResourceId));
        Assert.Equal(result.CrossingResources.SelectMany(resource => resource.InteractionIds), reversed.CrossingResources.SelectMany(resource => resource.InteractionIds));
        Assert.Equal(result.AllocationFingerprint, reversed.AllocationFingerprint);
    }

    [Fact]
    public void Physical_crossing_resource_groups_interactions_by_geometry_not_relationship_identity()
    {
        var cell = new ArchitectureV7RouteCell(3, 3);
        var crossings = new[]
        {
            new ArchitectureV7CrossingAllocation("crossing:a", cell, "h:a", "v:a", "test", "run:h:a", "run:v:a", "lane:H:3:0", "lane:V:3:0",
                0, 0, "clean-crossing", 3, 3, new ArchitectureV7PhysicalRelativePosition(0, 0), new[] { "interaction:a" }),
            new ArchitectureV7CrossingAllocation("crossing:b", cell, "h:b", "v:b", "test", "run:h:b", "run:v:b", "lane:H:3:0", "lane:V:3:0",
                0, 0, "clean-crossing", 3, 3, new ArchitectureV7PhysicalRelativePosition(0, 0), new[] { "interaction:b" })
        };

        var freeze = new ArchitectureV7CollectiveAllocationFreeze(
            Array.Empty<ArchitectureV7StraightRun>(), Array.Empty<ArchitectureV7PhysicalLane>(), Array.Empty<ArchitectureV7RunLaneAssignment>(),
            Array.Empty<ArchitectureV7TerminalSlotAssignment>(), Array.Empty<ArchitectureV7EndpointApproachReservation>(), Array.Empty<ArchitectureV7EndpointHandoff>(),
            Array.Empty<ArchitectureV7BendAllocation>(), crossings, Array.Empty<ArchitectureV7AllocationDiagnostic>(), "placement", "routes", "allocation",
            Array.Empty<ArchitectureV7CrossingInteraction>());

        var resource = Assert.Single(freeze.CrossingResources);
        Assert.Equal(new[] { "interaction:a", "interaction:b" }, resource.InteractionIds);
    }

    [Fact]
    public void Clean_crossing_lattice_does_not_charge_one_capacity_slot_per_intersection()
    {
        var routes = new List<ArchitectureV7LogicalRoute>();
        for (var horizontal = 0; horizontal < 3; horizontal++)
            routes.Add(Route("h" + horizontal, "hs" + horizontal, "ht" + horizontal, (3, 0), (3, 1), (3, 2), (3, 3), (3, 4 + horizontal), (3, 5 + horizontal), (3, 6 + horizontal)));
        for (var vertical = 0; vertical < 4; vertical++)
            routes.Add(Route("v" + vertical, "vs" + vertical, "vt" + vertical, (0, 3), (1, 3), (2, 3), (3, 3), (4, 3), (5, 3), (6, 3)));

        var result = Allocate(routes, Nodes(routes.SelectMany(route => new[] { route.SourcePhysicalNodeId, route.DestinationPhysicalNodeId }).ToArray()), spacing: 1);

        Assert.Equal(12, result.CrossingInteractions.Count);
        Assert.Equal(12, result.CrossingResources.Count);
        Assert.DoesNotContain(result.TrackDemands, demand => demand.Provenance.Contains("crossing-resource", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == "CROSSING-SLOT-CAPACITY-EXCEEDED");
    }

    [Fact]
    public void One_bend_with_multiple_passes_has_one_bend_envelope_and_multiple_constraints()
    {
        var routes = new[]
        {
            Route("turn", "s", "t", (1, 1), (2, 1), (2, 4)),
            Route("pass-a", "a", "b", (2, 0), (2, 1), (2, 2), (2, 3), (2, 4)),
            Route("pass-b", "c", "d", (2, 0), (2, 1), (2, 2), (2, 3), (2, 4))
        };

        var result = Allocate(routes, Nodes("s", "t", "a", "b", "c", "d"), spacing: 1);

        Assert.True(result.CrossingInteractions.Count >= 2);
        Assert.Single(result.Bends);
        Assert.Contains(result.TrackDemands, demand => demand.ResourceIds.Contains(result.Bends[0].BendId));
        Assert.DoesNotContain(result.TrackDemands, demand => demand.ResourceIds.Any(id => id.StartsWith("crossing-resource:", StringComparison.Ordinal)));
    }

    [Fact]
    public void Turn_pass_with_coincident_bend_geometry_reports_explicit_conflict()
    {
        var routes = new[]
        {
            Route("turn", "s", "t", (1, 1), (2, 1), (2, 3)),
            Route("pass", "p", "q", (2, 0), (2, 1), (2, 2), (2, 3))
        };

        var result = Allocate(routes, Nodes("s", "t", "p", "q"), spacing: 0);

        Assert.Contains(result.CrossingInteractions, interaction => interaction.Classification == "turn-pass");
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "CROSSING-TURN-PHYSICAL-CONFLICT");
    }

    [Fact]
    public void Incomplete_routes_fail_allocation_explicitly()
    {
        var route = new ArchitectureV7LogicalRoute("a", "a", "s", "t", new[] { new ArchitectureV7RouteCell(1, 1) }, false,
            new[] { new ArchitectureV7RouteDiagnostic("blocked", "blocked", true, Array.Empty<ArchitectureV7RouteCell>()) }, "test");
        var result = Allocate(new[] { route }, Nodes("s", "t"));
        Assert.Contains(result.Diagnostics, x => x.Code == "INCOMPLETE-ROUTE" && x.IsHardFailure);
    }

    private static ArchitectureV7CollectiveAllocationFreeze Allocate(IReadOnlyList<ArchitectureV7LogicalRoute> routes, IReadOnlyList<ArchitectureV7FrozenNodePlacement> nodes,
        int spacing = 1, int span = 9, int baseCellWidth = 100)
    {
        if (span != 9) nodes = nodes.Select(node => node with { LogicalSpan = span }).ToArray();
        var placement = new ArchitectureV7PlacementFreeze(nodes, Array.Empty<ArchitectureV7ProjectRegion>(),
            new ArchitectureV7ExternalRegion(0, Array.Empty<string>(), Array.Empty<ArchitectureV7FrozenNodePlacement>()),
            new ArchitectureV7StandaloneRegion(0, 0, 0, Array.Empty<string>(), Array.Empty<ArchitectureV7FrozenNodePlacement>()),
            new ArchitectureV7CommonDiagramGrid(10, 10, Array.Empty<ArchitectureV7LogicalCell>()), Array.Empty<ArchitectureV7ProjectTransform>(),
            "projection", "ownership", "sizing", "reservation", "placement");
        var freeze = new ArchitectureV7LogicalRouteFreeze(routes, Array.Empty<ArchitectureV7RouteDiagnostic>(), "placement", "projection", "routes");
        return new ArchitectureV7CollectivePostRoutingAllocationStage().Allocate(placement, freeze, new ArchitectureV7AllocationConfiguration(spacing, spacing, 0, baseCellWidth));
    }

    private static ArchitectureV7FrozenNodePlacement[] Nodes(params string[] ids) => ids.Select((id, index) =>
        new ArchitectureV7FrozenNodePlacement(id, id, "p", index + 1, 1 + index * 2, 9, 1 + index * 2, new[] { (index + 1, 1 + index * 2) }, false, false, false, "tree", id, id, "test")).ToArray();

    private static ArchitectureV7LogicalRoute Route(string id, string source, string target, params (int Row, int Column)[] cells) =>
        new(id, id, source, target, cells.Select(x => new ArchitectureV7RouteCell(x.Row, x.Column)).ToArray(), true, Array.Empty<ArchitectureV7RouteDiagnostic>(), "test");

}
