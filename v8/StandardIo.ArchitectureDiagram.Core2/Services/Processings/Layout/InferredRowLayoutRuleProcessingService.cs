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
            n => namedGroups.GetValueOrDefault(n.TypeName) ?? "unmatched");
        var outgoing = links.ToLookup(e => e.SourceId, e => e.TargetId);
        var incoming = links.ToLookup(e => e.TargetId, e => e.SourceId);
        var pending = nodes.Keys.ToHashSet(StringComparer.Ordinal);
        var counts = nodes.Keys.ToDictionary(id => id, id => incoming[id].Count());
        var rows = new Dictionary<string, int>();
        var rowCategories = new Dictionary<int, string>();
        int row = 0;
        while (pending.Count > 0)
        {
            var ready = pending.Where(id => counts[id] == 0).ToArray();
            // The depth rule already removed cycle-closing links, so ready cannot be empty.
            var groups = ready.GroupBy(id => membership[id]).ToArray();
            string category = groups.OrderBy(g => pending.Where(id => membership[id] == g.Key)
                    .Any(id => incoming[id].Any(parent => pending.Contains(parent) && membership[parent] != g.Key)))
                .ThenBy(g => g.Min(id => nodes[id].Y)).ThenBy(g => g.Key, StringComparer.Ordinal).First().Key;
            // Consume a category as one band where the dependency graph permits it.
            // If another category must intervene, retain the category on a later exclusive row.
            while (true)
            {
                var occupants = pending.Where(id => membership[id] == category && counts[id] == 0).ToArray();
                if (occupants.Length == 0) break;
                rowCategories[row] = category;
                foreach (string id in occupants) { rows[id] = row; pending.Remove(id); }
                foreach (string id in occupants)
                    foreach (string child in outgoing[id]) counts[child]--;
                row++;
            }
        }
        model.Rows = rows.GroupBy(p => p.Value).ToDictionary(g => rowCategories[g.Key] + ":" + g.Key,
            g => new RenderRowGroup(g.Key, g.Select(p => p.Key).ToArray()));
        // Neighbouring projects use the same slots; downstream tiers start fresh and
        // omit bands absent from every project in that tier.
        var tierRows = model.Projects.GroupBy(p => projectRows[p.Id]).ToDictionary(g => g.Key,
            g => g.SelectMany(p => p.Nodes).Select(n => rows[n.Id]).Distinct().OrderBy(r => r)
                .Select((value, index) => (value, index)).ToDictionary(p => p.value, p => p.index));
        foreach (var project in model.Projects)
            for (int i = 0; i < project.Nodes.Length; i++)
            {
                var node = project.Nodes[i];
                project.Nodes[i] = node with { Y = 60 + tierRows[projectRows[project.Id]][rows[node.Id]] * model.Configuration.Architecture.RowDepth };
            }
    }
}
