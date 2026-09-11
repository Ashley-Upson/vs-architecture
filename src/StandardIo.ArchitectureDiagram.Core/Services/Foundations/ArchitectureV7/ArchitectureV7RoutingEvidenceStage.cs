using System;
using System.Collections.Generic;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core.Models.ArchitectureV7;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.ArchitectureV7;

public sealed class ArchitectureV7RoutingEvidenceStage
{
    public IReadOnlyList<ArchitectureV7RelationshipRoutingEvidence> Analyze(ArchitectureV7PlacementFreeze placement, ArchitectureV7LogicalRouteFreeze routes)
    {
        var cells = placement.DiagramGrid.Cells.ToDictionary(x => (x.Row, x.Column));
        var nodes = placement.Nodes.ToDictionary(x => x.PhysicalNodeId, StringComparer.Ordinal);
        var result = new List<ArchitectureV7RelationshipRoutingEvidence>();
        foreach (var route in routes.Routes.Where(x => !x.IsComplete))
        {
            if (!nodes.TryGetValue(route.SourcePhysicalNodeId, out var source) || !nodes.TryGetValue(route.DestinationPhysicalNodeId, out var target)) continue;
            var sourceCell = new ArchitectureV7RouteCell(source.DiagramRow, source.CentreCell);
            var targetCell = new ArchitectureV7RouteCell(target.DiagramRow, target.CentreCell);
            foreach (var diagnostic in route.Diagnostics)
            {
                var attempted = diagnostic.AttemptedCells;
                var failure = attempted.Count == 0 ? null : attempted[attempted.Count - 1];
                var scenario = Scenario(diagnostic.Code, sourceCell, targetCell);
                var candidateSummary = scenario == "destination-approach"
                    ? AttemptCandidates(route.AttemptEvidence.FirstOrDefault(x => x.Scenario == "destination-approach"))
                    : scenario.StartsWith("upward", StringComparison.Ordinal) && scenario.Contains("escape", StringComparison.Ordinal)
                    ? AuthoritativeUpwardEscapeCandidates(route.AttemptEvidence.FirstOrDefault(x => x.Scenario == "upward-escape") ??
                        route.AttemptEvidence.LastOrDefault(x => x.Scenario == "general-routing-escape-row"))
                    : scenario.Contains("continuation", StringComparison.Ordinal)
                        ? AttemptCandidates(route.AttemptEvidence.LastOrDefault(x => x.Scenario == "continuation-selection"))
                        : EmptyCandidateSummary();
                result.Add(new ArchitectureV7RelationshipRoutingEvidence(route.SemanticLinkId, route.PhysicalLinkId, route.SourcePhysicalNodeId, route.DestinationPhysicalNodeId,
                    sourceCell, targetCell, scenario, attempted, failure, Direction(attempted, -1), Direction(attempted, 0),
                    failure is not null && cells.TryGetValue((failure.Row, failure.Column), out var failureLogical) ? failureLogical.Capabilities : null,
                    diagnostic.Message, failure?.Column, targetCell.Column, candidateSummary, EndpointClasses(attempted, source, target, cells),
                    "project=" + (source.ProjectId ?? "<none>") + ";source-tree=" + source.TreeId + ";target-tree=" + target.TreeId + ";grid=" + placement.PlacementFingerprint,
                    route.Diagnostics.Select(x => x.Code).Distinct(StringComparer.Ordinal).ToArray()));
            }
        }
        return result;
    }

    public IReadOnlyList<ArchitectureV7PlacementRoutingEvidence> Placement(ArchitectureV7PlacementFreeze placement)
    {
        var byTree = placement.Nodes.GroupBy(x => x.TreeId, StringComparer.Ordinal).ToDictionary(x => x.Key, x => x.ToArray(), StringComparer.Ordinal);
        return placement.Nodes.Select(node =>
        {
            var siblings = byTree[node.TreeId].Where(x => x.PhysicalNodeId != node.PhysicalNodeId).Select(x => x.PhysicalNodeId).OrderBy(x => x, StringComparer.Ordinal).ToArray();
            var project = placement.Transforms.FirstOrDefault(x => x.ProjectId == node.ProjectId);
            var localColumn = project is null ? node.DiagramColumn : node.DiagramColumn - project.InteriorOriginColumn;
            var localRow = project is null ? node.DiagramRow : node.DiagramRow - project.InteriorOriginRow;
            var bounds = $"row={node.DiagramRow};column={node.DiagramColumn};span={node.LogicalSpan}";
            return new ArchitectureV7PlacementRoutingEvidence(node.PhysicalNodeId, null, node.TreeId, node.IsDetached ? node.TreeId : null, node.ProjectId,
                node.CentreCell - localColumn, node.LogicalSpan, $"row={localRow};column={localColumn};span={node.LogicalSpan}", node.CentreCell, node.LogicalSpan, bounds,
                node.CentreCell, node.LogicalSpan, bounds, node.TreeId, siblings, $"project-origin={project?.InteriorOriginColumn},{project?.InteriorOriginRow}", node.Provenance);
        }).ToArray();
    }

    private static string Scenario(string code, ArchitectureV7RouteCell source, ArchitectureV7RouteCell target) => code switch
    {
        "DirectChildBlocked" => "direct-child",
        "NoLegalDownwardContinuation" => "downward-continuation",
        "NoLegalUpwardEscape" => "upward-escape",
        "NoLegalUpwardContinuation" => "upward-continuation",
        "NoLegalDestinationApproach" => "destination-approach",
        _ when target.Row > source.Row => "downward-route",
        _ => "upward-route"
    };

    private static ArchitectureV7RoutingCandidateSummary ContinuationCandidates(ArchitectureV7RouteCell? failure, int intended, int direction,
        IReadOnlyDictionary<(int Row, int Column), ArchitectureV7LogicalCell> cells, ArchitectureV7FrozenNodePlacement source, ArchitectureV7FrozenNodePlacement target)
    {
        if (failure is null) return EmptyCandidateSummary();
        var max = cells.Keys.Select(x => x.Column).DefaultIfEmpty(0).Max();
        var columns = new List<int> { failure.Column };
        if (failure.Column - 2 >= 0) columns.Add(failure.Column - 2);
        if (failure.Column + 2 <= max) columns.Add(failure.Column + 2);
        if (intended >= 0 && intended <= max && !columns.Contains(intended)) columns.Add(intended);
        return SummarizeCandidates(columns, failure.Column, column =>
        {
            var traversed = new List<ArchitectureV7RouteCell>();
            var reason = "accepted";
            var row = failure.Row;
            var step = column >= failure.Column ? 1 : -1;
            var next = row + direction * 2;
            for (var r = row + direction; r != next + direction; r += direction) traversed.Add(new ArchitectureV7RouteCell(r, column));
            foreach (var cell in traversed)
            {
                if (!cells.TryGetValue((cell.Row, cell.Column), out var logical)) { reason = "grid cell absent"; break; }
                if (logical.OccupantId is not null && logical.OccupantId != source.PhysicalNodeId && logical.OccupantId != target.PhysicalNodeId) { reason = "unrelated occupied node"; break; }
                if (!logical.Capabilities.HasFlag(ArchitectureV7CellCapability.RoutingAllowed)) { reason = "routing capability absent"; break; }
            }
            return new CandidateProbe(column, Math.Abs(column - failure.Column) == 2, reason == "accepted", reason, traversed);
        });
    }

    private static ArchitectureV7RoutingCandidateSummary AttemptCandidates(ArchitectureV7RouteAttemptEvidence? attempt)
    {
        if (attempt is null || attempt.Candidates.Count == 0) return EmptyCandidateSummary();
        var candidates = attempt.Candidates.Select(candidate => new ArchitectureV7RoutingCandidateEvidence(
            candidate.CandidateColumn,
            Math.Abs(candidate.CandidateColumn - (attempt.CurrentContinuationColumn ?? candidate.CandidateColumn)) == 2,
            candidate.Accepted,
            candidate.RejectionReason,
            candidate.TraversedCells.Count,
            candidate.TraversedCells)).ToArray();
        var origin = attempt.CurrentContinuationColumn ?? candidates[0].CandidateColumn;
        var selected = candidates.FirstOrDefault(candidate => candidate.Accepted);
        var histogram = candidates.GroupBy(candidate => candidate.RejectionReason, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var distances = candidates.GroupBy(candidate => Math.Abs(candidate.CandidateColumn - origin))
            .ToDictionary(group => group.Key, group => group.Count());
        return new ArchitectureV7RoutingCandidateSummary(
            candidates.Length, candidates[0].CandidateColumn, candidates[candidates.Length - 1].CandidateColumn,
            selected?.CandidateColumn, candidates.Min(candidate => Math.Abs(candidate.CandidateColumn - origin)),
            candidates.Max(candidate => Math.Abs(candidate.CandidateColumn - origin)), histogram, distances,
            candidates.Sum(candidate => candidate.TraversedCellCount), candidates.Max(candidate => candidate.TraversedCellCount),
            selected, selected is null ? candidates[candidates.Length - 1] : null,
            candidates.Take(RepresentativeCandidateLimit).ToArray(),
            candidates.Skip(Math.Max(0, candidates.Length - RepresentativeCandidateLimit)).ToArray(), candidates);
    }

    private static ArchitectureV7RoutingCandidateSummary AuthoritativeUpwardEscapeCandidates(ArchitectureV7RouteAttemptEvidence? attempt)
    {
        if (attempt is null || attempt.Candidates.Count == 0) return EmptyCandidateSummary();

        var origin = attempt.CurrentContinuationColumn ?? 0;
        var candidates = attempt.Candidates.Select(candidate => new ArchitectureV7RoutingCandidateEvidence(
            candidate.CandidateColumn,
            Math.Abs(candidate.CandidateColumn - origin) == 2,
            candidate.Accepted,
            candidate.RejectionReason,
            candidate.TraversedCells.Count,
            candidate.TraversedCells)).ToArray();
        var selected = candidates.FirstOrDefault(candidate => candidate.Accepted);
        var distances = candidates.GroupBy(candidate => Math.Abs(candidate.CandidateColumn - origin))
            .ToDictionary(group => group.Key, group => group.Count());
        var rejectionReasons = candidates.GroupBy(candidate => candidate.RejectionReason, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var first = candidates.Take(RepresentativeCandidateLimit).ToArray();
        var last = candidates.Skip(Math.Max(0, candidates.Length - RepresentativeCandidateLimit)).ToArray();
        return new ArchitectureV7RoutingCandidateSummary(
            candidates.Length,
            candidates[0].CandidateColumn,
            candidates[candidates.Length - 1].CandidateColumn,
            selected?.CandidateColumn,
            candidates.Min(candidate => Math.Abs(candidate.CandidateColumn - origin)),
            candidates.Max(candidate => Math.Abs(candidate.CandidateColumn - origin)),
            rejectionReasons,
            distances,
            candidates.Sum(candidate => candidate.TraversedCellCount),
            candidates.Max(candidate => candidate.TraversedCellCount),
            selected,
            selected is null ? candidates[candidates.Length - 1] : null,
            first,
            last,
            candidates);
    }

    private const int RepresentativeCandidateLimit = 4;

    private sealed record CandidateProbe(int Column, bool IsPlusOrMinusTwo, bool Accepted, string RejectionReason,
        IReadOnlyList<ArchitectureV7RouteCell> TraversedCells);

    private static ArchitectureV7RoutingCandidateSummary SummarizeCandidates(IEnumerable<int> columns, int origin,
        Func<int, CandidateProbe> probeFactory)
    {
        var first = new List<ArchitectureV7RoutingCandidateEvidence>(RepresentativeCandidateLimit);
        var last = new Queue<ArchitectureV7RoutingCandidateEvidence>(RepresentativeCandidateLimit);
        var all = new List<ArchitectureV7RoutingCandidateEvidence>();
        var rejectionReasons = new Dictionary<string, int>(StringComparer.Ordinal);
        var distances = new Dictionary<int, int>();
        // Probe evidence observes candidate legality; it does not own the
        // router's continuation selection. Never promote a probe that merely
        // looks acceptable to an authoritative selected candidate.
        ArchitectureV7RoutingCandidateEvidence? lastProbe = null;
        var total = 0;
        var totalTraversed = 0;
        var maximumTraversed = 0;
        var nearest = int.MaxValue;
        var farthest = 0;
        int? firstColumn = null;
        int? lastColumn = null;

        foreach (var column in columns)
        {
            var probe = probeFactory(column);
            var distance = Math.Abs(column - origin);
            var traversedCount = probe.TraversedCells.Count;
            var detailed = new ArchitectureV7RoutingCandidateEvidence(probe.Column, probe.IsPlusOrMinusTwo, probe.Accepted,
                probe.RejectionReason, traversedCount, probe.TraversedCells);
            all.Add(detailed);

            total++;
            firstColumn ??= column;
            lastColumn = column;
            totalTraversed += traversedCount;
            maximumTraversed = Math.Max(maximumTraversed, traversedCount);
            nearest = Math.Min(nearest, distance);
            farthest = Math.Max(farthest, distance);
            rejectionReasons[probe.RejectionReason] = rejectionReasons.TryGetValue(probe.RejectionReason, out var reasonCount) ? reasonCount + 1 : 1;
            distances[distance] = distances.TryGetValue(distance, out var distanceCount) ? distanceCount + 1 : 1;

            if (first.Count < RepresentativeCandidateLimit) first.Add(detailed);
            last.Enqueue(detailed);
            if (last.Count > RepresentativeCandidateLimit) last.Dequeue();
            lastProbe = detailed;
        }

        return new ArchitectureV7RoutingCandidateSummary(total, firstColumn, lastColumn, null,
            total == 0 ? null : nearest, total == 0 ? null : farthest,
            rejectionReasons, distances, totalTraversed, maximumTraversed, null,
            lastProbe, first.ToArray(), last.ToArray(), all.ToArray());
    }

    private static ArchitectureV7RoutingCandidateSummary EmptyCandidateSummary() => new(0, null, null, null, null, null,
        new Dictionary<string, int>(StringComparer.Ordinal), new Dictionary<int, int>(), 0, 0, null, null,
        Array.Empty<ArchitectureV7RoutingCandidateEvidence>(), Array.Empty<ArchitectureV7RoutingCandidateEvidence>());

    private static string? Direction(IReadOnlyList<ArchitectureV7RouteCell> cells, int offset)
    {
        if (cells.Count < 2) return null;
        var a = cells[cells.Count - 2]; var b = cells[cells.Count - 1];
        return b.Row == a.Row ? (b.Column > a.Column ? "right" : "left") : (b.Row > a.Row ? "down" : "up");
    }

    private static IReadOnlyList<string> EndpointClasses(IReadOnlyList<ArchitectureV7RouteCell> path, ArchitectureV7FrozenNodePlacement source,
        ArchitectureV7FrozenNodePlacement target, IReadOnlyDictionary<(int Row, int Column), ArchitectureV7LogicalCell> cells) => path.Select((cell, index) =>
        {
            var exists = cells.TryGetValue((cell.Row, cell.Column), out var logical);
            var occupant = logical?.OccupantId ?? "<none>";
            if (index == 0) return "source endpoint;legal=" + (exists && (logical!.OccupantId is null || logical.OccupantId == source.PhysicalNodeId)) + ";occupant=" + occupant;
            if (index == path.Count - 1) return "target endpoint;legal=" + (exists && (logical!.OccupantId is null || logical.OccupantId == target.PhysicalNodeId)) + ";occupant=" + occupant;
            return exists && logical!.OccupantId is not null && logical.OccupantId != source.PhysicalNodeId && logical.OccupantId != target.PhysicalNodeId ? "unrelated occupied node;occupant=" + occupant : "routing cell;capability=" + (exists ? logical!.Capabilities.ToString() : "<absent>");
        }).ToArray();
}
