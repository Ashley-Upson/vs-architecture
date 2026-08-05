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
        Assert.False(plan.StageStatus.RoutingDeferred);
    }

    [Fact]
    public void Placement_preserves_depth_and_first_match_role_bands_without_collisions()
    {
        var request = GraphRequest(NodeProjectionMode.Canonical) with
        {
            NodePlacement = GraphRequest(NodeProjectionMode.Canonical).NodePlacement with
            {
                RoleRules = new[]
                {
                    new ArchitectureV6RoleRule("SpecificService", "ChildService$", 0),
                    new ArchitectureV6RoleRule("Service", "Service$", 1),
                    new ArchitectureV6RoleRule("Broker", "Broker$", 2)
                }
            }
        };
        var plan = new ArchitectureDiagramV6Planner().Plan(request);
        var root = Assert.Single(plan.NodeMetadata, item => item.SemanticNodeId == "root");
        var child = Assert.Single(plan.NodeMetadata, item => item.SemanticNodeId == "child");

        Assert.Equal("Service", root.RoleSelector);
        Assert.Equal("SpecificService", child.RoleSelector);
        Assert.Equal(0, root.PhysicalRow);
        Assert.True(child.PhysicalRow > root.PhysicalRow);
        Assert.DoesNotContain(plan.Diagnostics.Findings, finding => finding.Code == "GeometryCollision");
    }

    [Fact]
    public void Routing_emits_one_orthogonal_edge_per_physical_link_with_node_terminals()
    {
        var plan = new ArchitectureDiagramV6Planner().Plan(GraphRequest(NodeProjectionMode.Canonical));
        Assert.NotNull(plan.Geometry);
        var routes = plan.Geometry!.Routes;
        var page = new DrawioArchitectureV6Renderer().Render(plan, new ArchitectureRenderRequest(ArchitectureValidationMode.Normal, "drawio", true));
        var edges = page.GraphModel.Descendants("mxCell").Where(cell => (string?)cell.Attribute("edge") == "1").ToArray();

        Assert.Equal(plan.PhysicalLinks.Count, routes.Count);
        Assert.Equal(routes.Count, edges.Length);
        Assert.All(routes, route => Assert.All(route.Segments, segment => Assert.True(segment.Start.X == segment.End.X || segment.Start.Y == segment.End.Y)));
        Assert.All(edges, edge => Assert.NotNull(edge.Attribute("source")));
        Assert.All(edges, edge => Assert.NotNull(edge.Attribute("target")));
        Assert.All(edges, edge =>
        {
            Assert.Contains("edgeStyle=orthogonalEdgeStyle", edge.Attribute("style")!.Value);
            Assert.Contains("orthogonal=1", edge.Attribute("style")!.Value);
            Assert.Contains("exitX=0.5", edge.Attribute("style")!.Value);
            Assert.Contains("entryY=0", edge.Attribute("style")!.Value);
            Assert.Equal(string.Empty, edge.Attribute("value")!.Value);
            var geometry = edge.Element("mxGeometry");
            Assert.NotNull(geometry);
            Assert.Equal("1", geometry!.Attribute("relative")!.Value);
            var points = geometry.Element("Array");
            Assert.NotNull(points);
            Assert.Equal("points", points!.Attribute("as")!.Value);
            Assert.All(points.Elements("mxPoint"), point =>
            {
                Assert.NotNull(point.Attribute("x"));
                Assert.NotNull(point.Attribute("y"));
                Assert.Null(point.Attribute("as"));
            });
            Assert.Empty(geometry.Elements("mxPoint"));
        });
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
    public void V6_renderer_emits_vertices_from_completed_geometry_without_edges()
    {
        var plan = new ArchitectureDiagramV6Planner().Plan(Request(NodeProjectionMode.Canonical));
        var page = new DrawioArchitectureV6Renderer().Render(plan, new ArchitectureRenderRequest(ArchitectureValidationMode.Normal, "drawio", true));

        Assert.Equal("architecture", page.StablePageKey);
        Assert.Equal("mxGraphModel", page.GraphModel.Name.LocalName);
        Assert.Equal(0, page.GraphModel.Descendants("mxCell").Count(cell => (string?)cell.Attribute("edge") == "1"));
        Assert.Equal(plan.PhysicalNodes.Count, page.GraphModel.Descendants("mxCell").Count(cell => (string?)cell.Attribute("vertex") == "1"));
        Assert.Contains(page.Diagnostics, diagnostic => diagnostic.Code == "V6VertexCount");
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

    [Fact]
    public void Planner_compiles_positive_absolute_geometry_and_bounds_metrics()
    {
        var plan = new ArchitectureDiagramV6Planner().Plan(GraphRequest(NodeProjectionMode.Canonical));

        Assert.NotNull(plan.Geometry);
        Assert.Equal(plan.PhysicalNodes.Count, plan.Geometry!.Nodes.Count);
        Assert.Equal(plan.Geometry.AbsoluteDiagramBounds.Width, plan.Diagnostics.Metrics.GeometryWidth);
        Assert.Equal(plan.Geometry.AbsoluteDiagramBounds.Height, plan.Diagnostics.Metrics.GeometryHeight);
        Assert.True(plan.Geometry.AbsoluteDiagramBounds.Width > 0);
        Assert.True(plan.Geometry.AbsoluteDiagramBounds.Height > 0);
        Assert.All(plan.Geometry.Nodes, node =>
        {
            Assert.True(node.RelativeBounds.Width > 0);
            Assert.True(node.RelativeBounds.Height > 0);
            Assert.True(node.AbsoluteBounds.Width > 0);
            Assert.True(node.AbsoluteBounds.Height > 0);
        });
        Assert.DoesNotContain(plan.Diagnostics.Findings, finding => finding.Code.StartsWith("Geometry", StringComparison.Ordinal) || finding.Code.StartsWith("InvalidGeometry", StringComparison.Ordinal));
        Assert.True(plan.StageStatus.SizingCompleted);
        Assert.True(plan.StageStatus.AbsoluteGeometryCompleted);
        Assert.False(plan.StageStatus.SizingDeferred);
        Assert.False(plan.StageStatus.AbsoluteGeometryDeferred);
    }

    [Fact]
    public void Geometry_preserves_duplicate_physical_identity_and_is_repeatable()
    {
        var request = GraphRequest(NodeProjectionMode.DuplicateBranches) with
        {
            NodeProjection = new NodeProjectionPolicy(NodeProjectionMode.DuplicateBranches, new[] { "Shared" })
        };
        var first = new ArchitectureDiagramV6Planner().Plan(request);
        var second = new ArchitectureDiagramV6Planner().Plan(request);
        var firstSignature = string.Join("|", first.Geometry!.Nodes.Select(node => $"{node.PhysicalNodeId}:{node.SemanticNodeId}:{node.AbsoluteBounds}"));
        var secondSignature = string.Join("|", second.Geometry!.Nodes.Select(node => $"{node.PhysicalNodeId}:{node.SemanticNodeId}:{node.AbsoluteBounds}"));

        Assert.Equal(firstSignature, secondSignature);
        Assert.Equal(2, first.Geometry.Nodes.Count(node => node.SemanticNodeId == "shared"));
        Assert.All(first.Geometry.Nodes.Where(node => node.SemanticNodeId == "shared"), node => Assert.Contains(node.PhysicalNodeId, first.PhysicalNodes.Select(item => item.PhysicalNodeId)));
    }

    [Fact]
    public void Geometry_keeps_nodes_inside_project_bounds_without_collisions()
    {
        var plan = new ArchitectureDiagramV6Planner().Plan(GraphRequest(NodeProjectionMode.Canonical));

        foreach (var project in plan.Geometry!.Projects)
        {
            var owned = plan.Geometry.Nodes.Where(node => node.ProjectId == project.ProjectId).ToArray();
            Assert.All(owned, node =>
                Assert.True(node.RelativeBounds.X >= project.RelativeBounds.X && node.RelativeBounds.Y >= project.RelativeBounds.Y &&
                    node.RelativeBounds.X + node.RelativeBounds.Width <= project.RelativeBounds.X + project.RelativeBounds.Width &&
                    node.RelativeBounds.Y + node.RelativeBounds.Height <= project.RelativeBounds.Y + project.RelativeBounds.Height));
            for (var left = 0; left < owned.Length; left++)
                for (var right = left + 1; right < owned.Length; right++)
                    Assert.False(Intersects(owned[left].RelativeBounds, owned[right].RelativeBounds));
        }
    }

    [Fact]
    public void V6_renderer_preserves_hierarchy_identity_and_xml_escapes_labels()
    {
        var request = GraphRequest(NodeProjectionMode.Canonical) with
        {
            SemanticModel = new ArchitectureDiagramModel(
                new[] { new ArchitectureProject("project:p", "Project", new[]
                {
                    new ArchitectureNode("special", "project:p", "A&B <Service>", "Project.A&B <Service>", "Class", "special", Array.Empty<string>()),
                    new ArchitectureNode("child", "project:p", "ChildService", "Project.ChildService", "Class", "child", Array.Empty<string>())
                }, "project:p") },
                Array.Empty<ArchitectureExternalNode>(),
                new[] { new ArchitectureLink("special-child", "special", "child", "internal") }, null)
        };
        var plan = new ArchitectureDiagramV6Planner().Plan(request);
        var page = new DrawioArchitectureV6Renderer().Render(plan, new ArchitectureRenderRequest(ArchitectureValidationMode.Normal, "drawio", true));
        var vertices = page.GraphModel.Descendants("mxCell").Where(cell => (string?)cell.Attribute("vertex") == "1").ToArray();
        var special = Assert.Single(vertices, cell => (string?)cell.Attribute("semanticNodeId") == "special");

        Assert.NotEqual("1", special.Attribute("parent")!.Value);
        Assert.Contains(vertices, cell => cell.Attribute("id")!.Value == special.Attribute("parent")!.Value && (string?)cell.Attribute("projectId") == "project:p");
        Assert.Equal("physical:special", special.Attribute("physicalNodeId")!.Value);
        Assert.Contains("A&amp;B &lt;Service&gt;", page.GraphModel.ToString());
        Assert.Contains(page.GraphModel.Descendants("mxCell"), cell => (string?)cell.Attribute("edge") == "1");
    }

    [Fact]
    public void V6_renderer_uses_stable_ids_for_duplicate_physical_nodes()
    {
        var request = GraphRequest(NodeProjectionMode.DuplicateBranches) with
        {
            NodeProjection = new NodeProjectionPolicy(NodeProjectionMode.DuplicateBranches, new[] { "Shared" })
        };
        var plan = new ArchitectureDiagramV6Planner().Plan(request);
        var page = new DrawioArchitectureV6Renderer().Render(plan, new ArchitectureRenderRequest(ArchitectureValidationMode.Normal, "drawio", true));
        var physicalIds = plan.PhysicalNodes.Select(node => node.PhysicalNodeId).ToArray();
        var cells = page.GraphModel.Descendants("mxCell").Where(cell => (string?)cell.Attribute("vertex") == "1" && cell.Attribute("physicalNodeId") is not null).ToArray();

        Assert.Equal(physicalIds.Length, cells.Length);
        Assert.Equal(cells.Length, cells.Select(cell => cell.Attribute("id")!.Value).Distinct(StringComparer.Ordinal).Count());
        Assert.Contains(cells, cell => (string?)cell.Attribute("projectionMode") == "DuplicateBranch" && cell.Attribute("duplicationReason") is not null);
    }

    [Fact]
    public void V6_geometry_uses_one_normal_gap_between_adjacent_logical_groups()
    {
        var plan = new ArchitectureDiagramV6Planner().Plan(GraphRequest(NodeProjectionMode.Canonical));
        var page = new DrawioArchitectureV6Renderer().Render(plan, new ArchitectureRenderRequest(ArchitectureValidationMode.Normal, "drawio", true));
        var gapMessages = page.Diagnostics.Where(diagnostic => diagnostic.Code == "V6NodeGap" && diagnostic.Message.Contains("policy=normal-horizontal", StringComparison.Ordinal)).ToArray();

        Assert.NotEmpty(gapMessages);
        Assert.All(gapMessages, diagnostic => Assert.Contains("rendered=20", diagnostic.Message));
    }

    [Fact]
    public void V6_renderer_emits_distinctive_configured_style_and_reports_usage()
    {
        var request = GraphRequest(NodeProjectionMode.Canonical) with
        {
            StylePolicies = new[]
            {
                new ArchitectureV6StyleRule("RootOrchestrationService", "#010203", "#040506", "#070809", "hexagon", false, "align=left;"),
                new ArchitectureV6StyleRule("NeverMatches", "#101112", "#131415", "#161718", "ellipse", false, null)
            }
        };
        var plan = new ArchitectureDiagramV6Planner().Plan(request);
        var page = new DrawioArchitectureV6Renderer().Render(plan, new ArchitectureRenderRequest(ArchitectureValidationMode.Normal, "drawio", true));
        var root = Assert.Single(page.GraphModel.Descendants("mxCell"), cell => (string?)cell.Attribute("semanticNodeId") == "root");

        Assert.Contains("fillColor=#010203", root.Attribute("style")!.Value);
        Assert.Contains("shape=hexagon", root.Attribute("style")!.Value);
        Assert.Contains(page.Diagnostics, diagnostic => diagnostic.Code == "V6StyleRuleUsage" && diagnostic.Message.Contains("rule=RootOrchestrationService", StringComparison.Ordinal));
        Assert.DoesNotContain(page.Diagnostics, diagnostic => diagnostic.Code == "V6UnmatchedStyleSelector" && diagnostic.Message.Contains("RootOrchestrationService", StringComparison.Ordinal));
        Assert.Contains(page.Diagnostics, diagnostic => diagnostic.Code == "V6UnmatchedStyleSelector" && diagnostic.Message.Contains("rule=NeverMatches", StringComparison.Ordinal));
        Assert.Contains(page.Diagnostics, diagnostic => diagnostic.Code == "V6StyleFallbackCount" && diagnostic.Message != "0");
    }


    private static bool Intersects(RelativeRectangle left, RelativeRectangle right) =>
        left.X < right.X + right.Width && right.X < left.X + left.Width && left.Y < right.Y + right.Height && right.Y < left.Y + left.Height;

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
