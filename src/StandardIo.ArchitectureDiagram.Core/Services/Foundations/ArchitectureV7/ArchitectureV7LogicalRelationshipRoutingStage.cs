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
                route.Cells, route.IsComplete, routeDiagnostics, "v7-common-diagram-router;same-authority;cross-project=" + (!string.Equals(link.SourceProjectId, link.DestinationProjectId, StringComparison.Ordinal)).ToString().ToLowerInvariant(), operationMetrics: route.OperationMetrics));
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
            return Validate(path, source, destination, cells, true) ? new RouteAttempt(path, true, Array.Empty<ArchitectureV7RouteDiagnostic>(), new ArchitectureV7RoutingOperationMetrics(0, 0)) : Failed(path, "DirectChildBlocked", "The immediate child route is not legal on the frozen grid.");
        }

        static RouteAttempt General(ArchitectureV7RouteCell start, ArchitectureV7RouteCell end,
            ArchitectureV7FrozenNodePlacement source, ArchitectureV7FrozenNodePlacement destination,
            IReadOnlyDictionary<(int Row, int Column), ArchitectureV7LogicalCell> cells)
        {
            var path = new List<ArchitectureV7RouteCell> { start };
            var metrics = new RouteOperationMetrics();
            if (!Append(path, new ArchitectureV7RouteCell(start.Row + 1, start.Column), source, destination, cells)) return Failed(path, "NoLegalRoute", "The router could not leave the source downward.", metrics);

            var currentRow = start.Row + 1;
            var currentColumn = start.Column;
            if (end.Row <= start.Row)
            {
                var maxColumn = cells.Keys.Select(key => key.Column).DefaultIfEmpty(0).Max();
                if (!TryEscape(path, ref currentColumn, currentRow, source, destination, cells, maxColumn, metrics)) return Failed(path, "NoLegalUpwardEscape", "The upward route could not escape the source footprint.", metrics);
                currentRow -= 2;
                while (currentRow > end.Row - 1)
                {
                    if (!Continue(path, ref currentRow, ref currentColumn, -1, source, destination, cells, metrics)) return Failed(path, "NoLegalUpwardContinuation", "No legal upward continuation column exists.", metrics);
                }
            }
            else
            {
                while (currentRow < end.Row - 1)
                {
                    if (!Continue(path, ref currentRow, ref currentColumn, 1, source, destination, cells, metrics)) return Failed(path, "NoLegalDownwardContinuation", "No legal downward continuation column exists.", metrics);
                }
            }

            if (!AppendHorizontal(path, currentRow, currentColumn, end.Column, source, destination, cells) || !Append(path, end, source, destination, cells))
                return Failed(path, "NoLegalDestinationApproach", "The destination cannot be approached and entered legally.", metrics);
            return Validate(path, source, destination, cells, true) ? new RouteAttempt(path, true, Array.Empty<ArchitectureV7RouteDiagnostic>(), metrics.Freeze()) : Failed(path, "IllegalRoute", "The constructed route failed frozen-cell legality evaluation.", metrics);
        }

        static bool Continue(List<ArchitectureV7RouteCell> path, ref int currentRow, ref int currentColumn, int direction,
            ArchitectureV7FrozenNodePlacement source, ArchitectureV7FrozenNodePlacement destination,
            IReadOnlyDictionary<(int Row, int Column), ArchitectureV7LogicalCell> cells, RouteOperationMetrics metrics)
        {
            var maxColumn = cells.Keys.Select(key => key.Column).DefaultIfEmpty(0).Max();
            if (TryContinuation(path, ref currentColumn, currentRow, direction, source, destination, cells, maxColumn, metrics))
            {
                currentRow += direction * 2;
                return true;
            }
            return false;
        }

        static bool TryContinuation(List<ArchitectureV7RouteCell> path, ref int currentColumn, int currentRow, int direction,
            ArchitectureV7FrozenNodePlacement source, ArchitectureV7FrozenNodePlacement destination,
            IReadOnlyDictionary<(int Row, int Column), ArchitectureV7LogicalCell> cells, int maxColumn, RouteOperationMetrics metrics)
        {
            if (TrySelectContinuation(path, currentColumn, currentRow, direction, source, destination, cells, maxColumn, metrics, out var candidate))
            {
                if (!AppendContinuation(path, currentColumn, currentRow, candidate, direction, source, destination, cells)) return false;
                currentColumn = candidate;
                return true;
            }
            return false;
        }

        static bool TrySelectContinuation(IReadOnlyList<ArchitectureV7RouteCell> path, int currentColumn, int currentRow, int direction,
            ArchitectureV7FrozenNodePlacement source, ArchitectureV7FrozenNodePlacement destination,
            IReadOnlyDictionary<(int Row, int Column), ArchitectureV7LogicalCell> cells, int maxColumn, RouteOperationMetrics metrics, out int candidate)
        {
            metrics.ContinuationCandidatesEvaluated++;
            if (CanContinueAtColumn(path, currentColumn, currentRow, currentColumn, direction, source, destination, cells))
            {
                candidate = currentColumn;
                return true;
            }

            for (var distance = 2; ; distance += 2)
            {
                var left = currentColumn - distance;
                var right = currentColumn + distance;
                var evaluated = false;
                if (left >= 0)
                {
                    evaluated = true;
                    metrics.ContinuationCandidatesEvaluated++;
                    if (CanContinueAtColumn(path, currentColumn, currentRow, left, direction, source, destination, cells))
                    {
                        candidate = left;
                        return true;
                    }
                }
                if (right <= maxColumn)
                {
                    evaluated = true;
                    metrics.ContinuationCandidatesEvaluated++;
                    if (CanContinueAtColumn(path, currentColumn, currentRow, right, direction, source, destination, cells))
                    {
                        candidate = right;
                        return true;
                    }
                }
                if (!evaluated)
                {
                    candidate = currentColumn;
                    return false;
                }
            }
        }

        static bool CanContinueAtColumn(IReadOnlyList<ArchitectureV7RouteCell> path, int currentColumn, int currentRow, int candidate,
            int direction, ArchitectureV7FrozenNodePlacement source, ArchitectureV7FrozenNodePlacement destination,
            IReadOnlyDictionary<(int Row, int Column), ArchitectureV7LogicalCell> cells)
        {
            var previous = path.Count > 1 ? path[path.Count - 2] : path[path.Count - 1];
            var current = path[path.Count - 1];
            if (!cells.TryGetValue((current.Row, current.Column), out var originCell)) return false;
            if (!cells.TryGetValue((currentRow, candidate), out var continuationCell)) return false;
            if (!CanEnterCell(currentRow, candidate, source, destination, cells)) return false;
            var entry = candidate == currentColumn ? DirectionOf(previous, current) : candidate > currentColumn ? Direction.Right : Direction.Left;
            if (!Allows(continuationCell.Capabilities, entry, DirectionFor(direction))) return false;
            if (candidate != currentColumn && !originCell.Capabilities.HasFlag(ArchitectureV7CellCapability.GeneralRouting) &&
                !CanReachRestrictedHorizontalCorridor(path, currentColumn, currentRow, candidate, source, destination, cells)) return false;
            var middleRow = currentRow + direction;
            var nextRow = currentRow + direction * 2;
            if (!CanEnterCell(middleRow, candidate, source, destination, cells)) return false;
            if (!CanEnterCell(nextRow, candidate, source, destination, cells)) return false;

            if (!cells.TryGetValue((middleRow, candidate), out var middleCell) || !Allows(middleCell.Capabilities, DirectionFor(direction), DirectionFor(direction))) return false;
            return true;
        }

        static bool CanReachRestrictedHorizontalCorridor(IReadOnlyList<ArchitectureV7RouteCell> path, int currentColumn, int currentRow, int candidate,
            ArchitectureV7FrozenNodePlacement source, ArchitectureV7FrozenNodePlacement destination,
            IReadOnlyDictionary<(int Row, int Column), ArchitectureV7LogicalCell> cells)
        {
            var previous = path.Count > 1 ? path[path.Count - 2] : path[path.Count - 1];
            var current = path[path.Count - 1];
            var horizontalStep = candidate >= currentColumn ? 1 : -1;
            for (var column = currentColumn + horizontalStep; column != candidate; column += horizontalStep)
            {
                var next = new ArchitectureV7RouteCell(currentRow, column);
                if (!CanEnterCell(next.Row, next.Column, source, destination, cells)) return false;
                if (!cells.TryGetValue((current.Row, current.Column), out var currentCell)) return false;
                if (!Allows(currentCell.Capabilities, DirectionOf(previous, current), DirectionOf(current, next))) return false;
                previous = current;
                current = next;
            }
            return true;
        }

        static bool AppendContinuation(List<ArchitectureV7RouteCell> path, int currentColumn, int currentRow, int candidate,
            int direction, ArchitectureV7FrozenNodePlacement source, ArchitectureV7FrozenNodePlacement destination,
            IReadOnlyDictionary<(int Row, int Column), ArchitectureV7LogicalCell> cells)
        {
            var segment = new List<ArchitectureV7RouteCell>();
            var horizontalStep = candidate >= currentColumn ? 1 : -1;
            for (var column = currentColumn + horizontalStep; column != candidate + horizontalStep; column += horizontalStep)
                segment.Add(new ArchitectureV7RouteCell(currentRow, column));
            var nextRow = currentRow + direction * 2;
            segment.Add(new ArchitectureV7RouteCell(currentRow + direction, candidate));
            segment.Add(new ArchitectureV7RouteCell(nextRow, candidate));
            if (!CanAppendSegment(path, segment, source, destination, cells)) return false;
            path.AddRange(segment);
            return true;
        }

        static bool CanEnterCell(int row, int column, ArchitectureV7FrozenNodePlacement source, ArchitectureV7FrozenNodePlacement destination,
            IReadOnlyDictionary<(int Row, int Column), ArchitectureV7LogicalCell> cells) =>
            cells.TryGetValue((row, column), out var cell) &&
            (cell.OccupantId is null || cell.OccupantId == source.PhysicalNodeId || cell.OccupantId == destination.PhysicalNodeId);

        static bool TryEscape(List<ArchitectureV7RouteCell> path, ref int currentColumn, int currentRow,
            ArchitectureV7FrozenNodePlacement source, ArchitectureV7FrozenNodePlacement destination,
            IReadOnlyDictionary<(int Row, int Column), ArchitectureV7LogicalCell> cells, int maxColumn, RouteOperationMetrics metrics)
        {
            var sourceCentre = source.CentreCell;
            for (var distance = 2; ; distance += 2)
            {
                var left = sourceCentre - distance;
                var right = sourceCentre + distance;
                var evaluated = false;
                if (left >= 0 && left < source.DiagramColumn)
                {
                    evaluated = true;
                    metrics.UpwardEscapeCandidatesEvaluated++;
                    if (CanContinueAtColumn(path, currentColumn, currentRow, left, -1, source, destination, cells))
                    {
                        if (!AppendContinuation(path, currentColumn, currentRow, left, -1, source, destination, cells)) return false;
                        currentColumn = left;
                        return true;
                    }
                }
                if (right <= maxColumn && right >= source.DiagramColumn + source.LogicalSpan)
                {
                    evaluated = true;
                    metrics.UpwardEscapeCandidatesEvaluated++;
                    if (CanContinueAtColumn(path, currentColumn, currentRow, right, -1, source, destination, cells))
                    {
                        if (!AppendContinuation(path, currentColumn, currentRow, right, -1, source, destination, cells)) return false;
                        currentColumn = right;
                        return true;
                    }
                }
                if (!evaluated) return false;
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

        static bool Append(List<ArchitectureV7RouteCell> path, ArchitectureV7RouteCell cell,
            ArchitectureV7FrozenNodePlacement source, ArchitectureV7FrozenNodePlacement destination,
            IReadOnlyDictionary<(int Row, int Column), ArchitectureV7LogicalCell> cells)
        {
            if (path.Count > 0 && Math.Abs(path[path.Count - 1].Row - cell.Row) + Math.Abs(path[path.Count - 1].Column - cell.Column) != 1) return false;
            path.Add(cell);
            if (!IsIncrementallyLegal(path, cell, source, destination, cells)) { path.RemoveAt(path.Count - 1); return false; }
            return true;
        }

        static bool CanAppendSegment(IReadOnlyList<ArchitectureV7RouteCell> path, IReadOnlyList<ArchitectureV7RouteCell> segment,
            ArchitectureV7FrozenNodePlacement source, ArchitectureV7FrozenNodePlacement destination,
            IReadOnlyDictionary<(int Row, int Column), ArchitectureV7LogicalCell> cells)
        {
            if (segment.Count == 0 || path.Count == 0) return false;
            var previous = path.Count > 1 ? path[path.Count - 2] : path[path.Count - 1];
            var current = path[path.Count - 1];
            foreach (var cell in segment)
            {
                if (Math.Abs(current.Row - cell.Row) + Math.Abs(current.Column - cell.Column) != 1) return false;
                if (!cells.TryGetValue((cell.Row, cell.Column), out var destinationCell)) return false;
                if (destinationCell.OccupantId is { } occupant && occupant != source.PhysicalNodeId && occupant != destination.PhysicalNodeId) return false;
                if (path.Count > 1 || current != path[0])
                {
                    if (!cells.TryGetValue((current.Row, current.Column), out var currentCell)) return false;
                    if (!Allows(currentCell.Capabilities, DirectionOf(previous, current), DirectionOf(current, cell))) return false;
                }
                previous = current;
                current = cell;
            }
            return true;
        }

        static bool IsIncrementallyLegal(IReadOnlyList<ArchitectureV7RouteCell> path, ArchitectureV7RouteCell cell,
            ArchitectureV7FrozenNodePlacement source, ArchitectureV7FrozenNodePlacement destination,
            IReadOnlyDictionary<(int Row, int Column), ArchitectureV7LogicalCell> cells)
        {
            if (!cells.TryGetValue((cell.Row, cell.Column), out var destinationCell)) return false;
            var isDestination = cell == new ArchitectureV7RouteCell(destination.DiagramRow, destination.CentreCell);
            if (!isDestination && destinationCell.OccupantId is { } occupant && occupant != source.PhysicalNodeId) return false;
            if (path.Count < 3) return true;
            var previous = path[path.Count - 3];
            var current = path[path.Count - 2];
            if (!cells.TryGetValue((current.Row, current.Column), out var currentCell)) return false;
            var entry = DirectionOf(previous, current);
            var exit = DirectionOf(current, cell);
            return Allows(currentCell.Capabilities, entry, exit);
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

        static Direction DirectionFor(int direction) => direction > 0 ? Direction.Down : Direction.Up;

        static RouteAttempt Failed(IReadOnlyList<ArchitectureV7RouteCell> attempted, string code, string message, RouteOperationMetrics? metrics = null) =>
            new RouteAttempt(attempted, false, new[] { Failure(code, message, attempted) }, metrics?.Freeze() ?? new ArchitectureV7RoutingOperationMetrics(0, 0));

        static ArchitectureV7RouteDiagnostic Failure(string code, string message, IReadOnlyList<ArchitectureV7RouteCell> attempted) =>
            new(code, message, true, attempted.ToArray());
    }

    private enum Direction { None, Up, Down, Left, Right }
    private sealed class RouteOperationMetrics
    {
        public int ContinuationCandidatesEvaluated { get; set; }
        public int UpwardEscapeCandidatesEvaluated { get; set; }
        public ArchitectureV7RoutingOperationMetrics Freeze() => new(ContinuationCandidatesEvaluated, UpwardEscapeCandidatesEvaluated);
    }

    private sealed record RouteAttempt(IReadOnlyList<ArchitectureV7RouteCell> Cells, bool IsComplete, IReadOnlyList<ArchitectureV7RouteDiagnostic> Diagnostics, ArchitectureV7RoutingOperationMetrics OperationMetrics);
}
