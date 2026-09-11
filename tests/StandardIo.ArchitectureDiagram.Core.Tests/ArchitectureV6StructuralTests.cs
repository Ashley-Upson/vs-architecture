using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
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
    public void Content_management_regression_dataset_preserves_real_failure_shape()
    {
        var model = RegressionSemanticModel();

        Assert.Equal(20, model.Projects.Single().Nodes.Count);
        Assert.Equal(3, model.ExternalNodes.Count);
        Assert.Contains(model.Links, link => link.SourceId == "cycle-a" && link.TargetId == "cycle-b");
        Assert.Contains(model.Links, link => link.SourceId == "cycle-b" && link.TargetId == "cycle-a");
        Assert.Equal(4, model.Links.Count(link => link.Kind == "external"));
        Assert.Equal(8, model.Projects.Single().Nodes.Count(node => node.Id.StartsWith("standalone-", StringComparison.Ordinal)));
    }

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
    public void Planner_keeps_canonical_multi_parent_node_in_one_profile_interval()
    {
        var request = Request() with
        {
            SemanticModel = new ArchitectureDiagramModel(new[]
            {
                new ArchitectureProject("project:p", "Project", new[]
                {
                    new ArchitectureNode("left", "project:p", "Left", "Project.Left", "Class", "left", Array.Empty<string>()),
                    new ArchitectureNode("right", "project:p", "Right", "Project.Right", "Class", "right", Array.Empty<string>()),
                    new ArchitectureNode("shared", "project:p", "Shared", "Project.Shared", "Class", "shared", Array.Empty<string>())
                }, "project:p")
            }, Array.Empty<ArchitectureExternalNode>(), new[]
            {
                new ArchitectureLink("left-shared", "left", "shared", "internal"),
                new ArchitectureLink("right-shared", "right", "shared", "internal")
            }, null)
        };

        var plan = new ArchitectureDiagramV6Planner().Plan(request);

        Assert.Single(plan.PhysicalNodes.Where(node => node.SemanticNodeId == "shared"));
        Assert.Equal(2, plan.PhysicalLinks.Count);
        var sharedPlacement = plan.NodePlacements.Single(node => node.PhysicalNodeId.Contains("shared", StringComparison.Ordinal));
        Assert.Equal(3, plan.PhysicalNodes.Count);
        Assert.Equal(sharedPlacement.Footprint.Count, sharedPlacement.Footprint.Select(cell => cell.ColumnId).Distinct().Count());
    }

    [Fact]
    public void Boundary_authority_ids_do_not_change_structural_identity()
    {
        var first = new GridBoundaryIdentity(new PlanningGridId("project:p"),
            new PlanningGridCellId(new PlanningGridId("project:p"), new PlanningGridRowId("row:1"), new PlanningGridColumnId("column:1")),
            GridSide.Top, new LaneId("lane:a"), "project:p", "first");
        var second = new GridBoundaryIdentity(first.GridId, first.CellId, first.Side, first.Lane,
            first.OwnershipScope, "second");

        Assert.Equal(first, second);
        Assert.Equal(first.ToString(), second.ToString());
    }

    [Fact]
    public void Shared_boundary_identity_requires_adjacent_complementary_cells()
    {
        var grid = new PlanningGridId("project:p");
        var row = new PlanningGridRowId("row:1");
        var left = new PlanningGridCellId(grid, row, new PlanningGridColumnId("column:1"));
        var right = new PlanningGridCellId(grid, row, new PlanningGridColumnId("column:2"));
        var distant = new PlanningGridCellId(grid, row, new PlanningGridColumnId("column:4"));
        var first = new GridBoundaryIdentity(grid, left, GridSide.Right, new LaneId("lane:a"), "project:p", "one");
        var second = new GridBoundaryIdentity(grid, right, GridSide.Left, new LaneId("lane:a"), "project:p", "two");
        var nonAdjacent = new GridBoundaryIdentity(grid, distant, GridSide.Left, new LaneId("lane:a"), "project:p", "three");

        Assert.True(GridBoundaryIdentity.TryCreateShared(first, second, out var shared));
        Assert.NotNull(shared);
        Assert.Equal(shared, GridBoundaryIdentity.TryCreateShared(second, first, out var reverse) ? reverse : null);
        Assert.False(GridBoundaryIdentity.TryCreateShared(first, nonAdjacent, out _));
    }

    [Fact]
    public void Shared_horizontal_boundary_identity_requires_complementary_row_sides()
    {
        var grid = new PlanningGridId("project:p");
        var column = new PlanningGridColumnId("column:1");
        var upper = new GridBoundaryIdentity(grid,
            new PlanningGridCellId(grid, new PlanningGridRowId("row:1"), column), GridSide.Bottom,
            new LaneId("lane:a"), "project:p", "upper");
        var lower = new GridBoundaryIdentity(grid,
            new PlanningGridCellId(grid, new PlanningGridRowId("row:2"), column), GridSide.Top,
            new LaneId("lane:a"), "project:p", "lower");

        Assert.True(GridBoundaryIdentity.TryCreateShared(upper, lower, out var shared));
        Assert.NotNull(shared);
        Assert.False(shared!.IsExterior);
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
        Assert.Equal(plan.PhysicalNodes.Count, plan.NodePlacements.Count);
    }

    [Fact]
    public void Final_pipeline_keeps_unrelated_root_subtrees_separate_on_shared_rows()
    {
        var request = Request() with
        {
            NodePlacement = Request().NodePlacement with
            {
                BaselinePattern = "^does-not-match$",
                RoleRules = Array.Empty<ArchitectureV6RoleRule>()
            },
            SemanticModel = Request().SemanticModel with
            {
                Projects = new[]
                {
                    new ArchitectureProject("project:p", "Project", new[]
                    {
                        new ArchitectureNode("root-a", "project:p", "RootA", "Project.RootA", "Class", "root-a", Array.Empty<string>()),
                        new ArchitectureNode("child-a", "project:p", "ChildA", "Project.ChildA", "Class", "child-a", Array.Empty<string>()),
                        new ArchitectureNode("root-b", "project:p", "RootB", "Project.RootB", "Class", "root-b", Array.Empty<string>()),
                        new ArchitectureNode("child-b", "project:p", "ChildB", "Project.ChildB", "Class", "child-b", Array.Empty<string>())
                    }, "project:p")
                },
                Links = new[]
                {
                    new ArchitectureLink("root-a-child-a", "root-a", "child-a", "internal"),
                    new ArchitectureLink("root-b-child-b", "root-b", "child-b", "internal")
                }
            }
        };

        var plan = new ArchitectureDiagramV6Planner().Plan(request);

        Assert.DoesNotContain(plan.Diagnostics.Findings, finding => finding.Code == "LogicalPlacementSubtreeInterleave");
        Assert.DoesNotContain(plan.Diagnostics.Findings, finding => finding.Code == "LogicalPlacementSiblingInterleave");
    }

    [Fact]
    public void Planner_keeps_single_child_chain_vertically_aligned()
    {
        var request = Request() with
        {
            SemanticModel = Request().SemanticModel with
            {
                Projects = new[]
                {
                    new ArchitectureProject("project:p", "Project", new[]
                    {
                        new ArchitectureNode("parent", "project:p", "ParentController", "Project.ParentController", "Class", "parent", Array.Empty<string>()),
                        new ArchitectureNode("child", "project:p", "ChildService", "Project.ChildService", "Class", "child", Array.Empty<string>())
                    }, "project:p")
                },
                Links = new[] { new ArchitectureLink("parent-child", "parent", "child", "internal") }
            }
        };

        var plan = new ArchitectureDiagramV6Planner().Plan(request);
        var parent = plan.NodeMetadata.Single(node => node.SemanticNodeId == "parent");
        var child = plan.NodeMetadata.Single(node => node.SemanticNodeId == "child");

        Assert.Equal(parent.PhysicalColumn, child.PhysicalColumn);
        Assert.True(child.PhysicalRow > parent.PhysicalRow);
    }

    [Fact]
    public void Planner_centres_parent_over_two_immediate_child_subtrees()
    {
        var request = Request() with
        {
            SemanticModel = Request().SemanticModel with
            {
                Projects = new[]
                {
                    new ArchitectureProject("project:p", "Project", new[]
                    {
                        new ArchitectureNode("parent", "project:p", "ParentController", "Project.ParentController", "Class", "parent", Array.Empty<string>()),
                        new ArchitectureNode("left", "project:p", "LeftService", "Project.LeftService", "Class", "left", Array.Empty<string>()),
                        new ArchitectureNode("right", "project:p", "RightService", "Project.RightService", "Class", "right", Array.Empty<string>())
                    }, "project:p")
                },
                Links = new[]
                {
                    new ArchitectureLink("parent-left", "parent", "left", "internal"),
                    new ArchitectureLink("parent-right", "parent", "right", "internal")
                }
            }
        };

        var plan = new ArchitectureDiagramV6Planner().Plan(request);
        var parent = plan.NodeMetadata.Single(node => node.SemanticNodeId == "parent");
        var left = plan.NodeMetadata.Single(node => node.SemanticNodeId == "left");
        var right = plan.NodeMetadata.Single(node => node.SemanticNodeId == "right");

        Assert.True(left.PhysicalColumn < parent.PhysicalColumn);
        Assert.True(parent.PhysicalColumn < right.PhysicalColumn);
        Assert.Equal((left.PhysicalColumn + right.PhysicalColumn) / 2, parent.PhysicalColumn);
        Assert.Equal(left.PhysicalRow, right.PhysicalRow);
        Assert.True(left.PhysicalRow > parent.PhysicalRow);
    }

    [Fact]
    public void Final_pipeline_centres_parent_over_immediate_children_not_deepest_descendant()
    {
        var request = Request() with
        {
            SemanticModel = Request().SemanticModel with
            {
                Projects = new[]
                {
                    new ArchitectureProject("project:p", "Project", new[]
                    {
                        new ArchitectureNode("parent", "project:p", "ParentController", "Project.ParentController", "Class", "parent", Array.Empty<string>()),
                        new ArchitectureNode("left", "project:p", "LeftService", "Project.LeftService", "Class", "left", Array.Empty<string>()),
                        new ArchitectureNode("left-child", "project:p", "LeftChildService", "Project.LeftChildService", "Class", "left-child", Array.Empty<string>()),
                        new ArchitectureNode("right", "project:p", "RightService", "Project.RightService", "Class", "right", Array.Empty<string>())
                    }, "project:p")
                },
                Links = new[]
                {
                    new ArchitectureLink("parent-left", "parent", "left", "internal"),
                    new ArchitectureLink("left-child", "left", "left-child", "internal"),
                    new ArchitectureLink("parent-right", "parent", "right", "internal")
                }
            }
        };

        var plan = new ArchitectureDiagramV6Planner().Plan(request);
        var metadata = plan.NodeMetadata.ToDictionary(node => node.SemanticNodeId);
        var parent = metadata["parent"];
        var left = metadata["left"];
        var right = metadata["right"];
        Assert.Equal((left.PhysicalColumn + right.PhysicalColumn) / 2, parent.PhysicalColumn);
        Assert.True(parent.PhysicalRow < left.PhysicalRow);
        Assert.DoesNotContain(plan.Diagnostics.Findings, finding => finding.Code == "LogicalPlacementParentNotCentered");
        Assert.DoesNotContain(plan.Diagnostics.Findings, finding => finding.Code == "LogicalPlacementSubtreeInterleave");
    }

    [Fact]
    public void Planner_resolves_compact_labels_before_sizing_and_rendering()
    {
        var request = Request() with
        {
            SemanticModel = Request().SemanticModel with
            {
                Projects = new[]
                {
                    new ArchitectureProject("project:p", "Project", new[]
                    {
                        new ArchitectureNode("implementation", "project:p", "AuthorizationBroker", "Project.Brokers.AuthorizationBroker", "Class", "implementation",
                            new[] { "Project.Contracts.IAuthorizationBroker" }),
                        new ArchitectureNode("plain", "project:p", "PlainService", "Project.Services.PlainService", "Class", "plain", Array.Empty<string>())
                    }, "project:p")
                },
                ExternalNodes = new[]
                {
                    new ArchitectureExternalNode("external", "IEventHub", "External", "external", "External.IEventHub", "interface")
                },
                Links = new[]
                {
                    new ArchitectureLink("implementation-external", "implementation", "external", "external")
                }
            }
        };

        var plan = new ArchitectureDiagramV6Planner().Plan(request);

        Assert.Equal("AuthorizationBroker:IAuthorizationBroker",
            plan.PhysicalNodes.Single(node => node.SemanticNodeId == "implementation").DisplayLabel);
        Assert.Equal("PlainService", plan.PhysicalNodes.Single(node => node.SemanticNodeId == "plain").DisplayLabel);
        Assert.Equal("[External]\nIEventHub",
            plan.PhysicalNodes.Single(node => node.SemanticNodeId == "external").DisplayLabel);
        Assert.All(plan.Sizing.Rows.Where(row => row.Role == PlanningGridTrackRole.InterLayerRouting),
            row => Assert.True(row.FinalExtent >= 20));
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
    public void Planner_places_all_external_nodes_on_one_final_bottom_layer()
    {
        var request = Request() with
        {
            SemanticModel = Request().SemanticModel with
            {
                ExternalNodes = new[]
                {
                    new ArchitectureExternalNode("external-a", "IA", "External", "external-a", "External.IA", "interface"),
                    new ArchitectureExternalNode("external-b", "IB", "External", "external-b", "External.IB", "interface")
                },
                Links = new[]
                {
                    new ArchitectureLink("root-a", "root", "external-a", "external"),
                    new ArchitectureLink("root-b", "root", "external-b", "external")
                }
            }
        };

        var plan = new ArchitectureDiagramV6Planner().Plan(request);
        var external = plan.NodeMetadata.Where(node => node.IsExternal).ToArray();
        var nonExternal = plan.NodeMetadata.Where(node => !node.IsExternal && !node.IsStandalone).ToArray();

        Assert.NotNull(plan.ReservedDepthTable);
        Assert.Single(external.Select(node => node.PhysicalRow).Distinct());
        Assert.Equal(plan.ReservedDepthTable!.External.NodeRow, external[0].PhysicalRow);
        Assert.True(external.Min(node => node.PhysicalRow) > nonExternal.Max(node => node.PhysicalRow));
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
    public void Standalone_status_owns_placement_region_even_when_role_matches_connected_nodes()
    {
        var request = Request() with
        {
            SemanticModel = Request().SemanticModel with
            {
                Projects = new[]
                {
                    new ArchitectureProject("project:p", "Project", new[]
                    {
                        new ArchitectureNode("connected", "project:p", "ConnectedService", "Project.ConnectedService", "Class", "connected", Array.Empty<string>()),
                        new ArchitectureNode("dependency", "project:p", "Dependency", "Project.Dependency", "Class", "dependency", Array.Empty<string>()),
                        new ArchitectureNode("standalone", "project:p", "StandaloneService", "Project.StandaloneService", "Class", "standalone", Array.Empty<string>())
                    }, "project:p")
                },
                Links = new[] { new ArchitectureLink("connected-dependency", "connected", "dependency", "internal") }
            }
        };

        var plan = new ArchitectureDiagramV6Planner().Plan(request);
        var connected = plan.NodeMetadata.Single(node => node.SemanticNodeId == "connected");
        var standalone = plan.NodeMetadata.Single(node => node.SemanticNodeId == "standalone");

        Assert.False(connected.IsStandalone);
        Assert.True(standalone.IsStandalone);
        Assert.Equal("standalone-square-region", standalone.VerticalSpacingPolicy);
        Assert.DoesNotContain(plan.NodeMetadata.Where(node => node.IsStandalone), node => node.VerticalSpacingPolicy != "standalone-square-region");
        Assert.DoesNotContain(plan.Diagnostics.Findings, finding => finding.Code == "LogicalPlacementRoleLayerSplit" && finding.SubjectId == "standalone");
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
    public void Planner_collapses_repeated_semantic_discovery_without_duplicate_canonical_ids()
    {
        var request = Request() with
        {
            SemanticModel = Request().SemanticModel with
            {
                Projects = new[]
                {
                    new ArchitectureProject("project:p", "Project P", new[]
                    {
                        new ArchitectureNode("same", "project:p", "Same", "P.Same", "Class", "same", Array.Empty<string>())
                    }, "project:p"),
                    new ArchitectureProject("project:q", "Project Q", new[]
                    {
                        new ArchitectureNode("same", "project:q", "Same", "Q.Same", "Class", "same", Array.Empty<string>())
                    }, "project:q")
                },
                Links = Array.Empty<ArchitectureLink>()
            },
            SelectedScope = new ArchitectureSelectionScope("SelectedProjects", Array.Empty<string>(), Array.Empty<string>())
        };

        var plan = new ArchitectureDiagramV6Planner().Plan(request);

        Assert.Single(plan.PhysicalNodes);
        Assert.Equal(plan.PhysicalNodes.Count, plan.PhysicalNodes.Select(node => node.PhysicalNodeId).Distinct().Count());
        Assert.Single(plan.Projection!.SemanticNodeToPhysicalNodeIds["same"]);
    }

    [Fact]
    public void Occupancy_authority_reports_orphaned_anchor_metadata()
    {
        var plan = new ArchitectureDiagramV6Planner().Plan(Request());
        var missing = plan.NodePlacements[0].PhysicalNodeId;
        var authority = new ArchitectureV6OccupancyAuthority(plan.PhysicalNodes,
            plan.NodePlacements.Where(item => item.PhysicalNodeId != missing).ToArray(),
            plan.ProjectGrids.Select(item => item.Grid).Append(plan.DiagramGrid.Grid));

        Assert.Contains(authority.Diagnostics, finding => finding.Code == "OrphanedNodeAnchor");
    }

    [Fact]
    public void Occupancy_authority_resolves_anchor_and_footprint_cells_once()
    {
        var plan = new ArchitectureDiagramV6Planner().Plan(Request());
        var authority = new ArchitectureV6OccupancyAuthority(plan.PhysicalNodes, plan.NodePlacements,
            plan.ProjectGrids.Select(item => item.Grid).Append(plan.DiagramGrid.Grid));

        Assert.DoesNotContain(authority.Diagnostics, finding => finding.Code == "ConflictingNodeFootprint");
        foreach (var placement in plan.NodePlacements)
        {
            var anchor = authority.Resolve(placement.AnchorCellId);
            Assert.Equal(ArchitectureV6OccupancyStatus.Occupied, anchor.Status);
            Assert.Equal(new[] { placement.PhysicalNodeId }, anchor.PhysicalNodeIds);
            Assert.All(placement.Footprint, cell =>
            {
                var resolution = authority.Resolve(cell);
                Assert.Equal(ArchitectureV6OccupancyStatus.Occupied, resolution.Status);
                Assert.Equal(new[] { placement.PhysicalNodeId }, resolution.PhysicalNodeIds);
            });
        }
    }

    [Fact]
    public void Planner_uses_one_visual_row_for_equal_analysed_depth_across_branches()
    {
        var request = Request() with
        {
            SemanticModel = Request().SemanticModel with
            {
                Projects = new[]
                {
                    new ArchitectureProject("project:p", "Project", new[]
                    {
                        new ArchitectureNode("left", "project:p", "LeftController", "Project.LeftController", "Class", "left", Array.Empty<string>()),
                        new ArchitectureNode("right", "project:p", "RightController", "Project.RightController", "Class", "right", Array.Empty<string>()),
                        new ArchitectureNode("left-child", "project:p", "LeftService", "Project.LeftService", "Class", "left-child", Array.Empty<string>()),
                        new ArchitectureNode("right-child", "project:p", "RightService", "Project.RightService", "Class", "right-child", Array.Empty<string>())
                    }, "project:p")
                },
                Links = new[]
                {
                    new ArchitectureLink("left-link", "left", "left-child", "internal"),
                    new ArchitectureLink("right-link", "right", "right-child", "internal")
                }
            }
        };

        var plan = new ArchitectureDiagramV6Planner().Plan(request);
        var children = plan.NodeMetadata.Where(node => node.SemanticDepth == 1).ToArray();

        Assert.Equal(2, children.Length);
        Assert.Single(children.Select(node => node.PhysicalRow).Distinct());
        Assert.DoesNotContain(plan.Diagnostics.Findings, finding => finding.Code == "LogicalPlacementParentChildRowViolation");
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

    [Theory]
    [InlineData(RouteAxis.Vertical)]
    [InlineData(RouteAxis.Horizontal)]
    public void Boundary_contract_represents_adjacent_turns_with_a_one_cell_run(RouteAxis sharedAxis)
    {
        var contract = BuildAdjacentTurnContract(sharedAxis, includeSharedRun: true);
        var turnIndex = Array.FindIndex(contract.Components.ToArray(), component => component.Kind == PlannedRouteComponentKind.Turn);
        var first = contract.Components[turnIndex];
        var run = contract.Components[turnIndex + 1];
        var second = contract.Components[turnIndex + 2];

        Assert.True(contract.IsValid, string.Join("; ", contract.Findings.Select(finding => finding.Code + ":" + finding.Message + ": expected=" + finding.ExpectedBoundary + ": actual=" + finding.ActualBoundary)));
        Assert.Equal(PlannedRouteComponentKind.Turn, first.Kind);
        Assert.Equal(sharedAxis == RouteAxis.Horizontal
            ? PlannedRouteComponentKind.HorizontalStraightRun
            : PlannedRouteComponentKind.VerticalStraightRun, run.Kind);
        Assert.Equal(PlannedRouteComponentKind.Turn, second.Kind);
        Assert.Equal(2, run.Cells.Count);
        Assert.Equal("lane:shared", run.Lane!.Value.Value);
        Assert.Equal(first.ExitBoundary, run.EntryBoundary);
        Assert.Equal(run.ExitBoundary, second.EntryBoundary);
        Assert.NotEqual(first.Cells.Single(), second.Cells.Single());
        Assert.DoesNotContain(contract.Components.Zip(contract.Components.Skip(1)), pair =>
            pair.First.Kind == PlannedRouteComponentKind.Turn && pair.Second.Kind == PlannedRouteComponentKind.Turn);
    }

    [Fact]
    public void Boundary_contract_rejects_adjacent_turns_when_the_shared_run_is_missing()
    {
        var contract = BuildAdjacentTurnContract(RouteAxis.Vertical, includeSharedRun: false);

        Assert.False(contract.IsValid);
        Assert.Contains(contract.Findings, finding => finding.Code == "MissingAdjacentTurnRun");
        Assert.Contains(contract.Findings, finding => finding.Code == "AdjacentTurnComponents");
    }

    private static PlannedRouteBoundaryContract BuildAdjacentTurnContract(RouteAxis sharedAxis, bool includeSharedRun)
    {
        var gridId = new PlanningGridId("project:p");
        var row0 = new PlanningGridRowId("row:0");
        var row1 = new PlanningGridRowId("row:1");
        var column0 = new PlanningGridColumnId("column:0");
        var column1 = new PlanningGridColumnId("column:1");
        var column2 = new PlanningGridColumnId("column:2");
        var firstCell = new PlanningGridCellId(gridId, sharedAxis == RouteAxis.Vertical ? row0 : row1, column1);
        var secondCell = new PlanningGridCellId(gridId, sharedAxis == RouteAxis.Vertical ? row1 : row1, sharedAxis == RouteAxis.Vertical ? column1 : column2);
        var source = new PlannedPhysicalNode("physical:source", "source", PhysicalNodeProjectionMode.Canonical, null, "p", null, false, false);
        var destination = new PlannedPhysicalNode("physical:destination", "destination", PhysicalNodeProjectionMode.Canonical, null, "p", null, false, false);
        var placements = new[]
        {
            new PlannedNodePlacement(source.PhysicalNodeId, gridId, firstCell, 1, 1, new[] { firstCell }, column1),
            new PlannedNodePlacement(destination.PhysicalNodeId, gridId, secondCell, 1, 1, new[] { secondCell }, secondCell.ColumnId)
        };
        var sourceStep = new PlannedGridRouteStep(gridId, firstCell, GridSide.Top, GridSide.Bottom, RouteStepRole.SourceExit, 0);
        var firstTurn = sharedAxis == RouteAxis.Vertical
            ? new PlannedGridRouteStep(gridId, firstCell, GridSide.Left, GridSide.Top, RouteStepRole.Turn, 1)
            : new PlannedGridRouteStep(gridId, firstCell, GridSide.Top, GridSide.Right, RouteStepRole.Turn, 1);
        var secondTurn = sharedAxis == RouteAxis.Vertical
            ? new PlannedGridRouteStep(gridId, secondCell, GridSide.Bottom, GridSide.Right, RouteStepRole.Turn, 2)
            : new PlannedGridRouteStep(gridId, secondCell, GridSide.Left, GridSide.Bottom, RouteStepRole.Turn, 2);
        firstTurn = firstTurn with { AllocatedLane = new LaneId("lane:pre") };
        secondTurn = secondTurn with { AllocatedLane = new LaneId("lane:post") };
        var destinationStep = new PlannedGridRouteStep(gridId, secondCell, GridSide.Top, GridSide.Bottom, RouteStepRole.DestinationEntry, 3);
        var route = new PlannedGridRoute("physical-link:adjacent", 
            new NodeEndpoint(source.PhysicalNodeId, GridSide.Bottom, "source", 0, gridId),
            new[] { sourceStep, firstTurn, secondTurn, destinationStep }, Array.Empty<GridTransition>(),
            new NodeEndpoint(destination.PhysicalNodeId, GridSide.Top, "destination", 0, gridId),
            RouteTopologyFamily.Upward, "p", "p");

        var preAxis = sharedAxis == RouteAxis.Vertical ? RouteAxis.Horizontal : RouteAxis.Vertical;
        var postAxis = preAxis;
        var preRun = new PlannedStraightRun(route.PhysicalLinkId, gridId, preAxis, new[] { firstCell }, "pre", "pre", new LaneId("lane:pre"));
        var sharedRun = new PlannedStraightRun(route.PhysicalLinkId, gridId, sharedAxis, new[] { firstCell, secondCell }, "shared", "shared", new LaneId("lane:shared"));
        var postRun = new PlannedStraightRun(route.PhysicalLinkId, gridId, postAxis, new[] { secondCell }, "post", "post", new LaneId("lane:post"));
        var runs = includeSharedRun ? new[] { preRun, sharedRun, postRun } : new[] { preRun, postRun };
        var lanes = runs.Select(run => new PlannedLaneAllocation(run.Lane.Value, route.PhysicalLinkId, gridId, run.Axis,
            gridId.Value + ":" + run.Axis, 0, run.Lane, run.Cells, 0, 1, route.TopologyFamily, "project:p", "test"));
        var turns = new[]
        {
            new PlannedTurnAllocation(route.PhysicalLinkId, firstCell.ToString(), firstTurn.EntrySide, firstTurn.ExitSide,
                sharedAxis == RouteAxis.Horizontal ? "lane:shared" : "lane:pre", sharedAxis == RouteAxis.Vertical ? "lane:shared" : "lane:pre", "turn:first", 0, "test"),
            new PlannedTurnAllocation(route.PhysicalLinkId, secondCell.ToString(), secondTurn.EntrySide, secondTurn.ExitSide,
                sharedAxis == RouteAxis.Horizontal ? "lane:shared" : "lane:post", sharedAxis == RouteAxis.Vertical ? "lane:shared" : "lane:post", "turn:second", 0, "test")
        };
        var allocation = new ArchitectureLaneAllocationResult(new[] { route }, runs,
            lanes.Where(item => item.Axis == RouteAxis.Horizontal).ToArray(), lanes.Where(item => item.Axis == RouteAxis.Vertical).ToArray(),
            Array.Empty<PlannedEndpointAllocation>(), Array.Empty<PlannedDestinationApproachAllocation>(), turns,
            Array.Empty<PlannedCleanCrossing>(), Array.Empty<PlannedProjectTransitionAllocation>(), Array.Empty<LaneAllocationConflict>(),
            Array.Empty<NodeFootprintExpansionRequirement>(), new GridTrackSizingPlan(Array.Empty<PlanningGridRow>(), Array.Empty<PlanningGridColumn>(), Array.Empty<GridTrackConstraint>(), null),
            Array.Empty<ArchitecturePlanningDiagnostic>());

        var contract = new ArchitectureV6RouteBoundaryContractBuilder(allocation, new[] { source, destination }, placements).Build().Routes.Single();
        Assert.Equal(4, route.Steps.Count);
        Assert.Equal(2, route.Steps.Count(step => step.Role == RouteStepRole.Turn));
        return contract;
    }

    [Fact]
    public void Final_plan_assigns_layers_once_and_preserves_parent_child_order()
    {
        var request = Request() with
        {
            NodePlacement = Request().NodePlacement with
            {
                BaselinePattern = "*OrchestrationService",
                RoleRules = new[]
                {
                    new ArchitectureV6RoleRule("ProcessingService", "ProcessingService$", 0),
                    new ArchitectureV6RoleRule("CoordinationService", "CoordinationService$", 1),
                    new ArchitectureV6RoleRule("OrchestrationService", "OrchestrationService$", 2)
                }
            },
            SemanticModel = Request().SemanticModel with
            {
                Projects = new[]
                {
                    new ArchitectureProject("project:p", "Project", new[]
                    {
                        new ArchitectureNode("root", "project:p", "RootController", "Project.RootController", "Class", "root", Array.Empty<string>()),
                        new ArchitectureNode("processing", "project:p", "ProcessingService", "Project.ProcessingService", "Class", "processing", Array.Empty<string>()),
                        new ArchitectureNode("coordination", "project:p", "CoordinationService", "Project.CoordinationService", "Class", "coordination", Array.Empty<string>()),
                        new ArchitectureNode("orchestration", "project:p", "OrchestrationService", "Project.OrchestrationService", "Class", "orchestration", Array.Empty<string>())
                    }, "project:p")
                },
                Links = new[]
                {
                    new ArchitectureLink("root-processing", "root", "processing", "internal"),
                    new ArchitectureLink("processing-coordination", "processing", "coordination", "internal"),
                    new ArchitectureLink("coordination-orchestration", "coordination", "orchestration", "internal")
                }
            }
        };

        var plan = new ArchitectureDiagramV6Planner().Plan(request);
        var metadata = plan.NodeMetadata.ToDictionary(node => node.SemanticNodeId, StringComparer.Ordinal);

        Assert.Equal("ProcessingService", metadata["processing"].RoleSelector);
        Assert.Equal("CoordinationService", metadata["coordination"].RoleSelector);
        Assert.Equal("OrchestrationService", metadata["orchestration"].RoleSelector);
        Assert.True(metadata["processing"].PhysicalRow < metadata["coordination"].PhysicalRow);
        Assert.True(metadata["coordination"].PhysicalRow < metadata["orchestration"].PhysicalRow);
        Assert.All(metadata.Values.Where(node => node.PositionalOwnerId is not null), node =>
            Assert.True(node.PhysicalRow > metadata.Values.Single(parent => parent.PhysicalNodeId == node.PositionalOwnerId).PhysicalRow));
        Assert.DoesNotContain(plan.Diagnostics.Findings, finding => finding.Code == "LogicalPlacementParentNotAboveChild");
    }

    [Fact]
    public void Final_plan_preserves_configured_role_order_without_visual_baseline_bands()
    {
        var request = Request() with
        {
            NodePlacement = Request().NodePlacement with
            {
                RoleRules = new[]
                {
                    new ArchitectureV6RoleRule("CoordinationService", "CoordinationService$", 0),
                    new ArchitectureV6RoleRule("OrchestrationService", "OrchestrationService$", 1),
                    new ArchitectureV6RoleRule("ProcessingService", "ProcessingService$", 2),
                    new ArchitectureV6RoleRule("Service", "Service$", 3)
                }
            },
            SemanticModel = Request().SemanticModel with
            {
                Projects = new[]
                {
                    new ArchitectureProject("project:p", "Project", new[]
                    {
                        new ArchitectureNode("root", "project:p", "RootController", "Project.RootController", "Class", "root", Array.Empty<string>()),
                        new ArchitectureNode("coordination", "project:p", "FooCoordinationService", "Project.FooCoordinationService", "Class", "coordination", Array.Empty<string>()),
                        new ArchitectureNode("orchestration", "project:p", "FooOrchestrationService", "Project.FooOrchestrationService", "Class", "orchestration", Array.Empty<string>()),
                        new ArchitectureNode("processing", "project:p", "FooProcessingService", "Project.FooProcessingService", "Class", "processing", Array.Empty<string>()),
                        new ArchitectureNode("service", "project:p", "FooService", "Project.FooService", "Class", "service", Array.Empty<string>())
                    }, "project:p")
                },
                Links = new[]
                {
                    new ArchitectureLink("root-coordination", "root", "coordination", "internal"),
                    new ArchitectureLink("root-orchestration", "root", "orchestration", "internal"),
                    new ArchitectureLink("root-processing", "root", "processing", "internal"),
                    new ArchitectureLink("root-service", "root", "service", "internal")
                }
            }
        };

        var plan = new ArchitectureDiagramV6Planner().Plan(request);
        var metadata = plan.NodeMetadata.ToDictionary(node => node.SemanticNodeId, StringComparer.Ordinal);

        Assert.Equal("CoordinationService", metadata["coordination"].RoleSelector);
        Assert.Equal("OrchestrationService", metadata["orchestration"].RoleSelector);
        Assert.Equal("ProcessingService", metadata["processing"].RoleSelector);
        Assert.Equal("Service", metadata["service"].RoleSelector);
        Assert.True(metadata["coordination"].PhysicalRow < metadata["orchestration"].PhysicalRow);
        Assert.True(metadata["orchestration"].PhysicalRow < metadata["processing"].PhysicalRow);
        Assert.True(metadata["processing"].PhysicalRow < metadata["service"].PhysicalRow);
        Assert.DoesNotContain(plan.Diagnostics.Findings, finding => finding.Code == "LogicalPlacementRoleOrderViolation");
    }

    [Fact]
    public void Final_plan_applies_minimum_horizontal_node_gap_to_different_width_siblings()
    {
        var request = FanoutRequest(3);
        var plan = new ArchitectureDiagramV6Planner().Plan(request);
        var nodes = plan.NodePlacements.Where(node => plan.PhysicalNodes.Single(item => item.PhysicalNodeId == node.PhysicalNodeId).SemanticNodeId is "child-a" or "child-b" or "child-c")
            .OrderBy(node => ColumnOrdinal(node.CentreColumnId)).ToArray();

        Assert.Equal(3, nodes.Length);
        for (var index = 1; index < nodes.Length; index++)
        {
            var previous = nodes[index - 1].Footprint.Max(cell => ColumnOrdinal(cell.ColumnId));
            var current = nodes[index].Footprint.Min(cell => ColumnOrdinal(cell.ColumnId));
            Assert.True(current - previous >= 2,
                $"logical gap between {nodes[index - 1].PhysicalNodeId} and {nodes[index].PhysicalNodeId} was {current - previous - 1}");
        }
        Assert.Empty(plan.Diagnostics.Findings.Where(finding => finding.Code == "LogicalPlacementFootprintOverlap"));
    }

    [Fact]
    public void Synthetic_analyser_standalones_are_a_separate_compact_region_below_external_nodes()
    {
        var fixture = new ArchitectureV6SemanticFixtureBuilder()
            .Project("project:standalone", "Standalone")
            .Node("owner", "OwnerService", "project:standalone")
            .Node("utility-a", "UtilityAService", "project:standalone")
            .Node("utility-b", "UtilityBService", "project:standalone")
            .Node("utility-c", "UtilityCService", "project:standalone")
            .Node("utility-d", "UtilityDService", "project:standalone")
            .External("external-a", "IAuth")
            .Link("owner-external", "owner", "external-a", "external");

        var plan = new ArchitectureDiagramV6Planner().Plan(fixture.BuildRequest());
        var metadata = plan.NodeMetadata.ToDictionary(item => item.SemanticNodeId, StringComparer.Ordinal);
        var externalLayer = metadata["external-a"].PhysicalRow;
        var standalones = new[] { "utility-a", "utility-b", "utility-c", "utility-d" };

        Assert.All(standalones, id => Assert.True(metadata[id].IsStandalone));
        Assert.All(standalones, id => Assert.True(metadata[id].PhysicalRow > externalLayer));
        var standalonePhysicalIds = metadata.Values.Where(item => standalones.Contains(item.SemanticNodeId, StringComparer.Ordinal))
            .Select(item => item.PhysicalNodeId).ToHashSet(StringComparer.Ordinal);
        Assert.Equal(2, plan.NodePlacements.Where(item => standalonePhysicalIds.Contains(item.PhysicalNodeId))
            .GroupBy(item => item.AnchorCellId.RowId).Count());
    }

    [Fact]
    public void Final_plan_keeps_external_nodes_on_one_bottom_layer_and_affine_to_simple_owners()
    {
        var request = Request() with
        {
            SemanticModel = Request().SemanticModel with
            {
                ExternalNodes = new[]
                {
                    new ArchitectureExternalNode("external-a", "IA", "External", "external-a", "External.IA", "interface"),
                    new ArchitectureExternalNode("external-b", "IB", "External", "external-b", "External.IB", "interface")
                },
                Links = new[]
                {
                    new ArchitectureLink("root-a", "root", "external-a", "external"),
                    new ArchitectureLink("root-b", "root", "external-b", "external")
                }
            }
        };

        var plan = new ArchitectureDiagramV6Planner().Plan(request);
        var external = plan.NodeMetadata.Where(node => node.IsExternal).ToArray();
        var nonExternal = plan.NodeMetadata.Where(node => !node.IsExternal && !node.IsStandalone).ToArray();
        Assert.NotNull(plan.ReservedDepthTable);
        Assert.Single(external.Select(node => node.PhysicalRow).Distinct());
        Assert.Equal(plan.ReservedDepthTable!.External.NodeRow, external[0].PhysicalRow);
        Assert.True(external.Min(node => node.PhysicalRow) > nonExternal.Max(node => node.PhysicalRow));
    }

    [Fact]
    public void Final_plan_uses_first_matching_role_rule_and_resolves_reserved_depth()
    {
        var request = Request() with
        {
            NodePlacement = Request().NodePlacement with
            {
                RoleRules = new[]
                {
                    new ArchitectureV6RoleRule("CoordinationService", "CoordinationService$", 0),
                    new ArchitectureV6RoleRule("Service", "Service$", 1)
                }
            },
            SemanticModel = Request().SemanticModel with
            {
                Projects = new[]
                {
                    new ArchitectureProject("project:p", "Project", new[]
                    {
                        new ArchitectureNode("root", "project:p", "RootController", "Project.RootController", "Class", "root", Array.Empty<string>()),
                        new ArchitectureNode("coordination", "project:p", "SomeCoordinationService", "Project.SomeCoordinationService", "Class", "coordination", Array.Empty<string>()),
                        new ArchitectureNode("service", "project:p", "OtherService", "Project.OtherService", "Class", "service", Array.Empty<string>())
                    }, "project:p")
                },
                Links = new[]
                {
                    new ArchitectureLink("root-coordination", "root", "coordination", "internal"),
                    new ArchitectureLink("root-service", "root", "service", "internal")
                }
            }
        };

        var plan = new ArchitectureDiagramV6Planner().Plan(request);
        var metadata = plan.NodeMetadata.ToDictionary(node => node.SemanticNodeId, StringComparer.Ordinal);

        Assert.Equal("CoordinationService", metadata["coordination"].RoleSelector);
        Assert.Equal("Service", metadata["service"].RoleSelector);
        Assert.Equal(plan.ReservedDepthTable!.Reservations.Single(item => item.Name == "CoordinationService").NodeRow,
            metadata["coordination"].PhysicalRow);
        Assert.Equal(plan.ReservedDepthTable.Reservations.Single(item => item.Name == "Service").NodeRow,
            metadata["service"].PhysicalRow);
        Assert.True(metadata["coordination"].PhysicalRow < metadata["service"].PhysicalRow);
    }

    [Fact]
    public void Final_plan_keeps_multiple_nodes_in_one_configured_category_on_one_layer()
    {
        var request = Request() with
        {
            NodePlacement = Request().NodePlacement with
            {
                RoleRules = new[] { new ArchitectureV6RoleRule("Service", "Service$", 0) }
            },
            SemanticModel = Request().SemanticModel with
            {
                Projects = new[]
                {
                    new ArchitectureProject("project:p", "Project", new[]
                    {
                        new ArchitectureNode("root", "project:p", "RootController", "Project.RootController", "Class", "root", Array.Empty<string>()),
                        new ArchitectureNode("left", "project:p", "LeftService", "Project.LeftService", "Class", "left", Array.Empty<string>()),
                        new ArchitectureNode("right", "project:p", "RightService", "Project.RightService", "Class", "right", Array.Empty<string>())
                    }, "project:p")
                },
                Links = new[]
                {
                    new ArchitectureLink("root-left", "root", "left", "internal"),
                    new ArchitectureLink("root-right", "root", "right", "internal")
                }
            }
        };

        var plan = new ArchitectureDiagramV6Planner().Plan(request);
        var services = plan.NodeMetadata.Where(node => node.RoleSelector == "Service").ToArray();

        Assert.Equal(2, services.Length);
        Assert.Single(services.Select(node => node.RoleSelector).Distinct());
        Assert.Single(services.Select(node => node.PhysicalRow).Distinct());
        Assert.DoesNotContain(plan.Diagnostics.Findings, finding => finding.Code == "LogicalPlacementLayerOrderContradiction");
    }

    [Fact]
    public void Lane_envelope_includes_port_inset_and_trailing_clearance_for_horizontal_and_vertical_tracks()
    {
        Assert.Equal(122, ArchitectureV6LaneGeometry.RequiredEnvelope(7, 90, 25, 12));
        Assert.Equal(97, ArchitectureV6LaneGeometry.Coordinate(0, 6, 25, 12));
        Assert.True(ArchitectureV6LaneGeometry.Coordinate(0, 6, 25, 12) <
            ArchitectureV6LaneGeometry.RequiredEnvelope(7, 90, 25, 12));
        Assert.Equal(122, ArchitectureV6LaneGeometry.RequiredEnvelope(7, 90, 25, 12));
    }

    [Fact]
    public void Terminal_capacity_uses_edge_insets_and_evenly_spaced_slots()
    {
        var request = Request();

        Assert.Equal(8, ArchitectureV6TerminalCapacity.Inset(request));
        Assert.Equal(0, ArchitectureV6TerminalCapacity.RequiredWidth(request, 0));
        Assert.Equal(16, ArchitectureV6TerminalCapacity.RequiredWidth(request, 1));
        Assert.Equal(24, ArchitectureV6TerminalCapacity.RequiredWidth(request, 2));
        Assert.Equal(32, ArchitectureV6TerminalCapacity.RequiredWidth(request, 3));
        Assert.Equal(3, ArchitectureV6TerminalCapacity.RequiredOddSpan(request, 1));
        Assert.Equal(3, ArchitectureV6TerminalCapacity.RequiredOddSpan(request, 3));
        Assert.True(ArchitectureV6TerminalCapacity.IsInsideInset(request, 100, 0, 200));
        Assert.False(ArchitectureV6TerminalCapacity.IsInsideInset(request, 5, 0, 200));
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

    private static ArchitecturePlanningRequest FanoutRequest(int count)
    {
        var nodes = new List<ArchitectureNode>
        {
            new("root", "project:p", "RootController", "Project.RootController", "Class", "root", Array.Empty<string>())
        };
        var links = new List<ArchitectureLink>();
        for (var index = 0; index < count; index++)
        {
            var id = $"child-{(char)('a' + index)}";
            nodes.Add(new ArchitectureNode(id, "project:p", $"Child{index}Service", $"Project.Child{index}Service", "Class", id, Array.Empty<string>()));
            links.Add(new ArchitectureLink($"root-{id}", "root", id, "internal"));
        }

        return CleanRequest() with
        {
            SemanticModel = CleanRequest().SemanticModel with
            {
                Projects = new[] { new ArchitectureProject("project:p", "Project", nodes, "project:p") },
                Links = links
            }
        };
    }

    private static ArchitectureDiagramModel RegressionSemanticModel()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "ArchitectureV6",
            "content-management-terminal-capacity-regressions.json");
        return JsonSerializer.Deserialize<ArchitectureDiagramModel>(File.ReadAllText(path))
            ?? throw new InvalidOperationException("The regression analyser dataset could not be deserialized.");
    }

    private static ArchitecturePlanningRequest ReservedLayerInsertionRequest() => Request() with
    {
        NodePlacement = Request().NodePlacement with
        {
            RoleRules = new[]
            {
                new ArchitectureV6RoleRule("OrchestrationService", "OrchestrationService$", 0),
                new ArchitectureV6RoleRule("ProcessingService", "ProcessingService$", 1)
            }
        },
        SemanticModel = Request().SemanticModel with
        {
            Projects = new[]
            {
                new ArchitectureProject("project:p", "Project", new[]
                {
                    new ArchitectureNode("orchestration", "project:p", "RootOrchestrationService", "Project.RootOrchestrationService", "Class", "orchestration", Array.Empty<string>()),
                    new ArchitectureNode("helper", "project:p", "Helper", "Project.Helper", "Class", "helper", Array.Empty<string>()),
                    new ArchitectureNode("processing", "project:p", "LeafProcessingService", "Project.LeafProcessingService", "Class", "processing", Array.Empty<string>())
                }, "project:p")
            },
            Links = new[]
            {
                new ArchitectureLink("orchestration-helper", "orchestration", "helper", "internal"),
                new ArchitectureLink("helper-processing", "helper", "processing", "internal")
            }
        }
    };

    private static int ColumnOrdinal(PlanningGridColumnId id)
    {
        if (id.Value.StartsWith("c", StringComparison.Ordinal) && int.TryParse(id.Value[1..], out var compactValue))
            return compactValue;
        var separator = id.Value.LastIndexOf(':');
        return separator >= 0 && int.TryParse(id.Value[(separator + 1)..], out var value) ? value : int.MaxValue;
    }

    private static ArchitecturePlanningRequest CleanRequest() => Request() with
    {
        SemanticModel = Request().SemanticModel with
        {
            Projects = new[]
            {
                new ArchitectureProject("project:p", "Project", new[]
                {
                    new ArchitectureNode("root", "project:p", "RootController", "Project.RootController", "Class", "root", Array.Empty<string>()),
                    new ArchitectureNode("child", "project:p", "ChildService", "Project.ChildService", "Class", "child", Array.Empty<string>())
                }, "project:p")
            },
            ExternalNodes = Array.Empty<ArchitectureExternalNode>(),
            Links = new[] { new ArchitectureLink("root-child", "root", "child", "internal") }
        }
    };
}
