using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core.Models.ArchitectureV6;
using StandardIo.ArchitectureDiagram.Core.Models.Architectures;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.ArchitectureV6;
using System.Text.Json;
using Xunit;

using ArchitectureDiagramModel = StandardIo.ArchitectureDiagram.Core.Models.Architectures.ArchitectureDiagram;

namespace StandardIo.ArchitectureDiagram.Core.Tests;

public sealed class ArchitectureV6ManualDefectRegressionTests
{
    [Fact]
    public void Unreserved_dependencies_remain_below_their_positional_parent_and_dependencies()
    {
        var plan = Plan(new ArchitectureV6SemanticFixtureBuilder()
            .Project("p", "Project")
            .Node("parent", "ParentProcessingService", "p")
            .Node("broker", "ChildBroker", "p")
            .Node("leaf", "LeafService", "p")
            .Link("parent-broker", "parent", "broker")
            .Link("broker-leaf", "broker", "leaf"));

        var parent = PlacementBySemantic(plan, "parent");
        var broker = PlacementBySemantic(plan, "broker");
        var leaf = PlacementBySemantic(plan, "leaf");

        Assert.True(Row(parent) < Row(broker), $"parent={Row(parent)}, broker={Row(broker)}");
        Assert.True(Row(broker) < Row(leaf), $"broker={Row(broker)}, leaf={Row(leaf)}");
    }

    [Fact]
    public void Destination_terminal_enters_from_above_with_a_vertical_downward_final_segment()
    {
        var plan = Plan(new ArchitectureV6SemanticFixtureBuilder()
            .Project("p", "Project")
            .Node("source", "SourceService", "p")
            .External("target", "IExternalTarget")
            .Link("link", "source", "target"));

        var route = Assert.Single(plan.PhysicalScene!.Geometry.Routes);
        var destinationPhysicalId = plan.PhysicalLinks.Single(item => item.PhysicalLinkId == route.PhysicalLinkId).DestinationPhysicalNodeId;
        var destinationTerminal = Assert.Single(plan.PhysicalScene.Terminals.Where(item =>
            item.PhysicalLinkId == route.PhysicalLinkId &&
            item.PhysicalNodeId == destinationPhysicalId));
        var points = route.ReducedPoints;
        Assert.NotNull(points);
        Assert.Equal(GridSide.Top, destinationTerminal.Side);
        Assert.True(points.Count >= 2);

        var previous = points[^2].Point;
        var terminal = points[^1].Point;
        Assert.Equal(terminal.X, previous.X);
        Assert.True(terminal.Y > previous.Y, $"previous={previous}, terminal={terminal}");
    }

    [Fact]
    public void Source_terminal_leaves_from_the_bottom_and_initially_travels_downward()
    {
        var plan = Plan(new ArchitectureV6SemanticFixtureBuilder()
            .Project("p", "Project")
            .Node("source", "SourceService", "p")
            .External("target", "IExternalTarget")
            .Link("link", "source", "target"));

        var route = Assert.Single(plan.PhysicalScene!.Geometry.Routes);
        var sourcePhysicalId = plan.PhysicalLinks.Single(item => item.PhysicalLinkId == route.PhysicalLinkId).SourcePhysicalNodeId;
        var sourceTerminal = Assert.Single(plan.PhysicalScene.Terminals.Where(item =>
            item.PhysicalLinkId == route.PhysicalLinkId && item.PhysicalNodeId == sourcePhysicalId));
        var points = route.ReducedPoints;
        Assert.NotNull(points);
        Assert.Equal(GridSide.Bottom, sourceTerminal.Side);
        Assert.True(points.Count >= 2);
        Assert.Equal(sourceTerminal.Point.X, points[1].Point.X);
        Assert.True(points[1].Point.Y > sourceTerminal.Point.Y,
            $"terminal={sourceTerminal.Point}, next={points[1].Point}");
    }

    [Fact]
    public void Physical_route_points_are_orthogonal_for_fan_out_and_external_routes()
    {
        var builder = new ArchitectureV6SemanticFixtureBuilder()
            .Project("p", "Project")
            .Node("source", "AppEventService", "p")
            .Node("left", "LeftBroker", "p")
            .Node("middle", "MiddleBroker", "p")
            .Node("right", "RightBroker", "p")
            .External("hub", "IEventHub")
            .Link("left-link", "source", "left")
            .Link("middle-link", "source", "middle")
            .Link("right-link", "source", "right")
            .Link("external-link", "source", "hub");

        var plan = Plan(builder);
        var routes = plan.PhysicalScene!.Geometry.Routes;
        Assert.Equal(plan.PhysicalLinks.Count, routes.Count);

        foreach (var route in routes)
        {
            var points = route.ReducedPoints ?? Array.Empty<PlannedPhysicalRoutePoint>();
            foreach (var pair in points.Zip(points.Skip(1), (first, second) => (first, second)))
            {
                Assert.True(pair.first.Point.X == pair.second.Point.X || pair.first.Point.Y == pair.second.Point.Y,
                    $"{route.PhysicalLinkId}: {pair.first.PointId} [{pair.first.Role}/{pair.first.ComponentId}/{pair.first.CellId}] {pair.first.Point} -> {pair.second.PointId} [{pair.second.Role}/{pair.second.ComponentId}/{pair.second.CellId}] {pair.second.Point}; turns={string.Join(" | ", plan.LaneAllocation!.Turns.Where(turn => turn.RouteId == route.PhysicalLinkId).Select(turn => $"{turn.CellId}:{turn.HorizontalRunId}/{turn.VerticalRunId}"))}");
            }
        }
    }

    [Fact]
    public void Fan_out_terminals_are_unique_centered_and_respect_configured_spacing()
    {
        var request = new ArchitectureV6SemanticFixtureBuilder()
            .Project("p", "Project")
            .Node("source", "SourceService", "p")
            .Node("left", "LeftService", "p")
            .Node("middle", "MiddleService", "p")
            .Node("right", "RightService", "p")
            .Link("left-link", "source", "left")
            .Link("middle-link", "source", "middle")
            .Link("right-link", "source", "right")
            .BuildRequest() with
        {
            RoutePlanning = new RoutePlanningPolicy(12, 25, "[External]")
        };

        var plan = new ArchitectureDiagramV6Planner().Plan(request);
        var sourcePhysicalId = PhysicalId(plan, "source");
        var sourceTerminals = plan.PhysicalScene!.Terminals
            .Where(item => item.PhysicalNodeId == sourcePhysicalId && item.Side == GridSide.Bottom)
            .OrderBy(item => item.Point.X)
            .ToArray();

        Assert.Equal(3, sourceTerminals.Length);
        Assert.Equal(sourceTerminals.Length, sourceTerminals.Select(item => item.Point.X).Distinct().Count());
        Assert.All(sourceTerminals.Zip(sourceTerminals.Skip(1), (first, second) => (first, second)), pair =>
            Assert.True(pair.second.Point.X - pair.first.Point.X >= 25,
                $"terminal gap={pair.second.Point.X - pair.first.Point.X}"));
    }

    [Fact]
    public void Fan_out_and_fan_in_have_no_shared_physical_collinear_segments_or_ambiguous_crossings()
    {
        var plan = Plan(new ArchitectureV6SemanticFixtureBuilder()
            .Project("p", "Project")
            .Node("source", "SourceService", "p")
            .Node("left", "LeftService", "p")
            .Node("middle", "MiddleService", "p")
            .Node("right", "RightService", "p")
            .Node("target", "TargetService", "p")
            .Link("left-link", "source", "left")
            .Link("middle-link", "source", "middle")
            .Link("right-link", "source", "right")
            .Link("left-target", "left", "target")
            .Link("middle-target", "middle", "target")
            .Link("right-target", "right", "target"));

        Assert.DoesNotContain(plan.PhysicalScene!.Diagnostics, item =>
            item.Code is "SharedCollinearSegment" or "SharedBend" or "CleanCrossingTurnConflict");
    }

    [Fact]
    public void Shared_logical_runs_receive_distinct_physical_intervals_in_the_real_planner()
    {
        var plan = Plan(RegressionRequest());
        var sharedLogicalCells = plan.Routes
            .SelectMany(route => route.Steps.Select(step => (route.PhysicalLinkId, step.GridId, step.CellId)))
            .GroupBy(item => (item.GridId, item.CellId))
            .Where(group => group.Select(item => item.PhysicalLinkId).Distinct().Count() > 1)
            .ToArray();
        var shared = SharedPhysicalIntervals(plan);

        Assert.NotEmpty(sharedLogicalCells);
        Assert.True(shared.Count == 0, string.Join(Environment.NewLine, shared));
    }

    [Fact]
    public void Same_layer_route_preserves_bottom_departure_and_top_destination_entry()
    {
        var plan = Plan(new ArchitectureV6SemanticFixtureBuilder()
            .Project("p", "Project")
            .Node("source", "SourceService", "p")
            .Node("target", "TargetService", "p")
            .Node("peer", "PeerService", "p")
            .Link("source-target", "source", "target")
            .Link("source-peer", "source", "peer"));

        var route = plan.PhysicalScene!.Geometry.Routes.Single(item => item.PhysicalLinkId.Contains("source-target", StringComparison.Ordinal));
        var points = route.ReducedPoints ?? route.RawPoints ?? Array.Empty<PlannedPhysicalRoutePoint>();
        var sourceTerminal = points[0];
        var first = points.Skip(1).First(item => item.Point != sourceTerminal.Point);
        var destinationTerminal = points[^1];
        var last = points.Reverse().Skip(1).First(item => item.Point != destinationTerminal.Point);

        Assert.True(first.Point.X == sourceTerminal.Point.X && first.Point.Y > sourceTerminal.Point.Y,
            $"source terminal={sourceTerminal.Point}, first={first.Point}");
        Assert.True(last.Point.X == destinationTerminal.Point.X && last.Point.Y < destinationTerminal.Point.Y,
            $"last={last.Point}, destination terminal={destinationTerminal.Point}");
    }

    [Fact]
    public void Shared_horizontal_and_vertical_runs_receive_distinct_lane_ordinals_before_materialisation()
    {
        var plan = Plan(RegressionRequest());
        var allocation = plan.LaneAllocation;
        Assert.NotNull(allocation);

        Assert.Contains(true, allocation!.HorizontalLanes.GroupBy(lane => (lane.GridId, lane.DomainId))
            .SelectMany(group => OverlappingLanePairs(group)));
        Assert.Contains(true, allocation.VerticalLanes.GroupBy(lane => (lane.GridId, lane.DomainId))
            .SelectMany(group => OverlappingLanePairs(group)));
    }

    private static IEnumerable<bool> OverlappingLanePairs(IEnumerable<PlannedLaneAllocation> lanes)
    {
        var ordered = lanes.ToArray();
        for (var left = 0; left < ordered.Length; left++)
        for (var right = left + 1; right < ordered.Length; right++)
        {
            var first = ordered[left];
            var second = ordered[right];
            if (first.IntervalStart <= second.IntervalEnd + 1 && second.IntervalStart <= first.IntervalEnd + 1)
                yield return first.Ordinal != second.Ordinal;
        }
    }

    [Fact]
    public void Final_scene_routes_do_not_cross_unrelated_node_rectangles()
    {
        var plan = Plan(RegressionRequest());
        var intersections = RouteNodeIntersections(plan);

        Assert.True(intersections.Count == 0,
            $"validatorRouteNodeIntersections={plan.PhysicalScene!.Metrics.RouteNodeIntersectionCount};" +
            Environment.NewLine + string.Join(Environment.NewLine, intersections));
    }

    [Fact]
    public void Authoritative_validator_matches_renderer_equivalent_geometry_after_correction()
    {
        var plan = Plan(RegressionRequest());
        var validation = new ArchitectureDiagramV6Validator().Validate(plan);
        var intersections = validation.Findings
            .Where(item => item.Code == "RouteNodeIntersection")
            .ToArray();
        var independentlyReconstructed = RouteNodeIntersections(plan)
            .Select(item => item.Replace(" crosses ", "|", StringComparison.Ordinal))
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToArray();
        var validatorSubjects = intersections
            .Select(item => item.SubjectId ?? string.Empty)
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToArray();
        var emittedDiagonalCount = plan.PhysicalScene!.Geometry.Routes
            .SelectMany(route => (route.ReducedPoints ?? route.RawPoints ?? Array.Empty<PlannedPhysicalRoutePoint>())
                .Zip((route.ReducedPoints ?? route.RawPoints ?? Array.Empty<PlannedPhysicalRoutePoint>()).Skip(1),
                    (first, second) => (first, second)))
            .Count(pair => pair.first.Point.X != pair.second.Point.X && pair.first.Point.Y != pair.second.Point.Y);
        var filteredSegmentDiagonalCount = plan.PhysicalScene.Geometry.Routes
            .SelectMany(route => route.Segments)
            .Count(segment => segment.Start.X != segment.End.X && segment.Start.Y != segment.End.Y);

        Assert.Empty(intersections);
        Assert.Equal(independentlyReconstructed, validatorSubjects);
        Assert.Equal(0, independentlyReconstructed.Length);
        Assert.Equal(0, emittedDiagonalCount);
        Assert.Equal(0, filteredSegmentDiagonalCount);
        Assert.Empty(validation.Findings.Where(item => item.Code == "PhysicalSceneDiagonalSegment"));
    }

    [Fact]
    public void Real_unreserved_broker_and_external_dependency_chain_stays_below_its_positional_parent()
    {
        var plan = Plan(RegressionRequest());
        var metadata = plan.NodeMetadata.ToDictionary(item => item.SemanticNodeId, StringComparer.Ordinal);

        Assert.True(metadata["app"].PhysicalRow < metadata["broker"].PhysicalRow,
            Describe(metadata, "app", "broker"));
        Assert.True(metadata["page"].PhysicalRow < metadata["broker"].PhysicalRow,
            Describe(metadata, "page", "broker"));
        Assert.True(metadata["processing"].PhysicalRow < metadata["external-cache"].PhysicalRow,
            Describe(metadata, "processing", "external-cache"));
    }

    [Fact]
    public void Real_unreserved_root_shape_with_multiple_brokers_is_not_promoted_to_the_top_layer()
    {
        var plan = Plan(new ArchitectureV6SemanticFixtureBuilder()
            .Project("p", "Project")
            .Node("app", "AppService", "p")
            .Node("app-broker", "AppBroker", "p")
            .Node("page-broker", "PageBroker", "p")
            .Node("processing", "AppProcessingService", "p")
            .Node("user-role", "UserRoleBroker", "p")
            .Node("plain", "PlainService", "p")
            .External("auth", "ICoreAuthInfo")
            .Link("app-broker", "app", "app-broker")
            .Link("page-broker", "app", "page-broker")
            .Link("user-role", "processing", "user-role")
            .Link("plain-external", "plain", "auth"));

        Assert.True(Row(PlacementBySemantic(plan, "app")) < Row(PlacementBySemantic(plan, "app-broker")));
        Assert.True(Row(PlacementBySemantic(plan, "app")) < Row(PlacementBySemantic(plan, "page-broker")));
        Assert.True(Row(PlacementBySemantic(plan, "processing")) < Row(PlacementBySemantic(plan, "user-role")));
        Assert.True(Row(PlacementBySemantic(plan, "plain")) < Row(PlacementBySemantic(plan, "auth")));
    }

    [Fact]
    public void Real_routes_do_not_materialise_unnecessary_z_bends_or_diagonal_lane_changes()
    {
        var plan = Plan(RegressionRequest());
        var violations = plan.PhysicalScene!.Geometry.Routes
            .SelectMany(route => (route.ReducedPoints ?? Array.Empty<PlannedPhysicalRoutePoint>())
                .Zip((route.ReducedPoints ?? Array.Empty<PlannedPhysicalRoutePoint>()).Skip(1),
                    (first, second) => (route.PhysicalLinkId, First: first.Point, Second: second.Point)))
            .Where(item => item.First.X != item.Second.X && item.First.Y != item.Second.Y)
            .ToArray();

        Assert.True(violations.Length == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Physical_routes_do_not_cross_unrelated_node_bodies()
    {
        var plan = Plan(new ArchitectureV6SemanticFixtureBuilder()
            .Project("p", "Project")
            .Node("source", "SourceService", "p")
            .Node("left", "LeftService", "p")
            .Node("right", "RightService", "p")
            .Node("target", "TargetService", "p")
            .Node("obstacle", "ObstacleService", "p")
            .Link("left-target", "left", "target")
            .Link("right-target", "right", "target")
            .Link("source-obstacle", "source", "obstacle"));

        Assert.DoesNotContain(plan.PhysicalScene!.Diagnostics, item => item.Code == "RouteNodeIntersection");
    }

    [Fact]
    public void Standalone_nodes_are_below_external_and_packed_as_a_separate_square_region()
    {
        var builder = new ArchitectureV6SemanticFixtureBuilder()
            .Project("p", "Project")
            .Node("owner", "OwnerService", "p")
            .External("external", "IExternalDependency")
            .Link("external-link", "owner", "external");

        for (var i = 0; i < 9; i++)
            builder.Node("standalone-" + i, "StandaloneService" + i, "p");

        var plan = Plan(builder);
        var standalone = plan.NodeMetadata.Where(item => item.IsStandalone)
            .Select(item => Placement(plan, item.PhysicalNodeId))
            .ToArray();
        var external = plan.NodeMetadata.Where(item => item.IsExternal)
            .Select(item => Placement(plan, item.PhysicalNodeId))
            .ToArray();

        Assert.NotEmpty(standalone);
        Assert.NotEmpty(external);
        Assert.True(standalone.Min(Row) > external.Max(Row));

        var width = standalone.Max(item => Column(item)) - standalone.Min(item => Column(item)) + 1;
        var height = standalone.Max(Row) - standalone.Min(Row) + 1;
        Assert.True(Math.Abs(width - height) <= width / 2, $"width={width}, height={height}");
        AssertNoAdjacentStandaloneOverlap(plan, standalone);
    }

    [Fact]
    public void Unreserved_nodes_follow_recursive_dependency_depth_above_dependencies()
    {
        var roles = new[]
        {
            new ArchitectureV6RoleRule("Processing", "*ProcessingService", 0),
            new ArchitectureV6RoleRule("Service", "*Service", 1)
        };
        var placement = new NodePlacementPolicy(
            "",
            120,
            60,
            20,
            40,
            roles,
            ReservedLayerTypePatterns: new[]
            {
                new ArchitectureV6RoleRule("Processing", "*ProcessingService", 0)
            });

        var plan = Plan(new ArchitectureV6SemanticFixtureBuilder()
            .Project("p", "Project")
            .Node("parent", "ParentProcessingService", "p")
            .Node("child", "ChildService", "p")
            .Node("leaf", "LeafService", "p")
            .Link("parent-child", "parent", "child")
            .Link("child-leaf", "child", "leaf"), placement);

        Assert.True(Row(PlacementBySemantic(plan, "parent")) < Row(PlacementBySemantic(plan, "child")));
        Assert.True(Row(PlacementBySemantic(plan, "child")) < Row(PlacementBySemantic(plan, "leaf")));
    }

    private static PlannedArchitectureDiagram Plan(ArchitectureV6SemanticFixtureBuilder builder, NodePlacementPolicy? placement = null) =>
        new ArchitectureDiagramV6Planner().Plan(builder.BuildRequest(nodePlacement: placement));

    private static PlannedArchitectureDiagram Plan(ArchitecturePlanningRequest request) =>
        new ArchitectureDiagramV6Planner().Plan(request);

    private static ArchitecturePlanningRequest RegressionRequest()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "ArchitectureV6",
            "content-management-terminal-capacity-regressions.json");
        var model = JsonSerializer.Deserialize<ArchitectureDiagramModel>(File.ReadAllText(path))
            ?? throw new InvalidOperationException("The regression analyser dataset could not be deserialized.");

        return new ArchitecturePlanningRequest(
            model,
            new ArchitectureSelectionScope("SelectedProjects", new[] { "project:fixture" }, Array.Empty<string>()),
            new ArchitectureGenerationSettingsSnapshot("drawio", "[External]", Array.Empty<string>(), Array.Empty<string>()),
            new NodeProjectionPolicy(NodeProjectionMode.Canonical, Array.Empty<string>()),
            new ProjectPlacementPolicy(true, "border"),
            new NodePlacementPolicy(
                "",
                120,
                60,
                20,
                40,
                ReservedLayerTypePatterns: new[]
                {
                    new ArchitectureV6RoleRule("AggregationService", "*AggregationService", 0),
                    new ArchitectureV6RoleRule("ManagementService", "*ManagementService", 1),
                    new ArchitectureV6RoleRule("CoordinationService", "*CoordinationService", 2),
                    new ArchitectureV6RoleRule("OrchestrationService", "*OrchestrationService", 3),
                    new ArchitectureV6RoleRule("ProcessingService", "*ProcessingService", 4),
                    new ArchitectureV6RoleRule("Service", "*Service", 5)
                }),
            new RoutePlanningPolicy(12, 25, "[External]"),
            new GridSizingPolicy(20, 20, 20, 30),
            new ValidationPolicy(ArchitectureValidationMode.Normal),
            Array.Empty<string>(),
            Array.Empty<string>());
    }

    private static IReadOnlyList<string> SharedPhysicalIntervals(PlannedArchitectureDiagram plan)
    {
        var segments = plan.PhysicalScene!.Geometry.Routes
            .SelectMany(route => RouteSegments(route).Select(segment => (route.PhysicalLinkId, segment.Axis,
                segment.Fixed, segment.Start, segment.End)))
            .ToArray();
        var overlaps = new List<string>();
        for (var left = 0; left < segments.Length; left++)
        {
            for (var right = left + 1; right < segments.Length; right++)
            {
                var a = segments[left];
                var b = segments[right];
                if (a.PhysicalLinkId == b.PhysicalLinkId || a.Axis != b.Axis || a.Fixed != b.Fixed)
                    continue;

                var aStart = a.Axis == RouteAxis.Horizontal ? Math.Min(a.Start.X, a.End.X) : Math.Min(a.Start.Y, a.End.Y);
                var aEnd = a.Axis == RouteAxis.Horizontal ? Math.Max(a.Start.X, a.End.X) : Math.Max(a.Start.Y, a.End.Y);
                var bStart = b.Axis == RouteAxis.Horizontal ? Math.Min(b.Start.X, b.End.X) : Math.Min(b.Start.Y, b.End.Y);
                var bEnd = b.Axis == RouteAxis.Horizontal ? Math.Max(b.Start.X, b.End.X) : Math.Max(b.Start.Y, b.End.Y);
                if (Math.Min(aEnd, bEnd) - Math.Max(aStart, bStart) > 0)
                    overlaps.Add($"{a.PhysicalLinkId} and {b.PhysicalLinkId} share {a.Axis} interval at {a.Fixed}");
            }
        }

        return overlaps;
    }

    private static IEnumerable<(RouteAxis Axis, int Fixed, AbsolutePoint Start, AbsolutePoint End)> RouteSegments(PlannedPhysicalRoute route)
    {
        var points = route.ReducedPoints ?? route.RawPoints ?? Array.Empty<PlannedPhysicalRoutePoint>();
        foreach (var pair in points.Zip(points.Skip(1), (first, second) => (first.Point, second.Point)))
        {
            if (pair.Item1.X == pair.Item2.X && pair.Item1.Y != pair.Item2.Y)
                yield return (RouteAxis.Vertical, pair.Item1.X, pair.Item1, pair.Item2);
            else if (pair.Item1.Y == pair.Item2.Y && pair.Item1.X != pair.Item2.X)
                yield return (RouteAxis.Horizontal, pair.Item1.Y, pair.Item1, pair.Item2);
        }
    }

    private static IReadOnlyList<string> RouteNodeIntersections(PlannedArchitectureDiagram plan)
    {
        var nodes = plan.Geometry!.Nodes.ToDictionary(item => item.PhysicalNodeId, StringComparer.Ordinal);
        var links = plan.PhysicalLinks.ToDictionary(item => item.PhysicalLinkId, StringComparer.Ordinal);
        var intersections = new List<string>();
        foreach (var route in plan.PhysicalScene!.Geometry.Routes)
        {
            if (!links.TryGetValue(route.PhysicalLinkId, out var link))
                continue;
            var points = route.ReducedPoints ?? route.RawPoints ?? Array.Empty<PlannedPhysicalRoutePoint>();
            foreach (var pair in points.Zip(points.Skip(1), (first, second) => (first.Point, second.Point)))
            {
                foreach (var node in nodes.Values)
                {
                    if (node.PhysicalNodeId == link.SourcePhysicalNodeId || node.PhysicalNodeId == link.DestinationPhysicalNodeId)
                        continue;
                    if (SegmentIntersectsInterior(pair.Item1, pair.Item2, node.AbsoluteBounds))
                        intersections.Add($"{route.PhysicalLinkId} crosses {node.PhysicalNodeId}");
                }
            }
        }

        return intersections;
    }

    private static bool SegmentIntersectsInterior(AbsolutePoint start, AbsolutePoint end, AbsoluteRectangle rectangle)
    {
        var dx = (double)end.X - start.X;
        var dy = (double)end.Y - start.Y;
        var tMin = 0d;
        var tMax = 1d;
        foreach (var (p, q) in new[]
        {
            (-dx, start.X - rectangle.X),
            (dx, rectangle.X + rectangle.Width - start.X),
            (-dy, start.Y - rectangle.Y),
            (dy, rectangle.Y + rectangle.Height - start.Y)
        })
        {
            if (p == 0)
            {
                if (q <= 0) return false;
                continue;
            }

            var ratio = q / p;
            if (p < 0) tMin = Math.Max(tMin, ratio);
            else tMax = Math.Min(tMax, ratio);
            if (tMin >= tMax) return false;
        }

        return tMax > 0 && tMin < 1;
    }

    private static string Describe(IReadOnlyDictionary<string, PhysicalNodePlacementMetadata> metadata, string parent, string child) =>
        $"{parent}: row={metadata[parent].PhysicalRow}, owner={metadata[parent].PositionalOwnerId}; " +
        $"{child}: row={metadata[child].PhysicalRow}, owner={metadata[child].PositionalOwnerId}, " +
        $"root={metadata[child].TreeRootId}, depth={metadata[child].SemanticDepth}, role={metadata[child].RoleSelector}";

    private static PlannedNodePlacement Placement(PlannedArchitectureDiagram plan, string physicalNodeId) =>
        Assert.Single(plan.NodePlacements.Where(item => item.PhysicalNodeId == physicalNodeId));

    private static PlannedNodePlacement PlacementBySemantic(PlannedArchitectureDiagram plan, string semanticNodeId) =>
        Placement(plan, PhysicalId(plan, semanticNodeId));

    private static string PhysicalId(PlannedArchitectureDiagram plan, string semanticNodeId) =>
        Assert.Single(plan.PhysicalNodes.Where(item => item.SemanticNodeId == semanticNodeId)).PhysicalNodeId;

    private static int Row(PlannedNodePlacement placement) => int.Parse(placement.AnchorCellId.RowId.Value.TrimStart('r'));
    private static int Column(PlannedNodePlacement placement) => int.Parse(placement.CentreColumnId.Value.TrimStart('c'));

    private static void AssertNoAdjacentStandaloneOverlap(
        PlannedArchitectureDiagram plan,
        IReadOnlyList<PlannedNodePlacement> standalone)
    {
        var byCell = standalone.SelectMany(placement => placement.Footprint.Select(cell => (placement.PhysicalNodeId, Cell: cell)))
            .GroupBy(item => item.Cell)
            .ToArray();
        Assert.DoesNotContain(byCell, group => group.Select(item => item.PhysicalNodeId).Distinct().Count() > 1);

        var rows = standalone.Select(Row).Distinct().OrderBy(item => item).ToArray();
        var columns = standalone.Select(Column).Distinct().OrderBy(item => item).ToArray();
        Assert.All(rows.Zip(rows.Skip(1), (first, second) => second - first), gap => Assert.True(gap >= 2));
        Assert.All(columns.Zip(columns.Skip(1), (first, second) => second - first), gap => Assert.True(gap >= 2));
    }
}
