using System;
using System.Collections.Generic;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core.Tests;

public sealed class AdjacentDownwardLinkSegmentDemandObserverTests
{
    [Fact]
    public void Adjacent_downward_route_emits_complete_demands_and_exact_reconstruction()
    {
        var result = Assert.Single(AdjacentDownwardLinkSegmentDemandObserver.Observe(new[] { Context("route") }).Routes);

        Assert.True(result.Eligible);
        Assert.Equal(new[] { LinkSegmentRole.ConnectionDeparture, LinkSegmentRole.Through, LinkSegmentRole.ConnectionArrival },
            result.Demands.Select(item => item.Role));
        Assert.Equal(2, result.Transitions.Count);
        Assert.Equal(ObservationalRouteParity.ExactPointParity, result.Parity);
        Assert.Equal(result.CanonicalAuthoritativePoints, result.ReconstructedPoints);
    }

    [Fact]
    public void Existing_terminal_coordinates_are_preserved()
    {
        var context = Context("route");
        var result = Assert.Single(AdjacentDownwardLinkSegmentDemandObserver.Observe(new[] { context }).Routes);

        Assert.Equal(context.Route.SourcePoint, result.ReconstructedPoints[0]);
        Assert.Equal(context.Route.TargetPoint, result.ReconstructedPoints[^1]);
        Assert.Equal(context.Route.SourcePoint.X, result.SelectedAssignedLinkSegments[0].AxisCoordinate);
        Assert.Equal(context.Route.TargetPoint.X, result.SelectedAssignedLinkSegments[^1].AxisCoordinate);
    }

    [Fact]
    public void Multiple_routes_and_reversed_enumeration_have_stable_demand_identities()
    {
        var contexts = new[] { Context("b", lane: 1), Context("a", lane: 0) };
        var forward = AdjacentDownwardLinkSegmentDemandObserver.Observe(contexts).Routes;
        var reverse = AdjacentDownwardLinkSegmentDemandObserver.Observe(contexts.AsEnumerable().Reverse()).Routes;

        Assert.Equal(new[] { "a", "b" }, forward.Select(item => item.LogicalRouteId));
        Assert.Equal(forward.SelectMany(item => item.Demands).Select(item => item.Id),
            reverse.SelectMany(item => item.Demands).Select(item => item.Id));
    }

    [Fact]
    public void Existing_lane_sources_map_deterministically_to_assigned_rail()
    {
        var result = Assert.Single(AdjacentDownwardLinkSegmentDemandObserver.Observe(new[] { Context("route", includeAllLaneSources: true) }).Routes);

        Assert.Equal(new[]
        {
            ExistingLaneMappingSource.LegacyCorridor,
            ExistingLaneMappingSource.StageBHypothetical,
            ExistingLaneMappingSource.StageCGrouped
        }, result.ExistingLaneMappings.Select(item => item.Source));
        Assert.All(result.ExistingLaneMappings, item => Assert.Equal(120, item.Rail.AxisCoordinate));
        Assert.Equal(ExistingLaneMappingSource.LegacyCorridor, result.ExistingLaneMappings[0].Source);
    }

    [Fact]
    public void Assigned_independent_lanes_separate_but_same_rail_overlap_conflicts()
    {
        var first = ThroughRail("a", 120);
        var separate = ThroughRail("b", 144);
        var shared = ThroughRail("c", 120);

        Assert.False(ContactInteractionPolicy.CreatesAssignedLinkSegmentEdge(first, separate, 12));
        Assert.True(ContactInteractionPolicy.CreatesAssignedLinkSegmentEdge(first, shared, 12));
    }

    [Fact]
    public void Component_projection_removes_competition_after_existing_lane_separation()
    {
        var report = AdjacentDownwardLinkSegmentDemandObserver.Observe(new[] { Context("a", lane: 0), Context("b", lane: 1) });

        var projection = AdjacentDownwardComponentProjector.Project(report, 12);

        Assert.Single(projection.UnassignedComponents);
        Assert.Equal(2, projection.AssignedComponents.Count);
        Assert.Contains(projection.UnassignedEdges, item => item.Cause == "UnassignedLinkSegmentDemand");
        Assert.Empty(projection.AssignedEdges);
    }

    [Fact]
    public void Turn_transitions_are_orthogonal_and_owned_by_both_rails()
    {
        var result = Assert.Single(AdjacentDownwardLinkSegmentDemandObserver.Observe(new[] { Context("route") }).Routes);
        var rails = result.SelectedAssignedLinkSegments.ToDictionary(item => item.Id);

        Assert.All(result.Transitions, transition =>
        {
            Assert.NotEqual(rails[transition.FromAssignedLinkSegmentId].Orientation, rails[transition.ToAssignedLinkSegmentId].Orientation);
            Assert.Contains(transition.Turn, result.ReconstructedPoints);
        });
    }

    [Fact]
    public void Rejected_routes_do_not_suppress_eligible_route()
    {
        var skipped = Context("skipped", targetDepth: 2);
        var diagonalRoute = Context("diagonal") with
        {
            Route = Link("diagonal", new Point(20, 80), new Point(80, 200), new[] { new Point(50, 120) })
        };
        var report = AdjacentDownwardLinkSegmentDemandObserver.Observe(new[] { skipped, Context("eligible"), diagonalRoute });

        Assert.True(report.Routes.Single(item => item.LogicalRouteId == "eligible").Eligible);
        Assert.Equal(AdjacentDownwardRejectionReason.SkippedLayer,
            report.Routes.Single(item => item.LogicalRouteId == "skipped").RejectionReason);
        Assert.Equal(AdjacentDownwardRejectionReason.NonOrthogonal,
            report.Routes.Single(item => item.LogicalRouteId == "diagonal").RejectionReason);
    }

    [Fact]
    public void Revision_mismatch_is_rejected_route_locally()
    {
        var context = Context("route") with { RouteRevision = new RouteRevision(2) };

        var result = Assert.Single(AdjacentDownwardLinkSegmentDemandObserver.Observe(new[] { context }).Routes);

        Assert.Equal(AdjacentDownwardRejectionReason.RevisionMismatch, result.RejectionReason);
    }

    [Fact]
    public void Observation_revision_is_independent_of_logical_route_history_revision()
    {
        var result = Assert.Single(AdjacentDownwardLinkSegmentDemandObserver.Observe(new[]
        {
            Context("route", observationRevision: 2)
        }).Routes);

        Assert.True(result.Eligible);
        Assert.All(result.Demands, item => Assert.Equal(2, item.RouteRevision.Value));
    }

    [Fact]
    public void Multiple_memberships_in_one_band_remain_one_band_eligible()
    {
        var context = Context("route");
        var second = context.BandMemberships[0] with
        {
            Id = "route:band:second",
            FirstSegmentIndex = 1,
            LastSegmentIndex = 2
        };

        var result = Assert.Single(AdjacentDownwardLinkSegmentDemandObserver.Observe(new[]
        {
            context with { BandMemberships = context.BandMemberships.Concat(new[] { second }).ToArray() }
        }).Routes);

        Assert.True(result.Eligible);
        Assert.Equal(ObservationalRouteParity.ExactPointParity, result.Parity);
    }

    [Fact]
    public void Reconstruction_never_uses_original_points_as_fallback()
    {
        var reconstructed = AdjacentDownwardLinkSegmentDemandObserver.Reconstruct(
            new Point(20, 80), new Point(80, 200), Array.Empty<AssignedLinkSegment>(), Array.Empty<LinkTransition>());

        Assert.Empty(reconstructed);
    }

    [Fact]
    public void Common_allocator_reconstructs_without_reusing_existing_through_rail()
    {
        var observed = AdjacentDownwardLinkSegmentDemandObserver.Observe(new[] { Context("route", lane: 3) });

        var common = AdjacentDownwardCommonRailObserver.Observe(observed, 12, 4);

        var route = Assert.Single(common.Routes);
        Assert.NotNull(route.CommonThroughRail);
        Assert.Equal(0, route.CommonThroughRail!.SlotIndex);
        Assert.Equal(CommonRouteReconstructionParity.ValidDifferentGeometry, route.ReconstructionParity);
        Assert.Equal(84, route.ReconstructedPoints[1].Y);
    }

    [Fact]
    public void Common_allocator_assigns_complete_interacting_route_group_deterministically()
    {
        var contexts = new[] { Context("b", lane: 1), Context("a", lane: 0) };
        var forward = AdjacentDownwardCommonRailObserver.Observe(
            AdjacentDownwardLinkSegmentDemandObserver.Observe(contexts), 12, 4);
        var reverse = AdjacentDownwardCommonRailObserver.Observe(
            AdjacentDownwardLinkSegmentDemandObserver.Observe(contexts.AsEnumerable().Reverse()), 12, 4);

        Assert.Single(forward.Regions);
        Assert.Equal(2, forward.Regions[0].Assignment.SegmentsByDemandId.Count);
        Assert.Equal(forward.Routes.Select(item => (item.LogicalRouteId, item.CommonThroughRail!.SlotIndex)),
            reverse.Routes.Select(item => (item.LogicalRouteId, item.CommonThroughRail!.SlotIndex)));
    }

    [Fact]
    public void Common_required_extent_projects_a_persistent_constraint_without_moving_live_layout()
    {
        var contexts = Enumerable.Range(0, 12).Select(index => Context($"route-{index:D2}", lane: index)).ToArray();
        var observed = AdjacentDownwardLinkSegmentDemandObserver.Observe(contexts);
        var before = observed.Routes.Select(item => item.CanonicalAuthoritativePoints.ToArray()).ToArray();

        var common = AdjacentDownwardCommonRailObserver.Observe(observed, 12, 4);

        var region = Assert.Single(common.Regions);
        Assert.NotNull(region.ConstraintProposal);
        Assert.Equal(152, region.Assignment.RequiredExtent);
        Assert.Equal(before, observed.Routes.Select(item => item.CanonicalAuthoritativePoints.ToArray()).ToArray());
    }

    private static AdjacentDownwardRouteContext Context(
        string id,
        int lane = 0,
        int targetDepth = 1,
        bool includeAllLaneSources = false,
        int observationRevision = 0)
    {
        var layoutRevision = new LayoutRevision(1);
        var routeRevision = new RouteRevision(observationRevision);
        var bandId = new InterLayerBandId(0, 1, layoutRevision);
        var source = Node($"{id}-source", new Rect(0, 0, 100, 80), 0);
        var target = Node($"{id}-target", new Rect(40, 200, 100, 80), targetDepth);
        var laneY = 120 + lane * 24;
        var route = Link(id, new Point(20, 80), new Point(80, 200), new[] { new Point(20, laneY), new Point(80, laneY) });
        var membership = new BandRouteMembership($"{id}:band", id, routeRevision, bandId, 0, 2, BandMembershipRole.SourceTransition);
        var demand = new BandRouteDemand($"{id}:band:demand", id, routeRevision, bandId, 1,
            BandMembershipRole.SourceTransition, 20, 80, 0, BandRouteDirection.Right, lane);
        var corridorId = $"corridor:{id}";
        var corridor = new RoutingCorridor(corridorId, CorridorOrientation.Horizontal,
            new Rect(0, 100, 120, 40), 12, 4);
        var corridors = includeAllLaneSources
            ? new CorridorObservation(
                new Dictionary<string, RoutingCorridor> { [corridorId] = corridor },
                new Dictionary<string, CorridorJunction>(),
                new[] { new CorridorSegmentMapping(id, 1, corridorId, new Segment(new Point(20, laneY), new Point(80, laneY))) },
                new Dictionary<string, CorridorUsage> { [corridorId] = new(corridor, new[] { id }, 1) })
            : EmptyCorridors();
        var lanes = includeAllLaneSources
            ? new CorridorLaneAllocation(
                new Dictionary<string, IReadOnlyDictionary<string, AllocatedCorridorLane>>
                {
                    [corridorId] = new Dictionary<string, AllocatedCorridorLane>
                    {
                        [id] = new(corridorId, id, lane, laneY)
                    }
                }, Array.Empty<string>())
            : EmptyLanes();
        var grouped = includeAllLaneSources
            ? new GroupedVerticalBandPlan(
                new[] { new BandConflictGroup($"group:{id}", bandId, new[] { demand },
                    new Dictionary<string, int> { [demand.Id] = lane }, 1, 1, 80, 80, 0,
                    SpacingConstraintScope.LayerBoundary) },
                Array.Empty<MinimumSpacingConstraint>(), Array.Empty<string>(), new GroupedSpacingTelemetry(1, 0, 0, 0, 0))
            : null;
        return new AdjacentDownwardRouteContext(
            route, source, target, layoutRevision, routeRevision, new[] { membership }, new[] { demand },
            new Dictionary<InterLayerBandId, AxisInterval> { [bandId] = new(80, 200) },
            corridors, lanes, grouped, false);
    }

    private static LinkLayout Link(string id, Point source, Point target, IReadOnlyList<Point> points) => new(
        new RenderLink(id, $"{id}-source", $"{id}-target", "internal", 0), source, target, points, 0.2, 0.4);

    private static NodeLayout Node(string id, Rect rect, int depth) => new(
        new RenderNode(id, "project", id, id, "Class", false, string.Empty, 0,
            Array.Empty<string>(), Array.Empty<StandardIo.ArchitectureDiagram.Core.Models.TypeProperty>(), 0),
        rect, depth, false);

    private static AssignedLinkSegment ThroughRail(string id, int axis) => new(
        id, id, id, LinkSegmentOrientation.Horizontal, axis, 0, new AxisInterval(20, 80), LinkSegmentRole.Through,
        new LayoutRevision(1), new RouteRevision(0));

    private static CorridorObservation EmptyCorridors() => new(
        new Dictionary<string, RoutingCorridor>(), new Dictionary<string, CorridorJunction>(),
        Array.Empty<CorridorSegmentMapping>(), new Dictionary<string, CorridorUsage>());

    private static CorridorLaneAllocation EmptyLanes() => new(
        new Dictionary<string, IReadOnlyDictionary<string, AllocatedCorridorLane>>(), Array.Empty<string>());
}
