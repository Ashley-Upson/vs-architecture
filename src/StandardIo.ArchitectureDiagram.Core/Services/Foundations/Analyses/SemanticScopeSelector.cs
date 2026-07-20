using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core.Models;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Analyses;

internal static class SemanticScopeSelector
{
    public static DiagramModel Select(DiagramModel source, DiagramSettings settings)
    {
        var patterns = RootDiscoveryPatternParser.Parse(settings.RootDiscoveryPatternsText);
        var internalNodes = source.Projects.SelectMany(project => project.Types).ToArray();
        var nodeIdentity = internalNodes.ToDictionary(node => node.Id, node => node.FullName, StringComparer.Ordinal);
        foreach (var external in source.ExternalDependencies) nodeIdentity[external.Id] = external.FullName;

        if (patterns.Count == 0)
            return source with { Metadata = Metadata(source, "FullSelectedInput", patterns,
                Array.Empty<SemanticRootMatch>(), nodeIdentity.Keys, Array.Empty<string>(),
                source.Edges.Select(edge => edge.Id), Array.Empty<string>(), Array.Empty<int>()) };

        var roots = new List<SemanticRootMatch>();
        var seenRoots = new HashSet<string>(StringComparer.Ordinal);
        var unmatched = new List<int>();
        foreach (var pattern in patterns)
        {
            var regex = RootDiscoveryPatternParser.Compile(pattern);
            var matches = nodeIdentity.OrderBy(item => item.Value, StringComparer.Ordinal)
                .ThenBy(item => item.Key, StringComparer.Ordinal).Where(item => regex.IsMatch(item.Value)).ToArray();
            if (matches.Length == 0) unmatched.Add(pattern.PatternIndex);
            foreach (var match in matches)
                if (seenRoots.Add(match.Key)) roots.Add(new SemanticRootMatch(
                    match.Key, match.Value, pattern.PatternIndex, pattern.SourceLine, pattern.PatternText));
        }

        var outgoing = source.Edges.GroupBy(edge => edge.SourceId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.OrderBy(edge => edge.Id, StringComparer.Ordinal).ToArray(),
                StringComparer.Ordinal);
        var selected = new HashSet<string>(roots.Select(root => root.SemanticNodeId), StringComparer.Ordinal);
        var pending = new Queue<string>(roots.Select(root => root.SemanticNodeId));
        while (pending.Count > 0)
        {
            var id = pending.Dequeue();
            if (!outgoing.TryGetValue(id, out var links)) continue;
            foreach (var link in links)
                if (nodeIdentity.ContainsKey(link.TargetId) && selected.Add(link.TargetId)) pending.Enqueue(link.TargetId);
        }

        var selectedEdges = source.Edges.Where(edge => selected.Contains(edge.SourceId) && selected.Contains(edge.TargetId))
            .OrderBy(edge => edge.Id, StringComparer.Ordinal).ToArray();
        var projects = source.Projects.Select(project => project with
            {
                Types = project.Types.Where(type => selected.Contains(type.Id) || IsDataModel(type))
                    .OrderBy(type => type.FullName, StringComparer.Ordinal).ToImmutableArray()
            }).Where(project => project.Types.Count > 0).ToImmutableArray();
        var selectedExternal = source.ExternalDependencies.Where(node => selected.Contains(node.Id))
            .OrderBy(node => node.FullName, StringComparer.Ordinal).ToImmutableArray();
        var retained = new HashSet<string>(projects.SelectMany(project => project.Types).Select(type => type.Id)
            .Concat(selectedExternal.Select(node => node.Id)), StringComparer.Ordinal);
        return new DiagramModel(projects, selectedExternal, selectedEdges,
            Metadata(source, "ConfiguredRootReachability", patterns, roots,
                retained.OrderBy(id => id, StringComparer.Ordinal), nodeIdentity.Keys.Where(id => !retained.Contains(id)),
                selectedEdges.Select(edge => edge.Id), source.Edges.Select(edge => edge.Id).Except(selectedEdges.Select(edge => edge.Id)),
                unmatched));
    }

    internal static bool IsDataModel(TypeNode type) => type.Kind != "Interface" && type.Properties?.Count > 0 && type.MethodCount == 0;

    private static DiagramMetadata Metadata(DiagramModel source, string policy,
        IReadOnlyList<RootDiscoveryPatternDefinition> patterns, IReadOnlyList<SemanticRootMatch> roots,
        IEnumerable<string> selectedNodes, IEnumerable<string> omittedNodes,
        IEnumerable<string> selectedLinks, IEnumerable<string> omittedLinks, IEnumerable<int> unmatched) =>
        new(source.Metadata?.SchemaVersion ?? 1, source.Metadata?.GeneratedBy ?? "StandardIo.ArchitectureDiagram",
            new SemanticSelectionReport(policy, patterns, roots,
                selectedNodes.OrderBy(id => id, StringComparer.Ordinal).ToArray(),
                omittedNodes.OrderBy(id => id, StringComparer.Ordinal).ToArray(),
                selectedLinks.OrderBy(id => id, StringComparer.Ordinal).ToArray(),
                omittedLinks.OrderBy(id => id, StringComparer.Ordinal).ToArray(), unmatched.ToArray()));
}
