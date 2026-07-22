using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using StandardIo.ArchitectureDiagram.Core.Models;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

/// <summary>
/// Typed-renderer-only vertical policy. It preserves the completed placement's horizontal geometry and
/// replaces only rendered depth before project-local Y alignment and routing.
/// </summary>
internal static class ConfiguredSemanticLayerPlacement
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);

    public static ConfiguredSemanticLayerPlacementResult Apply(
        PlacedGraph placement,
        DiagramSettings settings)
    {
        var rules = settings.Layout.NodeLayerGroups ?? new List<NodeLayerGroupRule>();
        var originalDepths = placement.Nodes.ToDictionary(item => item.Key, item => item.Value.Depth,
            StringComparer.Ordinal);
        if (rules.Count == 0)
            return Result(placement, false, Array.Empty<SemanticLayerGroupDiagnostic>(),
                Array.Empty<UnmatchedSemanticLayerDiagnostic>(), originalDepths,
                new Dictionary<string, int>(), placement.Nodes);

        var compiledRules = rules.Select((rule, index) => new CompiledRule(
            index, rule.Name, rule.Pattern,
            new Regex(rule.Pattern, RegexOptions.CultureInvariant, RegexTimeout))).ToArray();
        var nodes = placement.Nodes.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        var groups = new List<SemanticLayerGroupDiagnostic>();
        var unmatchedDiagnostics = new List<UnmatchedSemanticLayerDiagnostic>();
        var externalDepths = new Dictionary<string, int>(StringComparer.Ordinal);
        var matchedNodeIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var project in placement.Graph.Projects.OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            var projectNodes = nodes.Values.Where(node => node.Node.ProjectId == project.Id).ToArray();
            var internalNodes = projectNodes.Where(node => !node.Node.IsExternal)
                .OrderBy(node => node.Node.Id, StringComparer.Ordinal).ToArray();
            var firstRuleByNode = internalNodes.ToDictionary(node => node.Node.Id,
                node => compiledRules.FirstOrDefault(rule => rule.Regex.IsMatch(node.Node.Name)),
                StringComparer.Ordinal);
            var matches = compiledRules.Select(rule => new
                {
                    Rule = rule,
                    Nodes = internalNodes.Where(node => firstRuleByNode[node.Node.Id] == rule).ToArray()
                }).Where(item => item.Nodes.Length > 0).ToArray();

            // A project with no active configured group keeps its existing internal depths.
            if (matches.Length == 0)
            {
                SetExternalDepth(project.Id, projectNodes, internalNodes.Select(node => node.Depth).DefaultIfEmpty(-1).Max() + 1);
                continue;
            }

            var semanticDepthByNode = new Dictionary<string, int>(StringComparer.Ordinal);
            var majorityBySemanticDepth = new Dictionary<int, int>();
            for (var semanticDepth = 0; semanticDepth < matches.Length; semanticDepth++)
            {
                var match = matches[semanticDepth];
                var distribution = match.Nodes.GroupBy(node => node.Depth).OrderBy(group => group.Key)
                    .ToDictionary(group => group.Key, group => group.Count());
                var majority = distribution.OrderByDescending(item => item.Value).ThenBy(item => item.Key).First().Key;
                majorityBySemanticDepth[semanticDepth] = majority;
                groups.Add(new SemanticLayerGroupDiagnostic(
                    project.Id, match.Rule.Order, match.Rule.Name, match.Rule.Pattern, match.Nodes.Length,
                    distribution, majority, semanticDepth));
                foreach (var node in match.Nodes)
                {
                    matchedNodeIds.Add(node.Node.Id);
                    semanticDepthByNode[node.Node.Id] = semanticDepth;
                }
            }

            var projectInternalIds = new HashSet<string>(internalNodes.Select(node => node.Node.Id), StringComparer.Ordinal);
            var incoming = placement.Graph.Links.Where(link => projectInternalIds.Contains(link.SourceId) &&
                    projectInternalIds.Contains(link.TargetId))
                .GroupBy(link => link.TargetId, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Select(link => link.SourceId).Distinct(StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);
            var outgoing = placement.Graph.Links.Where(link => projectInternalIds.Contains(link.SourceId) &&
                    projectInternalIds.Contains(link.TargetId))
                .GroupBy(link => link.SourceId, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Select(link => link.TargetId).Distinct(StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);
            var remaining = internalNodes.Where(node => !semanticDepthByNode.ContainsKey(node.Node.Id))
                .ToDictionary(node => node.Node.Id, StringComparer.Ordinal);

            while (remaining.Count > 0)
            {
                var candidates = remaining.Values.Select(node => Candidate(node, incoming, outgoing,
                        semanticDepthByNode, matchedNodeIds))
                    .Where(candidate => candidate.PlacedNeighbourCount > 0)
                    .OrderByDescending(candidate => candidate.PlacedNeighbourCount)
                    .ThenByDescending(candidate => candidate.MatchedPlacedNeighbourCount)
                    .ThenBy(candidate => candidate.Node.Depth)
                    .ThenBy(candidate => candidate.Node.Node.Id, StringComparer.Ordinal).ToArray();
                if (candidates.Length == 0) break;
                foreach (var candidate in candidates)
                {
                    if (!remaining.Remove(candidate.Node.Node.Id)) continue;
                    var decision = Decide(candidate.Node, candidate.ParentIds, candidate.ChildIds,
                        semanticDepthByNode, matchedNodeIds, majorityBySemanticDepth);
                    semanticDepthByNode[candidate.Node.Node.Id] = decision.Depth;
                    unmatchedDiagnostics.Add(new UnmatchedSemanticLayerDiagnostic(
                        project.Id, candidate.Node.Node.Id, candidate.Node.Node.Name, candidate.Node.Depth,
                        Evidence(candidate.ParentIds, semanticDepthByNode),
                        Evidence(candidate.ChildIds, semanticDepthByNode),
                        decision.Depth, decision.Reason, decision.Conflict));
                }
            }

            foreach (var node in remaining.Values.OrderBy(node => node.Depth)
                         .ThenBy(node => node.Node.Id, StringComparer.Ordinal))
            {
                var depth = NearestMajority(node.Depth, majorityBySemanticDepth);
                semanticDepthByNode[node.Node.Id] = depth;
                unmatchedDiagnostics.Add(new UnmatchedSemanticLayerDiagnostic(
                    project.Id, node.Node.Id, node.Node.Name, node.Depth,
                    Array.Empty<string>(), Array.Empty<string>(), depth,
                    "OriginalDepthNearestGroupMajority", false));
            }

            foreach (var item in semanticDepthByNode)
                nodes[item.Key] = nodes[item.Key] with { Depth = item.Value };
            SetExternalDepth(project.Id, projectNodes, semanticDepthByNode.Values.DefaultIfEmpty(-1).Max() + 1);
        }

        var revised = placement.Revise(nodes, PlacementPipeline.PositionProjects(placement.Graph, settings, nodes));
        return Result(revised, true, groups, unmatchedDiagnostics, originalDepths, externalDepths, placement.Nodes);

        void SetExternalDepth(string projectId, IEnumerable<NodeLayout> projectNodes, int depth)
        {
            var externals = projectNodes.Where(node => node.Node.IsExternal).ToArray();
            if (externals.Length == 0) return;
            externalDepths[projectId] = depth;
            foreach (var external in externals) nodes[external.Node.Id] = external with { Depth = depth };
        }
    }

    private static CandidateEvidence Candidate(
        NodeLayout node,
        IReadOnlyDictionary<string, string[]> incoming,
        IReadOnlyDictionary<string, string[]> outgoing,
        IReadOnlyDictionary<string, int> placed,
        ISet<string> matched)
    {
        var parents = incoming.TryGetValue(node.Node.Id, out var parentIds) ? parentIds : Array.Empty<string>();
        var children = outgoing.TryGetValue(node.Node.Id, out var childIds) ? childIds : Array.Empty<string>();
        var neighbours = parents.Concat(children).Distinct(StringComparer.Ordinal).Where(placed.ContainsKey).ToArray();
        return new CandidateEvidence(node, parents, children, neighbours.Length, neighbours.Count(matched.Contains));
    }

    private static LayerDecision Decide(
        NodeLayout node,
        IReadOnlyList<string> parentIds,
        IReadOnlyList<string> childIds,
        IReadOnlyDictionary<string, int> placed,
        ISet<string> matched,
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
            var candidates = children.Select(depth => depth - 1).ToArray();
            var mode = candidates.GroupBy(depth => depth).OrderByDescending(group => group.Count())
                .ThenBy(group => group.Key).First().Key;
            var selected = conflict
                ? Clamp(mode, 0, layerCount - 1)
                : Clamp(mode, Math.Max(0, minimum), Math.Min(layerCount - 1, maximum));
            return new LayerDecision(selected,
                conflict ? "ConstraintConflictChildPriority" : "ChildDerivedMode", conflict);
        }
        if (parents.Length > 0)
        {
            var preferred = parents.Max() + 1;
            return new LayerDecision(Clamp(preferred, 0, layerCount - 1), "ParentDerived", false);
        }
        return new LayerDecision(NearestMajority(node.Depth, majorities),
            "OriginalDepthNearestGroupMajority", false);
    }

    private static int NearestMajority(int originalDepth, IReadOnlyDictionary<int, int> majorities) =>
        majorities.OrderBy(item => Math.Abs(item.Value - originalDepth)).ThenBy(item => item.Key).First().Key;

    private static int Clamp(int value, int minimum, int maximum) => Math.Min(maximum, Math.Max(minimum, value));

    private static string[] Evidence(IEnumerable<string> ids, IReadOnlyDictionary<string, int> placed) =>
        ids.Where(placed.ContainsKey).OrderBy(id => id, StringComparer.Ordinal)
            .Select(id => $"{id}:layer-{placed[id]}").ToArray();

    private static ConfiguredSemanticLayerPlacementResult Result(
        PlacedGraph placement,
        bool enabled,
        IReadOnlyList<SemanticLayerGroupDiagnostic> groups,
        IReadOnlyList<UnmatchedSemanticLayerDiagnostic> unmatched,
        IReadOnlyDictionary<string, int> originalDepths,
        IReadOnlyDictionary<string, int> externalDepths,
        IReadOnlyDictionary<string, NodeLayout> baseline)
    {
        var xDeltas = placement.Nodes.Values.Select(node => Math.Abs(node.Rect.X - baseline[node.Node.Id].Rect.X)).ToArray();
        var widthDeltas = placement.Nodes.Values.Select(node => Math.Abs(node.Rect.Width - baseline[node.Node.Id].Rect.Width)).ToArray();
        var overlaps = placement.Nodes.Values.Where(node => node.Node.ProjectId is not null)
            .GroupBy(node => (node.Node.ProjectId!, node.Depth)).SelectMany(group =>
                group.OrderBy(node => node.Rect.X).ThenBy(node => node.Node.Id, StringComparer.Ordinal)
                    .SelectMany((left, index) => group.Skip(index + 1).Where(right =>
                            Math.Min(left.Rect.Right, right.Rect.Right) > Math.Max(left.Rect.X, right.Rect.X))
                        .Select(right => new SemanticLayerOverlapDiagnostic(
                            group.Key.Item1, group.Key.Depth, left.Node.Id, right.Node.Id,
                            Math.Min(left.Rect.Right, right.Rect.Right) - Math.Max(left.Rect.X, right.Rect.X)))))
            .OrderBy(item => item.ProjectId, StringComparer.Ordinal).ThenBy(item => item.SemanticDepth)
            .ThenBy(item => item.FirstNodeId, StringComparer.Ordinal).ThenBy(item => item.SecondNodeId, StringComparer.Ordinal)
            .ToArray();
        return new ConfiguredSemanticLayerPlacementResult(
            placement, enabled, groups, unmatched, overlaps, originalDepths,
            placement.Nodes.ToDictionary(item => item.Key, item => item.Value.Depth, StringComparer.Ordinal),
            placement.Nodes.Values.Count(node => node.Node.IsExternal), externalDepths,
            xDeltas.Count(delta => delta != 0), widthDeltas.Count(delta => delta != 0),
            xDeltas.DefaultIfEmpty(0).Max(), widthDeltas.DefaultIfEmpty(0).Max());
    }

    private sealed record CompiledRule(int Order, string Name, string Pattern, Regex Regex);
    private sealed record CandidateEvidence(
        NodeLayout Node, IReadOnlyList<string> ParentIds, IReadOnlyList<string> ChildIds,
        int PlacedNeighbourCount, int MatchedPlacedNeighbourCount);
    private sealed record LayerDecision(int Depth, string Reason, bool Conflict);
}
