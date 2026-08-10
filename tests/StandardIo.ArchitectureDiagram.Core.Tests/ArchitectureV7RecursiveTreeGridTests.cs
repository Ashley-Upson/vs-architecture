using System;
using System.Collections.Generic;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core.Models.Architectures;
using StandardIo.ArchitectureDiagram.Core.Models.ArchitectureV7;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.ArchitectureV7;
using Xunit;
using ArchitectureDiagramModel = StandardIo.ArchitectureDiagram.Core.Models.Architectures.ArchitectureDiagram;

namespace StandardIo.ArchitectureDiagram.Core.Tests;

public sealed class ArchitectureV7RecursiveTreeGridTests
{
    [Fact]
    public void Reduced_real_tree_roots_pack_complete_detached_child_units_without_overlap()
    {
        var packageName = new string('P', 70) + "PackageBroker";
        var cacheName = new string('C', 70) + "PageRenderCacheAggregationService";
        var nodes = new[]
        {
            Node("tree-physical-type-63a43a0df7db1529", new string('R', 120)),
            Node("content", new string('M', 90) + "ContentManagementMigrationAggregationService"),
            Node("branch", new string('B', 80)),
            Node("branch-parent", new string('C', 70)),
            Node("package", packageName),
            Node("tree-physical-type-d7e0c44624cbe67e", new string('S', 120)),
            Node("privilege", new string('P', 70) + "PrivilegeBroker"),
            Node("cache", cacheName),
            Node("user-role", new string('U', 70) + "UserRoleBroker")
        };
        var links = new[]
        {
            Link("a", nodes[0].Id, "content"), Link("b", nodes[0].Id, "branch"), Link("c", "branch", "branch-parent"), Link("d", "branch-parent", "package"),
            Link("e", nodes[5].Id, "privilege"), Link("f", nodes[5].Id, "cache"), Link("g", "cache", "user-role")
        };
        var reservations = Frozen(new ArchitectureV7FrozenReservation("PackageBroker", "*packagebroker", 0, 1, 1, false), new ArchitectureV7FrozenReservation("External", "<external>", int.MaxValue, 0, 5, true));
        var result = Build(Diagram(nodes, links), reservations);
        foreach (var tree in result.Trees)
        {
            var occupied = new HashSet<(int Row, int Column)>();
            foreach (var placement in tree.Placements)
                foreach (var column in Enumerable.Range(placement.LocalColumn, placement.LogicalSpan))
                    Assert.True(occupied.Add((placement.LocalRow, column)), tree.TreeId + " overlap at " + placement.PhysicalNodeId);
        }
    }
    [Fact]
    public void One_child_is_directly_below_and_exactly_centred()
    {
        var result = Build(Diagram(new[] { Node("root", "Root"), Node("child", "Child") }, new[] { Link("one", "root", "child") }));
        var tree = Assert.Single(result.Trees);
        var root = Placement(tree, "physical:root");
        var child = Placement(tree, "physical:child");
        Assert.Equal(0, root.LocalRow);
        Assert.Equal(2, child.LocalRow);
        Assert.Equal(child.CentreCell, root.CentreCell);
    }

    [Fact]
    public void Deep_single_child_chain_preserves_recursive_rows()
    {
        var nodes = new[] { Node("a", "A"), Node("b", "B"), Node("c", "C"), Node("d", "D") };
        var links = new[] { Link("1", "a", "b"), Link("2", "b", "c"), Link("3", "c", "d") };
        var tree = Assert.Single(Build(Diagram(nodes, links)).Trees);
        Assert.Equal(new[] { 0, 2, 4, 6 }, tree.Placements.OrderBy(item => item.LocalRow).Select(item => item.LocalRow));
    }

    [Fact]
    public void Multiple_children_use_direct_child_centres_and_one_separation_column()
    {
        var nodes = new[] { Node("root", "Root"), Node("a", "A"), Node("b", "B") };
        var tree = Assert.Single(Build(Diagram(nodes, new[] { Link("a", "root", "a"), Link("b", "root", "b") })).Trees);
        Assert.Equal(7, tree.Width);
        var root = Placement(tree, "physical:root");
        var children = new[] { Placement(tree, "physical:a"), Placement(tree, "physical:b") }.OrderBy(item => item.LocalColumn).ToArray();
        Assert.Equal((children[0].CentreCell + children[1].CentreCell) / 2, root.CentreCell);
        Assert.Equal(4, children[1].LocalColumn - children[0].LocalColumn);
    }

    [Fact]
    public void Uneven_descendant_width_does_not_redefine_parent_centreline()
    {
        var nodes = new[] { Node("root", "Root"), Node("left", "Left"), Node("left-child", new string('L', 61)), Node("right", "Right") };
        var links = new[] { Link("1", "root", "left"), Link("2", "left", "left-child"), Link("3", "root", "right") };
        var tree = Assert.Single(Build(Diagram(nodes, links)).Trees);
        var root = Placement(tree, "physical:root");
        var direct = new[] { Placement(tree, "physical:left"), Placement(tree, "physical:right") }.OrderBy(item => item.LocalColumn).ToArray();
        Assert.Equal((direct[0].CentreCell + direct[1].CentreCell) / 2, root.CentreCell);
        var leftSubtreeRight = tree.Placements.Where(item => item.PhysicalNodeId == "physical:left" || item.PhysicalNodeId == "physical:left-child")
            .Max(item => item.LocalColumn + item.LogicalSpan - 1);
        Assert.NotEqual((leftSubtreeRight + direct[1].CentreCell) / 2, root.CentreCell);
    }

    [Fact]
    public void Reserved_depth_inserts_node_routing_padding_without_mutating_table()
    {
        var diagram = Diagram(new[] { Node("root", "Root"), Node("processing", "Processing") }, new[] { Link("one", "root", "processing") });
        var reservations = Frozen(new ArchitectureV7FrozenReservation("Processing", "*processing", 0, 1, 3, false), new ArchitectureV7FrozenReservation("External", "<external>", int.MaxValue, 0, 5, true));
        var result = Build(diagram, reservations);
        var tree = Assert.Single(result.Trees);
        Assert.Equal(6, Placement(tree, "physical:processing").LocalRow);
        Assert.Equal(3, result.Reservations.Reservations.Single(item => item.Name == "Processing").NodeRow);
    }

    [Fact]
    public void Processing_broker_external_chain_preserves_unreserved_broker_depth()
    {
        var diagram = Diagram(new[] { Node("processing", "Processing"), Node("broker", "Broker"), Node("external", "ExternalApi") },
            new[] { Link("1", "processing", "broker"), Link("2", "broker", "external") });
        var tree = Assert.Single(Build(diagram).Trees);
        Assert.Equal(new[] { 0, 2, 10 }, tree.Placements.OrderBy(item => item.LocalRow).Select(item => item.LocalRow));
        Assert.Equal(1, Placement(tree, "physical:broker").NodeLayer);
    }

    [Fact]
    public void Reserved_order_conflict_detaches_dependency_and_keeps_relationship_ownership()
    {
        var diagram = Diagram(new[] { Node("root", "Processing"), Node("child", "Service") }, new[] { Link("one", "root", "child") });
        var reservations = Frozen(new ArchitectureV7FrozenReservation("Processing", "*processing", 0, 1, 3, false), new ArchitectureV7FrozenReservation("Service", "*service", 1, 1, 1, false), new ArchitectureV7FrozenReservation("External", "<external>", int.MaxValue, 0, 5, true));
        var tree = Assert.Single(Build(diagram, reservations).Trees);
        Assert.Contains(tree.DetachedUnits, unit => unit.RootPhysicalNodeId == "physical:child");
        Assert.DoesNotContain(tree.MainUnit.Placements, item => item.PhysicalNodeId == "physical:child");
        Assert.Equal("physical:root", BuildOwnership(diagram).Decisions.Single(item => item.PhysicalNodeId == "physical:child").PositionalParentPhysicalNodeId);
    }

    [Fact]
    public void Multiple_detached_units_keep_deterministic_fifo_order()
    {
        var diagram = Diagram(new[] { Node("root", "Processing"), Node("a", "Service"), Node("b", "Broker") },
            new[] { Link("first", "root", "a"), Link("second", "root", "b") });
        var reservations = Frozen(new ArchitectureV7FrozenReservation("Processing", "*processing", 0, 1, 3, false), new ArchitectureV7FrozenReservation("Service", "*service", 1, 1, 1, false), new ArchitectureV7FrozenReservation("Broker", "*broker", 2, 1, 1, false), new ArchitectureV7FrozenReservation("External", "<external>", int.MaxValue, 0, 7, true));
        var tree = Assert.Single(Build(diagram, reservations).Trees);
        Assert.Equal(new[] { "physical:a", "physical:b" }, tree.DetachedUnits.Select(unit => unit.RootPhysicalNodeId));
        var firstMin = tree.DetachedUnits[0].Placements.Min(item => item.LocalColumn);
        var secondMin = tree.DetachedUnits[1].Placements.Min(item => item.LocalColumn);
        Assert.Equal(1, firstMin - tree.MainUnit.Width);
        Assert.Equal(1, secondMin - (firstMin + tree.DetachedUnits[0].Width));
    }

    [Fact]
    public void Nested_detached_descendant_is_materialised_once_in_recursive_tree_result()
    {
        var diagram = Diagram(
            new[] { Node("root", "Processing"), Node("detached", "Processing"), Node("nested", "Processing") },
            new[] { Link("one", "root", "detached"), Link("two", "detached", "nested") });
        var reservations = Frozen(
            new ArchitectureV7FrozenReservation("Processing", "*processing", 0, 1, 1, false),
            new ArchitectureV7FrozenReservation("External", "<external>", int.MaxValue, 0, 3, true));

        var tree = Assert.Single(Build(diagram, reservations).Trees);

        Assert.Equal(3, tree.Placements.Count);
        Assert.Equal(3, tree.Placements.Select(item => item.PhysicalNodeId).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(new[] { "physical:detached", "physical:nested", "physical:root" },
            tree.Placements.Select(item => item.PhysicalNodeId).OrderBy(item => item, StringComparer.Ordinal));
        Assert.Single(tree.DetachedUnits);
        Assert.Equal(new[] { "physical:detached", "physical:nested" },
            tree.DetachedUnits[0].Placements.Select(item => item.PhysicalNodeId));
    }

    [Fact]
    public void Nested_detached_subtree_preserves_fifo_order_after_single_materialisation()
    {
        var diagram = Diagram(
            new[] { Node("root", "Processing"), Node("first", "Processing"), Node("first-nested", "Processing"), Node("second", "Processing") },
            new[] { Link("one", "root", "first"), Link("two", "first", "first-nested"), Link("three", "root", "second") });
        var reservations = Frozen(
            new ArchitectureV7FrozenReservation("Processing", "*processing", 0, 1, 1, false),
            new ArchitectureV7FrozenReservation("External", "<external>", int.MaxValue, 0, 3, true));

        var tree = Assert.Single(Build(diagram, reservations).Trees);

        Assert.Equal(new[] { "physical:first", "physical:first-nested", "physical:second" },
            tree.DetachedUnits.SelectMany(unit => unit.Placements).Select(item => item.PhysicalNodeId));
        Assert.Equal(4, tree.Placements.Select(item => item.PhysicalNodeId).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Nested_detached_subtree_preserves_tree_and_detached_provenance()
    {
        var diagram = Diagram(
            new[] { Node("root", "Processing"), Node("detached", "Processing"), Node("nested", "Processing") },
            new[] { Link("one", "root", "detached"), Link("two", "detached", "nested") });
        var reservations = Frozen(
            new ArchitectureV7FrozenReservation("Processing", "*processing", 0, 1, 1, false),
            new ArchitectureV7FrozenReservation("External", "<external>", int.MaxValue, 0, 3, true));

        var tree = Assert.Single(Build(diagram, reservations).Trees);
        var detached = tree.Placements.Where(item => item.IsDetached).ToArray();

        Assert.Equal(2, detached.Length);
        Assert.All(detached, item =>
        {
            Assert.Contains("v7-recursive", item.Provenance, StringComparison.Ordinal);
            Assert.True(item.IsDetached);
        });
        Assert.All(tree.DetachedUnits, unit => Assert.Contains("v7-recursive", unit.Provenance, StringComparison.Ordinal));
        Assert.StartsWith("tree:physical:root", tree.TreeId, StringComparison.Ordinal);
    }

    [Fact]
    public void Frozen_spans_3_5_7_are_preserved_exactly()
    {
        var nodes = new[] { Node("three", "A"), Node("five", new string('B', 41)), Node("seven", new string('C', 61)) };
        var tree = Assert.Single(Build(Diagram(nodes, new[] { Link("one", "three", "five"), Link("two", "three", "seven") }), configuration: Config()).Trees);
        Assert.Equal(new[] { 3, 5, 7 }, tree.Placements.OrderBy(item => item.LogicalSpan).Select(item => item.LogicalSpan));
    }

    [Fact]
    public void Changed_recursive_completion_order_does_not_change_result()
    {
        var diagram = Diagram(new[] { Node("root", "Root"), Node("a", "A"), Node("b", "B") }, new[] { Link("z", "root", "b"), Link("a", "root", "a") });
        var left = Build(diagram);
        var right = Build(diagram with { Links = diagram.Links.Reverse().ToArray() });
        Assert.Equal(left.FreezeFingerprint, right.FreezeFingerprint);
        Assert.Equal(left.Trees.Single().Placements.Select(item => (item.PhysicalNodeId, item.LocalRow, item.LocalColumn)), right.Trees.Single().Placements.Select(item => (item.PhysicalNodeId, item.LocalRow, item.LocalColumn)));
    }

    [Fact]
    public void Every_node_is_constructed_recursively_or_as_explicit_detached_unit_and_no_fallback_row_one_exists()
    {
        var diagram = Diagram(new[] { Node("root", "Processing"), Node("broker", "Broker"), Node("external", "ExternalApi") }, new[] { Link("1", "root", "broker"), Link("2", "broker", "external") });
        var result = Build(diagram);
        var tree = Assert.Single(result.Trees);
        Assert.Equal(3, tree.Placements.Count);
        Assert.DoesNotContain(tree.Placements, placement => placement.LocalRow == 1);
        Assert.All(tree.Placements, placement => Assert.Equal(0, placement.LocalRow % 2));
    }

    [Fact]
    public void Tree_result_freezes_input_fingerprints_and_has_no_mutation_api()
    {
        var result = Build(Diagram(new[] { Node("a", "A") }, Array.Empty<ArchitectureLink>()));
        var tree = Assert.Single(result.Trees);
        Assert.Equal(result.Sizing.FreezeFingerprint, tree.SpanFreezeFingerprint);
        Assert.Equal(result.Reservations.Fingerprint, tree.ReservationFingerprint);
        Assert.DoesNotContain(typeof(ArchitectureV7TopLevelTreeGrid).GetMethods(), method => method.Name.StartsWith("Set", StringComparison.Ordinal) || method.Name.StartsWith("Change", StringComparison.Ordinal));
    }

    private static ArchitectureV7RecursiveTreeGridResult Build(ArchitectureDiagramModel diagram, ArchitectureV7FrozenReservationTable? frozen = null, ArchitectureV7PrePlacementConfiguration? configuration = null)
    {
        var ownership = BuildOwnership(diagram);
        var config = configuration ?? Config();
        var sizing = new ArchitectureV7PreRoutingNodeSpanSizer().Size(ownership, config);
        var reservations = frozen ?? new ArchitectureV7ReservationReconciliationStage().Reconcile(new ArchitectureV7ReservedRoleConstraintInspector().Inspect(ownership, config)).Table;
        return new ArchitectureV7RecursiveTreeGridStage().Build(sizing, reservations);
    }

    private static ArchitectureV7PositionalOwnershipResult BuildOwnership(ArchitectureDiagramModel diagram)
    {
        var projection = new ArchitectureV7PhysicalProjectionStage().Project(diagram, new ArchitectureV7ProjectionPolicy(ArchitectureV7ProjectionMode.Canonical, Array.Empty<string>()));
        return new ArchitectureV7PositionalOwnershipStage().Resolve(projection);
    }

    private static ArchitectureV7FrozenReservationTable Frozen(params ArchitectureV7FrozenReservation[] reservations) =>
        new(reservations, "test-frozen");

    private static ArchitectureV7PrePlacementConfiguration Config() => new(10, 1, 1, 0, 5, 0, Array.Empty<ArchitectureV7ReservedRoleRule>());

    private static ArchitectureV7TreeGridNodePlacement Placement(ArchitectureV7TopLevelTreeGrid tree, string id) =>
        tree.Placements.Single(item => item.PhysicalNodeId == id);

    private static ArchitectureDiagramModel Diagram(ArchitectureNode[] nodes, ArchitectureLink[] links) =>
        new(new[] { new ArchitectureProject("project", "Project", nodes.Where(node => node.Id != "external").ToArray(), "project") },
            new[] { new ArchitectureExternalNode("external", "ExternalApi", "External", "external", "External.ExternalApi", "interface") }.Where(_ => nodes.Any(node => node.Id == "external")).ToArray(), links, null);

    private static ArchitectureNode Node(string id, string name) => new(id, "project", name, "Project." + name, "Class", id, Array.Empty<string>());
    private static ArchitectureLink Link(string id, string source, string target) => new(id, source, target, "dependency");
}
