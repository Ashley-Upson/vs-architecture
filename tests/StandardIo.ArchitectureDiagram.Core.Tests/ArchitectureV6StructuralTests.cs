using System;
using System.Collections.Generic;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core.Models.ArchitectureV6;
using StandardIo.ArchitectureDiagram.Core.Models.Architectures;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.ArchitectureV6;
using Xunit;
using ArchitectureDiagramModel = StandardIo.ArchitectureDiagram.Core.Models.Architectures.ArchitectureDiagram;

namespace StandardIo.ArchitectureDiagram.Core.Tests;

public sealed class ArchitectureV6StructuralTests
{
    [Fact]
    public void Planner_preserves_semantic_projection_and_physical_identity()
    {
        var plan = new ArchitectureDiagramV6Planner().Plan(Request());

        Assert.Equal(3, plan.PhysicalNodes.Count);
        Assert.Equal(2, plan.PhysicalLinks.Count);
        Assert.Single(plan.Projection!.SemanticNodeToPhysicalNodeIds["shared"]);
        Assert.Contains("physical:root", plan.PhysicalNodes.Select(node => node.PhysicalNodeId));
        Assert.Equal("physical:root", plan.PhysicalLinks[0].SourcePhysicalNodeId);
    }

    [Fact]
    public void Planner_retains_empty_project_grid_shells_without_placing_nodes()
    {
        var plan = new ArchitectureDiagramV6Planner().Plan(Request());

        var grid = Assert.Single(plan.ProjectGrids);
        Assert.Equal("project:project:p", grid.Grid.Id.Value);
        Assert.Equal(3, grid.OwnedPhysicalNodeIds.Count);
        Assert.Empty(grid.Grid.Rows);
        Assert.Empty(grid.Grid.Columns);
        Assert.Empty(grid.Grid.Cells);
        Assert.Empty(plan.NodePlacements);
        Assert.Empty(plan.SubtreeReservations);
    }

    [Fact]
    public void Planner_marks_placement_routing_sizing_and_geometry_as_deferred()
    {
        var plan = new ArchitectureDiagramV6Planner().Plan(Request());

        Assert.True(plan.StageStatus.ProjectionCompleted);
        Assert.False(plan.StageStatus.LogicalPlacementCompleted);
        Assert.True(plan.StageStatus.RoutingDeferred);
        Assert.True(plan.StageStatus.SizingDeferred);
        Assert.True(plan.StageStatus.AbsoluteGeometryDeferred);
        Assert.Contains(plan.Diagnostics.Findings, finding => finding.Code == "V6PlacementDeferred");
        Assert.Contains(plan.Diagnostics.Findings, finding => finding.Code == "V6RoutePlanningDeferred");
        Assert.Contains(plan.Diagnostics.Findings, finding => finding.Code == "V6SizingDeferred");
        Assert.Contains(plan.Diagnostics.Findings, finding => finding.Code == "V6GeometryDeferred");
    }

    [Fact]
    public void Planner_cycle_projection_terminates_without_routes_or_placement()
    {
        var request = Request() with
        {
            SemanticModel = Request().SemanticModel with
            {
                Links = new[]
                {
                    new ArchitectureLink("root-shared", "root", "shared", "internal"),
                    new ArchitectureLink("shared-root", "shared", "root", "internal")
                }
            }
        };

        var plan = new ArchitectureDiagramV6Planner().Plan(request);

        Assert.Equal(3, plan.PhysicalNodes.Count);
        Assert.Equal(2, plan.PhysicalLinks.Count);
        Assert.NotEmpty(plan.Projection!.CycleSemanticNodeIds);
        Assert.Empty(plan.Routes);
    }

    [Fact]
    public void Renderer_emits_only_minimal_page_shell()
    {
        var plan = new ArchitectureDiagramV6Planner().Plan(Request());
        var page = new DrawioArchitectureV6Renderer().Render(plan, new ArchitectureRenderRequest(ArchitectureValidationMode.Normal, "drawio", true));
        var cells = page.GraphModel.Descendants("mxCell").ToArray();

        Assert.Equal("architecture", page.StablePageKey);
        Assert.Equal(2, cells.Length);
        Assert.DoesNotContain(cells, cell => (string?)cell.Attribute("vertex") == "1");
        Assert.DoesNotContain(cells, cell => (string?)cell.Attribute("edge") == "1");
        Assert.Contains(page.Diagnostics, diagnostic => diagnostic.Code == "V6MinimalPage");
        Assert.Contains(page.Diagnostics, diagnostic => diagnostic.Code == "V6RoutingDeferred");
    }

    [Fact]
    public void Renderer_does_not_reconstruct_or_invent_geometry()
    {
        var plan = new ArchitectureDiagramV6Planner().Plan(Request());
        var page = new DrawioArchitectureV6Renderer().Render(plan, new ArchitectureRenderRequest(ArchitectureValidationMode.Diagnostic, "drawio", true));

        Assert.DoesNotContain(page.GraphModel.Descendants(), element => element.Name.LocalName == "mxGeometry" && element.Parent?.Name.LocalName == "mxCell");
        Assert.Contains(page.Diagnostics, diagnostic => diagnostic.Code == "V6PlacementDeferred");
    }

    [Fact]
    public void Structural_route_models_remain_available_without_an_active_route_builder()
    {
        var grid = new PlanningGridId("project:p");
        var cell = new PlanningGridCellId(grid, new PlanningGridRowId("row:0"), new PlanningGridColumnId("column:0"));
        var route = new PlannedGridRoute(
            "physical-link:l",
            new NodeEndpoint("physical:s", GridSide.Bottom, "source:0", 0),
            new[] { new PlannedGridRouteStep(grid, cell, GridSide.Top, GridSide.Right, RouteStepRole.SourceExit) },
            Array.Empty<GridTransition>(),
            new NodeEndpoint("physical:t", GridSide.Top, "target:0", 0),
            RouteTopologyFamily.AdjacentDownward,
            "p",
            "p");

        Assert.Equal(GridSide.Bottom, route.Source.Side);
        Assert.Equal(RouteStepRole.SourceExit, Assert.Single(route.Steps).Role);
        Assert.Equal(GridSide.Top, route.Destination.Side);
    }

    private static ArchitecturePlanningRequest Request() => new(
        new ArchitectureDiagramModel(
            new[]
            {
                new ArchitectureProject("project:p", "Project", new[]
                {
                    new ArchitectureNode("root", "project:p", "RootService", "Project.RootService", "Class", "root", Array.Empty<string>()),
                    new ArchitectureNode("shared", "project:p", "SharedService", "Project.SharedService", "Class", "shared", Array.Empty<string>()),
                    new ArchitectureNode("standalone", "project:p", "Standalone", "Project.Standalone", "Class", "standalone", Array.Empty<string>())
                }, "project:p")
            },
            Array.Empty<ArchitectureExternalNode>(),
            new[] { new ArchitectureLink("root-shared", "root", "shared", "internal"), new ArchitectureLink("shared-root", "shared", "root", "internal") },
            null),
        new ArchitectureSelectionScope("SelectedProjects", new[] { "project:p" }, Array.Empty<string>()),
        new ArchitectureGenerationSettingsSnapshot("drawio", "[External]", Array.Empty<string>(), Array.Empty<string>()),
        new NodeProjectionPolicy(NodeProjectionMode.Canonical, Array.Empty<string>()),
        new ProjectPlacementPolicy(true, "border"),
        new NodePlacementPolicy("*OrchestrationService", 120, 60, 20, 40),
        new RoutePlanningPolicy(12, 8, "[External]"),
        new GridSizingPolicy(20, 20, 20, 30),
        new ValidationPolicy(ArchitectureValidationMode.Normal),
        Array.Empty<string>(),
        Array.Empty<string>());
}
