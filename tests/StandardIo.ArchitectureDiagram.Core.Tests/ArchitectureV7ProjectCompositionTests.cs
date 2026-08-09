using System;
using System.Collections.Generic;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core.Models.Architectures;
using StandardIo.ArchitectureDiagram.Core.Models.ArchitectureV7;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.ArchitectureV7;
using Xunit;
using ArchitectureDiagramModel = StandardIo.ArchitectureDiagram.Core.Models.Architectures.ArchitectureDiagram;

namespace StandardIo.ArchitectureDiagram.Core.Tests;

public sealed class ArchitectureV7ProjectCompositionTests
{
    [Fact]
    public void Independent_top_level_trees_compose_fifo_with_one_column_and_no_interleaving()
    {
        var diagram = Diagram(new[] { Node("a", "A"), Node("a-child", "AChild"), Node("b", "B"), Node("b-child", "BChild") },
            new[] { Link("1", "a", "a-child"), Link("2", "b", "b-child") });
        var result = Compose(diagram);
        var project = Assert.Single(result.Projects);
        Assert.Equal(2, project.TreeIds.Count);
        var treeA = project.Transform.InteriorOriginColumn;
        var treeB = result.Nodes.Single(node => node.PhysicalNodeId == "physical:b").DiagramColumn;
        var treeAWidth = result.Nodes.Where(node => node.PhysicalNodeId.StartsWith("physical:a", StringComparison.Ordinal)).Max(node => node.DiagramColumn + node.LogicalSpan) - treeA;
        Assert.Equal(1, treeB - (treeA + treeAWidth));
        Assert.True(result.Nodes.Single(node => node.PhysicalNodeId == "physical:a-child").DiagramColumn < treeB);
    }

    [Fact]
    public void Top_level_tree_internal_geometry_is_preserved_after_composition()
    {
        var treeResult = BuildTrees(Diagram(new[] { Node("root", "Root"), Node("child", "Child") }, new[] { Link("one", "root", "child") }));
        var tree = Assert.Single(treeResult.Trees);
        var freeze = new ArchitectureV7ProjectCompositionStage().Compose(treeResult);
        var root = freeze.Nodes.Single(node => node.PhysicalNodeId == "physical:root");
        var child = freeze.Nodes.Single(node => node.PhysicalNodeId == "physical:child");
        Assert.Equal(2, child.DiagramRow - root.DiagramRow);
        Assert.Equal(0, child.CentreCell - root.CentreCell);
    }

    [Fact]
    public void External_nodes_use_final_reserved_row_and_prefer_owner_alignment()
    {
        var diagram = Diagram(new[] { Node("owner", "Owner"), Node("broker", "Broker"), Node("external", "IEventHub") },
            new[] { Link("1", "owner", "broker"), Link("2", "broker", "external") });
        var freeze = Compose(diagram);
        var external = Assert.Single(freeze.External.Placements);
        var broker = freeze.Nodes.Single(node => node.PhysicalNodeId == "physical:broker");
        Assert.Equal(freeze.External.NodeRow, external.DiagramRow);
        Assert.Equal(broker.CentreCell, external.CentreCell);
        Assert.DoesNotContain(freeze.Nodes.Where(node => !node.IsExternal), node => node.DiagramRow == freeze.External.NodeRow);
    }

    [Fact]
    public void Wide_external_units_have_exactly_one_logical_separation_column()
    {
        var wide = new string('X', 650);
        var diagram = new ArchitectureDiagramModel(
            new[] { new ArchitectureProject("project", "Project", new[] { Node("owner", "Owner") }, "project") },
            new[]
            {
                new ArchitectureExternalNode("external-a", wide + "A", "External", "external-a", "External.A", "interface"),
                new ArchitectureExternalNode("external-b", wide + "B", "External", "external-b", "External.B", "interface")
            },
            new[] { Link("a", "owner", "external-a"), Link("b", "owner", "external-b") }, null);
        var freeze = Compose(diagram);
        var external = freeze.External.Placements.OrderBy(item => item.DiagramColumn).ToArray();
        Assert.Equal(2, external.Length);
        Assert.Equal(external[0].DiagramColumn + external[0].LogicalSpan + 1, external[1].DiagramColumn);
    }

    [Fact]
    public void Connected_ordinary_nodes_do_not_enter_standalone_region_or_row_one()
    {
        var diagram = Diagram(new[] { Node("root", "Root"), Node("child", "Child"), Node("standalone", "Standalone") }, new[] { Link("one", "root", "child") });
        var freeze = Compose(diagram);
        Assert.Contains(freeze.Standalone.PhysicalNodeIds, id => id == "physical:standalone");
        Assert.DoesNotContain(freeze.Nodes.Where(node => !node.IsStandalone && !node.IsExternal), node => node.DiagramRow >= freeze.Standalone.FirstNodeRow);
        Assert.DoesNotContain(freeze.Nodes.Where(node => !node.IsStandalone && !node.IsExternal), node => node.DiagramRow == 1);
    }

    [Fact]
    public void Standalone_region_has_routing_row_and_uses_actual_spans_for_squareish_packing()
    {
        var nodes = new[] { Node("s1", "A"), Node("s2", new string('B', 41)), Node("s3", "C"), Node("s4", "D") };
        var freeze = Compose(Diagram(nodes, Array.Empty<ArchitectureLink>()));
        Assert.Equal(5, freeze.Nodes.Single(node => node.PhysicalNodeId == "physical:s2").LogicalSpan);
        Assert.Equal(2, freeze.Standalone.FirstNodeRow - freeze.External.NodeRow);
        Assert.True(freeze.Standalone.Height > 0);
        Assert.True(freeze.Standalone.Width >= 5);
    }

    [Fact]
    public void Project_surround_is_exactly_two_tracks_with_fixed_capabilities()
    {
        var freeze = Compose(Diagram(new[] { Node("a", "A") }, Array.Empty<ArchitectureLink>()));
        var project = Assert.Single(freeze.Projects);
        Assert.Equal(project.InteriorWidth + 4, project.Width);
        Assert.Equal(project.InteriorHeight + 4, project.Height);
        Assert.All(project.Cells.Where(cell => cell.Row == project.Transform.RegionOriginRow || cell.Row == project.Transform.RegionOriginRow + project.Height - 1), cell =>
            Assert.True(cell.Capabilities.HasFlag(ArchitectureV7CellCapability.GeneralRouting)));
        var innerTop = project.Cells.Where(cell => cell.Row == project.Transform.RegionOriginRow + 1).ToArray();
        Assert.NotEmpty(innerTop);
        Assert.All(innerTop, cell => Assert.True(cell.Capabilities.HasFlag(ArchitectureV7CellCapability.StraightPassthroughOnly) && cell.Capabilities.HasFlag(ArchitectureV7CellCapability.HeaderBlocked)));
        Assert.All(project.Cells.Where(cell => cell.Column == project.Transform.RegionOriginColumn + 1 && cell.Row > project.Transform.RegionOriginRow + 1), cell =>
            Assert.True(cell.Capabilities.HasFlag(ArchitectureV7CellCapability.StraightPassthroughOnly)));
    }

    [Fact]
    public void Project_interior_parity_is_routing_then_node_capable_and_transforms_are_composed_from_grid()
    {
        var freeze = Compose(new ArchitectureDiagramModel(
            new[] { new ArchitectureProject("p1", "P1", new[] { Node("a", "A"), Node("ac", "AC") }, "p1"), new ArchitectureProject("p2", "P2", new[] { Node("b", "B"), Node("bc", "BC") }, "p2") },
            Array.Empty<ArchitectureExternalNode>(), new[] { Link("1", "a", "ac"), Link("2", "b", "bc") }, null));
        Assert.Equal(0, freeze.Projects[0].Transform.RegionOriginRow);
        Assert.Equal(freeze.Projects[0].Width + 1, freeze.Projects[1].Transform.RegionOriginColumn);
        var node = freeze.Nodes.Single(item => item.PhysicalNodeId == "physical:a");
        Assert.Equal(3, node.DiagramRow);
        Assert.Equal(freeze.Projects[0].Transform.InteriorOriginRow + 1, node.DiagramRow);
        Assert.Equal(freeze.DiagramGrid.ColumnCount, freeze.Projects.Max(project => project.Transform.RegionOriginColumn + project.Width));
    }

    [Fact]
    public void Final_freeze_has_no_overlapping_footprints_and_parent_child_rows_are_downward()
    {
        var diagram = Diagram(new[] { Node("root", "Root"), Node("child", "Child"), Node("external", "ExternalApi") }, new[] { Link("1", "root", "child"), Link("2", "child", "external") });
        var treeResult = BuildTrees(diagram);
        var freeze = new ArchitectureV7ProjectCompositionStage().Compose(treeResult);
        var occupied = new HashSet<(int Row, int Column)>();
        foreach (var node in freeze.Nodes)
            foreach (var cell in node.LogicalFootprint) Assert.True(occupied.Add(cell));
        var byId = freeze.Nodes.ToDictionary(node => node.PhysicalNodeId, StringComparer.Ordinal);
        foreach (var decision in treeResult.Sizing.Ownership.Decisions)
            if (decision.PositionalParentPhysicalNodeId is { } parent && byId.TryGetValue(parent, out var parentPlacement) && byId.TryGetValue(decision.PhysicalNodeId, out var childPlacement) && !childPlacement.IsDetached)
                Assert.True(parentPlacement.DiagramRow < childPlacement.DiagramRow);
    }

    [Fact]
    public void Placement_fingerprint_is_stable_for_read_only_downstream_consumers()
    {
        var freeze = Compose(Diagram(new[] { Node("a", "A"), Node("b", "B") }, new[] { Link("one", "a", "b") }));
        var fingerprint = freeze.PlacementFingerprint;
        _ = freeze.Nodes.Count + freeze.DiagramGrid.Cells.Count + freeze.Projects.SelectMany(project => project.Cells).Count();
        Assert.Equal(fingerprint, freeze.PlacementFingerprint);
    }

    private static ArchitectureV7PlacementFreeze Compose(ArchitectureDiagramModel diagram) => new ArchitectureV7ProjectCompositionStage().Compose(BuildTrees(diagram));

    private static ArchitectureV7RecursiveTreeGridResult BuildTrees(ArchitectureDiagramModel diagram)
    {
        var projection = new ArchitectureV7PhysicalProjectionStage().Project(diagram, new ArchitectureV7ProjectionPolicy(ArchitectureV7ProjectionMode.Canonical, Array.Empty<string>()));
        var ownership = new ArchitectureV7PositionalOwnershipStage().Resolve(projection);
        var configuration = new ArchitectureV7PrePlacementConfiguration(10, 1, 1, 0, 5, 0, Array.Empty<ArchitectureV7ReservedRoleRule>());
        var sizing = new ArchitectureV7PreRoutingNodeSpanSizer().Size(ownership, configuration);
        var reservations = new ArchitectureV7ReservationReconciliationStage().Reconcile(new ArchitectureV7ReservedRoleConstraintInspector().Inspect(ownership, configuration)).Table;
        return new ArchitectureV7RecursiveTreeGridStage().Build(sizing, reservations);
    }

    private static ArchitectureDiagramModel Diagram(ArchitectureNode[] nodes, ArchitectureLink[] links) =>
        new(new[] { new ArchitectureProject("project", "Project", nodes.Where(node => node.Id != "external").ToArray(), "project") },
            new[] { new ArchitectureExternalNode("external", "IEventHub", "External", "external", "External.IEventHub", "interface") }.Where(_ => nodes.Any(node => node.Id == "external")).ToArray(), links, null);

    private static ArchitectureNode Node(string id, string name) => new(id, "project", name, "Project." + name, "Class", id, Array.Empty<string>());
    private static ArchitectureLink Link(string id, string source, string target) => new(id, source, target, "dependency");
}
