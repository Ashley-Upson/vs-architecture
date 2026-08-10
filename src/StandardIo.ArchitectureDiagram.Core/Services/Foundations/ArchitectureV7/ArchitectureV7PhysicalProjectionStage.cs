using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using StandardIo.ArchitectureDiagram.Core.Models.Architectures;
using StandardIo.ArchitectureDiagram.Core.Models.ArchitectureV7;
using StandardIo.ArchitectureDiagram.Core.Models;
using ArchitectureDiagramModel = StandardIo.ArchitectureDiagram.Core.Models.Architectures.ArchitectureDiagram;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.ArchitectureV7;

public sealed class ArchitectureV7PhysicalProjectionStage
{
    public ArchitectureV7PhysicalProjectionResult Project(
        ArchitectureDiagramModel semanticDiagram,
        ArchitectureV7ProjectionPolicy policy)
    {
        if (semanticDiagram is null) throw new ArgumentNullException(nameof(semanticDiagram));
        if (policy is null) throw new ArgumentNullException(nameof(policy));

        var semanticNodes = ReadNodes(semanticDiagram).ToArray();
        var nodesById = semanticNodes.ToDictionary(node => node.Id, StringComparer.Ordinal);
        var inputOrdinals = semanticDiagram.Links
            .Select((link, index) => (link.Id, Index: index))
            .GroupBy(item => item.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().Index, StringComparer.Ordinal);
        var semanticLinks = (semanticDiagram.Links ?? Array.Empty<ArchitectureLink>())
            .OrderBy(link => link.Id, StringComparer.Ordinal)
            .ThenBy(link => link.SourceId, StringComparer.Ordinal)
            .ThenBy(link => link.TargetId, StringComparer.Ordinal)
            .ThenBy(link => link.Kind, StringComparer.Ordinal)
            .ToArray();
        var incoming = semanticLinks.Where(link => nodesById.ContainsKey(link.SourceId) && nodesById.ContainsKey(link.TargetId))
            .GroupBy(link => link.TargetId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

        var canonicalNodes = semanticNodes.Select(node => new ArchitectureV7PhysicalNode(
            "physical:" + node.Id,
            node.Id,
            node.ProjectId,
            node.IsExternal,
            node.IsStandalone,
            node.Name,
            node.FullName,
            node.Kind,
            ArchitectureV7ProjectionMode.Canonical,
            null,
            node.AnalyserOrdinal)).ToArray();
        var physicalNodes = canonicalNodes.ToDictionary(node => node.PhysicalNodeId, StringComparer.Ordinal);
        var nodeMap = semanticNodes.ToDictionary(node => node.Id,
            node => new List<string> { "physical:" + node.Id }, StringComparer.Ordinal);
        var duplicateOrdinals = new Dictionary<string, int>(StringComparer.Ordinal);
        var targetUses = new Dictionary<string, int>(StringComparer.Ordinal);
        var physicalLinks = new List<ArchitectureV7PhysicalLink>();
        var linkMap = semanticLinks.ToDictionary(link => link.Id, _ => new List<string>(), StringComparer.Ordinal);
        var diagnostics = new List<ArchitectureV7ProjectionDiagnostic>();
        var patterns = (policy.DuplicationPatterns ?? Array.Empty<string>())
            .Where(pattern => !string.IsNullOrWhiteSpace(pattern))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(pattern => pattern, StringComparer.Ordinal)
            .ToArray();
        var linkOccurrences = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var link in semanticLinks)
        {
            if (!nodesById.TryGetValue(link.SourceId, out var source) || !nodesById.TryGetValue(link.TargetId, out var target))
            {
                diagnostics.Add(new ArchitectureV7ProjectionDiagnostic(
                    "UnaccountedSemanticLink",
                    "A semantic link has an endpoint outside the selected semantic projection scope.",
                    link.Id));
                continue;
            }

            var targetPhysicalId = "physical:" + target.Id;
            var targetUseCount = targetUses.TryGetValue(target.Id, out var uses) ? uses : 0;
            if (policy.Mode == ArchitectureV7ProjectionMode.ConfiguredDuplicateBranches && targetUseCount > 0 &&
                patterns.Any(pattern => GlobMatcher.IsMatch(target.Name, pattern) || GlobMatcher.IsMatch(target.Id, pattern)))
            {
                var ordinal = duplicateOrdinals.TryGetValue(target.Id, out var current) ? current + 1 : 1;
                duplicateOrdinals[target.Id] = ordinal;
                targetPhysicalId = $"physical:{target.Id}:duplicate:{ordinal}";
                physicalNodes[targetPhysicalId] = new ArchitectureV7PhysicalNode(
                    targetPhysicalId,
                    target.Id,
                    target.ProjectId,
                    target.IsExternal,
                    false,
                    target.Name,
                    target.FullName,
                    target.Kind,
                    ArchitectureV7ProjectionMode.ConfiguredDuplicateBranches,
                    new ArchitectureV7DuplicationProvenance(target.Id, link.Id, "physical:" + source.Id, ordinal,
                        "Configured duplicate pattern matched a repeated semantic target use."),
                    target.AnalyserOrdinal);
                nodeMap[target.Id].Add(targetPhysicalId);
            }

            targetUses[target.Id] = targetUseCount + 1;
            var occurrence = linkOccurrences.TryGetValue(link.Id, out var linkOccurrence) ? linkOccurrence : 0;
            linkOccurrences[link.Id] = occurrence + 1;
            var physicalLinkId = occurrence == 0 ? "physical-link:" + link.Id : $"physical-link:{link.Id}:{occurrence}";
            var analyserOrdinal = link.AnalyserOrdinal >= 0
                ? link.AnalyserOrdinal
                : inputOrdinals.TryGetValue(link.Id, out var inputOrdinal) ? inputOrdinal : int.MaxValue;
            physicalLinks.Add(new ArchitectureV7PhysicalLink(physicalLinkId, link.Id, "physical:" + source.Id,
                targetPhysicalId, source.ProjectId, target.ProjectId, link.Kind, analyserOrdinal));
            linkMap[link.Id].Add(physicalLinkId);
        }

        foreach (var node in semanticNodes)
            if (!semanticLinks.Any(link => link.SourceId == node.Id || link.TargetId == node.Id))
                diagnostics.Add(new ArchitectureV7ProjectionDiagnostic("IsolatedSemanticNode", "Semantic node has no selected semantic relationships.", node.Id));

        var orderedNodes = physicalNodes.Values.OrderBy(node => node.ProjectId ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(node => node.SemanticNodeId, StringComparer.Ordinal)
            .ThenBy(node => node.PhysicalNodeId, StringComparer.Ordinal).ToArray();
        var orderedLinks = physicalLinks.OrderBy(link => link.SemanticLinkId, StringComparer.Ordinal)
            .ThenBy(link => link.PhysicalLinkId, StringComparer.Ordinal).ToArray();
        var readOnlyNodeMap = nodeMap.ToDictionary(item => item.Key,
            item => (IReadOnlyList<string>)item.Value.OrderBy(id => id, StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);
        var readOnlyLinkMap = linkMap.ToDictionary(item => item.Key,
            item => (IReadOnlyList<string>)item.Value.OrderBy(id => id, StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);
        var unaccountedNodes = semanticNodes.Where(node => !orderedNodes.Any(projected => projected.SemanticNodeId == node.Id))
            .Select(node => node.Id).OrderBy(id => id, StringComparer.Ordinal).ToArray();

        return new ArchitectureV7PhysicalProjectionResult(orderedNodes, orderedLinks, readOnlyNodeMap, readOnlyLinkMap,
            unaccountedNodes, semanticLinks.Where(link => readOnlyLinkMap[link.Id].Count == 0).Select(link => link.Id).Distinct(StringComparer.Ordinal)
                .OrderBy(id => id, StringComparer.Ordinal).ToArray(), diagnostics.OrderBy(item => item.Code, StringComparer.Ordinal)
                .ThenBy(item => item.SubjectId, StringComparer.Ordinal).ToArray(), Fingerprint(orderedNodes, orderedLinks, readOnlyNodeMap, readOnlyLinkMap));
    }

    private static IEnumerable<SemanticNode> ReadNodes(ArchitectureDiagramModel diagram)
    {
        var ordinal = 0;
        foreach (var project in diagram.Projects ?? Array.Empty<ArchitectureProject>())
            foreach (var node in project.Nodes ?? Array.Empty<ArchitectureNode>())
                yield return new SemanticNode(node.Id, project.Id, node.Name, node.FullName, node.Kind, false,
                    !diagram.Links.Any(link => link.SourceId == node.Id || link.TargetId == node.Id), ordinal++);
        foreach (var node in diagram.ExternalNodes ?? Array.Empty<ArchitectureExternalNode>())
            yield return new SemanticNode(node.Id, null, node.Name, node.FullName, "External", true,
                !diagram.Links.Any(link => link.SourceId == node.Id || link.TargetId == node.Id), ordinal++);
    }

    private static string Fingerprint(
        IReadOnlyList<ArchitectureV7PhysicalNode> nodes,
        IReadOnlyList<ArchitectureV7PhysicalLink> links,
        IReadOnlyDictionary<string, IReadOnlyList<string>> nodeMap,
        IReadOnlyDictionary<string, IReadOnlyList<string>> linkMap)
    {
        var text = string.Join("|", nodes.Select(node => string.Join(":", node.PhysicalNodeId, node.SemanticNodeId, node.ProjectId,
                node.ProjectionMode, node.DuplicationProvenance?.SemanticLinkId, node.AnalyserOrdinal))) + "#" +
            string.Join("|", links.Select(link => string.Join(":", link.PhysicalLinkId, link.SemanticLinkId, link.SourcePhysicalNodeId, link.DestinationPhysicalNodeId))) + "#" +
            string.Join("|", nodeMap.OrderBy(item => item.Key, StringComparer.Ordinal).Select(item => item.Key + "=" + string.Join(",", item.Value))) + "#" +
            string.Join("|", linkMap.OrderBy(item => item.Key, StringComparer.Ordinal).Select(item => item.Key + "=" + string.Join(",", item.Value)));
        using var sha = SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", string.Empty);
    }

    private sealed record SemanticNode(string Id, string? ProjectId, string Name, string FullName, string Kind, bool IsExternal, bool IsStandalone, int AnalyserOrdinal);
}
