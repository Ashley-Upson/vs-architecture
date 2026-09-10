// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System.Collections.Generic;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core2.Models;

namespace StandardIo.ArchitectureDiagram.Core2.Services.Processings.Layout;

// Connection endpoints remain fixed while layout moves nodes and updates route points.
internal sealed class LayoutTopology
{
    internal readonly Dictionary<string, string[]> Parents;
    internal readonly Dictionary<string, string[]> Children;

    internal LayoutTopology(RenderConnection[] connections)
    {
        var outgoing = connections.ToLookup(edge => edge.SourceId, edge => edge.TargetId);
        var reachable = outgoing.Select(group => group.Key).ToDictionary(id => id, id =>
        {
            var found = new HashSet<string>();
            var pending = new Queue<string>(outgoing[id]);
            while (pending.TryDequeue(out string? current))
                if (found.Add(current))
                    foreach (string child in outgoing[current]) pending.Enqueue(child);
            return found;
        });
        bool Reaches(string from, string to) => reachable.TryGetValue(from, out var found) && found.Contains(to);
        Parents = connections.Where(edge => edge.SourceId != edge.TargetId)
            .GroupBy(edge => edge.TargetId).ToDictionary(group => group.Key, group =>
            {
                var parents = group.Select(edge => edge.SourceId).Distinct().ToArray();
                // Keep every displayed edge. Only placement ignores an ancestor already represented
                // by a nearer parent. Mutually reachable parents belong to a cycle and are retained.
                return parents.Where(parent => !parents.Any(other => other != parent
                    && Reaches(parent, other) && !Reaches(other, parent)
                    && !Reaches(group.Key, other))).ToArray();
            });
        Children = Parents.SelectMany(pair => pair.Value.Select(parent => (Parent: parent, Child: pair.Key)))
            .GroupBy(pair => pair.Parent).ToDictionary(group => group.Key, group => group.Select(pair => pair.Child).ToArray());
    }
}
