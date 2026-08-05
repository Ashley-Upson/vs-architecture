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
    public void Planner_freezes_structural_track_cardinality_before_abstract_routes()
    {
        var plan = new ArchitectureDiagramV6Planner().Plan(Request());
        var metrics = plan.Diagnostics.Metrics;

        Assert.Equal(metrics.StructuralRowCountBeforeRouting, metrics.StructuralRowCountAfterRouting);
        Assert.Equal(metrics.StructuralColumnCountBeforeRouting, metrics.StructuralColumnCountAfterRouting);
        Assert.DoesNotContain(plan.Diagnostics.Findings, finding => finding.Code == "MissingStructuralRegion");
        Assert.Contains(plan.ProjectGrids.SelectMany(grid => grid.Grid.Rows), row => row.Role == PlanningGridTrackRole.InterLayerRouting);
        Assert.Contains(plan.ProjectGrids.SelectMany(grid => grid.Grid.Columns), column =>
            column.Role == PlanningGridTrackRole.NodeFootprint || column.Role == PlanningGridTrackRole.SubtreeSiblingGap);
    }

    [Fact]
    public void Planner_records_track_roles_and_sizing_metrics()
    {
        var plan = new ArchitectureDiagramV6Planner().Plan(Request());
        var metrics = plan.Diagnostics.Metrics;

        Assert.NotEmpty(metrics.StructuralRowRoleCounts!);
        Assert.NotEmpty(metrics.StructuralColumnRoleCounts!);
        Assert.Equal(0, metrics.RouteOnlyRowCount);
        Assert.Equal(0, metrics.RouteOnlyColumnCount);
        Assert.False(plan.StageStatus.SizingDeferred);
        Assert.True(plan.StageStatus.SizingCompleted);
        Assert.NotNull(plan.RelativeGeometry);
        Assert.True(metrics.SizedNodeCount > 0);
        Assert.True(metrics.GeometryWidth > 0);
    }

    [Fact]
    public void Planner_reuses_horizontal_tracks_for_a_deep_chain()
    {
        var nodes = Enumerable.Range(0, 8)
            .Select(index => new ArchitectureNode($"n{index}", "project:p", $"Node{index}Service", $"Project.Node{index}Service", "Class", $"n{index}", Array.Empty<string>()))
            .ToArray();
        var links = Enumerable.Range(0, 7)
            .Select(index => new ArchitectureLink($"l{index}", $"n{index}", $"n{index + 1}", "internal"))
            .ToArray();
        var request = Request() with
        {
            SemanticModel = new ArchitectureDiagramModel(new[] { new ArchitectureProject("project:p", "Project", nodes, "project:p") },
                Array.Empty<ArchitectureExternalNode>(), links, null)
        };

        var plan = new ArchitectureDiagramV6Planner().Plan(request);
        var footprintColumns = plan.ProjectGrids.Single().Grid.Columns
            .Where(column => column.Role == PlanningGridTrackRole.NodeFootprint).ToArray();

        Assert.Equal(3, footprintColumns.Length);
        Assert.Single(plan.NodePlacements.Select(placement => placement.CentreColumnId).Distinct());
        Assert.Equal(3, plan.SubtreeReservations.Max(reservation => reservation.OccupiedRowIntervals.Max(interval => interval.Columns.Count)));
        Assert.Equal(plan.Diagnostics.Metrics.StructuralColumnCountBeforeRouting,
            plan.Diagnostics.Metrics.StructuralColumnCountAfterRouting);
    }

    [Fact]
    public void Planner_composes_same_row_children_using_exact_profile_width()
    {
        var request = Request() with
        {
            SemanticModel = new ArchitectureDiagramModel(new[]
            {
                new ArchitectureProject("project:p", "Project", new[]
                {
                    new ArchitectureNode("root", "project:p", "Root", "Project.Root", "Class", "root", Array.Empty<string>()),
                    new ArchitectureNode("left", "project:p", "Left", "Project.Left", "Class", "left", Array.Empty<string>()),
                    new ArchitectureNode("right", "project:p", "Right", "Project.Right", "Class", "right", Array.Empty<string>())
                }, "project:p")
            }, Array.Empty<ArchitectureExternalNode>(), new[]
            {
                new ArchitectureLink("root-left", "root", "left", "internal"),
                new ArchitectureLink("root-right", "root", "right", "internal")
            }, null)
        };

        var plan = new ArchitectureDiagramV6Planner().Plan(request);

        Assert.Equal(7, plan.ProjectGrids.Single().Grid.Columns.Count(column => column.Role == PlanningGridTrackRole.NodeFootprint));
        Assert.Equal(7, plan.NodePlacements.Max(placement => placement.Footprint.Max(cell => int.Parse(cell.ColumnId.Value.Split(':').Last())) + 1));
    }

    [Fact]
    public void Planner_reuses_compatible_columns_between_independent_vertical_chains()
    {
        var nodes = new[]
        {
            new ArchitectureNode("root-a", "project:p", "RootA", "Project.RootA", "Class", "root-a", Array.Empty<string>()),
            new ArchitectureNode("child-a", "project:p", "ChildA", "Project.ChildA", "Class", "child-a", Array.Empty<string>()),
            new ArchitectureNode("root-b", "project:p", "RootB", "Project.RootB", "Class", "root-b", Array.Empty<string>()),
            new ArchitectureNode("child-b", "project:p", "ChildB", "Project.ChildB", "Class", "child-b", Array.Empty<string>())
        };
        var request = Request() with
        {
            SemanticModel = new ArchitectureDiagramModel(new[] { new ArchitectureProject("project:p", "Project", nodes, "project:p") },
                Array.Empty<ArchitectureExternalNode>(), new[]
                {
                    new ArchitectureLink("a", "root-a", "child-a", "internal"),
                    new ArchitectureLink("b", "root-b", "child-b", "internal")
                }, null)
        };

        var plan = new ArchitectureDiagramV6Planner().Plan(request);

        Assert.Equal(6, plan.ProjectGrids.Single().Grid.Columns.Count(column => column.Role == PlanningGridTrackRole.NodeFootprint));
        Assert.Equal(9, plan.ProjectGrids.Single().Grid.Columns.Count);
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
    public void Planner_creates_shared_destination_and_return_regions_before_routing()
    {
        var plan = new ArchitectureDiagramV6Planner().Plan(Request());
        var roles = plan.ProjectGrids.SelectMany(grid => grid.Grid.Columns).Select(column => column.Role).ToArray();
        var columns = plan.ProjectGrids.SelectMany(grid => grid.Grid.Columns).ToArray();

        Assert.Contains(PlanningGridTrackRole.DestinationApproach, roles);
        Assert.Contains(PlanningGridTrackRole.OwnershipLocalReturn, roles);
        Assert.All(columns.Where(column => column.Role == PlanningGridTrackRole.DestinationApproach ||
            column.Role == PlanningGridTrackRole.OwnershipLocalReturn), column => Assert.False(string.IsNullOrWhiteSpace(column.OwnerId)));
        Assert.DoesNotContain(plan.Diagnostics.Findings, finding => finding.Code == "MissingStructuralRegion");
    }

    [Fact]
    public void Planner_relative_node_bounds_equal_their_final_grid_footprints()
    {
        var plan = new ArchitectureDiagramV6Planner().Plan(Request());
        Assert.NotNull(plan.RelativeGeometry);
        Assert.Equal(plan.PhysicalNodes.Count, plan.RelativeGeometry!.Nodes.Count);
        Assert.DoesNotContain(plan.Diagnostics.Findings, finding => finding.Code == "V6PhysicalSizingDeferred");
        Assert.DoesNotContain(plan.Diagnostics.Findings, finding => finding.Code == "RelativeNodeOverlap");
    }

    [Fact]
    public void Planner_relative_sizing_is_deterministic_and_idempotent()
    {
        var first = new ArchitectureDiagramV6Planner().Plan(Request());
        var second = new ArchitectureDiagramV6Planner().Plan(Request());

        Assert.NotNull(first.RelativeGeometry);
        Assert.NotNull(second.RelativeGeometry);
        Assert.True(first.Diagnostics.Metrics.SizingIdempotent);
        Assert.Equal(first.RelativeGeometry!.DiagramBounds, second.RelativeGeometry!.DiagramBounds);
        Assert.Equal(first.RelativeGeometry.Nodes.Select(node => node.Bounds), second.RelativeGeometry.Nodes.Select(node => node.Bounds));
        Assert.Equal(first.ProjectGrids.SelectMany(grid => grid.Grid.Rows.Select(row => row.Id)),
            second.ProjectGrids.SelectMany(grid => grid.Grid.Rows.Select(row => row.Id)));
        Assert.Equal(first.ProjectGrids.SelectMany(grid => grid.Grid.Columns.Select(column => column.Id)),
            second.ProjectGrids.SelectMany(grid => grid.Grid.Columns.Select(column => column.Id)));
    }

    [Fact]
    public void Planner_sizes_existing_tracks_with_provenance_and_sparse_reservation_intervals()
    {
        var plan = new ArchitectureDiagramV6Planner().Plan(Request());
        var projectGrid = Assert.Single(plan.RelativeGeometry!.Grids, grid => grid.GridId.Value.StartsWith("project:", StringComparison.Ordinal));
        var projectSizing = plan.Sizing.Columns.Where(column => projectGrid.Columns.Any(item => item.Id.Equals(column.Id))).ToArray();

        Assert.Equal(plan.ProjectGrids.Single().Grid.Rows.Count, projectGrid.Rows.Count);
        Assert.Equal(plan.ProjectGrids.Single().Grid.Columns.Count, projectGrid.Columns.Count);
        Assert.All(projectGrid.Rows, track => Assert.True(track.FinalExtent > 0));
        Assert.All(projectGrid.Columns, track => Assert.True(track.FinalExtent > 0));
        Assert.All(plan.Sizing.Provenance!.Where(item => item.GridId.Equals(projectGrid.GridId)), item =>
        {
            Assert.True(item.FinalExtent >= item.MinimumExtent);
            Assert.NotEqual(PlanningGridTrackRole.Unknown, item.StructuralRole);
        });
        Assert.NotEmpty(plan.RelativeGeometry.Subtrees.SelectMany(subtree => subtree.Intervals!));
        Assert.Contains(plan.Sizing.Constraints, constraint => constraint.Kind == TrackConstraintKind.HorizontalLaneEnvelope);
        Assert.Contains(plan.Sizing.Constraints, constraint => constraint.Kind == TrackConstraintKind.VerticalLaneEnvelope);
        Assert.Contains(plan.Sizing.Constraints, constraint => constraint.Kind == TrackConstraintKind.TurnClearance);
        Assert.DoesNotContain(plan.Diagnostics.Findings, finding => finding.Code is "RelativeNodeOverlap" or "SizingInvalidTrack");
        Assert.NotEmpty(projectSizing);
    }

    [Fact]
    public void Planner_does_not_apply_node_content_width_to_routing_columns()
    {
        var request = Request() with
        {
            GridSizing = new GridSizingPolicy(200, 80, 40, 30)
        };
        var plan = new ArchitectureDiagramV6Planner().Plan(request);
        var routingColumns = plan.RelativeGeometry!.Grids.Single(grid => grid.GridId.Equals(plan.ProjectGrids.Single().Grid.Id)).Columns
            .Where(column => column.Role is PlanningGridTrackRole.DestinationApproach or PlanningGridTrackRole.OwnershipLocalReturn)
            .ToArray();

        Assert.NotEmpty(routingColumns);
        Assert.Contains(routingColumns, column => column.FinalExtent < request.GridSizing.CellWidth);
        Assert.All(routingColumns, column => Assert.Equal(1, column.MinimumExtent));
    }

    [Fact]
    public void Planner_distributes_node_width_across_footprint_span_only()
    {
        var request = Request() with
        {
            NodePlacement = new NodePlacementPolicy("*OrchestrationService", 200, 60, 20, 40),
            GridSizing = new GridSizingPolicy(200, 80, 40, 30)
        };
        var plan = new ArchitectureDiagramV6Planner().Plan(request);
        var placement = plan.NodePlacements.First(item => item.ColumnSpan == 3);
        var grid = plan.RelativeGeometry!.Grids.Single(item => item.GridId.Equals(placement.GridId));
        var footprintColumns = placement.Footprint.Select(cell => grid.Columns.Single(column => column.Id.Equals(cell.ColumnId))).ToArray();

        Assert.True(footprintColumns.Sum(column => column.FinalExtent) >= 200);
        Assert.All(footprintColumns, column => Assert.True(column.FinalExtent < 200));
        Assert.Contains(plan.Sizing.Provenance!, item => item.DominantConstraint == TrackConstraintKind.NodeFootprint);
        Assert.DoesNotContain(grid.Columns.Where(column => !placement.Footprint.Any(cell => cell.ColumnId.Equals(column.Id))),
            column => column.FinalExtent >= 200);
    }

    [Fact]
    public void Planner_records_real_structural_and_capacity_contributions()
    {
        var plan = new ArchitectureDiagramV6Planner().Plan(Request());
        var provenance = plan.Sizing.Provenance!;

        Assert.Contains(provenance, item => item.Contributions.Any(contribution => contribution.Kind == TrackConstraintKind.SingleColumnMinimum));
        Assert.Contains(provenance, item => item.Contributions.Any(contribution => contribution.Kind == TrackConstraintKind.SingleRowMinimum));
        Assert.Contains(provenance, item => item.Contributions.Any(contribution => contribution.Kind == TrackConstraintKind.NodeFootprint));
        Assert.Contains(plan.Sizing.Constraints, constraint => constraint.Kind == TrackConstraintKind.VerticalLaneEnvelope);
        Assert.Contains(plan.Sizing.Constraints, constraint => constraint.Kind == TrackConstraintKind.TurnClearance);
    }

    [Fact]
    public void Planner_adds_project_padding_once_outside_project_grid_tracks()
    {
        var request = Request() with { GridSizing = new GridSizingPolicy(40, 80, 40, 30) };
        var plan = new ArchitectureDiagramV6Planner().Plan(request);
        var project = Assert.Single(plan.RelativeGeometry!.Projects);
        var grid = Assert.Single(plan.RelativeGeometry.Grids, item => item.GridId.Equals(plan.ProjectGrids.Single().Grid.Id));

        Assert.Equal(grid.Columns.Sum(column => column.FinalExtent) + request.GridSizing.ContainerPadding * 2, project.Bounds.Width);
        Assert.Equal(grid.Rows.Sum(row => row.FinalExtent) + request.GridSizing.ContainerPadding * 2 + request.GridSizing.ProjectHeaderHeight, project.Bounds.Height);
    }

    [Fact]
    public void Planner_relative_node_bounds_are_exact_complete_footprint_envelopes()
    {
        var plan = new ArchitectureDiagramV6Planner().Plan(Request());

        foreach (var node in plan.RelativeGeometry!.Nodes)
        {
            var placement = plan.NodePlacements.Single(item => item.PhysicalNodeId == node.PhysicalNodeId);
            var grid = plan.RelativeGeometry.Grids.Single(item => item.GridId.Equals(node.GridId));
            var columns = placement.Footprint.Select(cell => grid.Columns.Single(column => column.Id.Equals(cell.ColumnId))).ToArray();
            var rows = placement.Footprint.Select(cell => grid.Rows.Single(row => row.Id.Equals(cell.RowId))).ToArray();
            var x = columns.Min(column => column.RelativeOffset);
            var y = rows.Min(row => row.RelativeOffset);
            var expected = new RelativeRectangle(x, y,
                columns.Max(column => column.RelativeOffset + column.FinalExtent) - x,
                rows.Max(row => row.RelativeOffset + row.FinalExtent) - y);

            Assert.Equal(expected, node.Bounds);
        }
    }

    [Fact]
    public void Planner_sizing_preserves_structural_cardinality_and_satisfies_constraints()
    {
        var plan = new ArchitectureDiagramV6Planner().Plan(Request());
        var project = Assert.Single(plan.ProjectGrids);
        var sizedProject = Assert.Single(plan.RelativeGeometry!.Grids, grid => grid.GridId.Equals(project.Grid.Id));

        Assert.Equal(project.Grid.Rows.Count, sizedProject.Rows.Count);
        Assert.Equal(project.Grid.Columns.Count, sizedProject.Columns.Count);
        Assert.Equal(project.Grid.Rows.Select(row => row.Id), sizedProject.Rows.Select(row => row.Id));
        Assert.Equal(project.Grid.Columns.Select(column => column.Id), sizedProject.Columns.Select(column => column.Id));
        Assert.DoesNotContain(plan.Diagnostics.Findings, finding => finding.Code is "SizingConstraintUnsatisfied" or "SizingConstraintTrackMissing" or "SizingGridBoundsMismatch");
        Assert.All(plan.RelativeGeometry.Grids, grid =>
        {
            Assert.Equal(grid.Columns.Sum(column => column.FinalExtent), grid.RelativeBounds.Width);
            Assert.Equal(grid.Rows.Sum(row => row.FinalExtent), grid.RelativeBounds.Height);
        });
    }

    [Fact]
    public void Planner_compiles_relative_geometry_after_grid_planning()
    {
        var plan = new ArchitectureDiagramV6Planner().Plan(Request());

        Assert.True(plan.StageStatus.ProjectionCompleted);
        Assert.True(plan.StageStatus.LogicalPlacementCompleted);
        Assert.True(plan.StageStatus.AbstractRoutingCompleted);
        Assert.False(plan.StageStatus.LaneAllocationDeferred);
        Assert.False(plan.StageStatus.SizingDeferred);
        Assert.True(plan.StageStatus.SizingCompleted);
        Assert.True(plan.StageStatus.CapacityConstraintsCompleted);
        Assert.False(plan.StageStatus.PhysicalSizingDeferred);
        Assert.False(plan.StageStatus.AbsoluteGeometryDeferred);
        Assert.True(plan.StageStatus.AbsoluteGeometryCompleted);
        Assert.DoesNotContain(plan.Diagnostics.Findings, finding => finding.Code == "V6PlacementDeferred");
        Assert.DoesNotContain(plan.Diagnostics.Findings, finding => finding.Code == "V6RoutePlanningDeferred");
        Assert.NotNull(plan.PhysicalScene);
        Assert.Equal(plan.PhysicalNodes.Count, plan.PhysicalScene!.Metrics.AbsoluteNodeCount);
        Assert.Equal(plan.PhysicalLinks.Count, plan.PhysicalScene.Metrics.PhysicalRouteCount);
        Assert.NotNull(plan.RelativeGeometry);
    }

    [Fact]
    public void Planner_compiles_absolute_nodes_from_the_relative_grid_envelope()
    {
        var plan = new ArchitectureDiagramV6Planner().Plan(Request());

        Assert.NotNull(plan.PhysicalScene);
        Assert.Equal(plan.PhysicalNodes.Count, plan.PhysicalScene!.Geometry.Nodes.Count);
        foreach (var node in plan.PhysicalScene.Geometry.Nodes)
        {
            var transform = plan.PhysicalScene.Transforms.Single(item => item.GridId.Equals(node.GridId));
            var relative = plan.RelativeGeometry!.Nodes.Single(item => item.PhysicalNodeId == node.PhysicalNodeId);
            Assert.Equal(relative.Bounds.X + transform.Origin.X, node.AbsoluteBounds.X);
            Assert.Equal(relative.Bounds.Y + transform.Origin.Y, node.AbsoluteBounds.Y);
            Assert.Equal(relative.Bounds.Width, node.AbsoluteBounds.Width);
            Assert.Equal(relative.Bounds.Height, node.AbsoluteBounds.Height);
        }
    }

    [Fact]
    public void Planner_materialises_one_route_and_two_terminals_per_physical_link()
    {
        var plan = new ArchitectureDiagramV6Planner().Plan(Request());

        Assert.NotNull(plan.PhysicalScene);
        Assert.Equal(plan.PhysicalLinks.Count, plan.PhysicalScene!.Metrics.PhysicalRouteCount);
        Assert.Equal(plan.PhysicalLinks.Count * 2, plan.PhysicalScene.Terminals.Count);
        Assert.All(plan.PhysicalScene.Terminals, terminal =>
        {
            var node = plan.PhysicalScene.Geometry.Nodes.Single(item => item.PhysicalNodeId == terminal.PhysicalNodeId);
            Assert.Equal(terminal.Side == GridSide.Bottom ? node.AbsoluteBounds.Y + node.AbsoluteBounds.Height : node.AbsoluteBounds.Y, terminal.Point.Y);
        });
    }

    [Fact]
    public void Planner_preserves_ordered_route_components_and_segment_provenance()
    {
        var plan = new ArchitectureDiagramV6Planner().Plan(Request());

        Assert.NotNull(plan.PhysicalScene);
        Assert.DoesNotContain(plan.Diagnostics.Findings, finding => finding.Code == "MissingPhysicalTurnForDirectionChange");
        Assert.All(plan.PhysicalScene!.Geometry.Routes, route =>
        {
            Assert.NotNull(route.RawPoints);
            Assert.NotNull(route.Components);
            Assert.NotEmpty(route.Components!);
            Assert.Equal(route.RawPoints!.Count, route.NormalizedPointCount);
            Assert.All(route.Components!, component =>
            {
                Assert.NotEmpty(component.ComponentId);
                Assert.NotEmpty(component.Points);
                Assert.All(component.Points, point => Assert.Equal(component.ComponentId, point.ComponentId));
            });
            Assert.All(route.Segments, segment =>
            {
                Assert.NotEmpty(segment.ComponentId);
                Assert.NotEmpty(segment.StartProvenance);
                Assert.NotEmpty(segment.EndProvenance);
                Assert.NotNull(segment.RelativeStart);
                Assert.NotNull(segment.RelativeEnd);
            });
        });
    }

    [Fact]
    public void Planner_repeated_physical_compilation_is_deterministic()
    {
        var first = new ArchitectureDiagramV6Planner().Plan(Request());
        var second = new ArchitectureDiagramV6Planner().Plan(Request());

        Assert.Equal(first.PhysicalScene!.Geometry.Nodes, second.PhysicalScene!.Geometry.Nodes);
        foreach (var route in first.PhysicalScene.Geometry.Routes)
        {
            var other = second.PhysicalScene.Geometry.Routes.Single(item => item.PhysicalLinkId == route.PhysicalLinkId);
            Assert.Equal(route.RouteLength, other.RouteLength);
            Assert.Equal(route.Segments.Select(segment => (segment.Start, segment.End, segment.Axis)),
                other.Segments.Select(segment => (segment.Start, segment.End, segment.Axis)));
        }
        Assert.Equal(first.PhysicalScene.Terminals, second.PhysicalScene.Terminals);
        Assert.Equal(first.PhysicalScene.Metrics.AbsoluteNodeCount, second.PhysicalScene.Metrics.AbsoluteNodeCount);
        Assert.Equal(first.PhysicalScene.Metrics.TerminalCount, second.PhysicalScene.Metrics.TerminalCount);
        Assert.Equal(first.PhysicalScene.Metrics.SegmentCount, second.PhysicalScene.Metrics.SegmentCount);
        Assert.Equal(first.PhysicalScene.Metrics.TotalRouteLength, second.PhysicalScene.Metrics.TotalRouteLength);
        Assert.Equal(first.PhysicalScene.Metrics.TopologyCounts, second.PhysicalScene.Metrics.TopologyCounts);
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
    public void Planner_compiles_topology_owned_routes_and_collective_approaches()
    {
        var plan = new ArchitectureDiagramV6Planner().Plan(Request());

        Assert.True(plan.StageStatus.AbstractRoutingCompleted);
        Assert.False(plan.StageStatus.LaneAllocationDeferred);
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
        Assert.NotNull(plan.RelativeGeometry);
        Assert.NotNull(plan.Sizing);
        Assert.Equal(plan.SubtreeReservations.Count, plan.ProjectGrids.SelectMany(grid => grid.SubtreeReservations).Count());
        Assert.Empty(plan.LaneAllocation!.Conflicts);
        Assert.DoesNotContain(plan.Diagnostics.Findings, finding => finding.Code == "IncompatibleLaneOrdering");
        Assert.True(new ArchitectureDiagramV6Validator().Validate(plan).IsValid);
    }

    [Fact]
    public void Planner_reports_collective_lane_ordering_without_changing_structural_tracks()
    {
        var plan = new ArchitectureDiagramV6Planner().Plan(Request());
        var metrics = plan.Diagnostics.Metrics;

        Assert.NotNull(plan.LaneAllocation!.Performance);
        Assert.True(metrics.LaneDomainCount > 0);
        Assert.Equal(plan.StraightRuns.Count, metrics.LaneOrderingVertexCount);
        Assert.True(metrics.LaneOrderingEdgeCount >= 0);
        Assert.Equal(0, metrics.LaneOrderingCycleCount);
        Assert.Equal(metrics.StructuralColumnCountBeforeRouting, metrics.StructuralColumnCountAfterRouting);
        Assert.Empty(plan.LaneAllocation.Conflicts);
        Assert.Equal(plan.LaneAllocation.Turns.Count,
            plan.LaneAllocation.Turns.Select(turn => turn.BendIdentity).Distinct(StringComparer.Ordinal).Count());
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
    public void Planner_keeps_external_departure_outside_an_expanded_source_footprint()
    {
        var request = Request() with
        {
            SemanticModel = new ArchitectureDiagramModel(
                new[]
                {
                    new ArchitectureProject("project:p", "Project", new[]
                    {
                        new ArchitectureNode("source", "project:p", "SourceWithAnIntentionallyLongName", "Project.SourceWithAnIntentionallyLongName", "Class", "source", Array.Empty<string>())
                    }, "project:p")
                },
                new[]
                {
                    new ArchitectureExternalNode("external", "ExternalDependency", "External.Assembly", "external", "External.ExternalDependency", "[External]")
                },
                new[] { new ArchitectureLink("source-external", "source", "external", "external") },
                null)
        };

        var plan = new ArchitectureDiagramV6Planner().Plan(request);
        var route = Assert.Single(plan.Routes);
        var sourcePlacement = Assert.Single(plan.NodePlacements.Where(item => item.PhysicalNodeId == route.Source.PhysicalNodeId));
        var sourceGrid = Assert.Single(plan.ProjectGrids.Where(grid => grid.Grid.Id.Equals(sourcePlacement.GridId)));
        var departure = route.Steps[1];

        Assert.True(route.IsStructurallySupported);
        Assert.DoesNotContain(sourcePlacement.Footprint, cell => cell.ColumnId.Equals(departure.CellId.ColumnId));
        Assert.Null(sourceGrid.Grid.Cells[departure.CellId].FootprintOwnerId);
        Assert.Empty(plan.LaneAllocation!.Conflicts);
        Assert.Equal(0, plan.Diagnostics.Metrics.UnsupportedRouteCount);
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
