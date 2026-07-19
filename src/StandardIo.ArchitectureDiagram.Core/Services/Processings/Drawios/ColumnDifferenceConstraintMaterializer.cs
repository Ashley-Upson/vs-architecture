using System;
using System.Collections.Generic;
using System.Linq;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal static class ColumnDifferenceConstraintMaterializer
{
    public static IReadOnlyList<GenerationConstraint> Propose(
        PlacedGraph immutableBase,
        IEnumerable<ColumnToEnvelopeDifferenceConstraint> envelopeConstraints,
        IEnumerable<ColumnToColumnDifferenceConstraint> columnConstraints,
        IReadOnlyDictionary<string, LinkLayout> links,
        ISet<string> commonAuthorityRouteIds,
        IDictionary<string, HorizontalMovementDirection>? lockedDirections = null)
    {
        var result = new List<GenerationConstraint>();
        foreach (var group in envelopeConstraints.GroupBy(item => item.DestinationSubtreeId, StringComparer.Ordinal)
                     .OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            var constraints = group.OrderBy(item => item.Id, StringComparer.Ordinal).ToArray();
            var representative = constraints[0];
            var blockers = constraints.Select(item => item.BlockingSubtreeId).Distinct(StringComparer.Ordinal).ToArray();
            var leftDelta = constraints.Min(item => item.LeftClearance.RequiredCoordinate - item.ColumnX);
            var rightDelta = constraints.Max(item => item.RightClearance.RequiredCoordinate - item.ColumnX);
            var left = representative.LeftClearance with
            {
                RequiredCoordinate = representative.ColumnX + leftDelta,
                MovementScopes = constraints.SelectMany(item => item.LeftClearance.MovementScopes).Distinct().ToArray()
            };
            var right = representative.RightClearance with
            {
                RequiredCoordinate = representative.ColumnX + rightDelta,
                MovementScopes = constraints.SelectMany(item => item.RightClearance.MovementScopes).Distinct().ToArray()
            };
            var alternatives = new[]
            {
                Candidate(immutableBase, representative, left, blockers, links, commonAuthorityRouteIds),
                Candidate(immutableBase, representative, right, blockers, links, commonAuthorityRouteIds)
            }.Where(item => item is not null).Cast<CandidateConstraint>()
                .Where(item => lockedDirections is null || !lockedDirections.TryGetValue(group.Key, out var locked) ||
                    item.Direction == locked)
                .OrderBy(item => item.NodeCount).ThenBy(item => item.Distance)
                .ThenBy(item => item.Scope.Kind).ThenBy(item => item.Scope.Id, StringComparer.Ordinal)
                .ThenBy(item => item.Kind).ToArray();
            if (alternatives.Length == 0) continue;
            var selected = alternatives[0];
            if (lockedDirections is not null && !lockedDirections.ContainsKey(group.Key))
                lockedDirections[group.Key] = selected.Direction;
            result.Add(new GenerationConstraint(new GenerationConstraintKey(selected.Scope, selected.Kind),
                selected.AbsoluteScopeX, $"ColumnToEnvelope:{string.Join("+", constraints.Select(item => item.Id))}"));

            foreach (var constraint in constraints)
            {
                var blockerChoices = new[]
                {
                    BlockerCandidate(constraint, HorizontalMovementDirection.Left,
                        constraint.ColumnX - constraint.RequiredClearance - constraint.BlockingEnvelope.Right),
                    BlockerCandidate(constraint, HorizontalMovementDirection.Right,
                        constraint.ColumnX + constraint.RequiredClearance - constraint.BlockingEnvelope.X)
                }.Where(item => item is not null).Cast<CandidateConstraint>()
                    .OrderBy(item => item.NodeCount).ThenBy(item => item.Distance)
                    .ThenBy(item => item.Scope.Kind).ThenBy(item => item.Scope.Id, StringComparer.Ordinal).ToArray();
                if (blockerChoices.Length == 0) continue;
                var blocker = blockerChoices[0];
                result.Add(new GenerationConstraint(new GenerationConstraintKey(blocker.Scope, blocker.Kind),
                    blocker.AbsoluteScopeX, $"BlockingEnvelopeToColumn:{constraint.Id}"));

                CandidateConstraint? BlockerCandidate(
                    ColumnToEnvelopeDifferenceConstraint item,
                    HorizontalMovementDirection direction,
                    int delta)
                {
                    if (direction == HorizontalMovementDirection.Left && delta >= 0 ||
                        direction == HorizontalMovementDirection.Right && delta <= 0) return null;
                    foreach (var scope in PreAssignmentMovementPlanner.CandidateScopes(item.BlockingSubtreeId, immutableBase, direction))
                    {
                        if (!Complete(scope, immutableBase, links, commonAuthorityRouteIds)) continue;
                        var members = HorizontalMovementConstraintMaterializer.Members(scope, immutableBase);
                        if (members.Contains(item.DestinationSubtreeId, StringComparer.Ordinal)) continue;
                        var accumulated = item.BlockingEnvelope.X - immutableBase.Nodes[item.BlockingSubtreeId].Rect.X;
                        return new CandidateConstraint(scope, direction,
                            direction == HorizontalMovementDirection.Left
                                ? GenerationConstraintKind.MaximumX : GenerationConstraintKind.MinimumX,
                            members.Min(id => immutableBase.Nodes[id].Rect.X) + accumulated + delta,
                            Math.Abs(delta), members.Count);
                    }
                    return null;
                }
            }
        }

        foreach (var constraint in columnConstraints.OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            var leftId = constraint.FirstColumnX <= constraint.SecondColumnX
                ? constraint.FirstDestinationSubtreeId : constraint.SecondDestinationSubtreeId;
            var rightId = leftId == constraint.FirstDestinationSubtreeId
                ? constraint.SecondDestinationSubtreeId : constraint.FirstDestinationSubtreeId;
            var leftX = leftId == constraint.FirstDestinationSubtreeId ? constraint.FirstColumnX : constraint.SecondColumnX;
            var rightX = rightId == constraint.FirstDestinationSubtreeId ? constraint.FirstColumnX : constraint.SecondColumnX;
            var currentSeparation = rightX - leftX;
            var delta = Math.Max(0, constraint.RequiredSeparation - currentSeparation);
            if (delta == 0) continue;
            var choices = new[]
            {
                ScopeChoice(leftId, rightId, HorizontalMovementDirection.Left, -delta),
                ScopeChoice(rightId, leftId, HorizontalMovementDirection.Right, delta)
            }.Where(item => item is not null).Cast<CandidateConstraint>()
                .Where(item => lockedDirections is null ||
                    !lockedDirections.TryGetValue(item.Direction == HorizontalMovementDirection.Left ? leftId : rightId, out var locked) ||
                    item.Direction == locked)
                .OrderBy(item => item.NodeCount).ThenBy(item => item.Distance)
                .ThenBy(item => item.Scope.Kind).ThenBy(item => item.Scope.Id, StringComparer.Ordinal).ToArray();
            if (choices.Length == 0) continue;
            var selected = choices[0];
            var selectedId = selected.Direction == HorizontalMovementDirection.Left ? leftId : rightId;
            if (lockedDirections is not null && !lockedDirections.ContainsKey(selectedId))
                lockedDirections[selectedId] = selected.Direction;
            result.Add(new GenerationConstraint(new GenerationConstraintKey(selected.Scope, selected.Kind),
                selected.AbsoluteScopeX, $"ColumnToColumn:{constraint.Id}"));

            CandidateConstraint? ScopeChoice(
                string movingId, string stationaryId, HorizontalMovementDirection direction, int movement)
            {
                foreach (var scope in PreAssignmentMovementPlanner.CandidateScopes(movingId, immutableBase, direction))
                {
                    if (!Complete(scope, immutableBase, links, commonAuthorityRouteIds)) continue;
                    var members = HorizontalMovementConstraintMaterializer.Members(scope, immutableBase);
                    if (members.Contains(stationaryId, StringComparer.Ordinal)) continue;
                    return new CandidateConstraint(scope, direction,
                        direction == HorizontalMovementDirection.Left
                            ? GenerationConstraintKind.MaximumX : GenerationConstraintKind.MinimumX,
                        members.Min(id => immutableBase.Nodes[id].Rect.X) + movement,
                        Math.Abs(movement), members.Count);
                }
                return null;
            }
        }
        return result;
    }

    private static CandidateConstraint? Candidate(
        PlacedGraph placement,
        ColumnToEnvelopeDifferenceConstraint constraint,
        HorizontalDifferenceAlternative alternative,
        IReadOnlyList<string> blockingSubtreeIds,
        IReadOnlyDictionary<string, LinkLayout> links,
        ISet<string> commonAuthorityRouteIds)
    {
        var delta = alternative.RequiredCoordinate - constraint.ColumnX;
        if (alternative.Direction == HorizontalMovementDirection.Left && delta >= 0 ||
            alternative.Direction == HorizontalMovementDirection.Right && delta <= 0) return null;
        foreach (var scope in alternative.MovementScopes.OrderBy(item => item.Kind).ThenBy(item => item.Id, StringComparer.Ordinal))
        {
            IReadOnlyList<string> members;
            try { members = HorizontalMovementConstraintMaterializer.Members(scope, placement); }
            catch (InvalidOperationException) { continue; }
            if (!members.Contains(constraint.DestinationSubtreeId, StringComparer.Ordinal) ||
                blockingSubtreeIds.Any(id => members.Contains(id, StringComparer.Ordinal)) ||
                !Complete(scope, placement, links, commonAuthorityRouteIds)) continue;
            var left = members.Min(id => placement.Nodes[id].Rect.X);
            var accumulatedTranslation = constraint.DestinationEnvelope.X -
                placement.Nodes[constraint.DestinationSubtreeId].Rect.X;
            return new CandidateConstraint(scope, alternative.Direction,
                delta < 0 ? GenerationConstraintKind.MaximumX : GenerationConstraintKind.MinimumX,
                left + accumulatedTranslation + delta, Math.Abs(delta), members.Count);
        }
        return null;
    }

    private static bool Complete(
        MovementScopeIdentity scope,
        PlacedGraph placement,
        IReadOnlyDictionary<string, LinkLayout> links,
        ISet<string> supported)
    {
        IReadOnlyList<string> members;
        try { members = HorizontalMovementConstraintMaterializer.Members(scope, placement); }
        catch (InvalidOperationException) { return false; }
        var moved = new HashSet<string>(members, StringComparer.Ordinal);
        return links.Values.Where(link => moved.Contains(link.Link.SourceId) || moved.Contains(link.Link.TargetId))
            .All(link => supported.Contains(link.Link.Id));
    }

    private sealed record CandidateConstraint(
        MovementScopeIdentity Scope,
        HorizontalMovementDirection Direction,
        GenerationConstraintKind Kind,
        int AbsoluteScopeX,
        int Distance,
        int NodeCount);
}
