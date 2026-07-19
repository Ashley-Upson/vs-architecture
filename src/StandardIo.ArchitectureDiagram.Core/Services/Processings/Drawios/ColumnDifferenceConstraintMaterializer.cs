using System;
using System.Collections.Generic;
using System.Linq;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal static class ColumnDifferenceConstraintMaterializer
{
    public static DifferenceAlternativeSelection Select(
        PlacedGraph immutableBase,
        IEnumerable<ColumnToEnvelopeDifferenceConstraint> envelopeConstraints,
        IEnumerable<ColumnToColumnDifferenceConstraint> columnConstraints,
        IReadOnlyDictionary<string, LinkLayout> links,
        ISet<string> commonAuthorityRouteIds)
    {
        var alternatives = new List<DifferenceAlternativeChoice>();
        foreach (var group in envelopeConstraints.GroupBy(item => item.DestinationSubtreeId, StringComparer.Ordinal)
                     .OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            var constraints = group.OrderBy(item => item.Id, StringComparer.Ordinal).ToArray();
            var representative = constraints[0];
            AddDestinationAlternatives(HorizontalMovementDirection.Left,
                constraints.Min(item => item.LeftClearance.RequiredCoordinate), representative.LeftClearance.MovementScopes);
            AddDestinationAlternatives(HorizontalMovementDirection.Right,
                constraints.Max(item => item.RightClearance.RequiredCoordinate), representative.RightClearance.MovementScopes);
            AddBlockingAlternatives(constraints);

            void AddDestinationAlternatives(HorizontalMovementDirection direction, int coordinate,
                IEnumerable<MovementScopeIdentity> candidateScopes)
            {
                var delta = coordinate - representative.ColumnX;
                if (direction == HorizontalMovementDirection.Left && delta >= 0 ||
                    direction == HorizontalMovementDirection.Right && delta <= 0) return;
                foreach (var scope in candidateScopes.Distinct().OrderBy(item => item.Kind)
                             .ThenBy(item => item.Id, StringComparer.Ordinal))
                {
                    if (!TryMembers(scope, immutableBase, out var members) ||
                        !members.Contains(representative.DestinationSubtreeId, StringComparer.Ordinal) ||
                        constraints.Any(item => members.Contains(item.BlockingSubtreeId, StringComparer.Ordinal)) ||
                        !Complete(members, links, commonAuthorityRouteIds)) continue;
                    var accumulated = representative.DestinationEnvelope.X -
                                      immutableBase.Nodes[representative.DestinationSubtreeId].Rect.X;
                    var absolute = members.Min(id => immutableBase.Nodes[id].Rect.X) + accumulated + delta;
                    var kind = direction == HorizontalMovementDirection.Left
                        ? GenerationConstraintKind.MaximumX : GenerationConstraintKind.MinimumX;
                    var alternativeId = $"column-envelope:{group.Key}:{direction}:{scope.Kind}:{scope.Id}";
                    var generation = new GenerationConstraint(new GenerationConstraintKey(scope, kind), absolute,
                        $"ColumnToEnvelope:{string.Join("+", constraints.Select(item => item.Id))}");
                    foreach (var constraint in constraints)
                        alternatives.Add(new DifferenceAlternativeChoice($"column-envelope:{group.Key}", alternativeId,
                            scope, representative.DestinationSubtreeId, constraint.BlockingSubtreeId, direction,
                            Math.Max(1, Math.Abs(delta)), Math.Abs(delta), members.Count, Math.Max(0, delta), generation));
                }
            }

            void AddBlockingAlternatives(IReadOnlyList<ColumnToEnvelopeDifferenceConstraint> blockers)
            {
                Add(HorizontalMovementDirection.Left);
                Add(HorizontalMovementDirection.Right);
                void Add(HorizontalMovementDirection direction)
                {
                    var blockerIds = blockers.Select(item => item.BlockingSubtreeId)
                        .Distinct(StringComparer.Ordinal).ToArray();
                    var movements = blockers.Select(item =>
                    {
                        var requiredX = direction == HorizontalMovementDirection.Left
                            ? item.ColumnX - item.RequiredClearance - item.BlockingEnvelope.Width - 1
                            : item.ColumnX + item.RequiredClearance + 1;
                        return requiredX - item.BlockingEnvelope.X;
                    }).ToArray();
                    var movement = direction == HorizontalMovementDirection.Left
                        ? movements.Min() : movements.Max();
                    if (direction == HorizontalMovementDirection.Left && movement >= 0 ||
                        direction == HorizontalMovementDirection.Right && movement <= 0) return;
                    var scopes = blockerIds.SelectMany(id =>
                            PreAssignmentMovementPlanner.CandidateScopes(id, immutableBase, direction))
                        .Distinct().OrderBy(item => item.Kind).ThenBy(item => item.Id, StringComparer.Ordinal);
                    foreach (var scope in scopes)
                    {
                        if (!TryMembers(scope, immutableBase, out var members) ||
                            !blockerIds.All(id => members.Contains(id, StringComparer.Ordinal)) ||
                            members.Contains(representative.DestinationSubtreeId, StringComparer.Ordinal) ||
                            !Complete(members, links, commonAuthorityRouteIds)) continue;
                        var absolute = members.Min(id => immutableBase.Nodes[id].Rect.X) + movement;
                        var kind = direction == HorizontalMovementDirection.Left
                            ? GenerationConstraintKind.MaximumX : GenerationConstraintKind.MinimumX;
                        var alternativeId = $"blocking-envelope:{group.Key}:{direction}:{scope.Kind}:{scope.Id}";
                        var generation = new GenerationConstraint(new GenerationConstraintKey(scope, kind), absolute,
                            $"BlockingEnvelopeToColumn:{string.Join("+", blockers.Select(item => item.Id))}");
                        foreach (var blocker in blockers)
                            alternatives.Add(new DifferenceAlternativeChoice($"column-envelope:{group.Key}", alternativeId,
                                scope, blocker.BlockingSubtreeId, blocker.DestinationSubtreeId, direction,
                                Math.Max(1, Math.Abs(movement)), Math.Abs(movement), members.Count,
                                Math.Max(0, movement), generation));
                    }
                }
            }
        }

        foreach (var constraint in columnConstraints.OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            var leftId = constraint.FirstColumnX <= constraint.SecondColumnX
                ? constraint.FirstDestinationSubtreeId : constraint.SecondDestinationSubtreeId;
            var rightId = leftId == constraint.FirstDestinationSubtreeId
                ? constraint.SecondDestinationSubtreeId : constraint.FirstDestinationSubtreeId;
            var delta = Math.Max(0, constraint.RequiredSeparation -
                Math.Abs(constraint.SecondColumnX - constraint.FirstColumnX));
            if (delta == 0) continue;
            Add(leftId, rightId, HorizontalMovementDirection.Left, -delta);
            Add(rightId, leftId, HorizontalMovementDirection.Right, delta);
            void Add(string movingId, string opposingId, HorizontalMovementDirection direction, int movement)
            {
                foreach (var scope in PreAssignmentMovementPlanner.CandidateScopes(movingId, immutableBase, direction))
                {
                    if (!TryMembers(scope, immutableBase, out var members) ||
                        members.Contains(opposingId, StringComparer.Ordinal) ||
                        !Complete(members, links, commonAuthorityRouteIds)) continue;
                    var kind = direction == HorizontalMovementDirection.Left
                        ? GenerationConstraintKind.MaximumX : GenerationConstraintKind.MinimumX;
                    var absolute = members.Min(id => immutableBase.Nodes[id].Rect.X) + movement;
                    var alternativeId = $"column-column:{constraint.Id}:{direction}:{scope.Kind}:{scope.Id}";
                    alternatives.Add(new DifferenceAlternativeChoice($"column-column:{constraint.Id}", alternativeId,
                        scope, movingId, opposingId, direction, delta, delta, members.Count, Math.Max(0, movement),
                        new GenerationConstraint(new GenerationConstraintKey(scope, kind), absolute,
                            $"ColumnToColumn:{constraint.Id}")));
                }
            }
        }
        return DifferenceAlternativeComponentSolver.Solve(alternatives);
    }

    public static IReadOnlyList<GenerationConstraint> Propose(
        PlacedGraph immutableBase,
        IEnumerable<ColumnToEnvelopeDifferenceConstraint> envelopeConstraints,
        IEnumerable<ColumnToColumnDifferenceConstraint> columnConstraints,
        IReadOnlyDictionary<string, LinkLayout> links,
        ISet<string> commonAuthorityRouteIds,
        IDictionary<string, HorizontalMovementDirection>? lockedDirections = null)
    {
        var selection = Select(immutableBase, envelopeConstraints, columnConstraints, links, commonAuthorityRouteIds);
        if (!selection.IsSatisfiable) return Array.Empty<GenerationConstraint>();
        if (lockedDirections is not null)
            foreach (var item in selection.Selected)
                if (!lockedDirections.ContainsKey(item.MovingStructureId))
                    lockedDirections[item.MovingStructureId] = item.Direction;
        return selection.Selected.Select(item => item.Constraint)
            .GroupBy(item => item.Key).Select(group => group.Key.Kind == GenerationConstraintKind.MaximumX
                ? group.OrderBy(item => item.Minimum).First()
                : group.OrderByDescending(item => item.Minimum).First())
            .OrderBy(item => item.Key.Scope.Kind).ThenBy(item => item.Key.Scope.Id, StringComparer.Ordinal)
            .ThenBy(item => item.Key.Kind).ToArray();
    }

    private static bool TryMembers(MovementScopeIdentity scope, PlacedGraph placement,
        out IReadOnlyList<string> members)
    {
        try
        {
            members = HorizontalMovementConstraintMaterializer.Members(scope, placement);
            return true;
        }
        catch (InvalidOperationException)
        {
            members = Array.Empty<string>();
            return false;
        }
    }

    private static bool Complete(IReadOnlyList<string> members, IReadOnlyDictionary<string, LinkLayout> links,
        ISet<string> supported)
    {
        var moved = new HashSet<string>(members, StringComparer.Ordinal);
        return links.Values.Where(link => moved.Contains(link.Link.SourceId) || moved.Contains(link.Link.TargetId))
            .All(link => supported.Contains(link.Link.Id));
    }
}
