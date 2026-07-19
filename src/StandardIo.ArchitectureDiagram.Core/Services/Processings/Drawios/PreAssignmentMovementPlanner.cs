using System;
using System.Collections.Generic;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core.Models;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal static class PreAssignmentMovementPlanner
{
    public static PreAssignmentMovementResult Solve(
        PlacedGraph immutableBase,
        IEnumerable<PositionalConstraintDemand> source,
        DiagramSettings settings,
        IReadOnlyDictionary<string, LinkLayout> links,
        ISet<string>? commonAuthorityRouteIds = null)
    {
        var demands = source.OrderBy(item => item.Id, StringComparer.Ordinal).ToArray();
        var components = Components(demands, immutableBase, links);
        var store = new GenerationConstraintStore();
        var solutions = new List<PositionalConstraintSolution>();
        foreach (var component in components)
        {
            var evaluated = component.Demands.SelectMany(demand =>
                Candidates(demand, immutableBase, links, commonAuthorityRouteIds)).ToArray();
            var accepted = new List<PositionalMovementCandidate>();
            var constraints = new List<GenerationConstraint>();
            foreach (var demand in component.Demands)
            {
                var candidate = evaluated.Where(item => item.DemandId == demand.Id && item.IsValid)
                    .OrderBy(item => item.NodeIds.Count)
                    .ThenBy(item => item.ProjectWidthExpansion)
                    .ThenBy(item => item.ParentUmbrellaDisruption)
                    .ThenBy(item => item.InvalidatedLinkCount)
                    .ThenBy(item => item.ResultingDepartureLength)
                    .ThenBy(item => item.Scope.Kind)
                    .ThenBy(item => item.Scope.Id, StringComparer.Ordinal)
                    .ThenBy(item => item.Direction)
                    .FirstOrDefault();
                if (candidate is null) continue;
                accepted.Add(candidate);
                var left = candidate.NodeIds.Min(id => immutableBase.Nodes[id].Rect.X);
                var minimum = left + (candidate.Direction == HorizontalMovementDirection.Right
                    ? candidate.Delta
                    : -candidate.Delta);
                var kind = candidate.Direction == HorizontalMovementDirection.Right
                    ? GenerationConstraintKind.MinimumX
                    : GenerationConstraintKind.MaximumX;
                var constraint = new GenerationConstraint(new GenerationConstraintKey(candidate.Scope, kind), minimum,
                    $"{demand.Reason}:{demand.Id}");
                store.Merge(constraint);
                constraints.Add(constraint);
            }
            var valid = accepted.Count == component.Demands.Count;
            solutions.Add(new PositionalConstraintSolution(component, constraints, evaluated, accepted, valid,
                valid ? "Solved" : "NoCompleteCoherentMovement"));
        }

        var validConstraints = solutions.Where(item => item.IsValid).SelectMany(item => item.Constraints).ToArray();
        var movement = HorizontalMovementConstraintMaterializer.Materialize(immutableBase, validConstraints, settings, links);
        return new PreAssignmentMovementResult(movement.Placement, components, solutions, store.Snapshot(),
            movement.InvalidatedLinkIds, validConstraints.Length == 0 ? 0 : 1);
    }

    internal static IReadOnlyList<MovementScopeIdentity> CandidateScopes(
        string nodeId,
        PlacedGraph placement,
        HorizontalMovementDirection direction)
    {
        var hierarchy = placement.PositionalHierarchy;
        var scopes = new List<MovementScopeIdentity>();
        scopes.Add(new MovementScopeIdentity(
            hierarchy.ChildrenByNode[nodeId].Count == 0 ? MovementScopeKind.Node : MovementScopeKind.LayoutSubtree,
            nodeId));
        if (hierarchy.ParentByNode.ContainsKey(nodeId))
            scopes.Add(new MovementScopeIdentity(
                direction == HorizontalMovementDirection.Right
                    ? MovementScopeKind.OrderedSiblingSuffix
                    : MovementScopeKind.OrderedSiblingPrefix, nodeId));
        var projectId = placement.Nodes[nodeId].Node.ProjectId;
        if (projectId is not null)
        {
            scopes.Add(new MovementScopeIdentity(MovementScopeKind.ProjectRoot, projectId));
            scopes.Add(new MovementScopeIdentity(
                direction == HorizontalMovementDirection.Right
                    ? MovementScopeKind.OrderedProjectSuffix
                    : MovementScopeKind.OrderedProjectPrefix, projectId));
        }
        return scopes.Distinct().ToArray();
    }

    private static IEnumerable<PositionalMovementCandidate> Candidates(
        PositionalConstraintDemand demand,
        PlacedGraph placement,
        IReadOnlyDictionary<string, LinkLayout> links,
        ISet<string>? commonAuthorityRouteIds)
    {
        foreach (var scope in demand.CandidateMovementScopes.OrderBy(item => item.Kind).ThenBy(item => item.Id, StringComparer.Ordinal))
        {
            if (!TryMembers(scope, placement, out var members, out var rejection))
            {
                yield return Rejected(demand, scope, rejection);
                continue;
            }
            var movesLeft = members.Contains(demand.LeftStructureId, StringComparer.Ordinal);
            var movesRight = members.Contains(demand.RightStructureId, StringComparer.Ordinal);
            if (movesLeft == movesRight)
            {
                yield return Rejected(demand, scope, movesLeft ? "ScopeMovesBothStructures" : "ScopeMovesNeitherStructure");
                continue;
            }
            var left = placement.Nodes[demand.LeftStructureId].Rect;
            var right = placement.Nodes[demand.RightStructureId].Rect;
            var deficit = Math.Max(0, left.Right + demand.MinimumSeparation - right.X);
            var direction = movesLeft ? HorizontalMovementDirection.Left : HorizontalMovementDirection.Right;
            var movedSet = new HashSet<string>(members, StringComparer.Ordinal);
            var invalidatedRoutes = links.Values.Where(link => movedSet.Contains(link.Link.SourceId) ||
                movedSet.Contains(link.Link.TargetId)).Select(link => link.Link.Id).ToArray();
            if (commonAuthorityRouteIds is not null && invalidatedRoutes.Any(id => !commonAuthorityRouteIds.Contains(id)))
            {
                var missing = invalidatedRoutes.Where(id => !commonAuthorityRouteIds.Contains(id))
                    .OrderBy(id => id, StringComparer.Ordinal).First();
                yield return Rejected(demand, scope, $"IncompleteCommonAuthorityClosure:{missing}");
                continue;
            }
            var invalidated = invalidatedRoutes.Length;
            var projectExpansion = scope.Kind == MovementScopeKind.ProjectRoot ||
                scope.Kind == MovementScopeKind.OrderedProjectPrefix || scope.Kind == MovementScopeKind.OrderedProjectSuffix
                ? deficit : 0;
            var umbrella = members.Count(id => placement.PositionalHierarchy.ChildrenByNode[id].Count > 0) * deficit;
            yield return new PositionalMovementCandidate(demand.Id, scope, direction, deficit, members,
                projectExpansion, umbrella, invalidated, deficit, true, string.Empty);
        }
    }

    private static PositionalMovementCandidate Rejected(
        PositionalConstraintDemand demand, MovementScopeIdentity scope, string reason) =>
        new(demand.Id, scope, demand.Direction, 0, Array.Empty<string>(), int.MaxValue, int.MaxValue,
            int.MaxValue, int.MaxValue, false, reason);

    private static IReadOnlyList<PositionalConstraintComponent> Components(
        IReadOnlyList<PositionalConstraintDemand> demands,
        PlacedGraph placement,
        IReadOnlyDictionary<string, LinkLayout> links)
    {
        var pending = new HashSet<string>(demands.Select(item => item.Id), StringComparer.Ordinal);
        var result = new List<PositionalConstraintComponent>();
        while (pending.Count > 0)
        {
            var seed = pending.OrderBy(item => item, StringComparer.Ordinal).First();
            var ids = new HashSet<string>(new[] { seed }, StringComparer.Ordinal);
            var changed = true;
            while (changed)
            {
                changed = false;
                var selected = demands.Where(item => ids.Contains(item.Id)).ToArray();
                var nodes = new HashSet<string>(selected.SelectMany(item => new[] { item.LeftStructureId, item.RightStructureId }), StringComparer.Ordinal);
                var routeIds = new HashSet<string>(selected.SelectMany(item => item.SourceLinkIds), StringComparer.Ordinal);
                foreach (var candidate in demands.Where(item => pending.Contains(item.Id) && !ids.Contains(item.Id)))
                {
                    var intervalOverlap = selected.Any(item =>
                        item.AffectedLayerInterval.Minimum <= candidate.AffectedLayerInterval.Maximum &&
                        candidate.AffectedLayerInterval.Minimum <= item.AffectedLayerInterval.Maximum);
                    if (!intervalOverlap) continue;
                    if (!nodes.Contains(candidate.LeftStructureId) && !nodes.Contains(candidate.RightStructureId) &&
                        !candidate.SourceLinkIds.Any(routeIds.Contains)) continue;
                    ids.Add(candidate.Id);
                    changed = true;
                }
            }
            foreach (var id in ids) pending.Remove(id);
            var componentDemands = demands.Where(item => ids.Contains(item.Id)).OrderBy(item => item.Id, StringComparer.Ordinal).ToArray();
            var componentNodes = componentDemands.SelectMany(item => new[] { item.LeftStructureId, item.RightStructureId })
                .Distinct(StringComparer.Ordinal).OrderBy(item => item, StringComparer.Ordinal).ToArray();
            var incident = links.Values.Where(link => componentNodes.Contains(link.Link.SourceId, StringComparer.Ordinal) ||
                    componentNodes.Contains(link.Link.TargetId, StringComparer.Ordinal))
                .Select(item => item.Link.Id).Concat(componentDemands.SelectMany(item => item.SourceLinkIds))
                .Distinct(StringComparer.Ordinal).OrderBy(item => item, StringComparer.Ordinal).ToArray();
            result.Add(new PositionalConstraintComponent(string.Join("+", ids.OrderBy(item => item, StringComparer.Ordinal)),
                componentDemands, componentNodes, incident));
        }
        return result.OrderBy(item => item.Id, StringComparer.Ordinal).ToArray();
    }

    private static bool TryMembers(
        MovementScopeIdentity scope,
        PlacedGraph placement,
        out IReadOnlyList<string> members,
        out string rejection)
    {
        try
        {
            members = HorizontalMovementConstraintMaterializer.Members(scope, placement);
            rejection = string.Empty;
            return true;
        }
        catch (InvalidOperationException error)
        {
            members = Array.Empty<string>();
            rejection = error.Message;
            return false;
        }
    }
}
