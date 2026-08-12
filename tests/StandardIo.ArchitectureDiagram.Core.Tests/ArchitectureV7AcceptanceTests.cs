using System;
using System.Collections.Generic;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core.Models;
using StandardIo.ArchitectureDiagram.Core.Models.ArchitectureV7;
using StandardIo.ArchitectureDiagram.Core.Models.Architectures;
using StandardIo.ArchitectureDiagram.Core.Services.Orchestrations.Diagrams;
using ArchitectureDiagramModel = StandardIo.ArchitectureDiagram.Core.Models.Architectures.ArchitectureDiagram;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.ArchitectureV7;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core.Tests;

public sealed class ArchitectureV7AcceptanceTests
{
    [Fact]
    public void Mechanical_renderer_emits_literal_waypoints_without_drawio_routing_authority()
    {
        var pipeline = Build();
        var page = new ArchitectureV7MechanicalDrawioRenderer().Render(new ArchitectureDiagramModel(Array.Empty<ArchitectureProject>(), Array.Empty<ArchitectureExternalNode>(), Array.Empty<ArchitectureLink>(), null),
            pipeline.Projection, pipeline.Placement, pipeline.Scene, new ArchitectureRenderSettings());
        var edge = page.GraphModel.Descendants("mxCell").Single(item => (string?)item.Attribute("physicalLinkId") == "a");
        var style = (string)edge.Attribute("style")!;
        var source = pipeline.Scene.Terminals.Single(item => item.EndpointKind == ArchitectureV7EndpointKind.SourceDeparture).Position;
        var target = pipeline.Scene.Terminals.Single(item => item.EndpointKind == ArchitectureV7EndpointKind.DestinationArrival).Position;
        var points = edge.Element("mxGeometry")!.Element("Array")!.Elements("mxPoint").ToArray();

        Assert.DoesNotContain("segmentEdgeStyle", style, StringComparison.Ordinal);
        Assert.Contains("edgeStyle=none", style, StringComparison.Ordinal);
        Assert.Contains("orthogonal=0", style, StringComparison.Ordinal);
        Assert.Equal(pipeline.Scene.Routes.Single().Points.Count - 2, points.Length);
        Assert.Equal(source.X, double.Parse((string)edge.Attribute("v7SourceTerminalX")!, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(source.Y, double.Parse((string)edge.Attribute("v7SourceTerminalY")!, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(target.X, double.Parse((string)edge.Attribute("v7TargetTerminalX")!, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(target.Y, double.Parse((string)edge.Attribute("v7TargetTerminalY")!, System.Globalization.CultureInfo.InvariantCulture));
        Assert.DoesNotContain(points.Zip(points.Skip(1), (left, right) => left.Attribute("x")?.Value == right.Attribute("x")?.Value && left.Attribute("y")?.Value == right.Attribute("y")?.Value), value => value);
    }

    [Fact]
    public void Mechanical_renderer_emits_project_children_in_container_coordinates()
    {
        var pipeline = Build();
        var sceneLeft = pipeline.Scene.Nodes.Min(node => node.Bounds.Left);
        var sceneTop = pipeline.Scene.Nodes.Min(node => node.Bounds.Top);
        var project = new ArchitectureV7ProjectRegion("p", new ArchitectureV7ProjectTransform("p", 0, 0, 0, 0, 100, 100),
            Array.Empty<string>(), Array.Empty<ArchitectureV7LogicalCell>(), 100, 100);
        var placement = new ArchitectureV7PlacementFreeze(pipeline.Placement.Nodes, new[] { project }, pipeline.Placement.External,
            pipeline.Placement.Standalone, pipeline.Placement.DiagramGrid, pipeline.Placement.Transforms, pipeline.Placement.ProjectionFingerprint,
            pipeline.Placement.OwnershipFingerprint, pipeline.Placement.SizingFingerprint, pipeline.Placement.ReservationFingerprint, pipeline.Placement.PlacementFingerprint);
        var page = new ArchitectureV7MechanicalDrawioRenderer().Render(new ArchitectureDiagramModel(Array.Empty<ArchitectureProject>(), Array.Empty<ArchitectureExternalNode>(), Array.Empty<ArchitectureLink>(), null),
            pipeline.Projection, placement, pipeline.Scene, new ArchitectureRenderSettings());
        var container = page.GraphModel.Descendants("mxCell").Single(item => (string?)item.Attribute("id") == ArchitectureV7MechanicalDrawioRenderer.IdFor("project", "p"));
        var containerGeometry = container.Element("mxGeometry")!;
        foreach (var node in pipeline.Scene.Nodes)
        {
            var cell = page.GraphModel.Descendants("mxCell").Single(item => (string?)item.Attribute("id") == ArchitectureV7MechanicalDrawioRenderer.IdFor("node", node.PhysicalNodeId));
            var geometry = cell.Element("mxGeometry")!;
            Assert.Equal(node.Bounds.Left - sceneLeft, double.Parse((string)geometry.Attribute("x")!, System.Globalization.CultureInfo.InvariantCulture));
            Assert.Equal(node.Bounds.Top - sceneTop, double.Parse((string)geometry.Attribute("y")!, System.Globalization.CultureInfo.InvariantCulture));
        }
        Assert.Equal(sceneLeft, double.Parse((string)containerGeometry.Attribute("x")!, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(sceneTop, double.Parse((string)containerGeometry.Attribute("y")!, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Renderer_translates_project_relative_geometry_to_global_coordinates_exactly_once()
    {
        var fixture = BuildRendererCoordinateFixture();
        var page = new ArchitectureV7MechanicalDrawioRenderer().Render(fixture.Diagram, fixture.Projection, fixture.Placement, fixture.Scene, new ArchitectureRenderSettings());

        foreach (var project in fixture.Projects)
        {
            var origin = fixture.Origins[project.ProjectId];
            var container = page.GraphModel.Descendants("mxCell").Single(item => (string?)item.Attribute("id") == ArchitectureV7MechanicalDrawioRenderer.IdFor("project", project.ProjectId));
            var containerGeometry = container.Element("mxGeometry")!;
            Assert.Equal(origin.X, double.Parse((string)containerGeometry.Attribute("x")!, System.Globalization.CultureInfo.InvariantCulture));
            Assert.Equal(origin.Y, double.Parse((string)containerGeometry.Attribute("y")!, System.Globalization.CultureInfo.InvariantCulture));
            foreach (var node in fixture.Nodes.Where(item => item.ProjectId == project.ProjectId))
            {
                var emitted = page.GraphModel.Descendants("mxCell").Single(item => (string?)item.Attribute("id") == ArchitectureV7MechanicalDrawioRenderer.IdFor("node", node.PhysicalNodeId));
                var geometry = emitted.Element("mxGeometry")!;
                var expectedRelativeX = node.Relative.Left;
                var expectedRelativeY = node.Relative.Top;
                Assert.Equal(expectedRelativeX, double.Parse((string)geometry.Attribute("x")!, System.Globalization.CultureInfo.InvariantCulture));
                Assert.Equal(expectedRelativeY, double.Parse((string)geometry.Attribute("y")!, System.Globalization.CultureInfo.InvariantCulture));
                Assert.Equal(node.Relative.Left + origin.X, node.Global.Left);
                Assert.Equal(node.Relative.Top + origin.Y, node.Global.Top);
            }

            foreach (var link in fixture.Links.Where(item => item.ProjectId == project.ProjectId))
            {
                var route = fixture.Scene.Routes.Single(item => item.PhysicalLinkId == link.LinkId);
                var edge = page.GraphModel.Descendants("mxCell").Single(item => (string?)item.Attribute("physicalLinkId") == link.LinkId);
                var emittedWaypoints = edge.Element("mxGeometry")!.Element("Array")!.Elements("mxPoint").Select(point =>
                    (X: double.Parse((string)point.Attribute("x")!, System.Globalization.CultureInfo.InvariantCulture),
                     Y: double.Parse((string)point.Attribute("y")!, System.Globalization.CultureInfo.InvariantCulture))).ToArray();
                var expectedGlobal = fixture.RelativeRoutePoints[link.LinkId].Select(point => (X: point.X + origin.X, Y: point.Y + origin.Y)).ToArray();
                var emittedSource = (X: double.Parse((string)edge.Attribute("v7SourceTerminalX")!, System.Globalization.CultureInfo.InvariantCulture),
                    Y: double.Parse((string)edge.Attribute("v7SourceTerminalY")!, System.Globalization.CultureInfo.InvariantCulture));
                var emittedTarget = (X: double.Parse((string)edge.Attribute("v7TargetTerminalX")!, System.Globalization.CultureInfo.InvariantCulture),
                    Y: double.Parse((string)edge.Attribute("v7TargetTerminalY")!, System.Globalization.CultureInfo.InvariantCulture));

                Assert.Equal(expectedGlobal[0], emittedSource);
                Assert.Equal(expectedGlobal[^1], emittedTarget);
                Assert.Equal(expectedGlobal.Skip(1).SkipLast(1), emittedWaypoints);
                Assert.All(expectedGlobal.Zip(fixture.RelativeRoutePoints[link.LinkId], (global, relative) => (global, relative)), pair =>
                {
                    Assert.Equal(pair.relative.X, pair.global.X - origin.X);
                    Assert.Equal(pair.relative.Y, pair.global.Y - origin.Y);
                });
                Assert.Equal(expectedGlobal, route.Points.Select(point => (X: point.X, Y: point.Y)));
            }
        }
    }

    [Fact]
    public void Renderer_uses_each_projects_own_origin_for_nodes_terminals_and_route_points()
    {
        var fixture = BuildRendererCoordinateFixture();
        var page = new ArchitectureV7MechanicalDrawioRenderer().Render(fixture.Diagram, fixture.Projection, fixture.Placement, fixture.Scene, new ArchitectureRenderSettings());
        var projectA = fixture.Links.Single(item => item.ProjectId == "A");
        var projectB = fixture.Links.Single(item => item.ProjectId == "B");
        var routeA = fixture.Scene.Routes.Single(item => item.PhysicalLinkId == projectA.LinkId);
        var routeB = fixture.Scene.Routes.Single(item => item.PhysicalLinkId == projectB.LinkId);
        var edgeA = page.GraphModel.Descendants("mxCell").Single(item => (string?)item.Attribute("physicalLinkId") == projectA.LinkId);
        var edgeB = page.GraphModel.Descendants("mxCell").Single(item => (string?)item.Attribute("physicalLinkId") == projectB.LinkId);

        Assert.Equal(fixture.RelativeRoutePoints[projectA.LinkId], fixture.RelativeRoutePoints[projectB.LinkId]);
        Assert.Equal(routeA.Points.Select(point => (point.X - fixture.Origins["A"].X, point.Y - fixture.Origins["A"].Y)),
            routeB.Points.Select(point => (point.X - fixture.Origins["B"].X, point.Y - fixture.Origins["B"].Y)));
        Assert.NotEqual(routeA.Points, routeB.Points);
        Assert.Equal(routeA.Points.Skip(1).SkipLast(1).Select(point => (point.X, point.Y)),
            edgeA.Element("mxGeometry")!.Element("Array")!.Elements("mxPoint").Select(point =>
                (double.Parse((string)point.Attribute("x")!, System.Globalization.CultureInfo.InvariantCulture), double.Parse((string)point.Attribute("y")!, System.Globalization.CultureInfo.InvariantCulture))));
        Assert.Equal(routeB.Points.Skip(1).SkipLast(1).Select(point => (point.X, point.Y)),
            edgeB.Element("mxGeometry")!.Element("Array")!.Elements("mxPoint").Select(point =>
                (double.Parse((string)point.Attribute("x")!, System.Globalization.CultureInfo.InvariantCulture), double.Parse((string)point.Attribute("y")!, System.Globalization.CultureInfo.InvariantCulture))));
    }

    [Fact]
    public void Renderer_project_translation_preserves_route_geometry()
    {
        var fixture = BuildRendererCoordinateFixture();
        var page = new ArchitectureV7MechanicalDrawioRenderer().Render(fixture.Diagram, fixture.Projection, fixture.Placement, fixture.Scene, new ArchitectureRenderSettings());

        foreach (var link in fixture.Links)
        {
            var origin = fixture.Origins[link.ProjectId];
            var route = fixture.Scene.Routes.Single(item => item.PhysicalLinkId == link.LinkId);
            var full = route.Points.Select(point => (point.X, point.Y)).ToArray();
            Assert.Equal(0, full.Zip(full.Skip(1), (left, right) => Math.Abs(left.X - right.X) > 0.001 && Math.Abs(left.Y - right.Y) > 0.001).Count(value => value));

            var obstacle = fixture.Nodes.Single(node => node.ProjectId == link.ProjectId && node.PhysicalNodeId.EndsWith("obstacle", StringComparison.Ordinal)).Global;
            Assert.DoesNotContain(route.Segments, segment => IntersectsInterior(segment.Start, segment.End, obstacle));

            var edge = page.GraphModel.Descendants("mxCell").Single(item => (string?)item.Attribute("physicalLinkId") == link.LinkId);
            Assert.Equal(full[0].X, double.Parse((string)edge.Attribute("v7SourceTerminalX")!, System.Globalization.CultureInfo.InvariantCulture));
            Assert.Equal(full[^1].Y, double.Parse((string)edge.Attribute("v7TargetTerminalY")!, System.Globalization.CultureInfo.InvariantCulture));
            Assert.Equal(fixture.RelativeRoutePoints[link.LinkId], full.Select(point => (point.X - origin.X, point.Y - origin.Y)));
        }
    }

    [Fact]
    public void Valid_complete_pipeline_is_strictly_eligible()
    {
        var pipeline = Build();
        var report = new ArchitectureV7FinalAcceptanceValidationStage().Validate(pipeline.Projection, pipeline.Ownership, pipeline.Sizing,
            pipeline.Reservation, pipeline.Placement, pipeline.Routes, pipeline.Allocation, pipeline.Scene, pipeline.Configuration);
        Assert.False(report.HasHardFailures, string.Join(";", report.Findings.Select(x => x.Code + ":" + x.Message)));
        Assert.True(report.IsStrictEligible);
        Assert.True(report.IsNormalEligible);
    }

    [Fact]
    public void Diagonal_in_final_unsimplified_scene_is_rejected()
    {
        var pipeline = Build();
        var route = pipeline.Scene.Routes.Single();
        var diagonal = new ArchitectureV7PhysicalRoute(route.PhysicalLinkId,
            new[] { new ArchitectureV7PhysicalPoint(0, 0, "test"), new ArchitectureV7PhysicalPoint(10, 10, "test") },
            new[] { new ArchitectureV7PhysicalSegment(route.PhysicalLinkId, new ArchitectureV7PhysicalPoint(0, 0, "test"), new ArchitectureV7PhysicalPoint(10, 10, "test"), new[] { 0, 1 }, route.PhysicalLinkId == "a" ? pipeline.Routes.Routes.Single().Cells.Take(2).ToArray() : Array.Empty<ArchitectureV7RouteCell>(), "run:a:0", "lane:V:1:0", "test") }, "test");
        var scene = new ArchitectureV7PhysicalSceneFreeze(pipeline.Scene.Rows, pipeline.Scene.Columns, pipeline.Scene.Nodes, pipeline.Scene.Terminals,
            new[] { diagonal }, pipeline.Scene.Diagnostics, pipeline.Scene.PlacementFingerprint, pipeline.Scene.RouteFingerprint, pipeline.Scene.AllocationFingerprint, pipeline.Scene.PhysicalSceneFingerprint);
        var report = Validate(pipeline, scene);
        Assert.Contains(report.Findings, x => x.Code == "PHYSICAL-DIAGONAL");
        Assert.False(report.IsStrictEligible);
    }

    [Fact]
    public void Endpoint_inclusive_reversal_and_overshoot_are_rejected_before_reduction()
    {
        var pipeline = Build();
        var route = pipeline.Scene.Routes.Single();
        var source = pipeline.Scene.Terminals.Single(item => item.EndpointKind == ArchitectureV7EndpointKind.SourceDeparture);
        var destination = pipeline.Scene.Terminals.Single(item => item.EndpointKind == ArchitectureV7EndpointKind.DestinationArrival);
        var middle = new ArchitectureV7PhysicalPoint(source.Position.X, (source.Position.Y + destination.Position.Y) / 2d, "test-middle");
        var overshoot = new ArchitectureV7PhysicalPoint(source.Position.X + 100, middle.Y, "test-overshoot");
        var returned = new ArchitectureV7PhysicalPoint(source.Position.X, middle.Y, "test-return");
        var points = new[] { source.Position, middle, overshoot, returned, destination.Position };
        var cells = new[] { pipeline.Routes.Routes.Single().Cells[0] };
        var segments = points.Zip(points.Skip(1), (from, to) => new ArchitectureV7PhysicalSegment(route.PhysicalLinkId, from, to, new[] { 0 }, cells,
            "run:a:0", "lane:V:1:0", "run=run:a:0;lane=lane:V:1:0")).ToArray();
        var scene = new ArchitectureV7PhysicalSceneFreeze(pipeline.Scene.Rows, pipeline.Scene.Columns, pipeline.Scene.Nodes, pipeline.Scene.Terminals,
            new[] { new ArchitectureV7PhysicalRoute(route.PhysicalLinkId, points, segments, "test") }, pipeline.Scene.Diagnostics,
            pipeline.Scene.PlacementFingerprint, pipeline.Scene.RouteFingerprint, pipeline.Scene.AllocationFingerprint, pipeline.Scene.PhysicalSceneFingerprint);

        var report = Validate(pipeline, scene);

        Assert.Contains(report.Findings, finding => finding.Code == "PHYSICAL-COLLINEAR-REVERSAL");
        Assert.Contains(report.Findings, finding => finding.Code == "PHYSICAL-OVERSHOOT-RETURN");
    }

    [Fact]
    public void Physical_resource_provenance_must_match_the_frozen_run_lane_and_cells()
    {
        var pipeline = Build();
        var route = pipeline.Scene.Routes.Single();
        var corrupted = route.Segments.Select((segment, index) => index == 0
            ? new ArchitectureV7PhysicalSegment(segment.PhysicalLinkId, segment.Start, segment.End, segment.RouteCellIndices, segment.LogicalCells,
                "run:missing", segment.LaneId, "run=run:missing;lane=" + segment.LaneId)
            : segment).ToArray();
        var scene = new ArchitectureV7PhysicalSceneFreeze(pipeline.Scene.Rows, pipeline.Scene.Columns, pipeline.Scene.Nodes, pipeline.Scene.Terminals,
            new[] { new ArchitectureV7PhysicalRoute(route.PhysicalLinkId, route.Points, corrupted, route.Provenance) }, pipeline.Scene.Diagnostics,
            pipeline.Scene.PlacementFingerprint, pipeline.Scene.RouteFingerprint, pipeline.Scene.AllocationFingerprint, pipeline.Scene.PhysicalSceneFingerprint);

        var report = Validate(pipeline, scene);

        Assert.Contains(report.Findings, finding => finding.Code == "PHYSICAL-RESOURCE-PROVENANCE-MISMATCH");
    }

    [Fact]
    public void Route_through_unrelated_node_is_a_hard_final_scene_failure()
    {
        var pipeline = Build();
        var route = pipeline.Scene.Routes.Single();
        var segment = route.Segments.First(item => item.Start != item.End);
        var midX = (segment.Start.X + segment.End.X) / 2d;
        var midY = (segment.Start.Y + segment.End.Y) / 2d;
        var unrelated = segment.Start.Y == segment.End.Y
            ? new ArchitectureV7PhysicalBounds(midX - 1, midY - 5, midX + 1, midY + 5)
            : new ArchitectureV7PhysicalBounds(midX - 5, midY - 1, midX + 5, midY + 1);
        var scene = WithNode(pipeline.Scene, new ArchitectureV7PhysicalSceneNode("unrelated", unrelated, "test-unrelated-node"));

        var report = Validate(pipeline, scene);

        Assert.Contains(report.Findings, finding => finding.Code == "ROUTE-THROUGH-NODE" && finding.Provenance.Any(value => value == "intersected-node=unrelated"));
        Assert.False(report.IsStrictEligible);
    }

    [Fact]
    public void Route_with_clear_physical_separation_from_unrelated_node_remains_eligible_for_that_invariant()
    {
        var pipeline = Build();
        var route = pipeline.Scene.Routes.Single();
        var segment = route.Segments.First(item => item.Start != item.End);
        var midX = (segment.Start.X + segment.End.X) / 2d;
        var midY = (segment.Start.Y + segment.End.Y) / 2d;
        var unrelated = segment.Start.Y == segment.End.Y
            ? new ArchitectureV7PhysicalBounds(midX - 1, midY + 20, midX + 1, midY + 30)
            : new ArchitectureV7PhysicalBounds(midX + 20, midY - 1, midX + 30, midY + 1);
        var scene = WithNode(pipeline.Scene, new ArchitectureV7PhysicalSceneNode("near", unrelated, "test-near-node"));

        var report = Validate(pipeline, scene);

        Assert.DoesNotContain(report.Findings, finding => finding.Code == "ROUTE-THROUGH-NODE");
    }

    [Fact]
    public void Altered_route_fingerprint_is_rejected_before_normal_or_strict_eligibility()
    {
        var pipeline = Build();
        var routes = new ArchitectureV7LogicalRouteFreeze(pipeline.Routes.Routes, pipeline.Routes.Diagnostics, pipeline.Routes.PlacementFingerprint, pipeline.Routes.ProjectionFingerprint, "altered-route");
        var report = new ArchitectureV7FinalAcceptanceValidationStage().Validate(pipeline.Projection, pipeline.Ownership, pipeline.Sizing,
            pipeline.Reservation, pipeline.Placement, routes, pipeline.Allocation, pipeline.Scene, pipeline.Configuration);
        Assert.Contains(report.Findings, x => x.Code == "FINGERPRINT-MISMATCH");
        Assert.False(report.IsStrictEligible);
        Assert.False(report.IsNormalEligible);
    }

    [Fact]
    public void Non_external_node_on_external_row_is_rejected_with_node_provenance()
    {
        var pipeline = Build();
        var badNodes = pipeline.Placement.Nodes.Select(node => node.PhysicalNodeId == "s" ? node with { DiagramRow = 5, LogicalFootprint = new[] { (5, 1) } } : node).ToArray();
        var badPlacement = new ArchitectureV7PlacementFreeze(badNodes, pipeline.Placement.Projects, pipeline.Placement.External, pipeline.Placement.Standalone,
            pipeline.Placement.DiagramGrid, pipeline.Placement.Transforms, pipeline.Placement.ProjectionFingerprint, pipeline.Placement.OwnershipFingerprint,
            pipeline.Placement.SizingFingerprint, pipeline.Placement.ReservationFingerprint, pipeline.Placement.PlacementFingerprint);
        var report = new ArchitectureV7FinalAcceptanceValidationStage().Validate(pipeline.Projection, pipeline.Ownership, pipeline.Sizing,
            pipeline.Reservation, badPlacement, pipeline.Routes, pipeline.Allocation, pipeline.Scene, pipeline.Configuration);
        Assert.Contains(report.Findings, x => x.Code == "NON-EXTERNAL-ON-EXTERNAL-ROW" && x.SubjectId == "s");
    }

    [Fact]
    public void Missing_projected_relationship_is_rejected_instead_of_disappearing()
    {
        var pipeline = Build();
        var extra = new ArchitectureV7PhysicalLink("missing", "missing", "s", "t", "p", "p", "dependency");
        var projection = new ArchitectureV7PhysicalProjectionResult(pipeline.Projection.PhysicalNodes, pipeline.Projection.PhysicalLinks.Concat(new[] { extra }).ToArray(),
            pipeline.Projection.SemanticNodeToPhysicalNodeIds, pipeline.Projection.SemanticLinkToPhysicalLinkIds, pipeline.Projection.UnaccountedSemanticNodeIds,
            pipeline.Projection.UnaccountedSemanticLinkIds, pipeline.Projection.Diagnostics, pipeline.Projection.FreezeFingerprint);
        var report = new ArchitectureV7FinalAcceptanceValidationStage().Validate(projection, pipeline.Ownership, pipeline.Sizing,
            pipeline.Reservation, pipeline.Placement, pipeline.Routes, pipeline.Allocation, pipeline.Scene, pipeline.Configuration);
        Assert.Contains(report.Findings, x => x.Code == "MISSING-RELATIONSHIP-ACCOUNTING" && x.SubjectId == "missing");
    }

    internal static ArchitectureV7AcceptanceReport Validate(Pipeline pipeline, ArchitectureV7PhysicalSceneFreeze scene) =>
        new ArchitectureV7FinalAcceptanceValidationStage().Validate(pipeline.Projection, pipeline.Ownership, pipeline.Sizing, pipeline.Reservation,
            pipeline.Placement, pipeline.Routes, pipeline.Allocation, scene, pipeline.Configuration);

    private static ArchitectureV7PhysicalSceneFreeze WithNode(ArchitectureV7PhysicalSceneFreeze scene, ArchitectureV7PhysicalSceneNode node) =>
        new(scene.Rows, scene.Columns, scene.Nodes.Concat(new[] { node }).ToArray(), scene.Terminals, scene.Routes, scene.Diagnostics,
            scene.PlacementFingerprint, scene.RouteFingerprint, scene.AllocationFingerprint, scene.PhysicalSceneFingerprint);

    internal static Pipeline Build()
    {
        var projection = new ArchitectureV7PhysicalProjectionResult(
            new[] { new ArchitectureV7PhysicalNode("s", "s", "p", false, false, "s", "P.s", "Class", ArchitectureV7ProjectionMode.Canonical, null), new ArchitectureV7PhysicalNode("t", "t", "p", false, false, "t", "P.t", "Class", ArchitectureV7ProjectionMode.Canonical, null) },
            new[] { new ArchitectureV7PhysicalLink("a", "a", "s", "t", "p", "p", "dependency") },
            new Dictionary<string, IReadOnlyList<string>> { ["s"] = new[] { "s" }, ["t"] = new[] { "t" } },
            new Dictionary<string, IReadOnlyList<string>> { ["a"] = new[] { "a" } }, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<ArchitectureV7ProjectionDiagnostic>(), "projection");
        var ownership = new ArchitectureV7PositionalOwnershipResult(projection,
            new[] { new ArchitectureV7PositionalOwnershipDecision("s", "s", null, Array.Empty<string>(), Array.Empty<string>()), new ArchitectureV7PositionalOwnershipDecision("t", "t", "s", new[] { "s" }, Array.Empty<string>()) }, "projection", "ownership");
        var sizing = new ArchitectureV7NodeSpanSizingResult(ownership, new[] { Requirement("s"), Requirement("t") }, "sizing");
        var inspection = new ArchitectureV7ReservationInspectionResult(ownership, new Dictionary<string, int> { ["s"] = 0, ["t"] = 1 }, Array.Empty<ArchitectureV7ReservedDepthRequirement>(), Array.Empty<string>(), "inspection");
        var reservation = new ArchitectureV7ReservationReconciliationResult(inspection, new ArchitectureV7FrozenReservationTable(new[] { new ArchitectureV7FrozenReservation("External", "", 0, 0, 5, true) }, "reservation"));
        var nodes = new[] { Placement("s", 1), Placement("t", 3) };
        var grid = Enumerable.Range(0, 6).SelectMany(row => Enumerable.Range(0, 6).Select(column => new ArchitectureV7LogicalCell(row, column, ArchitectureV7CellCapability.RoutingAllowed | ArchitectureV7CellCapability.GeneralRouting))).ToArray();
        var placement = new ArchitectureV7PlacementFreeze(nodes, Array.Empty<ArchitectureV7ProjectRegion>(), new ArchitectureV7ExternalRegion(5, Array.Empty<string>(), Array.Empty<ArchitectureV7FrozenNodePlacement>()), new ArchitectureV7StandaloneRegion(0, 0, 0, Array.Empty<string>(), Array.Empty<ArchitectureV7FrozenNodePlacement>()), new ArchitectureV7CommonDiagramGrid(6, 6, grid), Array.Empty<ArchitectureV7ProjectTransform>(), "projection", "ownership", "sizing", "reservation", "placement");
        var routes = new ArchitectureV7LogicalRouteFreeze(new[] { new ArchitectureV7LogicalRoute("a", "a", "s", "t", new[] { new ArchitectureV7RouteCell(1, 1), new ArchitectureV7RouteCell(2, 1), new ArchitectureV7RouteCell(3, 1) }, true, Array.Empty<ArchitectureV7RouteDiagnostic>(), "test") }, Array.Empty<ArchitectureV7RouteDiagnostic>(), "placement", "projection", "routes");
        var allocation = new ArchitectureV7CollectivePostRoutingAllocationStage().Allocate(placement, routes, new ArchitectureV7AllocationConfiguration(4, 4, 0));
        var configuration = new ArchitectureV7PhysicalSceneConfiguration(10, 20, 20, 10, 20, 20, 1, 0, 2, 1, 4, 4, 0);
        var scene = new ArchitectureV7PhysicalSceneCompilationStage().Compile(placement, routes, allocation, configuration);
        return new Pipeline(projection, ownership, sizing, reservation, placement, routes, allocation, scene, configuration);
    }

    private static RendererCoordinateFixture BuildRendererCoordinateFixture()
    {
        var projects = new[]
        {
            new ArchitectureV7ProjectRegion("A", new ArchitectureV7ProjectTransform("A", 0, 0, 0, 0, 100, 100), Array.Empty<string>(), Array.Empty<ArchitectureV7LogicalCell>(), 100, 100),
            new ArchitectureV7ProjectRegion("B", new ArchitectureV7ProjectTransform("B", 0, 0, 0, 0, 100, 100), Array.Empty<string>(), Array.Empty<ArchitectureV7LogicalCell>(), 100, 100)
        };
        var origins = new Dictionary<string, (double X, double Y)>
        {
            ["A"] = (10000, 5000),
            ["B"] = (30000, 12000)
        };
        var relativeNodes = new[]
        {
            new RendererNode("A-anchor", "A", new ArchitectureV7PhysicalBounds(0, 0, 50, 50)),
            new RendererNode("A-source", "A", new ArchitectureV7PhysicalBounds(100, 100, 300, 180)),
            new RendererNode("A-target", "A", new ArchitectureV7PhysicalBounds(700, 500, 900, 580)),
            new RendererNode("A-obstacle", "A", new ArchitectureV7PhysicalBounds(400, 650, 520, 730)),
            new RendererNode("B-anchor", "B", new ArchitectureV7PhysicalBounds(0, 0, 50, 50)),
            new RendererNode("B-source", "B", new ArchitectureV7PhysicalBounds(100, 100, 300, 180)),
            new RendererNode("B-target", "B", new ArchitectureV7PhysicalBounds(700, 500, 900, 580)),
            new RendererNode("B-obstacle", "B", new ArchitectureV7PhysicalBounds(400, 650, 520, 730))
        };
        var nodes = relativeNodes.Select(node => new RendererGlobalNode(node, origins[node.ProjectId])).ToArray();
        var links = new[] { new RendererLink("A-link", "A", "A-source", "A-target"), new RendererLink("B-link", "B", "B-source", "B-target") };
        var physicalNodes = nodes.Select(node => new ArchitectureV7PhysicalNode(node.PhysicalNodeId, node.PhysicalNodeId, node.ProjectId, false, false, node.PhysicalNodeId, node.PhysicalNodeId, "Class", ArchitectureV7ProjectionMode.Canonical, null)).ToArray();
        var physicalLinks = links.Select(link => new ArchitectureV7PhysicalLink(link.LinkId, link.LinkId, link.SourceId, link.TargetId, link.ProjectId, link.ProjectId, "dependency")).ToArray();
        var projection = new ArchitectureV7PhysicalProjectionResult(physicalNodes, physicalLinks,
            physicalNodes.ToDictionary(node => node.PhysicalNodeId, node => (IReadOnlyList<string>)new[] { node.PhysicalNodeId }),
            physicalLinks.ToDictionary(link => link.PhysicalLinkId, link => (IReadOnlyList<string>)new[] { link.PhysicalLinkId }),
            Array.Empty<string>(), Array.Empty<string>(), Array.Empty<ArchitectureV7ProjectionDiagnostic>(), "renderer-projection");
        var placementNodes = nodes.Select((node, index) => new ArchitectureV7FrozenNodePlacement(node.PhysicalNodeId, node.PhysicalNodeId, node.ProjectId, index * 2, index, 1, 1,
            new[] { (index * 2, index) }, false, false, false, "tree", node.PhysicalNodeId, node.PhysicalNodeId, "renderer-fixture")).ToArray();
        var cell = new ArchitectureV7LogicalCell(0, 0, ArchitectureV7CellCapability.RoutingAllowed | ArchitectureV7CellCapability.GeneralRouting);
        var placement = new ArchitectureV7PlacementFreeze(placementNodes, projects,
            new ArchitectureV7ExternalRegion(0, Array.Empty<string>(), Array.Empty<ArchitectureV7FrozenNodePlacement>()),
            new ArchitectureV7StandaloneRegion(0, 0, 0, Array.Empty<string>(), Array.Empty<ArchitectureV7FrozenNodePlacement>()),
            new ArchitectureV7CommonDiagramGrid(1, 1, new[] { cell }), projects.Select(project => project.Transform).ToArray(),
            "renderer-projection", "renderer-ownership", "renderer-sizing", "renderer-reservation", "renderer-placement");
        var sceneNodes = nodes.Select(node => new ArchitectureV7PhysicalSceneNode(node.PhysicalNodeId, node.Global, "renderer-fixture")).ToArray();
        var sceneRoutes = links.Select(link => CreateRendererRoute(link, origins[link.ProjectId], relativeNodes.Single(node => node.PhysicalNodeId == link.SourceId), relativeNodes.Single(node => node.PhysicalNodeId == link.TargetId))).ToArray();
        var relativeRoutePoints = links.ToDictionary(link => link.LinkId, link => (IReadOnlyList<(double X, double Y)>)CreateRelativeRoutePoints(relativeNodes.Single(node => node.PhysicalNodeId == link.SourceId), relativeNodes.Single(node => node.PhysicalNodeId == link.TargetId)));
        var terminals = links.SelectMany(link =>
        {
            var origin = origins[link.ProjectId];
            var source = relativeNodes.Single(node => node.PhysicalNodeId == link.SourceId).Relative;
            var target = relativeNodes.Single(node => node.PhysicalNodeId == link.TargetId).Relative;
            return new[]
            {
                new ArchitectureV7PhysicalTerminal(link.LinkId, link.SourceId, ArchitectureV7EndpointKind.SourceDeparture, 0, Point(origin, source.Left + 200, source.Top + 40), "renderer-fixture"),
                new ArchitectureV7PhysicalTerminal(link.LinkId, link.TargetId, ArchitectureV7EndpointKind.DestinationArrival, 0, Point(origin, target.Left, target.Top + 20), "renderer-fixture")
            };
        }).ToArray();
        var scene = new ArchitectureV7PhysicalSceneFreeze(Array.Empty<ArchitectureV7PhysicalTrackDimension>(), Array.Empty<ArchitectureV7PhysicalTrackDimension>(), sceneNodes, terminals, sceneRoutes,
            Array.Empty<ArchitectureV7PhysicalSceneDiagnostic>(), "renderer-placement", "renderer-routes", "renderer-allocation", "renderer-scene");
        return new RendererCoordinateFixture(new ArchitectureDiagramModel(Array.Empty<ArchitectureProject>(), Array.Empty<ArchitectureExternalNode>(), Array.Empty<ArchitectureLink>(), null), projection, placement, scene,
            projects, origins, nodes, links, relativeRoutePoints);
    }

    private static ArchitectureV7PhysicalRoute CreateRendererRoute(RendererLink link, (double X, double Y) origin, RendererNode source, RendererNode target)
    {
        var points = CreateRelativeRoutePoints(source, target).Select(point => Point(origin, point.X, point.Y)).ToArray();
        var segments = points.Zip(points.Skip(1), (start, end) => new ArchitectureV7PhysicalSegment(link.LinkId, start, end, Array.Empty<int>(), Array.Empty<ArchitectureV7RouteCell>(), "renderer-run", "renderer-lane", "renderer-fixture")).ToArray();
        return new ArchitectureV7PhysicalRoute(link.LinkId, points, segments, "renderer-fixture");
    }

    private static IReadOnlyList<(double X, double Y)> CreateRelativeRoutePoints(RendererNode source, RendererNode target) => new[]
        {
            (source.Relative.Left + 200, source.Relative.Top + 40),
            (source.Relative.Left + 200, 250d),
            (700d, 250d),
            (700d, target.Relative.Top + 20)
        };

    private static ArchitectureV7PhysicalPoint Point((double X, double Y) origin, double relativeX, double relativeY) =>
        new(origin.X + relativeX, origin.Y + relativeY, "renderer-fixture");

    private static bool IntersectsInterior(ArchitectureV7PhysicalPoint start, ArchitectureV7PhysicalPoint end, ArchitectureV7PhysicalBounds bounds) =>
        start.X == end.X
            ? start.X > bounds.Left && start.X < bounds.Right && Math.Max(start.Y, end.Y) > bounds.Top && Math.Min(start.Y, end.Y) < bounds.Bottom
            : start.Y == end.Y && start.Y > bounds.Top && start.Y < bounds.Bottom && Math.Max(start.X, end.X) > bounds.Left && Math.Min(start.X, end.X) < bounds.Right;

    private sealed record RendererNode(string PhysicalNodeId, string ProjectId, ArchitectureV7PhysicalBounds Relative);
    private sealed record RendererGlobalNode(RendererNode Source, (double X, double Y) Origin)
    {
        public string PhysicalNodeId => Source.PhysicalNodeId;
        public string ProjectId => Source.ProjectId;
        public ArchitectureV7PhysicalBounds Relative => Source.Relative;
        public ArchitectureV7PhysicalBounds Global => new(Source.Relative.Left + Origin.X, Source.Relative.Top + Origin.Y, Source.Relative.Right + Origin.X, Source.Relative.Bottom + Origin.Y);
    }
    private sealed record RendererLink(string LinkId, string ProjectId, string SourceId, string TargetId);
    private sealed record RendererCoordinateFixture(ArchitectureDiagramModel Diagram, ArchitectureV7PhysicalProjectionResult Projection, ArchitectureV7PlacementFreeze Placement,
        ArchitectureV7PhysicalSceneFreeze Scene, IReadOnlyList<ArchitectureV7ProjectRegion> Projects, IReadOnlyDictionary<string, (double X, double Y)> Origins,
        IReadOnlyList<RendererGlobalNode> Nodes, IReadOnlyList<RendererLink> Links, IReadOnlyDictionary<string, IReadOnlyList<(double X, double Y)>> RelativeRoutePoints);

    private static ArchitectureV7NodeSpanRequirement Requirement(string id) => new(id, 1, 1, 0, 0, 1, 10, 10, "test");
    private static ArchitectureV7FrozenNodePlacement Placement(string id, int row) => new(id, id, "p", row, 1, 1, 1, new[] { (row, 1) }, false, false, false, "tree", id, id, "test");
    internal sealed record Pipeline(ArchitectureV7PhysicalProjectionResult Projection, ArchitectureV7PositionalOwnershipResult Ownership, ArchitectureV7NodeSpanSizingResult Sizing, ArchitectureV7ReservationReconciliationResult Reservation, ArchitectureV7PlacementFreeze Placement, ArchitectureV7LogicalRouteFreeze Routes, ArchitectureV7CollectiveAllocationFreeze Allocation, ArchitectureV7PhysicalSceneFreeze Scene, ArchitectureV7PhysicalSceneConfiguration Configuration);
}
