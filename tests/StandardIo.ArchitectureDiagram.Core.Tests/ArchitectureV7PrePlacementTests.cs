using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.Core.Models.Architectures;
using StandardIo.ArchitectureDiagram.Core.Models.ArchitectureV7;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.ArchitectureV7;
using Xunit;
using ArchitectureDiagramModel = StandardIo.ArchitectureDiagram.Core.Models.Architectures.ArchitectureDiagram;

namespace StandardIo.ArchitectureDiagram.Core.Tests;

public sealed class ArchitectureV7PrePlacementTests
{
    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 3)]
    [InlineData(2, 5)]
    [InlineData(3, 7)]
    public void Semantic_depth_maps_to_one_shared_reserved_node_row_domain(int depth, int expectedRow)
    {
        Assert.Equal(expectedRow, ArchitectureV7ReservationCoordinates.ReservedNodeRowFromSemanticDepth(depth));
        Assert.Equal(depth, ArchitectureV7ReservationCoordinates.TreeLayerFromReservedNodeRow(expectedRow));
    }

    [Theory]
    [InlineData(1, 3)]
    [InlineData(3, 5)]
    [InlineData(5, 7)]
    public void Reserved_node_row_uses_one_common_grid_offset(int reservedRow, int expectedFinalRow)
    {
        Assert.Equal(expectedFinalRow, ArchitectureV7ReservationCoordinates.FinalCommonNodeRowFromReservedNodeRow(reservedRow));
    }

    [Theory]
    [InlineData(30, 3)]
    [InlineData(50, 5)]
    [InlineData(70, 7)]
    public void Span_is_the_smallest_odd_span_at_each_threshold(int minimumWidth, int expectedSpan)
    {
        var result = Size(Diagram(new[] { Node("a", "A") }, Array.Empty<ArchitectureLink>()), Config(minimumWidth: minimumWidth));
        Assert.Equal(expectedSpan, Assert.Single(result.Requirements).LogicalSpan);
    }

    [Fact]
    public void Label_requirement_can_expand_span()
    {
        var result = Size(Diagram(new[] { Node("a", "12345678901") }, Array.Empty<ArchitectureLink>()), Config(labelWidth: 5));
        var requirement = Assert.Single(result.Requirements);
        Assert.Equal(55, requirement.VisibleLabelRequirement);
        Assert.Equal(7, requirement.LogicalSpan);
    }

    [Fact]
    public void Incoming_and_outgoing_terminal_capacity_are_calculated_independently()
    {
        var nodes = Enumerable.Range(0, 5).Select(index => Node("n" + index, "N" + index)).ToArray();
        var links = Enumerable.Range(1, 4).Select(index => Link("l" + index, "n0", "n" + index)).ToArray();
        var result = Size(Diagram(nodes, links), Config(minimumWidth: 1, portSpacing: 10, inset: 5));
        var source = result.Requirements.Single(item => item.PhysicalNodeId == "physical:n0");
        var target = result.Requirements.Single(item => item.PhysicalNodeId == "physical:n1");
        Assert.Equal(40, source.OutgoingTerminalRequirement);
        Assert.Equal(0, source.IncomingTerminalRequirement);
        Assert.Equal(10, target.IncomingTerminalRequirement);
    }

    [Fact]
    public void Sizing_uses_maximum_top_or_bottom_capacity_not_total_degree()
    {
        var nodes = Enumerable.Range(0, 9).Select(index => Node("n" + index, "N" + index)).ToArray();
        var links = Enumerable.Range(1, 4).Select(index => Link("out" + index, "n0", "n" + index))
            .Concat(Enumerable.Range(5, 4).Select(index => Link("in" + index, "n" + index, "n0"))).ToArray();
        var result = Size(Diagram(nodes, links), Config(minimumWidth: 1, portSpacing: 10, inset: 5));
        var center = result.Requirements.Single(item => item.PhysicalNodeId == "physical:n0");
        Assert.Equal(40, center.IncomingTerminalRequirement);
        Assert.Equal(40, center.OutgoingTerminalRequirement);
        Assert.Equal(40, center.RequiredWidth);
    }

    [Fact]
    public void Non_default_base_cell_width_is_used_directly()
    {
        var result = Size(Diagram(new[] { Node("a", "A") }, Array.Empty<ArchitectureLink>()), Config(minimumWidth: 24, baseCellWidth: 5));
        Assert.Equal(5, Assert.Single(result.Requirements).LogicalSpan);
    }

    [Fact]
    public void Sizing_is_deterministic_under_shuffled_links()
    {
        var diagram = Diagram(new[] { Node("a", "A"), Node("b", "B"), Node("c", "C") },
            new[] { Link("z", "a", "c", 0), Link("a", "b", "c", 1) });
        var left = Size(diagram, Config());
        var right = Size(diagram with { Links = diagram.Links.Reverse().ToArray() }, Config());
        Assert.Equal(left.FreezeFingerprint, right.FreezeFingerprint);
        Assert.Equal(left.Requirements.Select(item => item.LogicalSpan), right.Requirements.Select(item => item.LogicalSpan));
    }

    [Fact]
    public void Empty_patterns_produce_external_only_and_zero_match_groups_are_removed()
    {
        var ownership = Ownership(Diagram(new[] { Node("a", "Alpha") }, Array.Empty<ArchitectureLink>()));
        var inspector = new ArchitectureV7ReservedRoleConstraintInspector();
        var empty = inspector.Inspect(ownership, Config(patterns: Array.Empty<ArchitectureV7ReservedRoleRule>()));
        var emptyTable = new ArchitectureV7ReservationReconciliationStage().Reconcile(empty).Table;
        Assert.Single(emptyTable.Reservations);
        Assert.Equal("External", emptyTable.External.Name);

        var zero = inspector.Inspect(ownership, Config(patterns: new[] { new ArchitectureV7ReservedRoleRule("Missing", "*Missing", 0) }));
        var zeroTable = new ArchitectureV7ReservationReconciliationStage().Reconcile(zero).Table;
        Assert.Single(zeroTable.Reservations);
    }

    [Fact]
    public void Reserved_matching_is_ordered_case_insensitive_suffix_first_match_wins()
    {
        var ownership = Ownership(Diagram(new[] { Node("a", "OrderProcessingService") }, Array.Empty<ArchitectureLink>()));
        var rules = new[] { new ArchitectureV7ReservedRoleRule("Processing", "*processingservice", 0), new ArchitectureV7ReservedRoleRule("Service", "*service", 1) };
        var result = new ArchitectureV7ReservedRoleConstraintInspector().Inspect(ownership, Config(patterns: rules));
        Assert.Equal(1, result.Requirements.Single(item => item.ReservationName == "Processing").MatchCount);
        Assert.Equal(0, result.Requirements.Single(item => item.ReservationName == "Service").MatchCount);
    }

    [Fact]
    public void One_reconciled_table_is_shared_by_all_projects()
    {
        var diagram = new ArchitectureDiagramModel(
            new[] { new ArchitectureProject("p1", "P1", new[] { Node("a", "AlphaProcessing") }, "p1"), new ArchitectureProject("p2", "P2", new[] { Node("b", "BetaProcessing") }, "p2") },
            Array.Empty<ArchitectureExternalNode>(), Array.Empty<ArchitectureLink>(), null);
        var table = new ArchitectureV7ReservationReconciliationStage().Reconcile(
            new ArchitectureV7ReservedRoleConstraintInspector().Inspect(Ownership(diagram), Config(patterns: new[] { new ArchitectureV7ReservedRoleRule("Processing", "*processing", 0) }))).Table;
        Assert.Equal(new[] { "Processing", "External" }, table.Reservations.Select(item => item.Name));
    }

    [Fact]
    public void Deeper_middle_reservation_propagates_to_downstream_reservations_and_external()
    {
        var diagram = Diagram(new[] { Node("root", "Root"), Node("branch", "Branch"), Node("middle", "DeepProcessing"), Node("service", "Service"), Node("external", "ExternalApi") },
            new[] { Link("one", "root", "branch"), Link("two", "branch", "middle"), Link("three", "middle", "service"), Link("four", "service", "external") });
        var table = Reconcile(diagram, new[] { new ArchitectureV7ReservedRoleRule("Processing", "*processing", 0), new ArchitectureV7ReservedRoleRule("Service", "*service", 1) });
        Assert.Equal(new[] { 5, 7, 9 }, table.Reservations.Select(item => item.NodeRow));
    }

    [Fact]
    public void External_depth_is_inspected_from_the_positional_chain()
    {
        var diagram = Diagram(new[] { Node("a", "A"), Node("b", "B"), Node("external", "ExternalApi") },
            new[] { Link("one", "a", "b"), Link("two", "b", "external") });
        var table = Reconcile(diagram, Array.Empty<ArchitectureV7ReservedRoleRule>());
        Assert.Equal(5, table.External.NodeRow);
    }

    [Fact]
    public void External_is_below_a_deeper_ordinary_chain_even_when_its_own_dependency_is_shallow()
    {
        var diagram = Diagram(new[] { Node("root", "Root"), Node("child", "Child"), Node("grandchild", "Grandchild"), Node("external", "ExternalApi") },
            new[] { Link("one", "root", "child"), Link("two", "child", "grandchild"), Link("three", "root", "external") });
        var table = Reconcile(diagram, Array.Empty<ArchitectureV7ReservedRoleRule>());

        Assert.Equal(7, table.External.NodeRow);
        Assert.True(table.External.NodeRow > 5);
    }

    [Fact]
    public void Empty_external_reservation_remains_below_a_deep_ordinary_chain()
    {
        var diagram = DiagramWithoutExternal(new[] { Node("root", "Root"), Node("child", "Child"), Node("grandchild", "Grandchild") },
            new[] { Link("one", "root", "child"), Link("two", "child", "grandchild") });
        var table = Reconcile(diagram, Array.Empty<ArchitectureV7ReservedRoleRule>());

        Assert.Equal(7, table.External.NodeRow);
    }

    [Fact]
    public void External_moves_below_an_ordinary_reserved_role_result()
    {
        var diagram = Diagram(new[] { Node("root", "Root"), Node("processing", "Processing"), Node("external", "ExternalApi") },
            new[] { Link("one", "root", "processing"), Link("two", "root", "external") });
        var table = Reconcile(diagram, new[] { new ArchitectureV7ReservedRoleRule("Processing", "*processing", 0) });

        Assert.Equal(5, table.External.NodeRow);
        Assert.True(table.External.NodeRow > table.Reservations.Single(item => item.Name == "Processing").NodeRow);
    }

    [Fact]
    public void Unreserved_broker_depth_is_preserved_without_a_broker_reservation()
    {
        var diagram = Diagram(new[] { Node("processing", "Processing"), Node("broker", "Broker"), Node("external", "ExternalApi") },
            new[] { Link("one", "processing", "broker"), Link("two", "broker", "external") });
        var ownership = Ownership(diagram);
        var inspection = new ArchitectureV7ReservedRoleConstraintInspector().Inspect(ownership, Config(patterns: new[] { new ArchitectureV7ReservedRoleRule("Processing", "*processing", 0) }));
        Assert.Equal(1, inspection.Requirements.Single(item => item.ReservationName == "Processing").MatchCount);
        Assert.Equal(1, inspection.NaturalDepthByPhysicalNodeId["physical:broker"]);
        Assert.Equal(2, inspection.NaturalDepthByPhysicalNodeId["physical:external"]);
        Assert.DoesNotContain(inspection.Requirements, item => item.ReservationName == "Broker");
        Assert.Equal(5, new ArchitectureV7ReservationReconciliationStage().Reconcile(inspection).Table.External.NodeRow);
    }

    [Fact]
    public async Task Inspection_is_deterministic_across_concurrent_reduction()
    {
        var diagram = Diagram(new[] { Node("a", "A"), Node("b", "B"), Node("external", "ExternalApi") },
            new[] { Link("two", "b", "external"), Link("one", "a", "b") });
        var tasks = Enumerable.Range(0, 8).Select(_ => Task.Run(() => new ArchitectureV7ReservedRoleConstraintInspector().Inspect(Ownership(diagram), Config()))).ToArray();
        var results = await Task.WhenAll(tasks);
        Assert.All(results, result => Assert.Equal(results[0].FreezeFingerprint, result.FreezeFingerprint));
    }

    [Fact]
    public void Preplacement_products_do_not_expose_coordinates_or_tree_composition()
    {
        var sizingNames = typeof(ArchitectureV7NodeSpanSizingResult).GetProperties().Select(property => property.Name).Concat(typeof(ArchitectureV7NodeSpanRequirement).GetProperties().Select(property => property.Name));
        Assert.DoesNotContain(sizingNames, name => name.Contains("Coordinate", StringComparison.OrdinalIgnoreCase) || name.Contains("Grid", StringComparison.OrdinalIgnoreCase) || name.Equals("Column", StringComparison.OrdinalIgnoreCase) || name.Equals("X", StringComparison.OrdinalIgnoreCase) || name.Equals("Y", StringComparison.OrdinalIgnoreCase));
        var inspectionNames = typeof(ArchitectureV7ReservationInspectionResult).GetProperties().Select(property => property.Name).Concat(typeof(ArchitectureV7FrozenReservationTable).GetProperties().Select(property => property.Name));
        Assert.DoesNotContain(inspectionNames, name => name.Contains("Coordinate", StringComparison.OrdinalIgnoreCase) || name.Contains("Grid", StringComparison.OrdinalIgnoreCase) || name.Equals("Column", StringComparison.OrdinalIgnoreCase) || name.Equals("X", StringComparison.OrdinalIgnoreCase) || name.Equals("Y", StringComparison.OrdinalIgnoreCase));
    }

    private static ArchitectureV7NodeSpanSizingResult Size(ArchitectureDiagramModel diagram, ArchitectureV7PrePlacementConfiguration configuration) =>
        new ArchitectureV7PreRoutingNodeSpanSizer().Size(Ownership(diagram), configuration);

    private static ArchitectureV7FrozenReservationTable Reconcile(ArchitectureDiagramModel diagram, IReadOnlyList<ArchitectureV7ReservedRoleRule> patterns) =>
        new ArchitectureV7ReservationReconciliationStage().Reconcile(new ArchitectureV7ReservedRoleConstraintInspector().Inspect(Ownership(diagram), Config(patterns: patterns))).Table;

    private static ArchitectureV7PositionalOwnershipResult Ownership(ArchitectureDiagramModel diagram)
    {
        var projection = new ArchitectureV7PhysicalProjectionStage().Project(diagram, new ArchitectureV7ProjectionPolicy(ArchitectureV7ProjectionMode.Canonical, Array.Empty<string>()));
        return new ArchitectureV7PositionalOwnershipStage().Resolve(projection);
    }

    private static ArchitectureV7PrePlacementConfiguration Config(int minimumWidth = 30, int baseCellWidth = 10, int labelWidth = 1, int portSpacing = 5, int inset = 0, IReadOnlyList<ArchitectureV7ReservedRoleRule>? patterns = null) =>
        new(baseCellWidth, minimumWidth, labelWidth, 0, portSpacing, inset, patterns ?? Array.Empty<ArchitectureV7ReservedRoleRule>());

    private static ArchitectureDiagramModel Diagram(ArchitectureNode[] nodes, ArchitectureLink[] links) =>
        new(new[] { new ArchitectureProject("project", "Project", nodes.Where(node => node.Id != "external").ToArray(), "project") }, new[] { new ArchitectureExternalNode("external", "ExternalApi", "External", "external", "External.ExternalApi", "interface") }.Where(_ => nodes.Any(node => node.Id == "external")).ToArray(), links, null);

    private static ArchitectureDiagramModel DiagramWithoutExternal(ArchitectureNode[] nodes, ArchitectureLink[] links) =>
        new(new[] { new ArchitectureProject("project", "Project", nodes, "project") }, Array.Empty<ArchitectureExternalNode>(), links, null);

    private static ArchitectureNode Node(string id, string name) =>
        new(id, "project", name, "Project." + name, "Class", id, Array.Empty<string>());

    private static ArchitectureLink Link(string id, string source, string target, int analyserOrdinal = -1) =>
        new(id, source, target, "dependency", analyserOrdinal);
}
