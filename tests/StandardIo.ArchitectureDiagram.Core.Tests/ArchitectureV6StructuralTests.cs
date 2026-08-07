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
    public void Planner_adds_permanent_exterior_rows_around_authoritative_project_rows()
    {
        var plan = new ArchitectureDiagramV6Planner().Plan(Request());
        var grid = Assert.Single(plan.ProjectGrids).Grid;

        Assert.Equal("routing:exterior:top", grid.Rows[0].Id.Value);
        Assert.Equal("routing:exterior:bottom", grid.Rows[^1].Id.Value);
        Assert.All(grid.Rows.Where(row => row.Id.Value.StartsWith("routing:exterior:", StringComparison.Ordinal)),
            row => Assert.Equal(PlanningGridTrackRole.InterLayerRouting, row.Role));
        Assert.All(plan.NodePlacements, placement =>
        {
            var row = grid.Rows.Single(item => item.Id.Equals(placement.AnchorCellId.RowId));
            Assert.True(row.LogicalOrder > grid.Rows[0].LogicalOrder);
            Assert.True(row.LogicalOrder < grid.Rows[^1].LogicalOrder);
        });
    }

    [Fact]
    public void Planner_routes_use_contiguous_authoritative_cells_without_adjacent_turns()
    {
        var plan = new ArchitectureDiagramV6Planner().Plan(Request());
        var grids = plan.ProjectGrids.Select(item => item.Grid).ToDictionary(item => item.Id);

        Assert.All(plan.Routes, route =>
        {
            Assert.True(route.IsStructurallySupported);
            for (var index = 1; index < route.Steps.Count; index++)
            {
                var previous = route.Steps[index - 1];
                var current = route.Steps[index];
                if (previous.GridId != current.GridId) continue;
                var grid = grids[previous.GridId];
                var rowDistance = Math.Abs(grid.Rows.Single(row => row.Id.Equals(previous.CellId.RowId)).LogicalOrder -
                    grid.Rows.Single(row => row.Id.Equals(current.CellId.RowId)).LogicalOrder);
                var columnDistance = Math.Abs(grid.Columns.Single(column => column.Id.Equals(previous.CellId.ColumnId)).LogicalOrder -
                    grid.Columns.Single(column => column.Id.Equals(current.CellId.ColumnId)).LogicalOrder);
                Assert.Equal(1, rowDistance + columnDistance);
                Assert.False(previous.Role == RouteStepRole.Turn && current.Role == RouteStepRole.Turn);
            }
        });
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
        Assert.Equal(plan.PhysicalLinks.Count, plan.PhysicalScene.Geometry.Routes.Count);
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
            var visible = relative.VisibleBounds ?? relative.Bounds;
            Assert.Equal(visible.X + transform.Origin.X, node.AbsoluteBounds.X);
            Assert.Equal(visible.Y + transform.Origin.Y, node.AbsoluteBounds.Y);
            Assert.Equal(visible.Width, node.AbsoluteBounds.Width);
            Assert.Equal(visible.Height, node.AbsoluteBounds.Height);
            Assert.Equal(relative.Bounds.X + transform.Origin.X, node.AbsoluteRoutingBounds!.Value.X);
            Assert.Equal(relative.Bounds.Width, node.AbsoluteRoutingBounds.Value.Width);
        }

        var projectTransform = Assert.Single(plan.PhysicalScene.Transforms,
            transform => transform.GridId.Equals(plan.ProjectGrids[0].Grid.Id));
        Assert.Equal(new RelativePoint(0, 0), projectTransform.Origin);
    }

    [Fact]
    public void Planner_materialises_one_route_and_two_terminals_per_physical_link()
    {
        var plan = new ArchitectureDiagramV6Planner().Plan(Request());

        Assert.NotNull(plan.PhysicalScene);
        Assert.Equal(plan.PhysicalLinks.Count, plan.PhysicalScene!.Metrics.PhysicalRouteCount);
        Assert.Equal(plan.PhysicalLinks.Count, plan.PhysicalScene.Geometry.Routes.Count);
        Assert.Equal(plan.PhysicalLinks.Count * 2, plan.PhysicalScene.Terminals.Count);
        Assert.Equal(plan.PhysicalScene.Metrics.InvalidRouteCount, plan.PhysicalScene.InvalidRouteIds.Count);
        Assert.All(plan.PhysicalScene.Geometry.Routes.SelectMany(route => route.Segments), segment =>
        {
            Assert.True(segment.Start.X == segment.End.X || segment.Start.Y == segment.End.Y);
            Assert.NotEmpty(segment.AllocatedCells!);
        });
        Assert.All(plan.PhysicalScene.Terminals, terminal =>
        {
            var node = plan.PhysicalScene.Geometry.Nodes.Single(item => item.PhysicalNodeId == terminal.PhysicalNodeId);
            Assert.Equal(terminal.Side == GridSide.Bottom ? node.AbsoluteBounds.Y + node.AbsoluteBounds.Height : node.AbsoluteBounds.Y, terminal.Point.Y);
        });
    }

    [Fact]
    public void Planner_aligns_endpoint_terminals_to_the_authoritative_route_lanes()
    {
        var scene = new ArchitectureDiagramV6Planner().Plan(Request()).PhysicalScene;
        Assert.NotNull(scene);

        foreach (var route in scene.Geometry.Routes)
        {
            var sourceTerminal = scene.Terminals.Single(item => item.PhysicalLinkId == route.PhysicalLinkId && item.Side == GridSide.Bottom);
            var destinationTerminal = scene.Terminals.Single(item => item.PhysicalLinkId == route.PhysicalLinkId && item.Side == GridSide.Top);
            var sourcePoint = route.RawPoints!.First(point => point.Point != sourceTerminal.Point);
            var destinationPoint = route.RawPoints!.Reverse().First(point => point.Point != destinationTerminal.Point);

            Assert.True(sourcePoint.Point.Y > sourceTerminal.Point.Y,
                $"source terminal={sourceTerminal.Point}, first route point={sourcePoint.Point}");
            Assert.True(destinationTerminal.Point.Y > destinationPoint.Point.Y,
                $"destination terminal={destinationTerminal.Point}, last route point={destinationPoint.Point}");

            var complete = new[] { sourceTerminal.Point }
                .Concat(route.RawPoints!.Select(point => point.Point))
                .Append(destinationTerminal.Point)
                .ToArray();
            Assert.All(complete.Zip(complete.Skip(1), (left, right) => (left, right)), pair =>
                Assert.True(pair.left.X == pair.right.X || pair.left.Y == pair.right.Y,
                    "terminal and route centreline must remain orthogonal"));
        }
    }

    [Fact]
    public void Planner_retains_invalid_materialisation_attempts_with_route_geometry()
    {
        var plan = new ArchitectureDiagramV6Planner().Plan(Request());
        Assert.NotNull(plan.PhysicalScene);
        var scene = plan.PhysicalScene!;

        Assert.Equal(scene.Metrics.InvalidRouteCount, scene.InvalidRouteIds.Count);
        Assert.Equal(scene.Metrics.PhysicalRouteCount, scene.Geometry.Routes.Count);
        Assert.All(scene.InvalidRouteIds, routeId =>
            Assert.Contains(scene.Geometry.Routes, route => route.PhysicalLinkId == routeId && route.IsInvalid &&
                route.RawPoints is not null && route.Components is not null));
        Assert.Equal(scene.Metrics.DiagonalSegmentCount,
            scene.AttemptedSegments.Count(attempt => attempt.FailureCode == "DiagonalComponentConnection"));
        Assert.Equal(0, scene.Metrics.DiagonalSegmentCount);
        Assert.All(scene.Geometry.Routes.SelectMany(route => route.Segments), segment =>
            Assert.True(segment.Start.X == segment.End.X || segment.Start.Y == segment.End.Y));
        // Malformed logical endpoint tails remain diagnostics, but the active
        // physical scene must never emit a diagonal Draw.io segment.
        Assert.DoesNotContain(scene.Geometry.Routes.SelectMany(route => route.Segments), segment =>
            segment.Start.X != segment.End.X && segment.Start.Y != segment.End.Y);
    }

    [Fact]
    public void Planner_records_complete_corridor_provenance_for_accepted_segments()
    {
        var scene = new ArchitectureDiagramV6Planner().Plan(Request()).PhysicalScene;
        Assert.NotNull(scene);

        Assert.All(scene.Geometry.Routes.SelectMany(route => route.Segments), segment =>
        {
            Assert.NotNull(segment.AllocatedCells);
            Assert.NotEmpty(segment.AllocatedCells!);
            Assert.NotEmpty(segment.ComponentId);
            Assert.NotEmpty(segment.StartProvenance);
            Assert.NotEmpty(segment.EndProvenance);
        });
    }

    [Fact]
    public void Planner_builds_authoritative_route_boundary_contracts_before_physical_materialisation()
    {
        var plan = new ArchitectureDiagramV6Planner().Plan(Request());
        var validation = plan.LaneAllocation!.BoundaryValidation;

        Assert.NotNull(validation);
        Assert.Equal(plan.PhysicalLinks.Count, validation!.Routes.Count);
        Assert.Equal(plan.PhysicalLinks.Count, validation.ValidRouteCount + validation.InvalidRouteCount);
        Assert.All(validation.Routes, route =>
        {
            Assert.NotEmpty(route.Components);
            Assert.All(route.Components, component =>
            {
                Assert.NotEmpty(component.ComponentId);
                Assert.NotNull(component.EntryBoundary);
                Assert.NotNull(component.ExitBoundary);
            });
            var pairs = route.Components.Zip(route.Components.Skip(1), (before, after) => (before, after)).ToArray();
            Assert.All(pairs.Where(pair => pair.before.ExitBoundary == pair.after.EntryBoundary), pair =>
                Assert.Equal(pair.before.ExitBoundary, pair.after.EntryBoundary));
        });
        Assert.DoesNotContain(validation.Findings, finding => finding.Code == "SourceDepartureNotBottomFacing");
        Assert.DoesNotContain(validation.Findings, finding => finding.Code == "DestinationApproachNotTopFacing");
    }

    [Fact]
    public void Planner_consumes_all_six_endpoint_components_without_direct_terminal_to_run_geometry()
    {
        var scene = new ArchitectureDiagramV6Planner().Plan(Request()).PhysicalScene;
        Assert.NotNull(scene);

        Assert.All(scene!.Geometry.Routes, route =>
        {
            var components = route.Components!;
            Assert.Contains(components, component => component.ComponentId.EndsWith(":source-terminal", StringComparison.Ordinal));
            Assert.Contains(components, component => component.ComponentId.EndsWith(":source-node-anchor", StringComparison.Ordinal));
            Assert.Contains(components, component => component.ComponentId.EndsWith(":source-departure", StringComparison.Ordinal));
            Assert.Contains(components, component => component.ComponentId.EndsWith(":destination-approach", StringComparison.Ordinal));
            Assert.Contains(components, component => component.ComponentId.EndsWith(":destination-node-anchor", StringComparison.Ordinal));
            Assert.Contains(components, component => component.ComponentId.EndsWith(":destination-terminal", StringComparison.Ordinal));

            var sourceAnchor = components.Single(component => component.ComponentId.EndsWith(":source-node-anchor", StringComparison.Ordinal));
            var sourceDeparture = components.Single(component => component.ComponentId.EndsWith(":source-departure", StringComparison.Ordinal));
            var destinationApproach = components.Single(component => component.ComponentId.EndsWith(":destination-approach", StringComparison.Ordinal));
            var destinationAnchor = components.Single(component => component.ComponentId.EndsWith(":destination-node-anchor", StringComparison.Ordinal));
            Assert.Equal(sourceAnchor.ExitPoint, sourceDeparture.EntryPoint);
            Assert.Equal(destinationApproach.ExitPoint, destinationAnchor.EntryPoint);
            Assert.NotEqual(sourceAnchor.ComponentId, sourceDeparture.ComponentId);
            Assert.NotEqual(destinationApproach.ComponentId, destinationAnchor.ComponentId);
        });
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
    public void Planner_binds_turns_to_both_adjoining_run_lanes()
    {
        var plan = new ArchitectureDiagramV6Planner().Plan(Request());
        var validation = plan.LaneAllocation!.BoundaryValidation!;

        Assert.DoesNotContain(validation.Findings, finding => finding.Code == "IncompleteTurnAllocation");
        Assert.All(validation.Routes.SelectMany(route => route.Components.Where(component => component.Kind == PlannedRouteComponentKind.Turn)), turn =>
        {
            Assert.NotNull(turn.EntryBoundary);
            Assert.NotNull(turn.ExitBoundary);
            Assert.NotNull(turn.EntryBoundary!.Lane);
            Assert.NotNull(turn.ExitBoundary!.Lane);
        });
    }

    [Fact]
    public void Planner_keeps_turn_handoffs_on_the_allocated_corridor()
    {
        var plan = new ArchitectureDiagramV6Planner().Plan(Request());
        var validation = plan.LaneAllocation!.BoundaryValidation!;
        var mismatches = validation.Routes
            .SelectMany(route => route.Components.Zip(route.Components.Skip(1), (before, after) => (route, before, after)))
            .Where(pair => pair.before.Kind == PlannedRouteComponentKind.Turn &&
                           (pair.after.Kind == PlannedRouteComponentKind.Turn || pair.after.Kind == PlannedRouteComponentKind.DestinationApproach) &&
                           pair.before.ExitBoundary != pair.after.EntryBoundary)
            .ToArray();

        Assert.True(mismatches.Length == 0,
            string.Join(Environment.NewLine, mismatches.Take(3).Select(pair =>
                $"{pair.route.PhysicalLinkId} {pair.before.Kind}->{pair.after.Kind}: {pair.before.ExitBoundary} != {pair.after.EntryBoundary}")));
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
            Assert.InRange(route.NormalizedPointCount, 0, route.RawPoints!.Count);
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
    public void Planner_keeps_baseline_members_on_one_final_layer_and_reports_hierarchy_conflict()
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
        var root = baseline.Single(node => node.SemanticNodeId == "root");
        var child = baseline.Single(node => node.SemanticNodeId == "child");
        Assert.Equal(root.PhysicalRow, child.PhysicalRow);
        Assert.Equal(0, plan.NodeMetadata.Single(node => node.SemanticNodeId == "root").SemanticDepth);
        Assert.Equal(1, plan.NodeMetadata.Single(node => node.SemanticNodeId == "child").SemanticDepth);
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
    public void Final_pipeline_uses_nearest_free_external_position_when_owner_column_is_occupied()
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
        var owner = plan.NodeMetadata.Single(node => node.SemanticNodeId == "root");

        Assert.Equal(2, external.Length);
        Assert.Contains(external, node => node.PhysicalColumn == owner.PhysicalColumn);
        Assert.Single(plan.Diagnostics.Findings.Where(finding => finding.Code == "LogicalPlacementExternalAffinityBlocked"));
    }

    [Fact]
    public void Final_pipeline_reports_a_category_order_cycle_explicitly()
    {
        var request = Request() with
        {
            NodePlacement = Request().NodePlacement with
            {
                BaselinePattern = "^does-not-match$",
                RoleRules = new[]
                {
                    new ArchitectureV6RoleRule("Beta", "Beta$", 0),
                    new ArchitectureV6RoleRule("Alpha", "Alpha$", 1)
                }
            },
            SemanticModel = Request().SemanticModel with
            {
                Projects = new[]
                {
                    new ArchitectureProject("project:p", "Project", new[]
                    {
                        new ArchitectureNode("alpha", "project:p", "Alpha", "Project.Alpha", "Class", "alpha", Array.Empty<string>()),
                        new ArchitectureNode("beta", "project:p", "Beta", "Project.Beta", "Class", "beta", Array.Empty<string>())
                    }, "project:p")
                },
                Links = new[] { new ArchitectureLink("alpha-beta", "alpha", "beta", "internal") }
            }
        };

        var plan = new ArchitectureDiagramV6Planner().Plan(request);

        Assert.Contains(plan.Diagnostics.Findings, finding => finding.Code == "LogicalPlacementCategoryOrderCycle");
    }

    [Fact]
    public void Planner_uses_baseline_pattern_and_role_rules_as_separate_visual_bands()
    {
        var request = Request() with
        {
            NodePlacement = Request().NodePlacement with
            {
                BaselinePattern = ".*(Aggregation|Orchestration)Service$",
                RoleRules = new[]
                {
                    new ArchitectureV6RoleRule("AggregationService", "AggregationService$", 0),
                    new ArchitectureV6RoleRule("OrchestrationService", "OrchestrationService$", 1)
                }
            },
            SemanticModel = Request().SemanticModel with
            {
                Projects = new[]
                {
                    new ArchitectureProject("project:p", "Project", new[]
                    {
                        new ArchitectureNode("root", "project:p", "RootController", "Project.RootController", "Class", "root", Array.Empty<string>()),
                        new ArchitectureNode("aggregation", "project:p", "AggregationService", "Project.AggregationService", "Class", "aggregation", Array.Empty<string>()),
                        new ArchitectureNode("orchestration", "project:p", "OrchestrationService", "Project.OrchestrationService", "Class", "orchestration", Array.Empty<string>())
                    }, "project:p")
                },
                Links = new[]
                {
                    new ArchitectureLink("root-aggregation", "root", "aggregation", "internal"),
                    new ArchitectureLink("root-orchestration", "root", "orchestration", "internal")
                }
            }
        };

        var plan = new ArchitectureDiagramV6Planner().Plan(request);
        var aggregation = plan.NodeMetadata.Single(node => node.SemanticNodeId == "aggregation");
        var orchestration = plan.NodeMetadata.Single(node => node.SemanticNodeId == "orchestration");

        Assert.True(aggregation.IsBaseline);
        Assert.True(orchestration.IsBaseline);
        Assert.NotEqual(aggregation.PhysicalRow, orchestration.PhysicalRow);
        Assert.Equal("AggregationService", aggregation.RoleSelector);
        Assert.Equal("OrchestrationService", orchestration.RoleSelector);
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
        Assert.True(plan.PhysicalScene!.Geometry.Nodes.Single(node => node.PhysicalNodeId == parent.PhysicalNodeId).AbsoluteBounds.Y <
            plan.PhysicalScene.Geometry.Nodes.Single(node => node.PhysicalNodeId == left.PhysicalNodeId).AbsoluteBounds.Y);
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
        var nonExternal = plan.NodeMetadata.Where(node => !node.IsExternal).ToArray();

        Assert.Single(external.Select(node => node.FinalVisualLayerOrdinal).Distinct());
        Assert.True(external.Min(node => node.FinalVisualLayerOrdinal) > nonExternal.Max(node => node.FinalVisualLayerOrdinal));
        Assert.DoesNotContain(plan.Diagnostics.Findings, finding => finding.Code == "LogicalPlacementExternalLayer");
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
    public void Planner_keeps_endpoint_boundaries_distinct_per_physical_link()
    {
        var plan = new ArchitectureDiagramV6Planner().Plan(Request());
        var contracts = plan.LaneAllocation!.BoundaryValidation!.Routes;
        var sourceBoundaries = contracts.Select(route => route.Components
                .Single(component => component.Kind == PlannedRouteComponentKind.SourceTerminal)
                .EntryBoundary)
            .Where(boundary => boundary is not null)
            .Select(boundary => boundary!.ToString())
            .ToArray();

        Assert.Equal(plan.PhysicalLinks.Count, sourceBoundaries.Length);
        Assert.Equal(sourceBoundaries.Length, sourceBoundaries.Distinct(StringComparer.Ordinal).Count());
        Assert.DoesNotContain(plan.Diagnostics.Findings, finding => finding.Code == "CanonicalBoundaryContradiction");
    }

    [Fact]
    public void Planner_uses_symmetric_endpoint_anchor_components_and_exterior_rows()
    {
        var plan = new ArchitectureDiagramV6Planner().Plan(Request());
        var contracts = plan.LaneAllocation!.BoundaryValidation!.Routes;

        Assert.All(contracts, contract =>
        {
            Assert.Single(contract.Components, component => component.Kind == PlannedRouteComponentKind.SourceTerminal);
            Assert.Single(contract.Components, component => component.Kind == PlannedRouteComponentKind.SourceNodeAnchor);
            Assert.Single(contract.Components, component => component.Kind == PlannedRouteComponentKind.SourceDeparture);
            Assert.Single(contract.Components, component => component.Kind == PlannedRouteComponentKind.DestinationApproach);
            Assert.Single(contract.Components, component => component.Kind == PlannedRouteComponentKind.DestinationNodeAnchor);
            Assert.Single(contract.Components, component => component.Kind == PlannedRouteComponentKind.DestinationTerminal);
        });

        Assert.All(plan.Routes, route =>
        {
            var sourcePlacement = plan.NodePlacements.Single(item => item.PhysicalNodeId == route.Source.PhysicalNodeId);
            var destinationPlacement = plan.NodePlacements.Single(item => item.PhysicalNodeId == route.Destination.PhysicalNodeId);
            var grid = plan.ProjectGrids.Single(item => item.Grid.Id.Equals(sourcePlacement.GridId)).Grid;
            var sourceRow = grid.Rows.Single(item => item.Id.Equals(sourcePlacement.AnchorCellId.RowId));
            var destinationRow = grid.Rows.Single(item => item.Id.Equals(destinationPlacement.AnchorCellId.RowId));
            var sourceExterior = route.Steps[1];
            var destinationAnchor = route.Steps[route.Steps.Count - 1];
            var approach = route.Steps[route.Steps.Count - 2];

            Assert.Equal(route.Source.PhysicalNodeId, plan.PhysicalNodes.Single(item => item.PhysicalNodeId == route.Source.PhysicalNodeId).PhysicalNodeId);
            Assert.Equal(sourcePlacement.AnchorCellId, route.Steps[0].CellId);
            var sourceExteriorRow = grid.Rows.Single(item => item.Id.Equals(sourceExterior.CellId.RowId));
            if (grid.Rows.Any(item => item.Role == PlanningGridTrackRole.InterLayerRouting && item.LogicalOrder > sourceRow.LogicalOrder))
                Assert.True(sourceExteriorRow.LogicalOrder > sourceRow.LogicalOrder);
            Assert.Equal(destinationPlacement.AnchorCellId, destinationAnchor.CellId);
            var approachRow = grid.Rows.Single(item => item.Id.Equals(approach.CellId.RowId));
            if (grid.Rows.Any(item => item.Role == PlanningGridTrackRole.InterLayerRouting && item.LogicalOrder < destinationRow.LogicalOrder))
                Assert.True(approachRow.LogicalOrder < destinationRow.LogicalOrder);
        });
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

        var diagram = Assert.Single(plan.RelativeGeometry!.Grids,
            grid => grid.GridId.Equals(plan.DiagramGrid.Grid.Id));
        var projectTransforms = plan.PhysicalScene!.Transforms
            .Where(transform => transform.GridId != plan.DiagramGrid.Grid.Id)
            .OrderBy(transform => transform.GridId.Value, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(2, projectTransforms.Length);
        Assert.NotEqual(projectTransforms[0].Origin, projectTransforms[1].Origin);
        foreach (var project in plan.ProjectGrids)
        {
            var column = Assert.Single(diagram.Columns,
                item => string.Equals(item.OwnerId, project.ProjectId, StringComparison.Ordinal));
            var row = Assert.Single(diagram.Rows,
                item => item.Role == PlanningGridTrackRole.DiagramProjectPlacement);
            var transform = Assert.Single(projectTransforms,
                item => item.GridId.Equals(project.Grid.Id));
            Assert.Equal(new RelativePoint(column.RelativeOffset, row.RelativeOffset), transform.Origin);
        }

        var projectGeometry = plan.PhysicalScene.Geometry.Projects;
        Assert.Equal(2, projectGeometry.Count);
        Assert.Empty(projectGeometry.SelectMany((left, index) => projectGeometry.Skip(index + 1)
            .Where(right => Intersects(left.AbsoluteBounds, right.AbsoluteBounds))));
        Assert.Equal(0, plan.PhysicalScene.Metrics.NodeOverlapCount);
    }

    [Fact]
    public void Planner_materialises_cross_project_handoffs_as_orthogonal_owned_components()
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
        var route = Assert.Single(plan.PhysicalScene!.Geometry.Routes);
        var repeated = new ArchitectureDiagramV6Planner().Plan(request);
        var repeatedRoute = Assert.Single(repeated.PhysicalScene!.Geometry.Routes);

        Assert.Equal(3, route.Components!.Count(component => component.Role == RouteStepRole.ProjectTransition));
        Assert.All(route.Segments, segment => Assert.True(segment.Start.X == segment.End.X || segment.Start.Y == segment.End.Y));
        Assert.Equal(0, plan.PhysicalScene.Metrics.DiagonalSegmentCount);
        Assert.Equal(route.RawPoints, repeatedRoute.RawPoints);
        Assert.Equal(
            route.Components.Select(component => (component.ComponentId, component.Role, component.OwnershipScope, component.EntryPoint, component.ExitPoint)),
            repeatedRoute.Components.Select(component => (component.ComponentId, component.Role, component.OwnershipScope, component.EntryPoint, component.ExitPoint)));
    }

    [Fact]
    public void Planner_project_transforms_are_deterministic_and_preserve_local_dimensions()
    {
        var request = Request() with
        {
            SemanticModel = new ArchitectureDiagramModel(
                new[]
                {
                    new ArchitectureProject("project:a", "A", new[]
                    {
                        new ArchitectureNode("a", "project:a", "A", "A.A", "Class", "a", Array.Empty<string>())
                    }, "project:a"),
                    new ArchitectureProject("project:b", "B", new[]
                    {
                        new ArchitectureNode("b", "project:b", "B", "B.B", "Class", "b", Array.Empty<string>())
                    }, "project:b")
                },
                Array.Empty<ArchitectureExternalNode>(),
                Array.Empty<ArchitectureLink>(),
                null),
            SelectedScope = new ArchitectureSelectionScope("SelectedProjects", new[] { "project:a", "project:b" }, Array.Empty<string>())
        };

        var first = new ArchitectureDiagramV6Planner().Plan(request);
        var second = new ArchitectureDiagramV6Planner().Plan(request);

        Assert.Equal(first.PhysicalScene!.Transforms, second.PhysicalScene!.Transforms);
        Assert.Equal(
            first.PhysicalScene.Geometry.Projects.Select(project => (project.ProjectId, project.RelativeBounds, project.AbsoluteBounds)),
            second.PhysicalScene.Geometry.Projects.Select(project => (project.ProjectId, project.RelativeBounds, project.AbsoluteBounds)));
        foreach (var node in first.PhysicalScene.Geometry.Nodes)
        {
            var relative = first.RelativeGeometry!.Nodes.Single(item => item.PhysicalNodeId == node.PhysicalNodeId);
            Assert.Equal(relative.Bounds.Width, node.AbsoluteBounds.Width);
            Assert.Equal(relative.Bounds.Height, node.AbsoluteBounds.Height);
        }
        Assert.Equal(0, first.PhysicalScene.Metrics.NodeOverlapCount);
    }

    private static bool Intersects(AbsoluteRectangle left, AbsoluteRectangle right) =>
        left.X < right.X + right.Width && left.X + left.Width > right.X &&
        left.Y < right.Y + right.Height && left.Y + left.Height > right.Y;

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
        var sourceRow = sourceGrid.Grid.Rows.Single(row => row.Id.Equals(sourcePlacement.AnchorCellId.RowId));
        var departureRow = sourceGrid.Grid.Rows.Single(row => row.Id.Equals(departure.CellId.RowId));
        Assert.True(departureRow.LogicalOrder > sourceRow.LogicalOrder);
        Assert.Contains(departure.CellId, sourceGrid.Grid.Cells.Keys);
        Assert.Null(sourceGrid.Grid.Cells[departure.CellId].FootprintOwnerId);
        Assert.Empty(plan.LaneAllocation!.Conflicts);
        Assert.Equal(0, plan.Diagnostics.Metrics.UnsupportedRouteCount);
    }

    [Fact]
    public void Occupancy_authority_resolves_anchor_and_footprint_cells_once()
    {
        var plan = new ArchitectureDiagramV6Planner().Plan(Request());
        var authority = new ArchitectureV6OccupancyAuthority(plan.PhysicalNodes, plan.NodePlacements,
            plan.ProjectGrids.Select(item => item.Grid).Append(plan.DiagramGrid.Grid));

        Assert.Empty(authority.Diagnostics);
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
    public void Planner_and_validator_reject_unrelated_anchor_traversal()
    {
        var plan = new ArchitectureDiagramV6Planner().Plan(Request());
        var authority = new ArchitectureV6OccupancyAuthority(plan.PhysicalNodes, plan.NodePlacements,
            plan.ProjectGrids.Select(item => item.Grid).Append(plan.DiagramGrid.Grid));

        Assert.DoesNotContain(plan.Diagnostics.Findings, finding => finding.Code == "RouteEntersUnrelatedFootprint");
        Assert.All(plan.Routes.SelectMany(route => route.Steps), step =>
        {
            var resolution = authority.Resolve(step.CellId);
            if (!resolution.IsOccupied) return;
            var link = plan.PhysicalLinks.Single(item => item.PhysicalLinkId == plan.Routes.Single(route => route.Steps.Contains(step)).PhysicalLinkId);
            Assert.True(authority.IsExactEndpointCell(step.CellId, step, link));
        });
    }

    [Fact]
    public void Endpoint_exception_is_limited_to_the_exact_endpoint_component()
    {
        var plan = new ArchitectureDiagramV6Planner().Plan(Request());
        var authority = new ArchitectureV6OccupancyAuthority(plan.PhysicalNodes, plan.NodePlacements,
            plan.ProjectGrids.Select(item => item.Grid).Append(plan.DiagramGrid.Grid));
        var route = plan.Routes.First(item => item.Steps.Count > 2);
        var link = plan.PhysicalLinks.Single(item => item.PhysicalLinkId == route.PhysicalLinkId);
        var sourceAnchor = plan.NodePlacements.Single(item => item.PhysicalNodeId == link.SourcePhysicalNodeId).AnchorCellId;
        var destinationAnchor = plan.NodePlacements.Single(item => item.PhysicalNodeId == link.DestinationPhysicalNodeId).AnchorCellId;
        var source = route.Steps[0] with { CellId = sourceAnchor, Role = RouteStepRole.SourceExit };
        var destination = route.Steps[^1] with { CellId = destinationAnchor, Role = RouteStepRole.DestinationEntry };
        var ordinary = source with { Role = RouteStepRole.Turn };

        Assert.True(authority.IsExactEndpointCell(sourceAnchor, source, link));
        Assert.True(authority.IsExactEndpointCell(destinationAnchor, destination, link));
        Assert.False(authority.IsExactEndpointCell(sourceAnchor, ordinary, link));
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
    public void Renderer_emits_physical_scene_vertices_and_edges()
    {
        var plan = new ArchitectureDiagramV6Planner().Plan(Request());
        var page = new DrawioArchitectureV6Renderer().Render(plan, new ArchitectureRenderRequest(ArchitectureValidationMode.Normal, "drawio", true));
        var cells = page.GraphModel.Descendants("mxCell").ToArray();

        Assert.Equal("architecture", page.StablePageKey);
        Assert.Equal(plan.PhysicalScene!.Geometry.Nodes.Count, cells.Count(cell => (string?)cell.Attribute("vertex") == "1") - plan.PhysicalScene.Geometry.Projects.Count);
        Assert.Equal(plan.PhysicalLinks.Count, cells.Count(cell => (string?)cell.Attribute("edge") == "1"));
        Assert.Contains(page.Diagnostics, diagnostic => diagnostic.Code == "V6PhysicalNodesEmitted");
        Assert.Contains(page.Diagnostics, diagnostic => diagnostic.Code == "V6RelationshipsEmitted");
    }

    [Fact]
    public void Renderer_projects_existing_geometry_without_reconstructing_layout()
    {
        var plan = new ArchitectureDiagramV6Planner().Plan(Request());
        var page = new DrawioArchitectureV6Renderer().Render(plan, new ArchitectureRenderRequest(ArchitectureValidationMode.Diagnostic, "drawio", true));

        Assert.Contains(page.GraphModel.Descendants(), element => element.Name.LocalName == "mxGeometry" && element.Parent?.Name.LocalName == "mxCell");
        Assert.Contains(page.Diagnostics, diagnostic => diagnostic.Code == "V6LogicalPlacementComplete");
    }

    [Fact]
    public void Planner_resolves_node_and_relationship_styles_before_rendering()
    {
        var request = Request() with
        {
            StylePolicies = new[]
            {
                new ArchitectureV6StyleRule("*Service", "#112233", "#445566", "#778899", "ellipse", false, "align=left;"),
                new ArchitectureV6StyleRule("Root*", "#abcdef", "#fedcba", "#010203", "rhombus", true, null)
            },
            ConnectorStyle = new ArchitectureV6ConnectorStyle("#123456", 7, true, true, "9 4", "open", "diamond", 2, 81, "#654321", true, false, true, "linkTextColor=#abcdef;")
        };

        var plan = new ArchitectureDiagramV6Planner().Plan(request);
        var root = plan.PhysicalNodes.Single(node => node.SemanticNodeId == "root");
        var shared = plan.PhysicalNodes.Single(node => node.SemanticNodeId == "shared");
        var page = new DrawioArchitectureV6Renderer().Render(plan, new ArchitectureRenderRequest(ArchitectureValidationMode.Normal, "drawio", true));
        var rootCell = page.GraphModel.Descendants("mxCell").Single(cell => (string?)cell.Attribute("physicalNodeId") == root.PhysicalNodeId);
        var sharedCell = page.GraphModel.Descendants("mxCell").Single(cell => (string?)cell.Attribute("physicalNodeId") == shared.PhysicalNodeId);
        var edge = page.GraphModel.Descendants("mxCell").First(cell => (string?)cell.Attribute("edge") == "1");

        Assert.Equal("#112233", root.ResolvedStyle!.FillColor);
        Assert.Equal("#112233", shared.ResolvedStyle!.FillColor);
        Assert.All(plan.PhysicalLinks, link => Assert.Same(request.ConnectorStyle, link.ResolvedStyle));
        Assert.Contains("shape=ellipse", (string)rootCell.Attribute("style")!);
        Assert.Contains("align=left;", (string)rootCell.Attribute("style")!);
        Assert.Contains("fillColor=#112233", (string)rootCell.Attribute("style")!);
        Assert.Contains("strokeColor=#112233", (string)edge.Attribute("style")!);
        Assert.Equal("target-node-background", (string)edge.Attribute("resolvedStyleSource")!);
        Assert.Equal((string)sharedCell.Attribute("id")!, (string)edge.Attribute("target")!);
        Assert.Contains("exitX=", (string)edge.Attribute("style")!);
        Assert.Contains("exitY=1", (string)edge.Attribute("style")!);
        Assert.Contains("entryX=", (string)edge.Attribute("style")!);
        Assert.Contains("entryY=0", (string)edge.Attribute("style")!);
        Assert.NotNull(edge.Attribute("sourceTerminalId"));
        Assert.NotNull(edge.Attribute("targetTerminalId"));
        Assert.Contains("strokeWidth=7", (string)edge.Attribute("style")!);
        Assert.Contains("dashed=1", (string)edge.Attribute("style")!);
        Assert.Contains("startArrow=open", (string)edge.Attribute("style")!);
        Assert.Contains("endArrow=diamond", (string)edge.Attribute("style")!);
        Assert.Contains("startFill=0", (string)edge.Attribute("style")!);
        Assert.Contains("endFill=1", (string)edge.Attribute("style")!);
        Assert.Contains("startSize=2", (string)edge.Attribute("style")!);
        Assert.Contains("fontColor=#654321", (string)edge.Attribute("style")!);
        Assert.Contains("linkTextColor=#abcdef", (string)edge.Attribute("style")!);
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

        Assert.True(metadata["processing"].FinalVisualLayerOrdinal < metadata["coordination"].FinalVisualLayerOrdinal);
        Assert.True(metadata["coordination"].FinalVisualLayerOrdinal < metadata["orchestration"].FinalVisualLayerOrdinal);
        Assert.All(metadata.Values.Where(node => node.PositionalOwnerId is not null), node =>
            Assert.True(node.FinalVisualLayerOrdinal > metadata.Values.Single(parent => parent.PhysicalNodeId == node.PositionalOwnerId).FinalVisualLayerOrdinal));

        var grid = Assert.Single(plan.ProjectGrids).Grid;
        var nodeRows = plan.NodePlacements.Select(placement => placement.AnchorCellId.RowId).Distinct()
            .OrderBy(row => grid.Rows.Single(item => item.Id.Equals(row)).LogicalOrder).ToArray();
        var layerRows = plan.NodeMetadata.GroupBy(node => node.FinalVisualLayerOrdinal)
            .OrderBy(group => group.Key).Select(group => group.Count()).ToArray();
        Assert.Equal(nodeRows.Length, layerRows.Length);
        Assert.DoesNotContain(plan.Diagnostics.Findings, finding => finding.Code == "LogicalPlacementParentNotAboveChild");
    }

    [Fact]
    public void Final_pipeline_keeps_configured_role_bands_distinct_when_baseline_pattern_overlaps_them()
    {
        var request = Request() with
        {
            NodePlacement = Request().NodePlacement with
            {
                BaselinePattern = ".*(Aggregation|Coordination|Orchestration)Service$",
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

        Assert.True(metadata["coordination"].FinalVisualLayerOrdinal < metadata["orchestration"].FinalVisualLayerOrdinal,
            $"coordination={metadata["coordination"].FinalVisualLayerOrdinal}/{metadata["coordination"].RoleSelector}/{metadata["coordination"].VerticalSpacingPolicy}/{metadata["coordination"].PhysicalRow}, orchestration={metadata["orchestration"].FinalVisualLayerOrdinal}/{metadata["orchestration"].RoleSelector}/{metadata["orchestration"].VerticalSpacingPolicy}/{metadata["orchestration"].PhysicalRow}");
        Assert.True(metadata["orchestration"].FinalVisualLayerOrdinal < metadata["processing"].FinalVisualLayerOrdinal,
            $"orchestration={metadata["orchestration"].FinalVisualLayerOrdinal}, processing={metadata["processing"].FinalVisualLayerOrdinal}");
        Assert.True(metadata["processing"].FinalVisualLayerOrdinal < metadata["service"].FinalVisualLayerOrdinal,
            $"processing={metadata["processing"].FinalVisualLayerOrdinal}, service={metadata["service"].FinalVisualLayerOrdinal}");
        Assert.Equal(4, metadata.Values.Where(node => node.RoleSelector != "Unmatched")
            .Select(node => node.PhysicalRow).Distinct().Count());
        Assert.DoesNotContain(plan.Diagnostics.Findings, finding => finding.Code == "LogicalPlacementRoleOrderViolation");
    }

    [Fact]
    public void Final_pipeline_reserves_header_and_route_clearance_before_rendering()
    {
        var plan = new ArchitectureDiagramV6Planner().Plan(CleanRequest());
        Assert.NotNull(plan.PhysicalScene);
        var scene = plan.PhysicalScene!;
        var project = Assert.Single(scene.Geometry.Projects);

        Assert.All(scene.Geometry.Nodes, node =>
            Assert.True(node.AbsoluteBounds.Y >= project.AbsoluteLabelBounds!.Value.Y + project.AbsoluteLabelBounds.Value.Height));
        Assert.All(plan.Sizing.Rows.Where(row => row.Role == PlanningGridTrackRole.InterLayerRouting), row =>
            Assert.True(row.FinalExtent >= plan.Request.GridSizing.RoutingRowMinimum + plan.Request.GridSizing.NodeToRouteClearance * 2));
    }

    [Fact]
    public void Final_pipeline_keeps_endpoint_component_provenance_after_envelope_validation()
    {
        var plan = new ArchitectureDiagramV6Planner().Plan(CleanRequest());

        Assert.NotNull(plan.PhysicalScene);
        Assert.All(plan.Diagnostics.Metrics.RouteEvidence!, evidence =>
        {
            Assert.Contains(evidence.ComponentTypes, type => type == nameof(RouteStepRole.SourceExit));
            Assert.Contains(evidence.ComponentTypes, type => type == nameof(RouteStepRole.DestinationEntry));
        });
        Assert.DoesNotContain(plan.Diagnostics.Findings, finding => finding.Code == "ComponentCorridorEscape");
    }

    [Fact]
    public void Final_pipeline_fanout_terminals_are_inset_monotonic_and_orthogonal()
    {
        var request = CleanRequest() with
        {
            SemanticModel = CleanRequest().SemanticModel with
            {
                Projects = new[]
                {
                    new ArchitectureProject("project:p", "Project", new[]
                    {
                        new ArchitectureNode("root", "project:p", "RootController", "Project.RootController", "Class", "root", Array.Empty<string>()),
                        new ArchitectureNode("child-a", "project:p", "AService", "Project.AService", "Class", "child-a", Array.Empty<string>()),
                        new ArchitectureNode("child-b", "project:p", "BService", "Project.BService", "Class", "child-b", Array.Empty<string>()),
                        new ArchitectureNode("child-c", "project:p", "CService", "Project.CService", "Class", "child-c", Array.Empty<string>()),
                        new ArchitectureNode("child-d", "project:p", "DService", "Project.DService", "Class", "child-d", Array.Empty<string>())
                    }, "project:p")
                },
                Links = new[]
                {
                    new ArchitectureLink("root-a", "root", "child-a", "internal"),
                    new ArchitectureLink("root-b", "root", "child-b", "internal"),
                    new ArchitectureLink("root-c", "root", "child-c", "internal"),
                    new ArchitectureLink("root-d", "root", "child-d", "internal")
                }
            }
        };

        var plan = new ArchitectureDiagramV6Planner().Plan(request);
        Assert.NotNull(plan.PhysicalScene);
        var scene = plan.PhysicalScene!;
        var root = scene.Geometry.Nodes.Single(node => node.SemanticNodeId == "root");
        var sourceTerminals = scene.Terminals.Where(terminal => terminal.PhysicalNodeId == root.PhysicalNodeId).OrderBy(terminal => terminal.Point.X).ToArray();

        Assert.Equal(4, sourceTerminals.Length);
        Assert.All(sourceTerminals, terminal =>
            Assert.InRange(terminal.Point.X, root.AbsoluteBounds.X + 1, root.AbsoluteBounds.X + root.AbsoluteBounds.Width - 1));
        Assert.DoesNotContain(plan.Diagnostics.Findings, finding => finding.Code == "TerminalJoinDiagonal");
        Assert.DoesNotContain(plan.Diagnostics.Findings, finding => finding.Code == "TerminalCornerOrInsetViolation");
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
        var nonExternal = plan.NodeMetadata.Where(node => !node.IsExternal).ToArray();
        Assert.Single(external.Select(node => node.FinalVisualLayerOrdinal).Distinct());
        Assert.True(external.Min(node => node.FinalVisualLayerOrdinal) > nonExternal.Max(node => node.FinalVisualLayerOrdinal));
        Assert.DoesNotContain(plan.Diagnostics.Findings, finding => finding.Code == "LogicalPlacementExternalLayer");
    }

    [Fact]
    public void Final_plan_uses_first_matching_role_rule_and_keeps_a_role_on_one_final_layer()
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
        Assert.True(metadata["coordination"].FinalVisualLayerOrdinal < metadata["service"].FinalVisualLayerOrdinal);
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
        Assert.Single(services.Select(node => node.FinalVisualLayerOrdinal).Distinct());
        Assert.DoesNotContain(plan.Diagnostics.Findings, finding => finding.Code == "LogicalPlacementLayerOrderContradiction");
    }

    [Fact]
    public void Final_plan_uses_resolved_labels_for_bounds_and_not_hidden_fqns_or_endpoint_demand()
    {
        var plan = new ArchitectureDiagramV6Planner().Plan(Request());
        Assert.NotNull(plan.PhysicalScene);
        var physicalById = plan.PhysicalNodes.ToDictionary(node => node.PhysicalNodeId, StringComparer.Ordinal);
        var geometryById = plan.PhysicalScene!.Geometry.Nodes.ToDictionary(node => node.PhysicalNodeId, StringComparer.Ordinal);

        Assert.All(plan.PhysicalNodes, node =>
        {
            var geometry = geometryById[node.PhysicalNodeId];
            Assert.True(geometry.AbsoluteBounds.Width >= plan.Request.NodePlacement.MinimumNodeWidth);
            Assert.Equal(node.DisplayLabel, physicalById[node.PhysicalNodeId].DisplayLabel);
        });
        var root = plan.PhysicalNodes.Single(node => node.SemanticNodeId == "root");
        Assert.True(geometryById[root.PhysicalNodeId].AbsoluteBounds.Width <= 400);
        Assert.DoesNotContain(plan.Diagnostics.Findings, finding => finding.Code == "RelativeNodeFqnSizing");
    }

    [Fact]
    public void Final_plan_visible_size_does_not_grow_with_terminal_demand()
    {
        var nodes = new List<ArchitectureNode>
        {
            new("low", "project:p", "SameService", "Project.Low", "Class", "low", Array.Empty<string>()),
            new("high", "project:p", "SameService", "Project.High", "Class", "high", Array.Empty<string>())
        };
        var links = new List<ArchitectureLink>();
        for (var index = 0; index < 8; index++)
        {
            var id = "child-" + index;
            nodes.Add(new ArchitectureNode(id, "project:p", "ChildService" + index, "Project.Child" + index, "Class", id, Array.Empty<string>()));
            links.Add(new ArchitectureLink("high-" + id, "high", id, "internal"));
        }

        var request = Request() with
        {
            SemanticModel = Request().SemanticModel with
            {
                Projects = new[] { new ArchitectureProject("project:p", "Project", nodes, "project:p") },
                Links = links
            }
        };

        var plan = new ArchitectureDiagramV6Planner().Plan(request);
        var geometries = plan.PhysicalScene!.Geometry.Nodes
            .Where(node => node.SemanticNodeId is "low" or "high")
            .ToArray();

        Assert.Equal(2, geometries.Length);
        Assert.Equal(geometries[0].AbsoluteBounds.Width, geometries[1].AbsoluteBounds.Width);
        Assert.Equal(geometries[0].AbsoluteBounds.Height, geometries[1].AbsoluteBounds.Height);
        Assert.NotEqual(geometries[0].AbsoluteRoutingBounds!.Value.Width, geometries[1].AbsoluteRoutingBounds!.Value.Width);
    }

    [Fact]
    public void Final_plan_routes_use_final_scene_bounds_and_renderer_is_mechanical()
    {
        var plan = new ArchitectureDiagramV6Planner().Plan(CleanRequest());
        Assert.NotNull(plan.PhysicalScene);
        var scene = plan.PhysicalScene!;
        var page = new DrawioArchitectureV6Renderer().Render(plan,
            new ArchitectureRenderRequest(ArchitectureValidationMode.Diagnostic, "drawio", true));
        var cells = page.GraphModel.Descendants("mxCell").ToArray();
        var projectBounds = scene.Geometry.Projects.ToDictionary(project => project.ProjectId, StringComparer.Ordinal);

        foreach (var node in scene.Geometry.Nodes)
        {
            var cell = cells.Single(item => (string?)item.Attribute("physicalNodeId") == node.PhysicalNodeId);
            var geometry = cell.Element("mxGeometry")!;
            var expected = node.ProjectId is not null && projectBounds.TryGetValue(node.ProjectId, out var project)
                ? new RelativeRectangle(node.AbsoluteBounds.X - project.AbsoluteBounds.X, node.AbsoluteBounds.Y - project.AbsoluteBounds.Y,
                    node.AbsoluteBounds.Width, node.AbsoluteBounds.Height)
                : new RelativeRectangle(node.AbsoluteBounds.X, node.AbsoluteBounds.Y, node.AbsoluteBounds.Width, node.AbsoluteBounds.Height);
            Assert.Equal(expected.X, (int)geometry.Attribute("x")!);
            Assert.Equal(expected.Y, (int)geometry.Attribute("y")!);
            Assert.Equal(expected.Width, (int)geometry.Attribute("width")!);
            Assert.Equal(expected.Height, (int)geometry.Attribute("height")!);
        }

        foreach (var route in scene.Geometry.Routes)
        {
            var edge = cells.Single(item => (string?)item.Attribute("physicalLinkId") == route.PhysicalLinkId);
            Assert.Equal(route.Segments[0].PhysicalLinkId, (string)edge.Attribute("physicalLinkId")!);
            Assert.Equal(route.Segments[0].PhysicalLinkId, plan.PhysicalLinks.Single(link => link.PhysicalLinkId == route.PhysicalLinkId).PhysicalLinkId);
            var expectedPoints = (route.ReducedPoints ?? Array.Empty<PlannedPhysicalRoutePoint>())
                .Select(point => point.Point).ToArray();
            var emittedPoints = edge.Descendants("mxPoint")
                .Select(point => new AbsolutePoint((int)point.Attribute("x")!, (int)point.Attribute("y")!)).ToArray();
            Assert.Equal(expectedPoints.Where(point => !scene.Terminals.Any(terminal => terminal.Point == point)).ToArray(), emittedPoints);
        }
    }

    [Fact]
    public void Final_plan_has_routing_capacity_and_terminal_ordering_after_all_stages()
    {
        var plan = new ArchitectureDiagramV6Planner().Plan(CleanRequest());
        Assert.NotNull(plan.PhysicalScene);
        Assert.All(plan.Sizing.Rows.Where(row => row.Role == PlanningGridTrackRole.InterLayerRouting), row => Assert.True(row.FinalExtent >= 20));
        Assert.All(plan.PhysicalScene!.Terminals.GroupBy(terminal => terminal.PhysicalLinkId), terminals =>
        {
            Assert.Equal(2, terminals.Count());
            Assert.Contains(terminals, terminal => terminal.Side == GridSide.Bottom);
            Assert.Contains(terminals, terminal => terminal.Side == GridSide.Top);
            Assert.All(terminals, terminal => Assert.True(terminal.Ordinal >= 0));
        });
        Assert.DoesNotContain(plan.Diagnostics.Findings, finding =>
            finding.Code is "SourceTerminalOrderInversion" or "DestinationTerminalOrderInversion");
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
    public void Final_plan_exposes_route_level_physical_evidence()
    {
        var plan = new ArchitectureDiagramV6Planner().Plan(CleanRequest());

        Assert.NotNull(plan.Diagnostics.Metrics.RouteEvidence);
        Assert.Equal(plan.PhysicalLinks.Count, plan.Diagnostics.Metrics.RouteEvidence!.Count);
        Assert.All(plan.Diagnostics.Metrics.RouteEvidence, evidence =>
        {
            Assert.NotEmpty(evidence.RouteId);
            Assert.NotEmpty(evidence.ComponentIds);
            Assert.NotNull(evidence.SourceTerminal);
            Assert.NotNull(evidence.DestinationTerminal);
        });
        Assert.NotNull(plan.Diagnostics.Metrics.TrackCapacity);
    }

    [Fact]
    public void Final_plan_clean_routing_regression_gate_is_zero_findings()
    {
        var plan = new ArchitectureDiagramV6Planner().Plan(CleanRequest());
        Assert.NotNull(plan.PhysicalScene);
        var metrics = plan.PhysicalScene!.Metrics;

        Assert.Equal(plan.PhysicalLinks.Count, metrics.PhysicalRouteCount);
        Assert.Equal(plan.PhysicalLinks.Count, metrics.PhysicalRouteCount - metrics.InvalidRouteCount);
        Assert.Equal(0, metrics.DiagonalSegmentCount);
        Assert.Equal(0, metrics.CorridorEscapeCount);
        Assert.Equal(0, metrics.ComponentContinuityFailureCount);
        Assert.Equal(0, metrics.RouteNodeIntersectionCount);
        Assert.Equal(0, metrics.SharedCollinearSegmentCount);
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
