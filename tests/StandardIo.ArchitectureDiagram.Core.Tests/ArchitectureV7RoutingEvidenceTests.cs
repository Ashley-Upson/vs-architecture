using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using StandardIo.ArchitectureDiagram.Core.Models.ArchitectureV7;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.ArchitectureV7;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core.Tests;

public sealed class ArchitectureV7RoutingEvidenceTests
{
    [Fact]
    public void Failed_upward_escape_evidence_retains_context_without_serializing_every_probe_cell()
    {
        const int width = 867;
        var projection = Projection(
            PhysicalNode("source", 5, 500, 3),
            PhysicalNode("target", 1, 700, 1),
            Link("failed-upward", "source", "target"));
        var route = new ArchitectureV7LogicalRelationshipRoutingStage().Route(
            Freeze(new[] { PlacementNode("source", 5, 500, 3), PlacementNode("target", 1, 700) }, BlockedEscapeGrid(9, width, 500)), projection);

        var evidence = new ArchitectureV7RoutingEvidenceStage().Analyze(
            Freeze(new[] { PlacementNode("source", 5, 500, 3), PlacementNode("target", 1, 700) }, BlockedEscapeGrid(9, width, 500)), route);
        var item = Assert.Single(evidence);
        var authoritative = Assert.Single(route.Routes).AttemptEvidence.Single(attempt => attempt.Scenario == "upward-escape");
        Assert.Equal("upward-escape", item.Scenario);
        Assert.Equal(authoritative.Candidates.Count, item.CandidateSummary.TotalCandidateCount);
        Assert.Equal(authoritative.Candidates[0].CandidateColumn, item.CandidateSummary.FirstCandidateExamined);
        Assert.Equal(authoritative.Candidates[authoritative.Candidates.Count - 1].CandidateColumn, item.CandidateSummary.LastCandidateExamined);
        Assert.NotNull(item.CandidateSummary.FirstCandidateExamined);
        Assert.NotNull(item.CandidateSummary.LastCandidateExamined);
        Assert.NotEmpty(item.CandidateSummary.RejectionReasonHistogram);
        Assert.NotEmpty(item.CandidateSummary.CandidateDistanceHistogram);
        Assert.True(item.CandidateSummary.TotalTraversedCellCount > item.CandidateSummary.TotalCandidateCount);
        Assert.NotEmpty(item.CandidateSummary.RepresentativeFirstCandidates);
        Assert.NotEmpty(item.CandidateSummary.RepresentativeLastCandidates);
        Assert.NotEmpty(item.CandidateSummary.FinalFailedCandidateEvidence!.TraversedCells);

        var json = JsonSerializer.Serialize(evidence);
        Assert.True(json.Length < 2_000 * item.CandidateSummary.TotalCandidateCount,
            $"Evidence JSON was {json.Length} characters for {item.CandidateSummary.TotalCandidateCount} candidates.");
    }

    [Fact]
    public void Evidence_does_not_change_authoritative_route_results_or_fingerprints()
    {
        var projection = Projection(
            PhysicalNode("source", 5, 20, 3),
            PhysicalNode("target", 1, 40, 1),
            Link("failed-upward", "source", "target"));
        var nodes = new[] { PlacementNode("source", 5, 20, 3), PlacementNode("target", 1, 40) };
        var grid = BlockedEscapeGrid(9, 61, 20);
        var withoutEvidence = new ArchitectureV7LogicalRelationshipRoutingStage().Route(Freeze(nodes, grid), projection);
        _ = new ArchitectureV7RoutingEvidenceStage().Analyze(Freeze(nodes, grid), withoutEvidence);
        var withEvidence = new ArchitectureV7LogicalRelationshipRoutingStage().Route(Freeze(nodes, grid), projection);

        Assert.Equal(withoutEvidence.RouteFingerprint, withEvidence.RouteFingerprint);
        Assert.Equal(withoutEvidence.Routes.Select(route => route.IsComplete), withEvidence.Routes.Select(route => route.IsComplete));
        Assert.Equal(withoutEvidence.Routes.Select(route => route.Cells), withEvidence.Routes.Select(route => route.Cells));
    }

    private static IReadOnlyList<ArchitectureV7LogicalCell> BlockedEscapeGrid(int rows, int columns, int sourceCentre)
    {
        var cells = Grid(rows, columns).ToDictionary(cell => (cell.Row, cell.Column));
        foreach (var column in Enumerable.Range(0, columns))
            cells[(6, column)] = cells[(6, column)] with
            {
                Capabilities = column == sourceCentre ? ArchitectureV7CellCapability.RoutingAllowed | ArchitectureV7CellCapability.GeneralRouting : ArchitectureV7CellCapability.HeaderBlocked
            };
        return cells.Values.ToArray();
    }

    private static ArchitectureV7FrozenNodePlacement PlacementNode(string id, int row, int column, int span = 1) =>
        new("physical:" + id, id, "p", row, column, span, column + (span - 1) / 2,
            Enumerable.Range(column, span).Select(value => (row, value)).ToArray(), false, false, false, "tree", id, "P." + id, "test");

    private static ArchitectureV7PhysicalNode PhysicalNode(string id, int row, int column, int span = 1) =>
        new("physical:" + id, id, "p", false, false, id, "P." + id, "Class", ArchitectureV7ProjectionMode.Canonical, null);

    private static ArchitectureV7PhysicalLink Link(string id, string source, string target) =>
        new(id, id, "physical:" + source, "physical:" + target, "p", "p", "dependency");

    private static ArchitectureV7PhysicalProjectionResult Projection(ArchitectureV7PhysicalNode source,
        ArchitectureV7PhysicalNode target, ArchitectureV7PhysicalLink link) =>
        new(new[] { source, target }, new[] { link }, new Dictionary<string, IReadOnlyList<string>>(),
            new Dictionary<string, IReadOnlyList<string>>(), Array.Empty<string>(), Array.Empty<string>(),
            Array.Empty<ArchitectureV7ProjectionDiagnostic>(), "projection");

    private static ArchitectureV7PlacementFreeze Freeze(IReadOnlyList<ArchitectureV7FrozenNodePlacement> nodes,
        IReadOnlyList<ArchitectureV7LogicalCell> cells)
    {
        var occupied = cells.ToDictionary(cell => (cell.Row, cell.Column), cell => cell);
        foreach (var node in nodes)
            foreach (var footprint in node.LogicalFootprint)
                if (occupied.TryGetValue(footprint, out var cell)) occupied[footprint] = cell with { OccupantId = node.PhysicalNodeId };
        return new(nodes, Array.Empty<ArchitectureV7ProjectRegion>(),
            new ArchitectureV7ExternalRegion(0, Array.Empty<string>(), Array.Empty<ArchitectureV7FrozenNodePlacement>()),
            new ArchitectureV7StandaloneRegion(0, 0, 0, Array.Empty<string>(), Array.Empty<ArchitectureV7FrozenNodePlacement>()),
            new ArchitectureV7CommonDiagramGrid(cells.Max(cell => cell.Row) + 1, cells.Max(cell => cell.Column) + 1, occupied.Values.ToArray()),
            Array.Empty<ArchitectureV7ProjectTransform>(), "projection", "ownership", "sizing", "reservation", "placement");
    }

    private static IReadOnlyList<ArchitectureV7LogicalCell> Grid(int rows, int columns)
    {
        return Enumerable.Range(0, rows).SelectMany(row => Enumerable.Range(0, columns).Select(column =>
            new ArchitectureV7LogicalCell(row, column, ArchitectureV7CellCapability.RoutingAllowed | ArchitectureV7CellCapability.GeneralRouting))).ToArray();
    }
}
