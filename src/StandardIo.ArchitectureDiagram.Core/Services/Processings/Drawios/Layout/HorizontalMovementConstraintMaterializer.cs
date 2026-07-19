using System;
using System.Collections.Generic;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core.Models;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal static class HorizontalMovementConstraintMaterializer
{
    public static HorizontalMovementIteration Materialize(
        PlacedGraph immutableBase,
        IReadOnlyList<GenerationConstraint> constraints,
        DiagramSettings settings,
        IReadOnlyDictionary<string, LinkLayout> links)
    {
        var nodes = immutableBase.Nodes.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        var applied = new List<MovementScopeIdentity>();
        var deltaByNode = immutableBase.Nodes.Keys.ToDictionary(item => item, _ => 0, StringComparer.Ordinal);
        foreach (var constraint in constraints
                     .Where(item => item.Key.Kind == GenerationConstraintKind.MinimumX ||
                         item.Key.Kind == GenerationConstraintKind.MaximumX)
                     .OrderBy(item => item.Key.Scope.Kind)
                     .ThenBy(item => item.Key.Scope.Id, StringComparer.Ordinal)
                     .ThenBy(item => item.Reason, StringComparer.Ordinal))
        {
            var members = Members(constraint.Key.Scope, immutableBase).ToArray();
            if (members.Length == 0) throw new InvalidOperationException($"Movement scope {constraint.Key.Scope} has no nodes.");
            var currentLeft = members.Min(id => immutableBase.Nodes[id].Rect.X);
            var delta = constraint.Key.Kind == GenerationConstraintKind.MinimumX
                ? Math.Max(0, constraint.Minimum - currentLeft)
                : Math.Min(0, constraint.Minimum - currentLeft);
            if (delta == 0) continue;
            foreach (var id in members)
            {
                var current = deltaByNode[id];
                deltaByNode[id] = current == 0 || Math.Abs(delta) > Math.Abs(current) ? delta : current;
            }
            applied.Add(constraint.Key.Scope);
        }
        foreach (var item in deltaByNode.Where(item => item.Value != 0))
            nodes[item.Key] = nodes[item.Key] with { Rect = nodes[item.Key].Rect.Translate(item.Value, 0) };
        var maximumDelta = deltaByNode.Values.Select(Math.Abs).DefaultIfEmpty(0).Max();

        var moved = nodes.Where(item => item.Value.Rect != immutableBase.Nodes[item.Key].Rect)
            .Select(item => item.Key).OrderBy(item => item, StringComparer.Ordinal).ToArray();
        if (moved.Length == 0)
            return new HorizontalMovementIteration(immutableBase, Array.Empty<MovementScopeIdentity>(), moved,
                Array.Empty<string>(), 0, false);
        var movedSet = new HashSet<string>(moved, StringComparer.Ordinal);
        var invalidated = links.Values.Where(link => movedSet.Contains(link.Link.SourceId) || movedSet.Contains(link.Link.TargetId))
            .Select(link => link.Link.Id).Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal).ToArray();
        var revised = immutableBase.Revise(nodes, PlacementPipeline.PositionProjects(immutableBase.Graph, settings, nodes));
        return new HorizontalMovementIteration(revised, applied.AsReadOnly(), moved, invalidated, maximumDelta, true);
    }

    internal static IReadOnlyList<string> Members(MovementScopeIdentity scope, PlacedGraph placement)
    {
        var hierarchy = placement.PositionalHierarchy;
        switch (scope.Kind)
        {
            case MovementScopeKind.Node:
                if (!placement.Nodes.ContainsKey(scope.Id)) throw new InvalidOperationException($"Unknown node {scope.Id}.");
                if (hierarchy.ChildrenByNode[scope.Id].Count > 0)
                    throw new InvalidOperationException($"Node {scope.Id} has positional descendants and cannot move independently.");
                return new[] { scope.Id };
            case MovementScopeKind.LayoutSubtree:
                return Descendants(scope.Id, hierarchy);
            case MovementScopeKind.OrderedSiblingPrefix:
                if (!hierarchy.ParentByNode.TryGetValue(scope.Id, out var prefixParentId))
                    throw new InvalidOperationException($"Node {scope.Id} has no positional sibling group.");
                var prefixSiblings = hierarchy.ChildrenByNode[prefixParentId];
                var end = prefixSiblings.ToList().IndexOf(scope.Id);
                return prefixSiblings.Take(end + 1).SelectMany(id => Descendants(id, hierarchy)).ToArray();
            case MovementScopeKind.OrderedSiblingSuffix:
                if (!hierarchy.ParentByNode.TryGetValue(scope.Id, out var parentId))
                    throw new InvalidOperationException($"Node {scope.Id} has no positional sibling group.");
                var siblings = hierarchy.ChildrenByNode[parentId];
                var start = siblings.ToList().IndexOf(scope.Id);
                return siblings.Skip(start).SelectMany(id => Descendants(id, hierarchy)).ToArray();
            case MovementScopeKind.ProjectRoot:
                return ProjectNodes(scope.Id, placement);
            case MovementScopeKind.OrderedProjectPrefix:
                var prefixProjects = placement.ProjectPlacement.StableProjectOrder;
                var prefixProjectIndex = prefixProjects.ToList().IndexOf(scope.Id);
                if (prefixProjectIndex < 0) throw new InvalidOperationException($"Unknown project {scope.Id}.");
                return prefixProjects.Take(prefixProjectIndex + 1).SelectMany(id => ProjectNodes(id, placement)).ToArray();
            case MovementScopeKind.OrderedProjectSuffix:
                var projects = placement.ProjectPlacement.StableProjectOrder;
                var projectIndex = projects.ToList().IndexOf(scope.Id);
                if (projectIndex < 0) throw new InvalidOperationException($"Unknown project {scope.Id}.");
                return projects.Skip(projectIndex).SelectMany(id => ProjectNodes(id, placement)).ToArray();
            default:
                throw new InvalidOperationException($"Movement scope {scope.Kind} is not horizontal.");
        }
    }

    private static IReadOnlyList<string> Descendants(string rootId, PositionalHierarchy hierarchy)
    {
        if (!hierarchy.ChildrenByNode.ContainsKey(rootId)) throw new InvalidOperationException($"Unknown positional root {rootId}.");
        var result = new List<string>();
        var pending = new Stack<string>();
        pending.Push(rootId);
        while (pending.Count > 0)
        {
            var id = pending.Pop();
            result.Add(id);
            foreach (var child in hierarchy.ChildrenByNode[id].Reverse()) pending.Push(child);
        }
        return result;
    }

    private static IReadOnlyList<string> ProjectNodes(string projectId, PlacedGraph placement)
    {
        if (!placement.ProjectPlacement.NodeIdsByProject.TryGetValue(projectId, out var ids))
            throw new InvalidOperationException($"Unknown project {projectId}.");
        return ids;
    }
}
