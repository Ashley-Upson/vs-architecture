using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using StandardIo.ArchitectureDiagram.Core2.Models;

namespace StandardIo.ArchitectureDiagram.Core2.Services.Processings.Layout;

internal sealed class InferredRowLayoutRuleProcessingService(DepthLayoutRuleProcessingService depthRule) : ILayoutRuleProcessingService
{
    public void ApplyRule(RenderModel model)
    {
        if (model.IsProjectGraph || model.DiagramType != DiagramTypes.Architecture) return;
        var nodes = model.Projects.SelectMany(p => p.Nodes).ToDictionary(n => n.Id);
        var projectGraph = LayoutGraph.ProjectGraph(model);
        depthRule.ApplyRule(new RenderModel(projectGraph.Width, projectGraph.Height, [projectGraph])
            { Configuration = model.Configuration, IsProjectGraph = true });
        var projectRows = projectGraph.Nodes.ToDictionary(n => n.Id, n => n.Y);
        var nodeProjects = model.Projects.SelectMany(p => p.Nodes.Select(n => (n.Id, ProjectId: p.Id))).ToDictionary(n => n.Id, n => n.ProjectId);
        // Respect the existing cycle-breaking depth calculation.
        var links = model.Projects.SelectMany(p => p.Connections)
            .Where(e => nodes[e.TargetId].Y > nodes[e.SourceId].Y)
            .Concat(model.CrossProjectConnections.Where(e => projectRows[nodeProjects[e.TargetId]] > projectRows[nodeProjects[e.SourceId]]))
            .ToArray();
        var names = nodes.Values.Select(n => n.TypeName).Distinct().ToArray();
        string[] Words(string name) => Regex.Matches(name.Split('.').Last().Split('<', '`')[0],
            @"[A-Z]+(?=[A-Z][a-z]|$)|[A-Z]?[a-z]+|[0-9]+")
            .Select(m => m.Value).ToArray();
        var candidates = names.SelectMany(name =>
        {
            var words = Words(name);
            return Enumerable.Range(1, Math.Max(0, words.Length - 1)).SelectMany(count => new[] {
                (Name: name, Key: "prefix:" + string.Concat(words.Take(count)), Count: count),
                (Name: name, Key: "suffix:" + string.Concat(words.TakeLast(count)), Count: count) });
        }).GroupBy(c => c.Key).Where(g => g.Count() >= 2).Select(g => new
        {
            Key = g.Key, Count = g.First().Count, Names = g.Select(c => c.Name).ToHashSet(StringComparer.Ordinal)
        }).ToArray();
        var remaining = names.ToHashSet(StringComparer.Ordinal);
        var namedGroups = new Dictionary<string, string>(StringComparer.Ordinal);
        while (true)
        {
            var candidate = candidates.Select(c => new { c.Key, c.Count, Names = c.Names.Where(remaining.Contains).ToHashSet(StringComparer.Ordinal) })
                .Where(c => c.Names.Count >= 2 && c.Key.Split(':')[1].Length > 1)
                .OrderBy(c => links.Count(e => c.Names.Contains(nodes[e.SourceId].TypeName) && c.Names.Contains(nodes[e.TargetId].TypeName)))
                .ThenByDescending(c => c.Count).ThenByDescending(c => c.Key.StartsWith("suffix:", StringComparison.Ordinal))
                .ThenByDescending(c => c.Names.Count).ThenBy(c => c.Key, StringComparer.Ordinal).FirstOrDefault();
            if (candidate is null) break;
            foreach (string name in candidate.Names)
            {
                namedGroups[name] = candidate.Key;
                remaining.Remove(name);
            }
        }
        var membership = nodes.Values.ToDictionary(n => n.Id,
            n => namedGroups.GetValueOrDefault(n.TypeName) ?? "node:" + n.Id);

        string[] Order(Dictionary<string, string> groups)
        {
            var keys = groups.Values.Distinct().OrderBy(k => k, StringComparer.Ordinal).ToArray();
            var edges = links.Select(e => (From: groups[e.SourceId], To: groups[e.TargetId]))
                .Where(e => e.From != e.To).Distinct().ToArray();
            var counts = keys.ToDictionary(k => k, k => edges.Count(e => e.To == k));
            var pending = new Queue<string>(keys.Where(k => counts[k] == 0));
            var order = new List<string>();
            var outgoing = edges.ToLookup(e => e.From, e => e.To);
            while (pending.TryDequeue(out string? key))
            {
                order.Add(key);
                foreach (string child in outgoing[key]) if (--counts[child] == 0) pending.Enqueue(child);
            }
            return order.ToArray();
        }

        var ordered = Order(membership);
        var ambiguous = membership.Values.Except(ordered).ToHashSet();
        // Contradictory naming groups must not force contradictory dependency depths.
        foreach (string id in membership.Keys.ToArray())
            if (ambiguous.Contains(membership[id])) membership[id] = "node:" + id;
        ordered = Order(membership);
        var offset = nodes.Keys.ToDictionary(id => id, _ => 0);
        var outgoingLinks = links.ToLookup(e => e.SourceId);
        foreach (var node in nodes.Values.OrderBy(n => projectRows[nodeProjects[n.Id]]).ThenBy(n => n.Y))
            foreach (var edge in outgoingLinks[node.Id])
                if (membership[node.Id] == membership[edge.TargetId])
                    offset[edge.TargetId] = Math.Max(offset[edge.TargetId], offset[node.Id] + 1);
        double depth = model.Configuration.Architecture.RowDepth;
        var members = membership.ToLookup(p => p.Value, p => p.Key);
        var starts = members.ToDictionary(g => g.Key, g => g.Max(id => (int)Math.Round((nodes[id].Y - 60) / depth) - offset[id]));
        var heights = members.ToDictionary(g => g.Key, g => g.Max(id => offset[id]));
        var groupEdges = links.Select(e => (From: membership[e.SourceId], To: membership[e.TargetId]))
            .Where(e => e.From != e.To).Distinct().ToLookup(e => e.From, e => e.To);
        foreach (string key in ordered)
            foreach (string child in groupEdges[key]) starts[child] = Math.Max(starts[child], starts[key] + heights[key] + 1);
        model.Rows = members.Where(g => !g.Key.StartsWith("node:", StringComparison.Ordinal))
            .ToDictionary(g => g.Key, g => new RenderRowGroup(starts[g.Key], g.ToArray()));
        // Only side-by-side project groups share vertical slots. A downstream project
        // starts a fresh set of rows instead of repeating its parent's empty bands.
        var origins = model.Projects.GroupBy(p => projectRows[p.Id]).ToDictionary(g => g.Key,
            g => g.SelectMany(p => p.Nodes).Select(n => starts[membership[n.Id]] + offset[n.Id]).DefaultIfEmpty(0).Min());
        foreach (var project in model.Projects)
            for (int i = 0; i < project.Nodes.Length; i++)
            {
                var node = project.Nodes[i];
                project.Nodes[i] = node with { Y = 60 + (starts[membership[node.Id]] + offset[node.Id] - origins[projectRows[project.Id]]) * depth };
            }
    }
}
