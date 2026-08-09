using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using StandardIo.ArchitectureDiagram.Core.Models.ArchitectureV7;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.ArchitectureV7;

public sealed class ArchitectureV7LogicalRelationshipRoutingStage
{
    public ArchitectureV7LogicalRouteFreeze Route(
        ArchitectureV7PlacementFreeze placement,
        ArchitectureV7PhysicalProjectionResult projection)
    {
        if (placement is null) throw new ArgumentNullException(nameof(placement));
        if (projection is null) throw new ArgumentNullException(nameof(projection));

        var nodePlacements = placement.Nodes.ToDictionary(node => node.PhysicalNodeId, StringComparer.Ordinal);
        var cells = placement.DiagramGrid.Cells.ToDictionary(cell => (cell.Row, cell.Column));
        var routes = new List<ArchitectureV7LogicalRoute>();
        var diagnostics = new List<ArchitectureV7RouteDiagnostic>();
        foreach (var link in projection.PhysicalLinks.OrderBy(link => link.PhysicalLinkId, StringComparer.Ordinal))
        {
            if (!nodePlacements.TryGetValue(link.SourcePhysicalNodeId, out var source) || !nodePlacements.TryGetValue(link.DestinationPhysicalNodeId, out var destination))
            {
                var diagnostic = Failure("MissingEndpoint", "A projected relationship endpoint is absent from the immutable placement freeze.", Array.Empty<ArchitectureV7RouteCell>());
                diagnostics.Add(diagnostic);
                routes.Add(new ArchitectureV7LogicalRoute(link.PhysicalLinkId, link.SemanticLinkId, link.SourcePhysicalNodeId, link.DestinationPhysicalNodeId,
                    Array.Empty<ArchitectureV7RouteCell>(), false, new[] { diagnostic }, "v7-common-diagram-router;missing-endpoint"));
                continue;
            }

            var start = new ArchitectureV7RouteCell(source.DiagramRow, source.CentreCell);
            var end = new ArchitectureV7RouteCell(destination.DiagramRow, destination.CentreCell);
            var route = start.Row + 2 == end.Row && start.Column == end.Column
                ? DirectChild(start, end, source, destination, cells)
                : General(start, end, source, destination, cells);
            var routeDiagnostics = route.Diagnostics;
            diagnostics.AddRange(routeDiagnostics);
            routes.Add(new ArchitectureV7LogicalRoute(link.PhysicalLinkId, link.SemanticLinkId, link.SourcePhysicalNodeId, link.DestinationPhysicalNodeId,
                route.Cells, route.IsComplete, routeDiagnostics, "v7-common-diagram-router;same-authority;cross-project=" + (!string.Equals(link.SourceProjectId, link.DestinationProjectId, StringComparison.Ordinal)).ToString().ToLowerInvariant()));
        }

        var fingerprintText = placement.PlacementFingerprint + "#" + projection.FreezeFingerprint + "#" + string.Join("|", routes.OrderBy(route => route.PhysicalLinkId, StringComparer.Ordinal).Select(route =>
            route.PhysicalLinkId + ":" + route.IsComplete + ":" + string.Join(",", route.Cells.Select(cell => cell.Row + ":" + cell.Column))));
        using var sha = SHA256.Create();
        var fingerprint = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(fingerprintText))).Replace("-", string.Empty);
        return new ArchitectureV7LogicalRouteFreeze(routes, diagnostics, placement.PlacementFingerprint, projection.FreezeFingerprint, fingerprint);

        static RouteAttempt DirectChild(ArchitectureV7RouteCell start, ArchitectureV7RouteCell end,
            ArchitectureV7FrozenNodePlacement source, ArchitectureV7FrozenNodePlacement destination,
            IReadOnlyDictionary<(int Row, int Column), ArchitectureV7LogicalCell> cells)
        {
            var path = new[] { start, new ArchitectureV7RouteCell(start.Row + 1, start.Column), end };
            return Validate(path, source, destination, cells, true) ? new RouteAttempt(path, true, Array.Empty<ArchitectureV7RouteDiagnostic>()) : Failed(path, "DirectChildBlocked", "The immediate child route is not legal on the frozen grid.");
        }

        static RouteAttempt General(ArchitectureV7RouteCell start, ArchitectureV7RouteCell end,
            ArchitectureV7FrozenNodePlacement source, ArchitectureV7FrozenNodePlacement destination,
            IReadOnlyDictionary<(int Row, int Column), ArchitectureV7LogicalCell> cells)
        {
            var path = new List<ArchitectureV7RouteCell> { start };
            if (!Append(path, new ArchitectureV7RouteCell(start.Row + 1, start.Column), source, destination, cells)) return Failed(path, "NoLegalRoute", "The router could not leave the source downward.");

            var currentRow = start.Row + 1;
            var currentColumn = start.Column;
            if (end.Row <= start.Row)
            {
                var escapes = FindEscapeColumns(start.Column, source.DiagramColumn, source.LogicalSpan, cells.Keys.Select(key => key.Column).DefaultIfEmpty(0).Max(), cells).ToArray();
                if (escapes.Length == 0 || !AppendHorizontal(path, currentRow, currentColumn, escapes[0], source, destination, cells)) return Failed(path, "NoLegalUpwardEscape", "The upward route could not escape the source footprint.");
                var escape = escapes[0];
                currentColumn = escape;
                while (currentRow > end.Row - 1)
                {
                    if (!Continue(path, ref currentRow, ref currentColumn, end.Column, -1, source, destination, cells)) return Failed(path, "NoLegalUpwardContinuation", "No legal upward continuation column exists.");
                }
            }
            else
            {
                while (currentRow < end.Row - 1)
                {
                    if (!Continue(path, ref currentRow, ref currentColumn, end.Column, 1, source, destination, cells)) return Failed(path, "NoLegalDownwardContinuation", "No legal downward continuation column exists.");
                }
            }

            if (!AppendHorizontal(path, currentRow, currentColumn, end.Column, source, destination, cells) || !Append(path, end, source, destination, cells))
                return Failed(path, "NoLegalDestinationApproach", "The destination cannot be approached and entered legally.");
            return Validate(path, source, destination, cells, true) ? new RouteAttempt(path, true, Array.Empty<ArchitectureV7RouteDiagnostic>()) : Failed(path, "IllegalRoute", "The constructed route failed frozen-cell legality evaluation.");
        }

        static bool Continue(List<ArchitectureV7RouteCell> path, ref int currentRow, ref int currentColumn, int intendedColumn, int direction,
            ArchitectureV7FrozenNodePlacement source, ArchitectureV7FrozenNodePlacement destination,
            IReadOnlyDictionary<(int Row, int Column), ArchitectureV7LogicalCell> cells)
        {
            foreach (var candidate in CandidateColumns(currentColumn, intendedColumn, cells.Keys.Select(key => key.Column).DefaultIfEmpty(0).Max()))
            {
                var trial = new List<ArchitectureV7RouteCell>(path);
                if (!AppendHorizontal(trial, currentRow, currentColumn, candidate, source, destination, cells)) continue;
                var nextRow = currentRow + direction * 2;
                if (!AppendVertical(trial, candidate, currentRow, nextRow, source, destination, cells)) continue;
                path.Clear();
                path.AddRange(trial);
                currentColumn = candidate;
                currentRow = nextRow;
                return true;
            }
            return false;
        }

        static IEnumerable<int> CandidateColumns(int current, int intended, int maxColumn)
        {
            yield return current;
            for (var distance = 2; distance <= maxColumn + 2; distance += 2)
            {
                if (current - distance >= 0) yield return current - distance;
                if (current + distance <= maxColumn) yield return current + distance;
            }
            if (intended >= 0 && intended <= maxColumn && intended != current) yield return intended;
        }

        static IEnumerable<int> FindEscapeColumns(int sourceCentre, int sourceLeft, int span, int maxColumn,
            IReadOnlyDictionary<(int Row, int Column), ArchitectureV7LogicalCell> cells)
        {
            for (var distance = 2; distance <= maxColumn + span + 2; distance += 2)
            {
                var left = sourceCentre - distance;
                var right = sourceCentre + distance;
                if (left >= 0 && left < sourceLeft) yield return left;
                if (right <= maxColumn && right >= sourceLeft + span) yield return right;
            }
        }

        static bool AppendHorizontal(List<ArchitectureV7RouteCell> path, int row, int from, int to,
            ArchitectureV7FrozenNodePlacement source, ArchitectureV7FrozenNodePlacement destination,
            IReadOnlyDictionary<(int Row, int Column), ArchitectureV7LogicalCell> cells)
        {
            var step = to >= from ? 1 : -1;
            for (var column = from + step; column != to + step; column += step)
                if (!Append(path, new ArchitectureV7RouteCell(row, column), source, destination, cells)) return false;
            return true;
        }

        static bool AppendVertical(List<ArchitectureV7RouteCell> path, int currentColumn, int currentRow, int targetRow,
            ArchitectureV7FrozenNodePlacement source, ArchitectureV7FrozenNodePlacement destination,
            IReadOnlyDictionary<(int Row, int Column), ArchitectureV7LogicalCell> cells)
        {
            var step = targetRow >= currentRow ? 1 : -1;
            for (var row = currentRow + step; row != targetRow + step; row += step)
                if (!Append(path, new ArchitectureV7RouteCell(row, currentColumn), source, destination, cells)) return false;
            return true;
        }

        static bool Append(List<ArchitectureV7RouteCell> path, ArchitectureV7RouteCell cell,
            ArchitectureV7FrozenNodePlacement source, ArchitectureV7FrozenNodePlacement destination,
            IReadOnlyDictionary<(int Row, int Column), ArchitectureV7LogicalCell> cells)
        {
            if (path.Count > 0 && Math.Abs(path[path.Count - 1].Row - cell.Row) + Math.Abs(path[path.Count - 1].Column - cell.Column) != 1) return false;
            path.Add(cell);
            var legal = Validate(path, source, destination, cells, false);
            if (!legal) path.RemoveAt(path.Count - 1);
            return legal;
        }

        static bool Validate(IReadOnlyList<ArchitectureV7RouteCell> path,
            ArchitectureV7FrozenNodePlacement source, ArchitectureV7FrozenNodePlacement destination,
            IReadOnlyDictionary<(int Row, int Column), ArchitectureV7LogicalCell> cells, bool requireDestination)
        {
            if (path.Count == 0 || path[0] != new ArchitectureV7RouteCell(source.DiagramRow, source.CentreCell)) return false;
            for (var index = 0; index < path.Count; index++)
            {
                if (!cells.TryGetValue((path[index].Row, path[index].Column), out var cell)) return false;
                var endpoint = index == 0 || index == path.Count - 1;
                if (!endpoint && cell.OccupantId is { } occupant && occupant != source.PhysicalNodeId && occupant != destination.PhysicalNodeId) return false;
                if (index == path.Count - 1) continue;
                var entry = index == 0 ? Direction.None : DirectionOf(path[index - 1], path[index]);
                var exit = DirectionOf(path[index], path[index + 1]);
                if (index > 0 && !Allows(cell.Capabilities, entry, exit)) return false;
                if (index >= 2 && path[index] == path[index - 2]) return false;
            }
            return !requireDestination || path[path.Count - 1] == new ArchitectureV7RouteCell(destination.DiagramRow, destination.CentreCell);
        }

        static bool Allows(ArchitectureV7CellCapability capability, Direction entry, Direction exit)
        {
            if (capability.HasFlag(ArchitectureV7CellCapability.HeaderBlocked)) return false;
            if (capability.HasFlag(ArchitectureV7CellCapability.GeneralRouting)) return true;
            if (capability.HasFlag(ArchitectureV7CellCapability.RoutingAllowed) && entry == exit && entry != Direction.None) return true;
            if (capability.HasFlag(ArchitectureV7CellCapability.NodeAllowed) && entry == exit && (entry == Direction.Up || entry == Direction.Down)) return true;
            if (capability.HasFlag(ArchitectureV7CellCapability.StraightPassthroughOnly) && entry == exit) return true;
            return false;
        }

        static Direction DirectionOf(ArchitectureV7RouteCell from, ArchitectureV7RouteCell to) =>
            to.Row == from.Row ? (to.Column > from.Column ? Direction.Right : Direction.Left) : (to.Row > from.Row ? Direction.Down : Direction.Up);

        static RouteAttempt Failed(IReadOnlyList<ArchitectureV7RouteCell> attempted, string code, string message) =>
            new RouteAttempt(attempted, false, new[] { Failure(code, message, attempted) });

        static ArchitectureV7RouteDiagnostic Failure(string code, string message, IReadOnlyList<ArchitectureV7RouteCell> attempted) =>
            new(code, message, true, attempted.ToArray());
    }

    private enum Direction { None, Up, Down, Left, Right }
    private sealed record RouteAttempt(IReadOnlyList<ArchitectureV7RouteCell> Cells, bool IsComplete, IReadOnlyList<ArchitectureV7RouteDiagnostic> Diagnostics);
}
