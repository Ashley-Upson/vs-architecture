using System;
using System.Collections.Generic;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core.Models.ArchitectureV6;
using StandardIo.ArchitectureDiagram.Core.Models.Architectures;
using StandardIo.ArchitectureDiagram.Core.Models.Drawios;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.ArchitectureV6;
using Xunit;
using ArchitectureDiagramModel = StandardIo.ArchitectureDiagram.Core.Models.Architectures.ArchitectureDiagram;

namespace StandardIo.ArchitectureDiagram.Core.Tests;

public sealed class ArchitectureV6StructuralTests
{
    [Fact]
    public void Request_represents_canonical_and_duplicate_projection_policies()
    {
        var canonical = Request(NodeProjectionMode.Canonical);
        var duplicate = Request(NodeProjectionMode.DuplicateBranches);

        Assert.Equal(NodeProjectionMode.Canonical, canonical.NodeProjection.Mode);
        Assert.Equal(NodeProjectionMode.DuplicateBranches, duplicate.NodeProjection.Mode);
        Assert.Equal("drawio", canonical.GenerationSettings.OutputRenderer);
    }

    [Fact]
    public void Grid_keeps_capability_occupancy_and_reservations_separate()
    {
        var gridId = new PlanningGridId("project:p");
        var cellId = new PlanningGridCellId(gridId, new PlanningGridRowId("r0"), new PlanningGridColumnId("c0"));
        var cell = new PlanningGridCell(cellId, CellCapability.RoutingAllowed | CellCapability.NodeAllowed,
            CellOccupancy.Empty, new[] { "subtree:root" });

        Assert.True(cell.Capabilities.HasFlag(CellCapability.RoutingAllowed));
        Assert.True(cell.Capabilities.HasFlag(CellCapability.NodeAllowed));
        Assert.Equal(CellOccupancy.Empty, cell.Occupancy);
        Assert.Equal("subtree:root", Assert.Single(cell.ReservationIds));
    }

    [Fact]
    public void Node_placement_requires_positive_odd_width_and_one_anchor()
    {
        var grid = new PlanningGridId("project:p");
        var row = new PlanningGridRowId("r0");
        var column = new PlanningGridColumnId("c0");
        var anchor = new PlanningGridCellId(grid, row, column);
        var placement = new PlannedNodePlacement("physical:p", grid, anchor, 3, 1, new[] { anchor }, column);

        Assert.Equal(anchor, placement.AnchorCellId);
        Assert.Equal(3, placement.ColumnSpan);
        Assert.Throws<ArgumentException>(() => new PlannedNodePlacement("physical:p", grid, anchor, 2, 1, new[] { anchor }, column));
    }

    [Fact]
    public void Subtree_reservations_support_nested_ownership()
    {
        var grid = new PlanningGridId("project:p");
        var cell = new PlanningGridCellId(grid, new PlanningGridRowId("r0"), new PlanningGridColumnId("c0"));
        var root = new SubtreeReservation("subtree:root", "node:root", grid, new[] { cell }, null);
        var child = new SubtreeReservation("subtree:child", "node:child", grid, new[] { cell }, root.SubtreeId);

        Assert.Equal(root.SubtreeId, child.AncestorReservationId);
        Assert.Equal("node:child", child.PositionalOwnerId);
    }

    [Fact]
    public void Completed_plan_copies_collection_inputs_and_planner_builds_empty_structural_plan()
    {
        var request = Request(NodeProjectionMode.Canonical);
        var plan = new ArchitectureDiagramV6Planner().Plan(request);
        var source = new[] { new PlannedPhysicalNode("physical:p", "semantic:p", PhysicalNodeProjectionMode.Canonical, "owner:p", "p", null, false, false) };
        var empty = EmptyPlan(request, source);

        Assert.Empty(plan.PhysicalNodes);
        Assert.NotSame(source, empty.PhysicalNodes);
        Assert.Single(empty.PhysicalNodes);
        Assert.True(plan.StageStatus.ProjectionCompleted);
        Assert.True(plan.StageStatus.LogicalPlacementCompleted);
        Assert.True(plan.StageStatus.RoutingDeferred);
    }

    [Fact]
    public void Route_structure_retains_endpoints_steps_and_cross_grid_transition()
    {
        var diagramGrid = new PlanningGridId("diagram");
        var projectGrid = new PlanningGridId("project:p");
        var sourceCell = new PlanningGridCellId(projectGrid, new PlanningGridRowId("r0"), new PlanningGridColumnId("c0"));
        var destinationCell = new PlanningGridCellId(diagramGrid, new PlanningGridRowId("r1"), new PlanningGridColumnId("c0"));
        var route = new PlannedGridRoute("physical:l", new NodeEndpoint("physical:s", GridSide.Bottom, "exit:0", 0),
            new[] { new PlannedGridRouteStep(projectGrid, sourceCell, GridSide.Top, GridSide.Right, RouteStepRole.SourceExit) },
            new[] { new GridTransition(projectGrid, sourceCell, diagramGrid, destinationCell, "project-boundary", "semantic:l") },
            new NodeEndpoint("physical:t", GridSide.Top, "entry:0", 0), RouteTopologyFamily.CrossProject, "p", "q");

        Assert.Equal("physical:s", route.Source.PhysicalNodeId);
        Assert.Equal(RouteStepRole.SourceExit, Assert.Single(route.Steps).Role);
        Assert.Equal("project-boundary", Assert.Single(route.Transitions).OwnershipTransition);
        Assert.Equal(RouteTopologyFamily.CrossProject, route.TopologyFamily);
    }

    [Fact]
    public void V6_renderer_consumes_completed_plan_and_emits_only_document_primitives()
    {
        var plan = new ArchitectureDiagramV6Planner().Plan(Request(NodeProjectionMode.Canonical));
        var page = new DrawioArchitectureV6Renderer().Render(plan, new ArchitectureRenderRequest(ArchitectureValidationMode.Normal, "drawio", true));

        Assert.Equal("architecture", page.StablePageKey);
        Assert.Equal("mxGraphModel", page.GraphModel.Name.LocalName);
        Assert.Empty(page.Diagnostics);
    }

    [Fact]
    public void Planner_canonical_mode_preserves_shared_and_external_relationships()
    {
        var plan = new ArchitectureDiagramV6Planner().Plan(GraphRequest(NodeProjectionMode.Canonical));
        var validator = new ArchitectureDiagramV6Validator().Validate(plan);

        Assert.True(validator.IsValid, string.Join("; ", validator.Findings.Select(finding => finding.Message)) + " placements=" + string.Join(",", plan.NodePlacements.Select(item => $"{item.PhysicalNodeId}:{item.AnchorCellId}")));
        Assert.Equal(6, plan.PhysicalNodes.Count);
        Assert.Equal(4, plan.PhysicalLinks.Count);
        Assert.Single(plan.Projection!.SemanticNodeToPhysicalNodeIds["shared"]);
        Assert.Single(plan.Projection.ExternalPhysicalNodeIds);
        Assert.Empty(plan.Projection.UnaccountedSemanticLinkIds);
        Assert.Contains(plan.NodeMetadata, node => node.IsExternal && node.PositionalOwnerId is not null);
        Assert.Single(plan.ProjectGrids);
        Assert.All(plan.NodePlacements, placement =>
        {
            Assert.True(placement.ColumnSpan >= 3);
            Assert.True(placement.ColumnSpan % 2 == 1);
            Assert.Contains(placement.AnchorCellId, placement.Footprint);
        });
        Assert.Contains(plan.SubtreeReservations, reservation => reservation.AncestorReservationId is not null);
        Assert.Contains(plan.ProjectGrids.Single().Grid.Cells.Values, cell => cell.Occupancy == CellOccupancy.Empty && cell.Capabilities.HasFlag(CellCapability.RoutingAllowed));
        var rootLayer = plan.NodeMetadata.Single(node => node.SemanticNodeId == "root").LogicalLayer;
        var childLayer = plan.NodeMetadata.Single(node => node.SemanticNodeId == "child").LogicalLayer;
        Assert.True(childLayer > rootLayer);
    }

    [Fact]
    public void Planner_duplicate_mode_creates_deterministic_branch_instances_with_provenance()
    {
        var request = GraphRequest(NodeProjectionMode.DuplicateBranches) with
        {
            NodeProjection = new NodeProjectionPolicy(NodeProjectionMode.DuplicateBranches, new[] { "Shared" })
        };
        var plan = new ArchitectureDiagramV6Planner().Plan(request);
        var shared = plan.PhysicalNodes.Where(node => node.SemanticNodeId == "shared").ToArray();

        Assert.Equal(2, shared.Length);
        Assert.All(shared.Where(node => node.ProjectionMode == PhysicalNodeProjectionMode.DuplicateBranch), node =>
        {
            Assert.NotNull(node.DuplicationProvenance);
            Assert.Contains("Originating link", node.DuplicationProvenance!.Reason);
        });
        var validation = new ArchitectureDiagramV6Validator().Validate(plan);
        Assert.True(validation.IsValid, string.Join("; ", validation.Findings.Select(finding => finding.Message)));
    }

    [Fact]
    public void Planner_cycles_terminate_and_receive_finite_positional_ownership()
    {
        var request = GraphRequest(NodeProjectionMode.Canonical, new[] {
            new ArchitectureLink("cycle-root-child", "root", "child", "internal"),
            new ArchitectureLink("cycle-child-root", "child", "root", "internal")
        });
        var plan = new ArchitectureDiagramV6Planner().Plan(request);

        Assert.Equal(6, plan.PhysicalNodes.Count);
        Assert.Equal(2, plan.PhysicalLinks.Count);
        Assert.NotEmpty(plan.Projection!.CycleSemanticNodeIds);
        Assert.True(plan.NodePlacements.Count == 6, string.Join(",", plan.NodePlacements.Select(placement => placement.PhysicalNodeId)));
        var cycleValidation = new ArchitectureDiagramV6Validator().Validate(plan);
        Assert.True(cycleValidation.IsValid, string.Join("; ", cycleValidation.Findings.Select(finding => finding.Message)));
    }

    [Fact]
    public void Planner_assigns_baseline_nodes_to_one_logical_row_and_keeps_standalones_separate()
    {
        var request = GraphRequest(NodeProjectionMode.Canonical) with
        {
            NodePlacement = new NodePlacementPolicy("*OrchestrationService", 120, 60, 20, 40)
        };
        var plan = new ArchitectureDiagramV6Planner().Plan(request);
        var baselineRows = plan.NodeMetadata.Where(node => node.IsBaseline).Select(node => node.LogicalLayer).Distinct().ToArray();

        Assert.Single(baselineRows);
        Assert.Contains(plan.Projection!.StandalonePhysicalNodeIds, id => plan.NodeMetadata.Single(node => node.PhysicalNodeId == id).PositionalOwnerId is null);
        Assert.All(plan.NodePlacements, placement => Assert.Equal(1, placement.RowSpan));
    }

    [Fact]
    public void Planner_output_is_deterministic_for_repeated_requests()
    {
        var first = new ArchitectureDiagramV6Planner().Plan(GraphRequest(NodeProjectionMode.Canonical));
        var second = new ArchitectureDiagramV6Planner().Plan(GraphRequest(NodeProjectionMode.Canonical));
        var firstSignature = string.Join("|", first.NodePlacements.Select(placement => $"{placement.PhysicalNodeId}:{placement.AnchorCellId}"));
        var secondSignature = string.Join("|", second.NodePlacements.Select(placement => $"{placement.PhysicalNodeId}:{placement.AnchorCellId}"));

        Assert.Equal(firstSignature, secondSignature);
    }

    private static ArchitecturePlanningRequest Request(NodeProjectionMode mode) => new(
        new ArchitectureDiagramModel(Array.Empty<ArchitectureProject>(), Array.Empty<ArchitectureExternalNode>(), Array.Empty<ArchitectureLink>(), null),
        new ArchitectureSelectionScope("SelectedProjects", Array.Empty<string>(), Array.Empty<string>()),
        new ArchitectureGenerationSettingsSnapshot("drawio", "[External]", Array.Empty<string>(), Array.Empty<string>()),
        new NodeProjectionPolicy(mode, Array.Empty<string>()),
        new ProjectPlacementPolicy(true, "border"),
        new NodePlacementPolicy("*OrchestrationService", 120, 60, 20, 40),
        new RoutePlanningPolicy(12, 8, "[External]"),
        new GridSizingPolicy(20, 20, 20, 30),
        new ValidationPolicy(ArchitectureValidationMode.Normal),
        Array.Empty<string>(), Array.Empty<string>());

    private static ArchitecturePlanningRequest GraphRequest(NodeProjectionMode mode, IReadOnlyList<ArchitectureLink>? links = null) => new(
        new ArchitectureDiagramModel(
            new[] { new ArchitectureProject("project:p", "Project", new[]
            {
                new ArchitectureNode("root", "project:p", "RootOrchestrationService", "Project.RootOrchestrationService", "Class", "root", Array.Empty<string>()),
                new ArchitectureNode("child", "project:p", "ChildService", "Project.ChildService", "Class", "child", Array.Empty<string>()),
                new ArchitectureNode("other", "project:p", "OtherService", "Project.OtherService", "Class", "other", Array.Empty<string>()),
                new ArchitectureNode("shared", "project:p", "Shared", "Project.Shared", "Class", "shared", Array.Empty<string>()),
                new ArchitectureNode("standalone", "project:p", "Standalone", "Project.Standalone", "Class", "standalone", Array.Empty<string>())
            }, "project:p") },
            new[] { new ArchitectureExternalNode("external", "IEventHub", "External", "external", "External.IEventHub", "[External]") },
            links?.ToArray() ?? new[]
            {
                new ArchitectureLink("root-child", "root", "child", "internal"),
                new ArchitectureLink("root-shared", "root", "shared", "internal"),
                new ArchitectureLink("other-shared", "other", "shared", "internal"),
                new ArchitectureLink("child-external", "child", "external", "external")
            }, null),
        new ArchitectureSelectionScope("SelectedProjects", new[] { "project:p" }, Array.Empty<string>()),
        new ArchitectureGenerationSettingsSnapshot("drawio", "[External]", Array.Empty<string>(), Array.Empty<string>()),
        new NodeProjectionPolicy(mode, Array.Empty<string>()),
        new ProjectPlacementPolicy(true, "border"),
        new NodePlacementPolicy("*OrchestrationService", 120, 60, 20, 40),
        new RoutePlanningPolicy(12, 8, "[External]"),
        new GridSizingPolicy(20, 20, 20, 30),
        new ValidationPolicy(ArchitectureValidationMode.Normal),
        Array.Empty<string>(), Array.Empty<string>());

    private static PlannedArchitectureDiagram EmptyPlan(ArchitecturePlanningRequest request, IReadOnlyList<PlannedPhysicalNode> nodes)
    {
        var gridId = new PlanningGridId("diagram");
        var grid = new PlanningGrid(gridId, Array.Empty<PlanningGridRow>(), Array.Empty<PlanningGridColumn>(),
            new Dictionary<PlanningGridCellId, PlanningGridCell>(), new GridTransform(gridId, new RelativePoint(0, 0)));
        var routing = new DiagramRoutingGrid(grid, Array.Empty<RelativeRectangle>(), Array.Empty<GridTransition>());
        var metrics = new ArchitecturePlanningMetrics(0, 0, nodes.Count, 0, 0, 0, 0, 0, 0,
            new Dictionary<string, int>(), 0, 0, 0, null, 0);
        return new PlannedArchitectureDiagram(request, nodes, Array.Empty<PlannedPhysicalLink>(), routing,
            Array.Empty<ProjectRoutingGrid>(), Array.Empty<PlannedNodePlacement>(), Array.Empty<PlannedGridRoute>(),
            new GridTrackSizingPlan(Array.Empty<PlanningGridRow>(), Array.Empty<PlanningGridColumn>(), Array.Empty<GridTrackConstraint>(), null),
            new ArchitecturePlanningDiagnostics(Array.Empty<ArchitecturePlanningDiagnostic>(), metrics));
    }
}
