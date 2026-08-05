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
    public void Planner_builds_sparse_project_grid_with_one_anchor_per_node()
    {
        var plan = new ArchitectureDiagramV6Planner().Plan(Request());

        var grid = Assert.Single(plan.ProjectGrids);
        Assert.Equal("project:project:p", grid.Grid.Id.Value);
        Assert.Equal(3, grid.OwnedPhysicalNodeIds.Count);
        Assert.NotEmpty(grid.Grid.Rows);
        Assert.NotEmpty(grid.Grid.Columns);
        Assert.Equal(3, plan.NodePlacements.Count);
        Assert.Equal(3, plan.NodePlacements.Select(item => item.AnchorCellId).Distinct().Count());
        Assert.All(plan.NodePlacements, placement =>
        {
            Assert.True(placement.ColumnSpan >= 3);
            Assert.Equal(1, placement.ColumnSpan % 2);
            Assert.Contains(placement.AnchorCellId, placement.Footprint);
        });
        Assert.Equal(3, plan.SubtreeReservations.Count);
    }

    [Fact]
    public void Planner_marks_placement_complete_and_later_stages_deferred()
    {
        var plan = new ArchitectureDiagramV6Planner().Plan(Request());

        Assert.True(plan.StageStatus.ProjectionCompleted);
        Assert.True(plan.StageStatus.LogicalPlacementCompleted);
        Assert.True(plan.StageStatus.AbstractRoutingCompleted);
        Assert.True(plan.StageStatus.LaneAllocationDeferred);
        Assert.False(plan.StageStatus.SizingDeferred);
        Assert.True(plan.StageStatus.CapacityConstraintsCompleted);
        Assert.True(plan.StageStatus.PhysicalSizingDeferred);
        Assert.True(plan.StageStatus.AbsoluteGeometryDeferred);
        Assert.DoesNotContain(plan.Diagnostics.Findings, finding => finding.Code == "V6PlacementDeferred");
        Assert.DoesNotContain(plan.Diagnostics.Findings, finding => finding.Code == "V6RoutePlanningDeferred");
        Assert.Contains(plan.Diagnostics.Findings, finding => finding.Code == "V6SizingDeferred");
        Assert.Contains(plan.Diagnostics.Findings, finding => finding.Code == "V6GeometryDeferred");
    }

    [Fact]
    public void Planner_cycle_projection_terminates_with_finite_placement()
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
        Assert.Equal(plan.PhysicalLinks.Count, plan.Routes.Count);
        Assert.Equal(plan.PhysicalNodes.Count, plan.NodePlacements.Count);
    }

    [Fact]
    public void Planner_keeps_baseline_members_on_one_row_without_changing_depth()
    {
        var request = Request() with
        {
            NodePlacement = Request().NodePlacement with { BaselinePattern = "*Service" },
            SemanticModel = Request().SemanticModel with
            {
                Projects = new[]
                {
                    new ArchitectureProject("project:p", "Project", new[]
                    {
                        new ArchitectureNode("root", "project:p", "RootService", "Project.RootService", "Class", "root", Array.Empty<string>()),
                        new ArchitectureNode("child", "project:p", "ChildService", "Project.ChildService", "Class", "child", Array.Empty<string>())
                    }, "project:p")
                },
                Links = new[] { new ArchitectureLink("root-child", "root", "child", "internal") }
            }
        };

        var plan = new ArchitectureDiagramV6Planner().Plan(request);
        var baseline = plan.NodeMetadata.Where(node => node.IsBaseline).ToArray();

        Assert.Equal(2, baseline.Length);
        Assert.Single(baseline.Select(node => node.PhysicalRow).Distinct());
        Assert.Equal(0, plan.NodeMetadata.Single(node => node.SemanticNodeId == "root").SemanticDepth);
        Assert.Equal(1, plan.NodeMetadata.Single(node => node.SemanticNodeId == "child").SemanticDepth);
        Assert.DoesNotContain(plan.Diagnostics.Findings, finding => finding.Code == "LogicalPlacementBaselineMisalignment");
    }

    [Fact]
    public void Planner_places_external_node_below_and_centred_on_its_owner()
    {
        var request = Request() with
        {
            SemanticModel = Request().SemanticModel with
            {
                ExternalNodes = new[] { new ArchitectureExternalNode("external", "IExternal", "External", "external", "External.IExternal", "interface") },
                Links = new[] { new ArchitectureLink("root-external", "root", "external", "external") }
            }
        };

        var plan = new ArchitectureDiagramV6Planner().Plan(request);
        var owner = plan.NodeMetadata.Single(node => node.SemanticNodeId == "root");
        var external = plan.NodeMetadata.Single(node => node.SemanticNodeId == "external");

        Assert.True(external.IsExternal);
        Assert.Equal(owner.PhysicalColumn, external.PhysicalColumn);
        Assert.True(external.PhysicalRow > owner.PhysicalRow);
        Assert.Equal(owner.PhysicalNodeId, external.PositionalOwnerId);
    }

    [Fact]
    public void Planner_packs_standalones_in_a_deterministic_near_square_region()
    {
        var request = Request() with
        {
            SemanticModel = Request().SemanticModel with
            {
                Projects = new[]
                {
                    new ArchitectureProject("project:p", "Project", new[]
                    {
                        new ArchitectureNode("root", "project:p", "Root", "Project.Root", "Class", "root", Array.Empty<string>()),
                        new ArchitectureNode("one", "project:p", "One", "Project.One", "Class", "one", Array.Empty<string>()),
                        new ArchitectureNode("two", "project:p", "Two", "Project.Two", "Class", "two", Array.Empty<string>()),
                        new ArchitectureNode("three", "project:p", "Three", "Project.Three", "Class", "three", Array.Empty<string>()),
                        new ArchitectureNode("four", "project:p", "Four", "Project.Four", "Class", "four", Array.Empty<string>())
                    }, "project:p")
                },
                Links = Array.Empty<ArchitectureLink>()
            }
        };

        var plan = new ArchitectureDiagramV6Planner().Plan(request);
        var standaloneRows = plan.NodeMetadata.Where(node => node.IsStandalone).Select(node => node.PhysicalRow).Distinct().Count();

        Assert.Equal(5, plan.NodeMetadata.Count(node => node.IsStandalone));
        Assert.Equal(2, standaloneRows);
        Assert.DoesNotContain(plan.Diagnostics.Findings, finding => finding.Code.Contains("Overlap", StringComparison.Ordinal));
    }

    [Fact]
    public void Planner_duplicate_projection_records_provenance_and_keeps_links()
    {
        var request = Request() with
        {
            NodeProjection = new NodeProjectionPolicy(NodeProjectionMode.DuplicateBranches, new[] { "*Shared*" }),
            SemanticModel = Request().SemanticModel with
            {
                Projects = new[]
                {
                    new ArchitectureProject("project:p", "Project", new[]
                    {
                        new ArchitectureNode("root", "project:p", "Root", "Project.Root", "Class", "root", Array.Empty<string>()),
                        new ArchitectureNode("other", "project:p", "Other", "Project.Other", "Class", "other", Array.Empty<string>()),
                        new ArchitectureNode("shared", "project:p", "SharedService", "Project.SharedService", "Class", "shared", Array.Empty<string>())
                    }, "project:p")
                },
                Links = new[]
                {
                    new ArchitectureLink("root-shared", "root", "shared", "internal"),
                    new ArchitectureLink("other-shared", "other", "shared", "internal")
                }
            }
        };

        var plan = new ArchitectureDiagramV6Planner().Plan(request);

        Assert.Equal(4, plan.PhysicalNodes.Count);
        Assert.Single(plan.PhysicalNodes.Where(node => node.ProjectionMode == PhysicalNodeProjectionMode.DuplicateBranch));
        Assert.Single(plan.PhysicalNodes.Where(node => node.DuplicationProvenance is not null));
        Assert.Equal(2, plan.PhysicalLinks.Count);
        Assert.Equal(plan.PhysicalNodes.Count, plan.NodePlacements.Count);
    }

    [Fact]
    public void Planner_compiles_topology_owned_routes_and_collective_approaches()
    {
        var plan = new ArchitectureDiagramV6Planner().Plan(Request());

        Assert.True(plan.StageStatus.AbstractRoutingCompleted);
        Assert.True(plan.StageStatus.LaneAllocationDeferred);
        Assert.Equal(plan.PhysicalLinks.Count, plan.Routes.Count);
        Assert.All(plan.Routes, route =>
        {
            Assert.Equal(GridSide.Bottom, route.Source.Side);
            Assert.Equal(GridSide.Top, route.Destination.Side);
            Assert.NotEmpty(route.Steps);
            Assert.True(route.IsStructurallySupported);
            Assert.NotEmpty(route.Provenance);
            Assert.NotNull(route.DestinationApproachReservationId);
        });
        Assert.Equal(plan.PhysicalNodes.Count, plan.DestinationApproaches.Count);
        Assert.NotEmpty(plan.StraightRuns);
        Assert.Equal(plan.Routes.Count * 2, plan.EndpointDemands.Count);
        Assert.NotNull(plan.LaneAllocation);
        Assert.Equal(plan.StraightRuns.Count, plan.LaneAllocation!.HorizontalLanes.Count + plan.LaneAllocation.VerticalLanes.Count);
        Assert.All(plan.Routes.SelectMany(route => route.Steps), step => Assert.NotNull(step.AllocatedLane));
        Assert.All(plan.LaneAllocation.HorizontalLanes.Concat(plan.LaneAllocation.VerticalLanes), allocation =>
        {
            Assert.False(string.IsNullOrWhiteSpace(allocation.DomainId));
            Assert.True(allocation.Ordinal >= 0);
            Assert.NotEmpty(allocation.Provenance);
        });
        Assert.NotEmpty(plan.Sizing.Constraints);
        Assert.Equal(plan.SubtreeReservations.Count, plan.ProjectGrids.SelectMany(grid => grid.SubtreeReservations).Count());
        Assert.True(new ArchitectureDiagramV6Validator().Validate(plan).IsValid);
    }

    [Fact]
    public void Planner_classifies_external_and_cross_project_routes_without_pixel_geometry()
    {
        var request = Request() with
        {
            SemanticModel = new ArchitectureDiagramModel(
                new[]
                {
                    new ArchitectureProject("project:a", "A", new[]
                    {
                        new ArchitectureNode("source", "project:a", "Source", "A.Source", "Class", "source", Array.Empty<string>())
                    }, "project:a"),
                    new ArchitectureProject("project:b", "B", new[]
                    {
                        new ArchitectureNode("target", "project:b", "Target", "B.Target", "Class", "target", Array.Empty<string>())
                    }, "project:b")
                },
                Array.Empty<ArchitectureExternalNode>(),
                new[] { new ArchitectureLink("cross", "source", "target", "cross-project") },
                null),
            SelectedScope = new ArchitectureSelectionScope("SelectedProjects", new[] { "project:a", "project:b" }, Array.Empty<string>())
        };

        var plan = new ArchitectureDiagramV6Planner().Plan(request);
        var route = Assert.Single(plan.Routes);

        Assert.Equal(RouteTopologyFamily.CrossProject, route.TopologyFamily);
        Assert.Equal(2, route.Transitions.Count);
        Assert.Contains(route.Steps, step => step.Role == RouteStepRole.ProjectExit);
        Assert.Contains(route.Steps, step => step.Role == RouteStepRole.DiagramGridPassage);
        Assert.Contains(route.Steps, step => step.Role == RouteStepRole.ProjectEntry);
        Assert.NotEmpty(plan.DiagramGrid.Grid.Cells);
        Assert.DoesNotContain(plan.ProjectGrids.SelectMany(grid => grid.Grid.Cells.Keys), cell => cell.GridId.Value == "diagram");
        Assert.DoesNotContain(plan.Diagnostics.Findings, finding => finding.Code == "UnsupportedAbstractRoute");
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
        Assert.Contains(page.Diagnostics, diagnostic => diagnostic.Code == "V6LinkEmissionDeferred");
    }

    [Fact]
    public void Renderer_does_not_reconstruct_or_invent_geometry()
    {
        var plan = new ArchitectureDiagramV6Planner().Plan(Request());
        var page = new DrawioArchitectureV6Renderer().Render(plan, new ArchitectureRenderRequest(ArchitectureValidationMode.Diagnostic, "drawio", true));

        Assert.DoesNotContain(page.GraphModel.Descendants(), element => element.Name.LocalName == "mxGeometry" && element.Parent?.Name.LocalName == "mxCell");
        Assert.Contains(page.Diagnostics, diagnostic => diagnostic.Code == "V6LogicalPlacementComplete");
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
