using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using StandardIo.ArchitectureDiagram.Core.Models;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

/// <summary>
/// Typed-renderer-only semantic depth authority. Produces depths and diagnostics before positional placement.
/// </summary>
internal static class ConfiguredSemanticLayerPlacement
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);

    public static ConfiguredSemanticLayerPlacementResult Assign(
        RenderGraph graph, DiagramSettings settings, LayoutRevision revision,
        IReadOnlyDictionary<string, int>? originalDepthOverride = null)
    {
        var hierarchy = HierarchyAnalyzer.Analyze(graph, revision);
        var originalDepths = (originalDepthOverride ?? hierarchy.VisualLayerByNode)
            .ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        var rules = settings.Layout.NodeLayerGroups ?? new List<NodeLayerGroupRule>();
        if (rules.Count == 0)
            return new ConfiguredSemanticLayerPlacementResult(false, Array.Empty<SemanticLayerGroupDiagnostic>(),
                Array.Empty<UnmatchedSemanticLayerDiagnostic>(), originalDepths, originalDepths,
                graph.Nodes.Count(node => node.IsExternal), new Dictionary<string, int>());

        var compiledRules = rules.Select((rule, index) => new CompiledRule(index, rule.Name, rule.Pattern,
            new Regex(rule.Pattern, RegexOptions.CultureInvariant, RegexTimeout))).ToArray();
        var finalDepths = new Dictionary<string, int>(originalDepths, StringComparer.Ordinal);
        var groups = new List<SemanticLayerGroupDiagnostic>();
        var unmatchedDiagnostics = new List<UnmatchedSemanticLayerDiagnostic>();
        var externalDepths = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var project in graph.Projects.OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            var projectNodes = graph.Nodes.Where(node => node.ProjectId == project.Id).ToArray();
            var internalNodes = projectNodes.Where(node => !node.IsExternal)
                .OrderBy(node => node.Id, StringComparer.Ordinal).ToArray();
            var firstRuleByNode = internalNodes.ToDictionary(node => node.Id,
                node => compiledRules.FirstOrDefault(rule => rule.Regex.IsMatch(node.Name)), StringComparer.Ordinal);
            var matches = compiledRules.Select(rule => new
                {
                    Rule = rule,
                    Nodes = internalNodes.Where(node => firstRuleByNode[node.Id] == rule).ToArray()
                }).Where(item => item.Nodes.Length > 0).ToArray();

            if (matches.Length == 0)
            {
                SetExternalDepth(project.Id, projectNodes,
                    internalNodes.Select(node => originalDepths[node.Id]).DefaultIfEmpty(-1).Max() + 1);
                continue;
            }

            var projectDepths = new Dictionary<string, int>(StringComparer.Ordinal);
            var matchedNodeIds = new HashSet<string>(StringComparer.Ordinal);
            var majorityBySemanticDepth = new Dictionary<int, int>();
            for (var semanticDepth = 0; semanticDepth < matches.Length; semanticDepth++)
            {
                var match = matches[semanticDepth];
                var distribution = match.Nodes.GroupBy(node => originalDepths[node.Id]).OrderBy(group => group.Key)
                    .ToDictionary(group => group.Key, group => group.Count());
                var majority = distribution.OrderByDescending(item => item.Value).ThenBy(item => item.Key).First().Key;
                majorityBySemanticDepth[semanticDepth] = majority;
                groups.Add(new SemanticLayerGroupDiagnostic(project.Id, match.Rule.Order, match.Rule.Name,
                    match.Rule.Pattern, match.Nodes.Length, distribution, majority, semanticDepth));
                foreach (var node in match.Nodes)
                {
                    matchedNodeIds.Add(node.Id);
                    projectDepths[node.Id] = semanticDepth;
                }
            }

            var internalIds = new HashSet<string>(internalNodes.Select(node => node.Id), StringComparer.Ordinal);
            var internalLinks = graph.Links.Where(link => internalIds.Contains(link.SourceId) &&
                internalIds.Contains(link.TargetId)).ToArray();
            var incoming = internalLinks.GroupBy(link => link.TargetId, StringComparer.Ordinal).ToDictionary(
                group => group.Key, group => group.Select(link => link.SourceId).Distinct(StringComparer.Ordinal).ToArray(),
                StringComparer.Ordinal);
            var outgoing = internalLinks.GroupBy(link => link.SourceId, StringComparer.Ordinal).ToDictionary(
                group => group.Key, group => group.Select(link => link.TargetId).Distinct(StringComparer.Ordinal).ToArray(),
                StringComparer.Ordinal);
            var remaining = internalNodes.Where(node => !projectDepths.ContainsKey(node.Id))
                .ToDictionary(node => node.Id, StringComparer.Ordinal);

            while (remaining.Count > 0)
            {
                var candidates = remaining.Values.Select(node => Candidate(node, originalDepths[node.Id], incoming,
                        outgoing, projectDepths, matchedNodeIds))
                    .Where(candidate => candidate.PlacedNeighbourCount > 0)
                    .OrderByDescending(candidate => candidate.PlacedNeighbourCount)
                    .ThenByDescending(candidate => candidate.MatchedPlacedNeighbourCount)
                    .ThenBy(candidate => candidate.OriginalDepth)
                    .ThenBy(candidate => candidate.Node.Id, StringComparer.Ordinal).ToArray();
                if (candidates.Length == 0) break;
                foreach (var candidate in candidates)
                {
                    if (!remaining.Remove(candidate.Node.Id)) continue;
                    var decision = Decide(candidate.OriginalDepth, candidate.ParentIds, candidate.ChildIds,
                        projectDepths, majorityBySemanticDepth);
                    projectDepths[candidate.Node.Id] = decision.Depth;
                    unmatchedDiagnostics.Add(new UnmatchedSemanticLayerDiagnostic(project.Id, candidate.Node.Id,
                        candidate.Node.Name, candidate.OriginalDepth, Evidence(candidate.ParentIds, projectDepths),
                        Evidence(candidate.ChildIds, projectDepths), decision.Depth, decision.Reason, decision.Conflict));
                }
            }

            foreach (var node in remaining.Values.OrderBy(node => originalDepths[node.Id])
                         .ThenBy(node => node.Id, StringComparer.Ordinal))
            {
                var depth = NearestMajority(originalDepths[node.Id], majorityBySemanticDepth);
                projectDepths[node.Id] = depth;
                unmatchedDiagnostics.Add(new UnmatchedSemanticLayerDiagnostic(project.Id, node.Id, node.Name,
                    originalDepths[node.Id], Array.Empty<string>(), Array.Empty<string>(), depth,
                    "OriginalDepthNearestGroupMajority", false));
            }

            foreach (var item in projectDepths) finalDepths[item.Key] = item.Value;
            SetExternalDepth(project.Id, projectNodes, projectDepths.Values.DefaultIfEmpty(-1).Max() + 1);
        }

        return new ConfiguredSemanticLayerPlacementResult(true, groups, unmatchedDiagnostics, originalDepths,
            finalDepths, graph.Nodes.Count(node => node.IsExternal), externalDepths);

        void SetExternalDepth(string projectId, IEnumerable<RenderNode> projectNodes, int depth)
        {
            var externals = projectNodes.Where(node => node.IsExternal).ToArray();
            if (externals.Length == 0) return;
            externalDepths[projectId] = depth;
            foreach (var external in externals) finalDepths[external.Id] = depth;
        }
    }

    private static CandidateEvidence Candidate(RenderNode node, int originalDepth,
        IReadOnlyDictionary<string, string[]> incoming, IReadOnlyDictionary<string, string[]> outgoing,
        IReadOnlyDictionary<string, int> placed, ISet<string> matched)
    {
        var parents = incoming.TryGetValue(node.Id, out var parentIds) ? parentIds : Array.Empty<string>();
        var children = outgoing.TryGetValue(node.Id, out var childIds) ? childIds : Array.Empty<string>();
        var neighbours = parents.Concat(children).Distinct(StringComparer.Ordinal).Where(placed.ContainsKey).ToArray();
        return new CandidateEvidence(node, originalDepth, parents, children, neighbours.Length,
            neighbours.Count(matched.Contains));
    }

    private static LayerDecision Decide(int originalDepth, IReadOnlyList<string> parentIds,
        IReadOnlyList<string> childIds, IReadOnlyDictionary<string, int> placed,
        IReadOnlyDictionary<int, int> majorities)
    {
        var layerCount = majorities.Count;
        var parents = parentIds.Where(placed.ContainsKey).Select(id => placed[id]).ToArray();
        var children = childIds.Where(placed.ContainsKey).Select(id => placed[id]).ToArray();
        var minimum = parents.Length == 0 ? 0 : parents.Max() + 1;
        var maximum = children.Length == 0 ? layerCount - 1 : children.Min() - 1;
        var conflict = minimum > maximum;
        if (children.Length > 0)
        {
            var mode = children.Select(depth => depth - 1).GroupBy(depth => depth)
                .OrderByDescending(group => group.Count()).ThenBy(group => group.Key).First().Key;
            var selected = conflict ? Clamp(mode, 0, layerCount - 1) :
                Clamp(mode, Math.Max(0, minimum), Math.Min(layerCount - 1, maximum));
            return new LayerDecision(selected, conflict ? "ConstraintConflictChildPriority" : "ChildDerivedMode", conflict);
        }
        if (parents.Length > 0)
            return new LayerDecision(Clamp(parents.Max() + 1, 0, layerCount - 1), "ParentDerived", false);
        return new LayerDecision(NearestMajority(originalDepth, majorities),
            "OriginalDepthNearestGroupMajority", false);
    }

    private static int NearestMajority(int originalDepth, IReadOnlyDictionary<int, int> majorities) =>
        majorities.OrderBy(item => Math.Abs(item.Value - originalDepth)).ThenBy(item => item.Key).First().Key;
    private static int Clamp(int value, int minimum, int maximum) => Math.Min(maximum, Math.Max(minimum, value));
    private static string[] Evidence(IEnumerable<string> ids, IReadOnlyDictionary<string, int> placed) =>
        ids.Where(placed.ContainsKey).OrderBy(id => id, StringComparer.Ordinal)
            .Select(id => $"{id}:layer-{placed[id]}").ToArray();

    private sealed record CompiledRule(int Order, string Name, string Pattern, Regex Regex);
    private sealed record CandidateEvidence(RenderNode Node, int OriginalDepth, IReadOnlyList<string> ParentIds,
        IReadOnlyList<string> ChildIds, int PlacedNeighbourCount, int MatchedPlacedNeighbourCount);
    private sealed record LayerDecision(int Depth, string Reason, bool Conflict);
}
