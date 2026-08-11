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
    public void Soft_cohort_specificity_only_consumes_a_candidate_after_it_meets_the_threshold()
    {
        var nodes = Enumerable.Range(0, 3).Select(index => Node("processing" + index, "Foo" + index + "ProcessingService"))
            .Concat(Enumerable.Range(0, 4).Select(index => Node("service" + index, "Bar" + index + "Service"))).ToArray();
        var links = nodes.Skip(1).Select((node, index) => Link("link" + index, "processing0", node.Id)).ToArray();
        var result = new ArchitectureV7SoftCohortAnalyzer().Analyze(Ownership(Diagram(nodes, links)), Config());

        var cohort = Assert.Single(result.Cohorts);
        Assert.Equal("Service", cohort.TokenSuffix);
        Assert.Equal(7, cohort.MemberPhysicalNodeIds.Count);
    }

    [Fact]
    public void Soft_cohort_specificity_wins_only_among_viable_candidates()
    {
        var nodes = Enumerable.Range(0, 5).Select(index => Node("processing" + index, "Foo" + index + "ProcessingService"))
            .Concat(Enumerable.Range(0, 2).Select(index => Node("service" + index, "Bar" + index + "Service"))).ToArray();
        var links = nodes.Skip(1).Select((node, index) => Link("link" + index, "processing0", node.Id)).ToArray();
        var result = new ArchitectureV7SoftCohortAnalyzer().Analyze(Ownership(Diagram(nodes, links)), Config());

        var cohort = Assert.Single(result.Cohorts);
        Assert.Equal("ProcessingService", cohort.TokenSuffix);
        Assert.Equal(5, cohort.MemberPhysicalNodeIds.Count);
    }

    [Fact]
    public void Soft_cohorts_exclude_hard_reserved_external_and_standalone_nodes()
    {
        var nodes = Enumerable.Range(0, 5).Select(index => Node("worker" + index, "Alpha" + index + "Worker"))
            .Concat(new[] { Node("hard", "HardWorker"), Node("standalone", "SoloWorker"), Node("external", "ExternalApi") }).ToArray();
        var links = Enumerable.Range(1, 4).Select(index => Link("link" + index, "worker0", "worker" + index))
            .Concat(new[] { Link("hard-link", "worker0", "hard"), Link("external-link", "worker0", "external") }).ToArray();
        var result = new ArchitectureV7SoftCohortAnalyzer().Analyze(Ownership(Diagram(nodes, links)), Config(patterns: new[] { new ArchitectureV7ReservedRoleRule("Hard", "*HardWorker", 0) }));

        var cohort = Assert.Single(result.Cohorts);
        Assert.Equal("Worker", cohort.TokenSuffix);
        Assert.DoesNotContain("physical:hard", cohort.MemberPhysicalNodeIds);
        Assert.DoesNotContain("physical:external", cohort.MemberPhysicalNodeIds);
        Assert.DoesNotContain("physical:standalone", cohort.MemberPhysicalNodeIds);
    }

    [Fact]
    public void Soft_cohort_analysis_is_deterministic_under_shuffled_input()
    {
        var nodes = Enumerable.Range(0, 5).Select(index => Node("node" + index, "Gamma" + index + "Handler")).ToArray();
        var links = nodes.Skip(1).Select((node, index) => Link("link" + index, "node0", node.Id)).ToArray();
        var left = new ArchitectureV7SoftCohortAnalyzer().Analyze(Ownership(Diagram(nodes, links)), Config());
        var rightDiagram = Diagram(nodes.AsEnumerable().Reverse().ToArray(), links.AsEnumerable().Reverse().ToArray());
        var right = new ArchitectureV7SoftCohortAnalyzer().Analyze(Ownership(rightDiagram), Config());

        Assert.Equal(left.Cohorts.Select(cohort => cohort.TokenSuffix), right.Cohorts.Select(cohort => cohort.TokenSuffix));
        Assert.Equal(left.Cohorts.SelectMany(cohort => cohort.MemberPhysicalNodeIds), right.Cohorts.SelectMany(cohort => cohort.MemberPhysicalNodeIds));
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
    public void Soft_cohort_is_scheduled_immediately_above_external_and_external_remains_final()
    {
        var nodes = Enumerable.Range(0, 5).Select(index => Node("worker" + index, "Alpha" + index + "Worker"))
            .Concat(new[] { Node("external", "ExternalApi") }).ToArray();
        var links = Enumerable.Range(0, 5).Select(index => Link("link" + index, "worker" + index, "external")).ToArray();
        var schedule = Schedule(Diagram(nodes, links), Array.Empty<ArchitectureV7ReservedRoleRule>());

        var preference = Assert.Single(schedule.SoftPreferences);
        Assert.Equal("External", preference.PreferredAnchor);
        Assert.True(schedule.Entries.Single(entry => entry.IsExternal).NodeLayer > schedule.PreferredLayerByPhysicalNodeId[preference.MemberPhysicalNodeIds[0]]);
    }

    [Fact]
    public void Soft_layer_insertion_shifts_hard_reservations_without_changing_order()
    {
        var nodes = new[] { Node("processing", "MainProcessing") }
            .Concat(Enumerable.Range(0, 5).Select(index => Node("worker" + index, "Alpha" + index + "Worker")))
            .Concat(new[] { Node("external", "ExternalApi") }).ToArray();
        var links = Enumerable.Range(0, 5).Select(index => Link("soft" + index, "processing", "worker" + index))
            .Concat(new[] { Link("external-link", "worker0", "external") }).ToArray();
        var schedule = Schedule(Diagram(nodes, links), new[] { new ArchitectureV7ReservedRoleRule("Processing", "*Processing", 0) });

        var hard = schedule.Entries.Where(entry => entry.IsHardReservation).OrderBy(entry => entry.NodeLayer).ToArray();
        Assert.Equal(new[] { "Processing", "External" }, hard.Select(entry => entry.Name));
        Assert.True(hard[1].NodeLayer > hard[0].NodeLayer);
        Assert.Contains(schedule.Entries, entry => !entry.IsHardReservation && entry.TokenSuffix == "Worker");
    }

    [Fact]
    public void Soft_row_can_reuse_an_existing_ordinary_layer_without_becoming_exclusive()
    {
        var cohort = Enumerable.Range(0, 5).Select(index => Node("worker" + index, "Alpha" + index + "Worker")).ToArray();
        var ordinary = Node("ordinary", "Unrelated");
        var nodes = cohort.Concat(new[] { ordinary }).ToArray();
        var links = cohort.Select((node, index) => Link("link" + index, "ordinary", node.Id)).ToArray();
        var schedule = Schedule(DiagramWithoutExternal(nodes, links), Array.Empty<ArchitectureV7ReservedRoleRule>());

        var preference = Assert.Single(schedule.SoftPreferences);
        Assert.DoesNotContain(schedule.Entries, entry => entry.TokenSuffix == preference.TokenSuffix);
        Assert.Contains(schedule.Entries, entry => entry.NodeLayer == preference.PreferredNodeLayer && entry.Name.StartsWith("ordinary:", StringComparison.Ordinal));
    }

    [Fact]
    public void Soft_to_soft_anchor_cycles_are_diagnosed_deterministically()
    {
        var left = Enumerable.Range(0, 5).Select(index => Node("left" + index, "Alpha" + index + "LeftType")).ToArray();
        var right = Enumerable.Range(0, 5).Select(index => Node("right" + index, "Beta" + index + "RightType")).ToArray();
        var nodes = left.Concat(right).ToArray();
        var links = left.Select((node, index) => Link("left-link" + index, node.Id, right[0].Id))
            .Concat(right.Select((node, index) => Link("right-link" + index, node.Id, left[0].Id))).ToArray();
        var schedule = Schedule(DiagramWithoutExternal(nodes, links), Array.Empty<ArchitectureV7ReservedRoleRule>());

        Assert.Equal(2, schedule.SoftPreferences.Count);
        Assert.Contains(schedule.Diagnostics, diagnostic => diagnostic.StartsWith("v7-soft-layer-cycle:", StringComparison.Ordinal));
    }

    [Fact]
    public void Modal_dependency_anchor_tie_selects_the_deeper_layer()
    {
        var workers = Enumerable.Range(0, 5).Select(index => Node("worker" + index, "Alpha" + index + "Worker")).ToArray();
        var nodes = workers.Concat(new[] { Node("root", "Root"), Node("child", "Child") }).ToArray();
        var links = new[] { Link("first", "root", "child"), Link("worker-root", "worker0", "root"), Link("worker-child", "worker1", "child") }
            .Concat(workers.Skip(2).Select((node, index) => Link("worker-extra" + index, node.Id, "root"))).ToArray();
        var schedule = Schedule(DiagramWithoutExternal(nodes, links), Array.Empty<ArchitectureV7ReservedRoleRule>());

        var preference = Assert.Single(schedule.SoftPreferences);
        Assert.Equal("ordinary:1", preference.PreferredAnchor);
    }

    [Fact]
    public void Soft_members_with_deeper_natural_depths_report_exceptions_but_legal_members_align()
    {
        var workers = Enumerable.Range(0, 5).Select(index => Node("worker" + index, "Alpha" + index + "Worker")).ToArray();
        var nodes = workers.Concat(new[] { Node("root", "Root") }).ToArray();
        var links = new[] { Link("one", "root", "worker0"), Link("two", "worker0", "worker1"), Link("three", "worker1", "worker2") }
            .Concat(new[] { Link("four", "worker3", "root"), Link("five", "worker4", "root") }).ToArray();
        var schedule = Schedule(DiagramWithoutExternal(nodes, links), Array.Empty<ArchitectureV7ReservedRoleRule>());

        var preference = Assert.Single(schedule.SoftPreferences);
        Assert.NotEmpty(preference.AlignmentExceptions);
        Assert.Contains("physical:worker3", schedule.PreferredLayerByPhysicalNodeId.Keys);
        Assert.Contains("physical:worker4", schedule.PreferredLayerByPhysicalNodeId.Keys);
    }

    [Fact]
    public void Soft_schedule_is_deterministic_under_shuffled_input_and_standalone_nodes_remain_excluded()
    {
        var workers = Enumerable.Range(0, 5).Select(index => Node("worker" + index, "Alpha" + index + "Worker")).ToArray();
        var nodes = workers.Concat(new[] { Node("root", "Root"), Node("standalone", "SoloWorker") }).ToArray();
        var links = workers.Select((node, index) => Link("link" + index, node.Id, "root")).ToArray();
        var left = Schedule(DiagramWithoutExternal(nodes, links), Array.Empty<ArchitectureV7ReservedRoleRule>());
        var right = Schedule(DiagramWithoutExternal(nodes.AsEnumerable().Reverse().ToArray(), links.AsEnumerable().Reverse().ToArray()), Array.Empty<ArchitectureV7ReservedRoleRule>());

        Assert.Equal(left.Entries.Select(item => (item.Name, item.NodeLayer, item.IsHardReservation)), right.Entries.Select(item => (item.Name, item.NodeLayer, item.IsHardReservation)));
        Assert.Equal(left.SoftPreferences.Select(item => item.TokenSuffix), right.SoftPreferences.Select(item => item.TokenSuffix));
        Assert.DoesNotContain(left.SoftPreferences.SelectMany(item => item.MemberPhysicalNodeIds), id => id == "physical:standalone");
    }

    [Fact]
    public void Tree_construction_consumes_the_frozen_soft_schedule_without_mutating_it()
    {
        var workers = Enumerable.Range(0, 5).Select(index => Node("worker" + index, "Alpha" + index + "Worker")).ToArray();
        var diagram = DiagramWithoutExternal(workers, Array.Empty<ArchitectureLink>());
        var ownership = Ownership(diagram);
        var configuration = Config(patterns: Array.Empty<ArchitectureV7ReservedRoleRule>());
        var reservation = new ArchitectureV7ReservationReconciliationStage().Reconcile(new ArchitectureV7ReservedRoleConstraintInspector().Inspect(ownership, configuration));
        var soft = new ArchitectureV7SoftLayerSchedulingStage().Schedule(ownership, reservation, new ArchitectureV7SoftCohortAnalyzer().Analyze(ownership, configuration));
        var sizing = Size(diagram, configuration);
        var before = soft.Fingerprint;
        var trees = new ArchitectureV7RecursiveTreeGridStage().Build(sizing, soft);

        Assert.Equal(before, soft.Fingerprint);
        Assert.Equal(5, trees.Trees.Count);
        Assert.All(trees.Trees.SelectMany(tree => tree.Placements), placement => Assert.True(placement.LocalRow >= 0));
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

    private static ArchitectureV7FrozenLayerSchedule Schedule(ArchitectureDiagramModel diagram, IReadOnlyList<ArchitectureV7ReservedRoleRule> patterns)
    {
        var ownership = Ownership(diagram);
        var configuration = Config(patterns: patterns);
        var reservation = new ArchitectureV7ReservationReconciliationStage().Reconcile(new ArchitectureV7ReservedRoleConstraintInspector().Inspect(ownership, configuration));
        var soft = new ArchitectureV7SoftCohortAnalyzer().Analyze(ownership, configuration);
        return new ArchitectureV7SoftLayerSchedulingStage().Schedule(ownership, reservation, soft);
    }

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
