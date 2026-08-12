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
            if (route.Cells.Zip(route.Cells.Skip(1), (a, b) => (a, b))
                .Any(pair => pair.a.Row != pair.b.Row && pair.a.Column != pair.b.Column))
            {
                route = Failed(route.Cells, "DiagonalLogicalTransition", "A frozen logical route changed row and column in one transition.");
            }
            var routeDiagnostics = route.Diagnostics;
            diagnostics.AddRange(routeDiagnostics);
            routes.Add(new ArchitectureV7LogicalRoute(link.PhysicalLinkId, link.SemanticLinkId, link.SourcePhysicalNodeId, link.DestinationPhysicalNodeId,
                route.Cells, route.IsComplete, routeDiagnostics, "v7-common-diagram-router;same-authority;cross-project=" + (!string.Equals(link.SourceProjectId, link.DestinationProjectId, StringComparison.Ordinal)).ToString().ToLowerInvariant(), route.AttemptEvidence, route.OperationMetrics));
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
            return Validate(path, source, destination, cells, true) ? new RouteAttempt(path, true, Array.Empty<ArchitectureV7RouteDiagnostic>(), Array.Empty<ArchitectureV7RouteAttemptEvidence>(), new ArchitectureV7RoutingOperationMetrics(0, 0)) : Failed(path, "DirectChildBlocked", "The immediate child route is not legal on the frozen grid.");
        }

        static RouteAttempt General(ArchitectureV7RouteCell start, ArchitectureV7RouteCell end,
            ArchitectureV7FrozenNodePlacement source, ArchitectureV7FrozenNodePlacement destination,
            IReadOnlyDictionary<(int Row, int Column), ArchitectureV7LogicalCell> cells)
        {
            var path = new List<ArchitectureV7RouteCell> { start };
            var metrics = new RouteOperationMetrics();
            var attemptEvidence = new List<ArchitectureV7RouteAttemptEvidence>();
            if (!Append(path, new ArchitectureV7RouteCell(start.Row + 1, start.Column), source, destination, cells)) return Failed(path, "NoLegalRoute", "The router could not leave the source downward.", metrics);

            var currentRow = start.Row + 1;
            var currentColumn = start.Column;
            if (end.Row <= start.Row)
            {
                if (!AdvanceToGeneralEscapeRow(path, ref currentRow, currentColumn, source, destination, cells))
                    return Failed(path, "NoLegalUpwardEscape", "The upward route could not reach a general-routing escape row.", metrics, attemptEvidence);
                var maxColumn = cells.Keys.Select(key => key.Column).DefaultIfEmpty(0).Max();
                if (!TryEscape(path, ref currentColumn, currentRow, source, destination, cells, maxColumn, metrics, attemptEvidence)) return Failed(path, "NoLegalUpwardEscape", "The upward route could not escape the source footprint.", metrics, attemptEvidence);
                currentRow -= 2;
                while (currentRow >= start.Row)
                {
                    currentRow--;
                    if (!Append(path, new ArchitectureV7RouteCell(currentRow, currentColumn), source, destination, cells))
                        return Failed(path, "NoLegalUpwardContinuation", "The upward route could not leave the escape track toward the source-adjacent routing row.", metrics, attemptEvidence);
                }
                while (currentRow > end.Row - 1)
                {
                    if (!Continue(path, ref currentRow, ref currentColumn, -1, source, destination, cells, metrics)) return Failed(path, "NoLegalUpwardContinuation", "No legal upward continuation column exists.", metrics, attemptEvidence);
                }
            }
            else
            {
                while (currentRow < end.Row - 1)
                {
                    if (currentRow + 2 == end.Row - 1 && TryAppendDestinationFromPreviousRoutingRow(path, currentRow, currentColumn, end, source, destination, cells))
                        return Validate(path, source, destination, cells, true)
                            ? new RouteAttempt(path, true, Array.Empty<ArchitectureV7RouteDiagnostic>(), attemptEvidence, metrics.Freeze())
                            : Failed(path, "IllegalRoute", "The constructed route failed frozen-cell legality evaluation.", metrics, attemptEvidence);
                    if (!Continue(path, ref currentRow, ref currentColumn, 1, source, destination, cells, metrics)) return Failed(path, "NoLegalDownwardContinuation", "No legal downward continuation column exists.", metrics);
                }
            }

            if (!AppendHorizontal(path, currentRow, currentColumn, end.Column, source, destination, cells) || !Append(path, end, source, destination, cells))
                return Failed(path, "NoLegalDestinationApproach", "The destination cannot be approached and entered legally.", metrics);
            return Validate(path, source, destination, cells, true) ? new RouteAttempt(path, true, Array.Empty<ArchitectureV7RouteDiagnostic>(), attemptEvidence, metrics.Freeze()) : Failed(path, "IllegalRoute", "The constructed route failed frozen-cell legality evaluation.", metrics, attemptEvidence);
        }

        static bool AdvanceToGeneralEscapeRow(List<ArchitectureV7RouteCell> path, ref int currentRow, int currentColumn,
            ArchitectureV7FrozenNodePlacement source, ArchitectureV7FrozenNodePlacement destination,
            IReadOnlyDictionary<(int Row, int Column), ArchitectureV7LogicalCell> cells)
        {
            while (true)
            {
                if (!cells.TryGetValue((currentRow, currentColumn), out var currentCell)) return false;
                if (currentCell.Capabilities.HasFlag(ArchitectureV7CellCapability.GeneralRouting)) return true;

                var nextRow = currentRow + 1;
                if (!Append(path, new ArchitectureV7RouteCell(nextRow, currentColumn), source, destination, cells)) return false;
                currentRow = nextRow;
            }
        }

        static bool TryAppendDestinationFromPreviousRoutingRow(List<ArchitectureV7RouteCell> path, int currentRow, int currentColumn,
            ArchitectureV7RouteCell end, ArchitectureV7FrozenNodePlacement source, ArchitectureV7FrozenNodePlacement destination,
            IReadOnlyDictionary<(int Row, int Column), ArchitectureV7LogicalCell> cells)
        {
            var initialCount = path.Count;
            var alignmentRow = currentRow;
            if (!AppendHorizontal(path, alignmentRow, currentColumn, end.Column, source, destination, cells) ||
                !Append(path, new ArchitectureV7RouteCell(alignmentRow + 1, end.Column), source, destination, cells) ||
                !Append(path, new ArchitectureV7RouteCell(alignmentRow + 2, end.Column), source, destination, cells) ||
                !Append(path, end, source, destination, cells))
            {
                while (path.Count > initialCount) path.RemoveAt(path.Count - 1);
                return false;
            }

            return true;
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
            var segment = ContinuationSegment(currentColumn, currentRow, candidate, direction);
            if (!CanAppendSegment(path, segment, source, destination, cells)) return false;
            path.AddRange(segment);
            return true;
        }

        static bool CanAppendContinuation(IReadOnlyList<ArchitectureV7RouteCell> path, int currentColumn, int currentRow, int candidate,
            int direction, ArchitectureV7FrozenNodePlacement source, ArchitectureV7FrozenNodePlacement destination,
            IReadOnlyDictionary<(int Row, int Column), ArchitectureV7LogicalCell> cells)
        {
            return CanAppendSegment(path, ContinuationSegment(currentColumn, currentRow, candidate, direction), source, destination, cells);
        }

        static bool CanEnterCell(int row, int column, ArchitectureV7FrozenNodePlacement source, ArchitectureV7FrozenNodePlacement destination,
            IReadOnlyDictionary<(int Row, int Column), ArchitectureV7LogicalCell> cells) =>
            cells.TryGetValue((row, column), out var cell) &&
            (cell.OccupantId is null || cell.OccupantId == source.PhysicalNodeId || cell.OccupantId == destination.PhysicalNodeId);

        static bool TryEscape(List<ArchitectureV7RouteCell> path, ref int currentColumn, int currentRow,
            ArchitectureV7FrozenNodePlacement source, ArchitectureV7FrozenNodePlacement destination,
            IReadOnlyDictionary<(int Row, int Column), ArchitectureV7LogicalCell> cells, int maxColumn, RouteOperationMetrics metrics,
            ICollection<ArchitectureV7RouteAttemptEvidence> evidence)
        {
            var candidates = new List<ArchitectureV7RouteCandidateEvidence>();
            foreach (var candidate in EscapeCandidates(source, maxColumn))
            {
                metrics.UpwardEscapeCandidatesEvaluated++;
                var segment = ContinuationSegment(currentColumn, currentRow, candidate, -1);
                var rejectionReason = !CanContinueAtColumn(path, currentColumn, currentRow, candidate, -1, source, destination, cells)
                    ? "continuation-illegal"
                    : !CanAppendContinuation(path, currentColumn, currentRow, candidate, -1, source, destination, cells)
                        ? "append-illegal" : null;
                if (rejectionReason is null)
                {
                    if (!AppendContinuation(path, currentColumn, currentRow, candidate, -1, source, destination, cells))
                        rejectionReason = "append-failed";
                    else
                    {
                        candidates.Add(new ArchitectureV7RouteCandidateEvidence(candidate, true, "accepted", segment));
                        evidence.Add(new ArchitectureV7RouteAttemptEvidence("upward-escape", path.ToArray(), null,
                            "Down", "Up", null, null, currentColumn, destination.CentreCell, candidates.ToArray(),
                            new[] { "SourceExteriorDeparture", "UpwardEscape" },
                            "v7-common-diagram-router;authoritative-upward-candidate-sequence"));
                        currentColumn = candidate;
                        return true;
                    }
                }
                candidates.Add(new ArchitectureV7RouteCandidateEvidence(candidate, false, rejectionReason!, segment));
            }
            evidence.Add(new ArchitectureV7RouteAttemptEvidence("upward-escape", path.ToArray(), null,
                "Down", "Up", null, "no-legal-candidate", currentColumn, destination.CentreCell, candidates.ToArray(),
                new[] { "SourceExteriorDeparture", "UpwardEscape" },
                "v7-common-diagram-router;authoritative-upward-candidate-sequence"));
            return false;
        }

        static IReadOnlyList<int> EscapeCandidates(ArchitectureV7FrozenNodePlacement source, int maxColumn)
        {
            const int separation = 1;
            var footprintLeft = source.DiagramColumn;
            var footprintRightExclusive = source.DiagramColumn + source.LogicalSpan;
            var result = new List<int>();
            for (var ordinal = 1; ; ordinal++)
            {
                var distance = ordinal * 2;
                var left = source.CentreCell - distance;
                var right = source.CentreCell + distance;
                var leftAvailable = left >= 0 && left <= footprintLeft - separation - 1;
                var rightAvailable = right <= maxColumn && right >= footprintRightExclusive + separation;
                if (!leftAvailable && !rightAvailable && left < 0 && right > maxColumn) break;
                if (leftAvailable) result.Add(left);
                if (rightAvailable) result.Add(right);
            }
            return result;
        }

        static IReadOnlyList<ArchitectureV7RouteCell> ContinuationSegment(int currentColumn, int currentRow, int candidate, int direction)
        {
            var segment = new List<ArchitectureV7RouteCell>();
            var horizontalStep = candidate >= currentColumn ? 1 : -1;
            for (var column = currentColumn + horizontalStep; column != candidate + horizontalStep; column += horizontalStep)
                segment.Add(new ArchitectureV7RouteCell(currentRow, column));
            segment.Add(new ArchitectureV7RouteCell(currentRow + direction, candidate));
            segment.Add(new ArchitectureV7RouteCell(currentRow + direction * 2, candidate));
            return segment;
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
            return ArchitectureV7CellTraversalPolicy.Allows(capability, ToTraversalDirection(entry), ToTraversalDirection(exit));
        }

        static ArchitectureV7TraversalDirection ToTraversalDirection(Direction direction) => direction switch
        {
            Direction.Up => ArchitectureV7TraversalDirection.Up,
            Direction.Down => ArchitectureV7TraversalDirection.Down,
            Direction.Left => ArchitectureV7TraversalDirection.Left,
            Direction.Right => ArchitectureV7TraversalDirection.Right,
            _ => ArchitectureV7TraversalDirection.None
        };

        static Direction DirectionOf(ArchitectureV7RouteCell from, ArchitectureV7RouteCell to) =>
            to.Row == from.Row ? (to.Column > from.Column ? Direction.Right : Direction.Left) : (to.Row > from.Row ? Direction.Down : Direction.Up);

        static Direction DirectionFor(int direction) => direction > 0 ? Direction.Down : Direction.Up;

        static RouteAttempt Failed(IReadOnlyList<ArchitectureV7RouteCell> attempted, string code, string message, RouteOperationMetrics? metrics = null,
            IReadOnlyList<ArchitectureV7RouteAttemptEvidence>? attemptEvidence = null) =>
            new RouteAttempt(attempted, false, new[] { Failure(code, message, attempted) }, attemptEvidence ?? Array.Empty<ArchitectureV7RouteAttemptEvidence>(), metrics?.Freeze() ?? new ArchitectureV7RoutingOperationMetrics(0, 0));

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

    private sealed record RouteAttempt(IReadOnlyList<ArchitectureV7RouteCell> Cells, bool IsComplete, IReadOnlyList<ArchitectureV7RouteDiagnostic> Diagnostics,
        IReadOnlyList<ArchitectureV7RouteAttemptEvidence> AttemptEvidence, ArchitectureV7RoutingOperationMetrics OperationMetrics);
}
