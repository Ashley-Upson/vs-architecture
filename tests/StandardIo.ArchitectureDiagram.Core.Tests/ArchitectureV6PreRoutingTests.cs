using System;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core.Models.ArchitectureV6;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.ArchitectureV6;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core.Tests;

public sealed class ArchitectureV6PreRoutingTests
{
    [Fact]
    public void Projection_preserves_fifo_nodes_and_every_selected_relationship_in_canonical_mode()
    {
        var request = new ArchitectureV6SemanticFixtureBuilder()
            .Project("p", "Project")
            .Node("first", "FirstService", "p")
            .Node("second", "SecondService", "p")
            .Node("shared", "SharedService", "p")
            .Link("l1", "first", "shared")
            .Link("l2", "second", "shared")
            .BuildRequest();

        var projection = ArchitectureV6ProjectionStage.Build(request);

        Assert.Equal(new[] { "physical:first", "physical:second", "physical:shared" },
            projection.PhysicalNodes.Select(node => node.PhysicalNodeId));
        Assert.Equal(3, projection.PhysicalNodes.Count);
        Assert.Equal(new[] { "l1", "l2" }, projection.PhysicalLinks.Select(link => link.SemanticLinkId));
        Assert.Equal(2, projection.PhysicalLinks.Count);
        Assert.Equal(projection.PhysicalNodes.Count, projection.SemanticNodeToPhysicalNodeIds.Values.Sum(values => values.Count));
        Assert.All(projection.PhysicalLinks, link => Assert.Contains(link.SemanticLinkId, new[] { "l1", "l2" }));
    }

    [Fact]
    public void Multi_parent_projection_selects_fifo_owner_and_keeps_secondary_parents_routing_only()
    {
        var request = new ArchitectureV6SemanticFixtureBuilder()
            .Project("p", "Project")
            .Node("first", "FirstService", "p")
            .Node("second", "SecondService", "p")
            .Node("shared", "SharedService", "p")
            .Link("first-link", "first", "shared")
            .Link("second-link", "second", "shared")
            .BuildRequest();

        var projection = ArchitectureV6ProjectionStage.Build(request);
        var shared = projection.PhysicalNodes.Single(node => node.SemanticNodeId == "shared");

        Assert.Equal("physical:first", shared.PositionalOwnerId);
        Assert.Equal(2, projection.PhysicalLinks.Count(link => link.DestinationPhysicalNodeId == shared.PhysicalNodeId));
        Assert.Equal(new[] { "physical:first", "physical:second" }, projection.PhysicalLinks
            .Where(link => link.DestinationPhysicalNodeId == shared.PhysicalNodeId)
            .Select(link => link.SourcePhysicalNodeId));
        Assert.Empty(projection.UnaccountedSemanticLinkIds);
        Assert.Equal(2, projection.SemanticLinkToPhysicalLinkIds.Count);
        Assert.Contains(projection.Diagnostics, diagnostic => diagnostic.Code == "ProjectionMultipleSemanticParents"
            && diagnostic.SubjectId == "shared");
    }

    [Fact]
    public void Projection_uses_fifo_incoming_parent_even_when_parent_is_discovered_later()
    {
        var request = new ArchitectureV6SemanticFixtureBuilder()
            .Project("p", "Project")
            .Node("child", "ChildService", "p")
            .Node("parent", "ParentService", "p")
            .Link("parent-child", "parent", "child")
            .BuildRequest();

        var projection = ArchitectureV6ProjectionStage.Build(request);

        Assert.Equal("physical:parent", projection.PhysicalNodes.Single(node => node.SemanticNodeId == "child").PositionalOwnerId);
    }

    [Fact]
    public void Projection_breaks_positional_cycles_deterministically_without_dropping_semantic_links()
    {
        var request = new ArchitectureV6SemanticFixtureBuilder()
            .Project("p", "Project")
            .Node("a", "AService", "p")
            .Node("b", "BService", "p")
            .Link("a-b", "a", "b")
            .Link("b-a", "b", "a")
            .BuildRequest();

        var first = ArchitectureV6ProjectionStage.Build(request);
        var second = ArchitectureV6ProjectionStage.Build(request);

        Assert.Equal(1, first.PhysicalNodes.Count(node => node.PositionalOwnerId is null));
        Assert.Single(first.PhysicalNodes.Where(node => node.PositionalOwnerId is not null));
        Assert.Equal(first.PhysicalNodes.Select(node => (node.PhysicalNodeId, node.PositionalOwnerId)),
            second.PhysicalNodes.Select(node => (node.PhysicalNodeId, node.PositionalOwnerId)));
        Assert.Equal(new[] { "a-b", "b-a" }, first.PhysicalLinks.Select(link => link.SemanticLinkId));
        Assert.Empty(first.UnaccountedSemanticLinkIds);
        Assert.Contains(first.Diagnostics, diagnostic => diagnostic.Code == "ProjectionPositionalCycleBreak");
        Assert.NotEmpty(first.CycleSemanticNodeIds);
    }

    [Fact]
    public void Configured_duplication_creates_only_configured_extra_instances_with_provenance()
    {
        var request = new ArchitectureV6SemanticFixtureBuilder()
            .Project("p", "Project")
            .Node("a", "AService", "p")
            .Node("b", "BService", "p")
            .Node("shared", "SharedUtility", "p")
            .Link("l1", "a", "shared")
            .Link("l2", "b", "shared")
            .BuildRequest() with
        {
            NodeProjection = new NodeProjectionPolicy(NodeProjectionMode.DuplicateBranches, new[] { "*Utility" })
        };

        var projection = ArchitectureV6ProjectionStage.Build(request);
        var instances = projection.SemanticNodeToPhysicalNodeIds["shared"];

        Assert.Equal(2, instances.Count);
        var duplicate = projection.PhysicalNodes.Single(node => node.ProjectionMode == PhysicalNodeProjectionMode.DuplicateBranch);
        Assert.Equal("shared", duplicate.SemanticNodeId);
        Assert.NotNull(duplicate.DuplicationProvenance);
        Assert.Contains("Configured duplicate pattern", duplicate.DuplicationProvenance!.Reason, StringComparison.Ordinal);
        Assert.Equal(2, projection.PhysicalLinks.Count);
        Assert.All(projection.PhysicalLinks, link => Assert.Equal("shared", projection.PhysicalNodes.Single(node => node.PhysicalNodeId == link.DestinationPhysicalNodeId).SemanticNodeId));
    }

    [Fact]
    public void Role_resolution_is_case_insensitive_and_first_match_wins_by_configured_order()
    {
        var rules = new[]
        {
            new ArchitectureV6RoleRule("Service", "*Service", 0),
            new ArchitectureV6RoleRule("Processing", "*ProcessingService", 1)
        };

        Assert.Equal("Service", ArchitectureV6RoleResolver.Resolve("EmailPROCESSINGSERVICE", rules));
        Assert.Equal("Processing", ArchitectureV6RoleResolver.Resolve("EmailPROCESSINGSERVICE", new[]
        {
            new ArchitectureV6RoleRule("Processing", "*ProcessingService", 0),
            new ArchitectureV6RoleRule("Service", "*Service", 1)
        }));

        var request = new ArchitectureV6SemanticFixtureBuilder()
            .Project("p", "Project")
            .Node("processing", "EmailPROCESSINGSERVICE", "p")
            .BuildRequest(rules);
        var projection = ArchitectureV6ProjectionStage.Build(request);
        Assert.Equal("Service", projection.PhysicalNodes.Single().ResolvedRole);
    }

    [Fact]
    public void Span_sizing_uses_fixed_base_width_and_independent_label_and_terminal_requirements()
    {
        var fixture = new ArchitectureV6SemanticFixtureBuilder()
            .Project("p", "Project")
            .Node("wide", "VeryLongServiceName", "p")
            .Node("one", "OneService", "p")
            .Node("two", "TwoService", "p")
            .Node("three", "ThreeService", "p")
            .Node("four", "FourService", "p")
            .Link("l1", "wide", "one")
            .Link("l2", "wide", "two")
            .Link("l3", "wide", "three")
            .Link("l4", "wide", "four");
        var request = fixture.BuildRequest() with
        {
            GridSizing = new GridSizingPolicy(10, 20, 20, 30) { ConfiguredBaseCellWidth = 10 }
        };
        var projection = ArchitectureV6ProjectionStage.Build(request);
        var requirements = new ArchitectureV6PreRoutingSpanSizer(request, projection).Build();
        var wide = requirements.Single(item => item.PhysicalNodeId == "physical:wide");

        var expectedLabel = "VeryLongServiceName".Length * 8 + 16;
        var expectedInset = Math.Max(request.RoutePlanning.MinimumPortSpacing, request.GridSizing.NodeToRouteClearance);
        var expectedTop = 0;
        var expectedBottom = expectedInset * 2 + (4 - 1) * request.RoutePlanning.MinimumPortSpacing;
        var expectedRequired = Math.Max(request.NodePlacement.MinimumNodeWidth,
            Math.Max(expectedLabel, Math.Max(expectedTop, expectedBottom)));
        var expectedSpan = Math.Max(3, (int)Math.Ceiling(expectedRequired / 10d));
        if (expectedSpan % 2 == 0) expectedSpan++;

        Assert.Equal(expectedLabel, wide.LabelWidth);
        Assert.Equal(expectedTop, wide.TopTerminalWidth);
        Assert.Equal(expectedBottom, wide.BottomTerminalWidth);
        Assert.Equal(expectedRequired, wide.RequiredPhysicalWidth);
        Assert.Equal(expectedSpan, wide.RequiredSpan);
        Assert.True(wide.RequiredSpan >= 3 && wide.RequiredSpan % 2 == 1);
    }

    [Fact]
    public void Span_sizing_produces_literal_contract_spans_three_five_and_seven()
    {
        var builder = new ArchitectureV6SemanticFixtureBuilder().Project("p", "Project")
            .Node("label", "A", "p")
            .Node("top", "Top", "p")
            .Node("bottom", "Bottom", "p");
        for (var index = 0; index < 4; index++)
        {
            builder.Node("top-source-" + index, "S" + index, "p").Link("top-link-" + index, "top-source-" + index, "top");
        }
        for (var index = 0; index < 6; index++)
        {
            builder.Node("bottom-target-" + index, "T" + index, "p").Link("bottom-link-" + index, "bottom", "bottom-target-" + index);
        }

        var request = builder.BuildRequest() with
        {
            NodePlacement = new NodePlacementPolicy("*OrchestrationService", 20, 60, 20, 40),
            RoutePlanning = new RoutePlanningPolicy(10, 10, "[External]"),
            GridSizing = new GridSizingPolicy(10, 20, 20, 30)
            {
                ConfiguredBaseCellWidth = 10,
                NodeToRouteClearance = 10
            }
        };
        var projection = ArchitectureV6ProjectionStage.Build(request);
        var requirements = new ArchitectureV6PreRoutingSpanSizer(request, projection).Build();

        // Contract arithmetic, deliberately independent of ArchitectureV6TerminalCapacity.
        const int baseCellWidth = 10;
        const int configuredMinimum = 20;
        const int inset = 10;
        const int portSpacing = 10;
        var labelRequirement = "A".Length * 8 + 16;
        var topRequirement = inset * 2 + (4 - 1) * portSpacing;
        var bottomRequirement = inset * 2 + (6 - 1) * portSpacing;
        var expectedLabelWidth = Math.Max(configuredMinimum, Math.Max(labelRequirement, 0));
        var expectedTopWidth = Math.Max(configuredMinimum, Math.Max("Top".Length * 8 + 16, topRequirement));
        var expectedBottomWidth = Math.Max(configuredMinimum, Math.Max("Bottom".Length * 8 + 16, bottomRequirement));
        static int OddSpan(int width, int baseWidth)
        {
            var span = Math.Max(3, (int)Math.Ceiling(width / (double)baseWidth));
            return span % 2 == 0 ? span + 1 : span;
        }

        var label = requirements.Single(item => item.PhysicalNodeId == "physical:label");
        var top = requirements.Single(item => item.PhysicalNodeId == "physical:top");
        var bottom = requirements.Single(item => item.PhysicalNodeId == "physical:bottom");
        Assert.Equal(labelRequirement, label.LabelWidth);
        Assert.Equal(0, label.TopTerminalWidth);
        Assert.Equal(0, label.BottomTerminalWidth);
        Assert.Equal(configuredMinimum, label.ConfiguredMinimumWidth);
        Assert.Equal(baseCellWidth, request.GridSizing.ConfiguredBaseCellWidth);
        Assert.Equal(expectedLabelWidth, label.RequiredPhysicalWidth);
        Assert.Equal(3, OddSpan(expectedLabelWidth, baseCellWidth));
        Assert.Equal(3, label.RequiredSpan);

        Assert.Equal(topRequirement, top.TopTerminalWidth);
        Assert.Equal(0, top.BottomTerminalWidth);
        Assert.Equal(expectedTopWidth, top.RequiredPhysicalWidth);
        Assert.Equal(5, OddSpan(expectedTopWidth, baseCellWidth));
        Assert.Equal(5, top.RequiredSpan);

        Assert.Equal(0, bottom.TopTerminalWidth);
        Assert.Equal(bottomRequirement, bottom.BottomTerminalWidth);
        Assert.Equal(expectedBottomWidth, bottom.RequiredPhysicalWidth);
        Assert.Equal(7, OddSpan(expectedBottomWidth, baseCellWidth));
        Assert.Equal(7, bottom.RequiredSpan);
    }

    [Fact]
    public void Reservation_table_removes_zero_match_groups_and_reconciles_odd_rows_with_downstream_shifts()
    {
        var rules = new[]
        {
            new ArchitectureV6RoleRule("Aggregation", "*AggregationService", 0),
            new ArchitectureV6RoleRule("Coordination", "*CoordinationService", 1),
            new ArchitectureV6RoleRule("Processing", "*ProcessingService", 2)
        };
        var request = new ArchitectureV6SemanticFixtureBuilder()
            .Project("p", "Project")
            .Node("root", "RootProcessingService", "p")
            .Node("middle", "MiddleService", "p")
            .Node("deep", "DeepAggregationService", "p")
            .Link("l1", "root", "middle")
            .Link("l2", "middle", "deep")
            .BuildRequest(rules);
        var projection = ArchitectureV6ProjectionStage.Build(request);
        var planner = new ArchitectureV6ReservedDepthPlanner(request, projection);
        var requirements = planner.Inspect();
        var table = planner.BuildFrozenTable(requirements);

        Assert.DoesNotContain(requirements, item => item.ReservationName == "Coordination");
        Assert.Equal(new[] { "Aggregation", "Processing", "External" }, table.Reservations.Select(item => item.Name));
        Assert.Equal(new[] { 5, 7, 9 }, table.Reservations.Select(item => item.NodeRow));
        Assert.All(table.Reservations, item => Assert.True(item.NodeRow % 2 == 1));
        Assert.Equal(1, requirements.Single(item => item.ReservationName == "Aggregation").MatchCount);
        Assert.Equal(1, requirements.Single(item => item.ReservationName == "Processing").MatchCount);
    }

    [Fact]
    public void External_reservation_uses_the_maximum_required_depth_and_remains_last()
    {
        var builder = new ArchitectureV6SemanticFixtureBuilder().Project("p", "Project");
        builder.Node("n0", "N0Service", "p");
        builder.Node("n1", "N1Service", "p");
        builder.Node("n2", "N2Service", "p");
        builder.Node("n3", "N3Service", "p");
        builder.External("external", "IMetadataCache");
        builder.Link("l0", "n0", "n1").Link("l1", "n1", "n2").Link("l2", "n2", "n3").Link("l3", "n3", "external");
        var request = builder.BuildRequest(new[] { new ArchitectureV6RoleRule("Service", "*Service", 0) });
        var projection = ArchitectureV6ProjectionStage.Build(request);
        var planner = new ArchitectureV6ReservedDepthPlanner(request, projection);
        var table = planner.BuildFrozenTable(planner.Inspect());

        Assert.Equal("External", table.Reservations[table.Reservations.Count - 1].Name);
        Assert.Equal(9, table.External.NodeRow);
        Assert.True(table.External.NodeRow >= 1 && table.External.NodeRow % 2 == 1);
    }

    [Fact]
    public void Reservation_inspection_and_freeze_are_deterministic_on_repeat()
    {
        var request = new ArchitectureV6SemanticFixtureBuilder()
            .Project("p", "Project")
            .Node("a", "AProcessingService", "p")
            .Node("b", "BExternalService", "p")
            .External("e", "IExternal")
            .Link("l1", "a", "b")
            .Link("l2", "b", "e")
            .BuildRequest(new[] { new ArchitectureV6RoleRule("Processing", "*ProcessingService", 0) });
        var first = ArchitectureV6ProjectionStage.Build(request);
        var second = ArchitectureV6ProjectionStage.Build(request);
        var firstPlanner = new ArchitectureV6ReservedDepthPlanner(request, first);
        var secondPlanner = new ArchitectureV6ReservedDepthPlanner(request, second);
        var firstTable = firstPlanner.BuildFrozenTable(firstPlanner.Inspect());
        var secondTable = secondPlanner.BuildFrozenTable(secondPlanner.Inspect());

        Assert.Equal(firstTable.Reservations.Select(item => (item.Name, item.NodeRow, item.MatchCount)),
            secondTable.Reservations.Select(item => (item.Name, item.NodeRow, item.MatchCount)));
        Assert.Equal(
            firstPlanner.Inspect().Select(item => (item.ReservationName, item.MatchCount, item.RequiredNodeRow,
                string.Join(",", item.Constraints.Select(constraint => constraint.PhysicalNodeId)))),
            secondPlanner.Inspect().Select(item => (item.ReservationName, item.MatchCount, item.RequiredNodeRow,
                string.Join(",", item.Constraints.Select(constraint => constraint.PhysicalNodeId)))));
    }

    [Fact]
    public void Frozen_reservations_are_independent_of_parallel_constraint_completion_order()
    {
        var request = new ArchitectureV6SemanticFixtureBuilder().Project("p", "Project")
            .BuildRequest(new[]
            {
                new ArchitectureV6RoleRule("Aggregation", "*AggregationService", 0),
                new ArchitectureV6RoleRule("Processing", "*ProcessingService", 1)
            });
        var firstConstraints = new[]
        {
            new ArchitectureV6ReservedDepthConstraint("Aggregation", "a", 2, 5, false, "tree-a"),
            new ArchitectureV6ReservedDepthConstraint("Processing", "p", 0, 1, false, "tree-p")
        };
        var secondConstraints = new[] { firstConstraints[1], firstConstraints[0] };
        var firstRequirements = new[]
        {
            new ArchitectureV6ReservedDepthRequirement("Aggregation", 1, 5, new[] { firstConstraints[0] }),
            new ArchitectureV6ReservedDepthRequirement("Processing", 1, 1, new[] { firstConstraints[1] }),
            new ArchitectureV6ReservedDepthRequirement("External", 0, 1, Array.Empty<ArchitectureV6ReservedDepthConstraint>())
        };
        var secondRequirements = new[]
        {
            new ArchitectureV6ReservedDepthRequirement("Processing", 1, 1, new[] { secondConstraints[0] }),
            new ArchitectureV6ReservedDepthRequirement("External", 0, 1, Array.Empty<ArchitectureV6ReservedDepthConstraint>()),
            new ArchitectureV6ReservedDepthRequirement("Aggregation", 1, 5, new[] { secondConstraints[1] })
        };
        var planner = new ArchitectureV6ReservedDepthPlanner(request, ArchitectureV6ProjectionStage.Build(request));
        var first = planner.BuildFrozenTable(firstRequirements);
        var second = planner.BuildFrozenTable(secondRequirements);

        Assert.Equal(first.Reservations.Select(item => (item.Name, item.NodeRow, item.MatchCount)),
            second.Reservations.Select(item => (item.Name, item.NodeRow, item.MatchCount)));
    }

    [Fact]
    public void Reserved_depth_requirements_copy_constraint_inputs()
    {
        var source = new[] { new ArchitectureV6ReservedDepthConstraint("Service", "node", 0, 1, false, "test") };
        var requirement = new ArchitectureV6ReservedDepthRequirement("Service", 1, 1, source);
        source[0] = new ArchitectureV6ReservedDepthConstraint("Service", "changed", 4, 9, false, "changed");

        Assert.Equal("node", requirement.Constraints[0].PhysicalNodeId);
    }
}
