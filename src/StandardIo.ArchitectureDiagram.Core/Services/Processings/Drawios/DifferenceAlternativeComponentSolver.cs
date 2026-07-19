using System;
using System.Collections.Generic;
using System.Linq;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal static class DifferenceAlternativeComponentSolver
{
    public static DifferenceAlternativeSelection Solve(IEnumerable<DifferenceAlternativeChoice> source)
    {
        var available = source.OrderBy(item => item.ConflictId, StringComparer.Ordinal)
            .ThenBy(item => item.AlternativeId, StringComparer.Ordinal).ToArray();
        var conflicts = available.GroupBy(item => item.ConflictId, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.GroupBy(item => item.AlternativeId, StringComparer.Ordinal)
                .Select(bundle => bundle.OrderBy(item => item.MovingStructureId, StringComparer.Ordinal)
                    .ThenBy(item => item.OpposingStructureId, StringComparer.Ordinal).ToArray())
                .OrderBy(Score).ThenBy(item => item[0].AlternativeId, StringComparer.Ordinal).ToArray(),
                StringComparer.Ordinal);
        if (conflicts.Any(item => item.Value.Length == 0))
            return new DifferenceAlternativeSelection(available, Array.Empty<DifferenceAlternativeChoice>(),
                Array.Empty<PositiveDifferenceCycle>(), 0, 0, 0, false);

        var initial = conflicts.ToDictionary(item => item.Key, item => item.Value[0], StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        DifferenceAlternativeChoice[]? best = null;
        var states = 0;
        var rejected = 0;
        var complete = 0;
        IReadOnlyList<PositiveDifferenceCycle> lastCycles = Array.Empty<PositiveDifferenceCycle>();

        Search(initial, new HashSet<string>(StringComparer.Ordinal));
        return new DifferenceAlternativeSelection(available, best ?? Array.Empty<DifferenceAlternativeChoice>(), lastCycles,
            states, rejected, complete, best is not null);

        void Search(Dictionary<string, DifferenceAlternativeChoice[]> selected, HashSet<string> frozen)
        {
            var signature = string.Join("|", selected.OrderBy(item => item.Key, StringComparer.Ordinal)
                .Select(item => item.Value[0].AlternativeId)) + "#" +
                string.Join("|", frozen.OrderBy(item => item, StringComparer.Ordinal));
            if (!visited.Add(signature)) return;
            states++;
            var cycles = FindPositiveCycles(selected.Values.SelectMany(item => item));
            if (cycles.Count == 0)
            {
                complete++;
                var candidate = selected.OrderBy(item => item.Key, StringComparer.Ordinal)
                    .SelectMany(item => item.Value).ToArray();
                if (best is null || Compare(candidate, best) < 0) best = candidate;
                return;
            }

            rejected++;
            lastCycles = cycles;
            var participating = cycles.SelectMany(cycle => cycle.Edges).Select(edge => edge.ConflictId)
                .Distinct(StringComparer.Ordinal)
                .Where(id => !frozen.Contains(id))
                .OrderBy(id => conflicts[id].Length)
                .ThenByDescending(id => cycles.Sum(cycle => cycle.Edges.Count(edge => edge.ConflictId == id)))
                .ThenByDescending(id => conflicts[id].Max(bundle => bundle.Sum(item => item.Movement)))
                .ThenBy(id => id, StringComparer.Ordinal).FirstOrDefault();
            if (participating is null) return;
            foreach (var alternative in conflicts[participating])
            {
                var next = new Dictionary<string, DifferenceAlternativeChoice[]>(selected, StringComparer.Ordinal)
                {
                    [participating] = alternative
                };
                var nextFrozen = new HashSet<string>(frozen, StringComparer.Ordinal) { participating };
                Search(next, nextFrozen);
            }
        }
    }

    internal static IReadOnlyList<PositiveDifferenceCycle> FindPositiveCycles(
        IEnumerable<DifferenceAlternativeChoice> selected)
    {
        var edges = selected.OrderBy(item => item.AlternativeId, StringComparer.Ordinal).ToArray();
        var vertices = edges.SelectMany(item => new[] { item.MovingStructureId, item.OpposingStructureId })
            .Distinct(StringComparer.Ordinal).OrderBy(item => item, StringComparer.Ordinal).ToArray();
        var adjacency = vertices.ToDictionary(item => item, _ => new List<string>(), StringComparer.Ordinal);
        foreach (var edge in edges)
        {
            var from = edge.Direction == HorizontalMovementDirection.Right
                ? edge.OpposingStructureId : edge.MovingStructureId;
            var to = edge.Direction == HorizontalMovementDirection.Right
                ? edge.MovingStructureId : edge.OpposingStructureId;
            adjacency[from].Add(to);
        }

        var components = StronglyConnected(vertices, adjacency);
        var result = new List<PositiveDifferenceCycle>();
        foreach (var component in components.Where(item => item.Count > 1 ||
                     edges.Any(edge => edge.MovingStructureId == edge.OpposingStructureId &&
                                       item.Contains(edge.MovingStructureId))))
        {
            var members = new HashSet<string>(component, StringComparer.Ordinal);
            var componentEdges = edges.Where(edge => members.Contains(edge.MovingStructureId) &&
                                                     members.Contains(edge.OpposingStructureId)).ToArray();
            if (!componentEdges.Any(edge => edge.Weight > 0)) continue;
            result.Add(new PositiveDifferenceCycle(component, componentEdges,
                componentEdges.Where(edge => edge.Weight > 0).Sum(edge => edge.Weight)));
        }
        return result.OrderBy(item => string.Join("|", item.ScopeIds), StringComparer.Ordinal).ToArray();
    }

    private static IReadOnlyList<IReadOnlyList<string>> StronglyConnected(
        IReadOnlyList<string> vertices,
        IReadOnlyDictionary<string, List<string>> adjacency)
    {
        var index = 0;
        var indexes = new Dictionary<string, int>(StringComparer.Ordinal);
        var low = new Dictionary<string, int>(StringComparer.Ordinal);
        var stack = new Stack<string>();
        var onStack = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<IReadOnlyList<string>>();
        foreach (var vertex in vertices)
            if (!indexes.ContainsKey(vertex)) Visit(vertex);
        return result;

        void Visit(string vertex)
        {
            indexes[vertex] = low[vertex] = index++;
            stack.Push(vertex);
            onStack.Add(vertex);
            foreach (var next in adjacency[vertex].OrderBy(item => item, StringComparer.Ordinal))
            {
                if (!indexes.ContainsKey(next))
                {
                    Visit(next);
                    low[vertex] = Math.Min(low[vertex], low[next]);
                }
                else if (onStack.Contains(next)) low[vertex] = Math.Min(low[vertex], indexes[next]);
            }
            if (low[vertex] != indexes[vertex]) return;
            var component = new List<string>();
            string item;
            do
            {
                item = stack.Pop();
                onStack.Remove(item);
                component.Add(item);
            } while (item != vertex);
            result.Add(component.OrderBy(value => value, StringComparer.Ordinal).ToArray());
        }
    }

    private static (int movement, int nodes, int width, string id) Score(DifferenceAlternativeChoice[] bundle) =>
        (bundle.Sum(item => item.Movement), bundle.Sum(item => item.MovedNodeCount),
            bundle.Sum(item => item.WidthExpansion), bundle[0].AlternativeId);

    private static int Compare(IReadOnlyList<DifferenceAlternativeChoice> left,
        IReadOnlyList<DifferenceAlternativeChoice> right)
    {
        var comparison = left.Sum(item => item.Movement).CompareTo(right.Sum(item => item.Movement));
        if (comparison != 0) return comparison;
        comparison = left.Sum(item => item.MovedNodeCount).CompareTo(right.Sum(item => item.MovedNodeCount));
        if (comparison != 0) return comparison;
        comparison = left.Sum(item => item.WidthExpansion).CompareTo(right.Sum(item => item.WidthExpansion));
        return comparison != 0 ? comparison : string.CompareOrdinal(
            string.Join("|", left.Select(item => item.AlternativeId)),
            string.Join("|", right.Select(item => item.AlternativeId)));
    }
}
