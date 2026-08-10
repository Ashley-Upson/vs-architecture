using System;
using System.Collections.Generic;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core.Models.ArchitectureV7;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.ArchitectureV7;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core.Tests;

public sealed class ArchitectureV7AcceptanceTests
{
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

    private static ArchitectureV7AcceptanceReport Validate(Pipeline pipeline, ArchitectureV7PhysicalSceneFreeze scene) =>
        new ArchitectureV7FinalAcceptanceValidationStage().Validate(pipeline.Projection, pipeline.Ownership, pipeline.Sizing, pipeline.Reservation,
            pipeline.Placement, pipeline.Routes, pipeline.Allocation, scene, pipeline.Configuration);

    private static Pipeline Build()
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

    private static ArchitectureV7NodeSpanRequirement Requirement(string id) => new(id, 1, 1, 0, 0, 1, 10, 10, "test");
    private static ArchitectureV7FrozenNodePlacement Placement(string id, int row) => new(id, id, "p", row, 1, 1, 1, new[] { (row, 1) }, false, false, false, "tree", id, id, "test");
    private sealed record Pipeline(ArchitectureV7PhysicalProjectionResult Projection, ArchitectureV7PositionalOwnershipResult Ownership, ArchitectureV7NodeSpanSizingResult Sizing, ArchitectureV7ReservationReconciliationResult Reservation, ArchitectureV7PlacementFreeze Placement, ArchitectureV7LogicalRouteFreeze Routes, ArchitectureV7CollectiveAllocationFreeze Allocation, ArchitectureV7PhysicalSceneFreeze Scene, ArchitectureV7PhysicalSceneConfiguration Configuration);
}
