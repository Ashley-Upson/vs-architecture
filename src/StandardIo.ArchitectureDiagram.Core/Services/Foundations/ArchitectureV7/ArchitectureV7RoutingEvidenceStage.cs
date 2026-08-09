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
                var candidates = scenario.StartsWith("upward", StringComparison.Ordinal) && scenario.Contains("escape", StringComparison.Ordinal)
                    ? UpwardEscapeCandidates(failure, source, target, cells)
                    : scenario.Contains("continuation", StringComparison.Ordinal)
                        ? ContinuationCandidates(failure, targetCell.Column, scenario.StartsWith("upward", StringComparison.Ordinal) ? -1 : 1, cells, source, target)
                        : Array.Empty<ArchitectureV7RoutingCandidateEvidence>();
                result.Add(new ArchitectureV7RelationshipRoutingEvidence(route.SemanticLinkId, route.PhysicalLinkId, route.SourcePhysicalNodeId, route.DestinationPhysicalNodeId,
                    sourceCell, targetCell, scenario, attempted, failure, Direction(attempted, -1), Direction(attempted, 0),
                    failure is not null && cells.TryGetValue((failure.Row, failure.Column), out var failureLogical) ? failureLogical.Capabilities : null,
                    diagnostic.Message, failure?.Column, targetCell.Column, candidates, EndpointClasses(attempted, source, target, cells),
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

    private static IReadOnlyList<ArchitectureV7RoutingCandidateEvidence> ContinuationCandidates(ArchitectureV7RouteCell? failure, int intended, int direction,
        IReadOnlyDictionary<(int Row, int Column), ArchitectureV7LogicalCell> cells, ArchitectureV7FrozenNodePlacement source, ArchitectureV7FrozenNodePlacement target)
    {
        if (failure is null) return Array.Empty<ArchitectureV7RoutingCandidateEvidence>();
        var max = cells.Keys.Select(x => x.Column).DefaultIfEmpty(0).Max();
        var columns = new List<int> { failure.Column };
        if (failure.Column - 2 >= 0) columns.Add(failure.Column - 2);
        if (failure.Column + 2 <= max) columns.Add(failure.Column + 2);
        if (intended >= 0 && intended <= max && !columns.Contains(intended)) columns.Add(intended);
        return columns.Select(column =>
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
            return new ArchitectureV7RoutingCandidateEvidence(column, Math.Abs(column - failure.Column) == 2, reason == "accepted", reason, traversed);
        }).ToArray();
    }

    private static IReadOnlyList<ArchitectureV7RoutingCandidateEvidence> UpwardEscapeCandidates(ArchitectureV7RouteCell? failure,
        ArchitectureV7FrozenNodePlacement source, ArchitectureV7FrozenNodePlacement target,
        IReadOnlyDictionary<(int Row, int Column), ArchitectureV7LogicalCell> cells)
    {
        if (failure is null) return Array.Empty<ArchitectureV7RoutingCandidateEvidence>();
        var max = cells.Keys.Select(x => x.Column).DefaultIfEmpty(0).Max();
        var candidates = new List<int>();
        for (var distance = 2; distance <= max + source.LogicalSpan + 2; distance += 2)
        {
            candidates.Add(source.CentreCell - distance);
            candidates.Add(source.CentreCell + distance);
        }

        return candidates.Select(column =>
        {
            var traversed = new List<ArchitectureV7RouteCell>();
            var reason = "accepted";
            var step = column >= failure.Column ? 1 : -1;
            for (var current = failure.Column + step; current != column + step; current += step)
                traversed.Add(new ArchitectureV7RouteCell(failure.Row, current));
            traversed.Add(new ArchitectureV7RouteCell(failure.Row - 1, column));
            traversed.Add(new ArchitectureV7RouteCell(failure.Row - 2, column));
            var outside = column < source.DiagramColumn || column >= source.DiagramColumn + source.LogicalSpan;
            if (!outside) reason = "inside source footprint";
            foreach (var cell in traversed)
            {
                if (!cells.TryGetValue((cell.Row, cell.Column), out var logical)) { reason = "grid cell absent"; break; }
                if (logical.OccupantId is not null && logical.OccupantId != source.PhysicalNodeId && logical.OccupantId != target.PhysicalNodeId) { reason = "unrelated occupied node"; break; }
                if (!logical.Capabilities.HasFlag(ArchitectureV7CellCapability.RoutingAllowed)) { reason = "routing capability absent"; break; }
            }
            return new ArchitectureV7RoutingCandidateEvidence(column, Math.Abs(column - failure.Column) == 2,
                reason == "accepted", reason, traversed);
        }).ToArray();
    }

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
