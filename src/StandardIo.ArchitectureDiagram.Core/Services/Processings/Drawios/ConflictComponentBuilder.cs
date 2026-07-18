using System;
using System.Collections.Generic;
using System.Linq;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal readonly record struct ConflictEdge(string FirstId, string SecondId, string Cause);

internal sealed record ConflictComponent<T>(
    string Id,
    IReadOnlyList<T> Members,
    IReadOnlyList<string> Causes);

internal static class ConflictComponentBuilder
{
    public static IReadOnlyList<ConflictComponent<T>> Build<T>(
        IEnumerable<T> items,
        Func<T, string> identity,
        IEnumerable<ConflictEdge> edges)
    {
        var ordered = items.OrderBy(identity, StringComparer.Ordinal).ToArray();
        var byId = ordered.ToDictionary(identity, StringComparer.Ordinal);
        var parent = byId.Keys.ToDictionary(id => id, id => id, StringComparer.Ordinal);
        var acceptedEdges = edges
            .Where(edge => byId.ContainsKey(edge.FirstId) && byId.ContainsKey(edge.SecondId))
            .Select(edge => string.CompareOrdinal(edge.FirstId, edge.SecondId) <= 0
                ? edge
                : new ConflictEdge(edge.SecondId, edge.FirstId, edge.Cause))
            .OrderBy(edge => edge.FirstId, StringComparer.Ordinal)
            .ThenBy(edge => edge.SecondId, StringComparer.Ordinal)
            .ThenBy(edge => edge.Cause, StringComparer.Ordinal)
            .ToArray();
        foreach (var edge in acceptedEdges) Union(parent, edge.FirstId, edge.SecondId);

        return ordered
            .GroupBy(item => Find(parent, identity(item)), StringComparer.Ordinal)
            .Select(group =>
            {
                var members = group.OrderBy(identity, StringComparer.Ordinal).ToArray();
                var memberIds = new HashSet<string>(members.Select(identity), StringComparer.Ordinal);
                var causes = acceptedEdges
                    .Where(edge => memberIds.Contains(edge.FirstId) && memberIds.Contains(edge.SecondId))
                    .Select(edge => edge.Cause)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(cause => cause, StringComparer.Ordinal)
                    .ToArray();
                return new ConflictComponent<T>(identity(members[0]), members, causes);
            })
            .OrderBy(component => component.Id, StringComparer.Ordinal)
            .ToArray();
    }

    private static string Find(IDictionary<string, string> parent, string id)
    {
        var root = id;
        while (!string.Equals(parent[root], root, StringComparison.Ordinal)) root = parent[root];
        while (!string.Equals(parent[id], id, StringComparison.Ordinal))
        {
            var next = parent[id];
            parent[id] = root;
            id = next;
        }
        return root;
    }

    private static void Union(IDictionary<string, string> parent, string first, string second)
    {
        var left = Find(parent, first);
        var right = Find(parent, second);
        if (string.Equals(left, right, StringComparison.Ordinal)) return;
        if (string.CompareOrdinal(left, right) < 0) parent[right] = left;
        else parent[left] = right;
    }
}
