using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using StandardIo.ArchitectureDiagram.Core.Models;
using StandardIo.ArchitectureDiagram.Core.Models.Architectures;
using ArchitectureDiagramModel = StandardIo.ArchitectureDiagram.Core.Models.Architectures.ArchitectureDiagram;

namespace StandardIo.ArchitectureDiagram.Core.Services.Processings.Architectures;

public sealed class ArchitectureTopologyProjector : IArchitectureTopologyProjector
{
    private const string RenderIdPrefix = "tree_";

    public ArchitectureRenderGraph Project(ArchitectureDiagramModel diagram, NodeDuplicationSettings settings)
    {
        if (diagram is null) throw new ArgumentNullException(nameof(diagram));
        settings ??= new NodeDuplicationSettings();

        var policy = DuplicationPolicy.Create(settings);
        var projects = diagram.Projects.OrderBy(project => project.Id, StringComparer.Ordinal)
            .Select((project, order) => new ArchitectureRenderProject(project.Id, project.Name, order)).ToArray();
        var semanticNodes = BuildSemanticNodes(diagram);
        var nodeById = semanticNodes.ToDictionary(node => node.Id, StringComparer.Ordinal);
        var links = diagram.Links.Where(link => nodeById.ContainsKey(link.SourceId) && nodeById.ContainsKey(link.TargetId))
            .OrderBy(link => link.Id, StringComparer.Ordinal).ToArray();
        var outgoing = links.GroupBy(link => link.SourceId, StringComparer.Ordinal).ToDictionary(
            group => group.Key,
            group => group.OrderBy(link => nodeById[link.TargetId].SemanticTypeIdentity, StringComparer.Ordinal)
                .ThenBy(link => link.TargetId, StringComparer.Ordinal)
                .ThenBy(link => link.Id, StringComparer.Ordinal).ToArray(),
            StringComparer.Ordinal);
        var configuredRoots = diagram.Selection?.Roots.OrderBy(root => root.PatternIndex)
            .ThenBy(root => root.MatchedCanonicalValue, StringComparer.Ordinal)
            .ThenBy(root => root.SemanticNodeId, StringComparer.Ordinal)
            .Select(root => root.SemanticNodeId).Where(nodeById.ContainsKey)
            .Distinct(StringComparer.Ordinal).ToArray() ?? Array.Empty<string>();
        var roots = configuredRoots.Length > 0
            ? configuredRoots.ToList()
            : InferRoots(semanticNodes.Select(node => node.Id), links).ToList();

        var nodes = new List<ArchitectureRenderNode>();
        var renderLinks = new List<ArchitectureRenderLink>();
        var instances = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var canonicalBySemanticId = new Dictionary<string, string>(StringComparer.Ordinal);
        var expandedCanonical = new HashSet<string>(StringComparer.Ordinal);
        var represented = new HashSet<string>(StringComparer.Ordinal);

        foreach (var root in roots.ToArray())
            VisitRoot(root);

        while (represented.Count < semanticNodes.Count)
        {
            var remaining = semanticNodes.Where(node => !represented.Contains(node.Id))
                .OrderBy(node => node.Id, StringComparer.Ordinal).First();
            roots.Add(remaining.Id);
            VisitRoot(remaining.Id);
        }

        return new ArchitectureRenderGraph(
            projects,
            nodes,
            renderLinks,
            roots,
            instances.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<string>)pair.Value.ToArray(), StringComparer.Ordinal));

        void VisitRoot(string semanticRootId)
        {
            if (!nodeById.ContainsKey(semanticRootId)) return;
            Visit(semanticRootId, semanticRootId, SafeId(semanticRootId), null,
                new Dictionary<string, string>(StringComparer.Ordinal), policy.AllowAll);
        }

        string Visit(
            string semanticNodeId,
            string rootId,
            string path,
            string? placementParentId,
            Dictionary<string, string> ancestors,
            bool duplicateBranch)
        {
            if (ancestors.TryGetValue(semanticNodeId, out var cycleTarget))
                return cycleTarget;

            var semantic = nodeById[semanticNodeId];
            var mayDuplicate = duplicateBranch || policy.Matches(semantic.Name, semantic.SemanticTypeIdentity);
            if (!mayDuplicate && canonicalBySemanticId.TryGetValue(semanticNodeId, out var existing))
                return existing;

            var renderId = $"{RenderIdPrefix}{SafeId(rootId)}__{SafeId(path)}__{SafeId(semanticNodeId)}";
            var occurrence = instances.TryGetValue(semanticNodeId, out var prior) && prior.Count > 0
                ? ArchitectureRenderNodeOccurrence.Duplicated
                : ArchitectureRenderNodeOccurrence.Canonical;
            var ownerProjectId = semantic.ProjectId;
            if (semantic.IsExternal && mayDuplicate)
            {
                ownerProjectId = placementParentId is not null
                    ? nodes.Single(node => node.Id == placementParentId).ProjectId
                    : semantic.ProjectId;
            }

            var rendered = new ArchitectureRenderNode(
                renderId, semantic.Id, ownerProjectId, semantic.Name, semantic.SemanticTypeIdentity,
                semantic.Kind, semantic.IsExternal, semantic.ExternalTag, semantic.InterfaceResolution,
                semantic.InterfaceIdentity, semantic.ImplementationIdentity, semantic.ImplementationCount,
                occurrence,
                occurrence == ArchitectureRenderNodeOccurrence.Duplicated
                    ? policy.Reason
                    : ArchitectureDuplicationReason.None,
                placementParentId, nodes.Count);
            nodes.Add(rendered);
            represented.Add(semanticNodeId);
            if (prior is null) instances[semanticNodeId] = prior = new List<string>();
            prior.Add(renderId);
            if (!mayDuplicate) canonicalBySemanticId[semanticNodeId] = renderId;

            var nextAncestors = new Dictionary<string, string>(ancestors, StringComparer.Ordinal)
            {
                [semanticNodeId] = renderId
            };
            if (!outgoing.TryGetValue(semanticNodeId, out var children) ||
                (!mayDuplicate && !expandedCanonical.Add(renderId)))
                return renderId;

            for (var index = 0; index < children.Length; index++)
            {
                var link = children[index];
                var childPath = $"{path}_{index}_{SafeId(link.TargetId)}";
                var childId = Visit(link.TargetId, rootId, childPath, renderId, nextAncestors, mayDuplicate);
                renderLinks.Add(new ArchitectureRenderLink(
                    $"{RenderIdPrefix}{SafeId(rootId)}__{SafeId(path)}__{SafeId(link.Id)}",
                    link.Id, renderId, childId, link.SourceId, link.TargetId, link.Kind, renderLinks.Count));
            }

            return renderId;
        }
    }

    private static IReadOnlyList<SemanticNode> BuildSemanticNodes(ArchitectureDiagramModel diagram)
    {
        var nodes = diagram.Projects.SelectMany(project => project.Nodes.Select(node => new SemanticNode(
                node.Id, node.ProjectId, node.Name, node.SemanticTypeIdentity ?? node.FullName, node.Kind,
                false, string.Empty, node.InterfaceResolution, node.InterfaceIdentity,
                node.ImplementationIdentity, node.ImplementationCount)))
            .OrderBy(node => node.Id, StringComparer.Ordinal).ToList();
        var projectByInternalId = nodes.Where(node => node.ProjectId is not null)
            .ToDictionary(node => node.Id, node => node.ProjectId, StringComparer.Ordinal);
        foreach (var external in diagram.ExternalNodes.OrderBy(node => node.Id, StringComparer.Ordinal))
        {
            var owners = diagram.Links.Where(link => link.TargetId == external.Id)
                .Select(link => projectByInternalId.TryGetValue(link.SourceId, out var projectId) ? projectId : null)
                .Where(projectId => projectId is not null).Distinct(StringComparer.Ordinal).ToArray();
            nodes.Add(new SemanticNode(external.Id, owners.Length == 1 ? owners[0] : null,
                external.Name, string.IsNullOrWhiteSpace(external.FullName) ? external.Name : external.FullName,
                "External", true, external.Tag, InterfaceResolutionStatus.NotApplicable, null, null, 0));
        }
        return nodes.OrderBy(node => node.Id, StringComparer.Ordinal).ToArray();
    }

    private static IReadOnlyList<string> InferRoots(
        IEnumerable<string> nodeIds,
        IReadOnlyList<ArchitectureLink> links)
    {
        var ids = nodeIds.OrderBy(id => id, StringComparer.Ordinal).ToArray();
        var outgoing = links.GroupBy(link => link.SourceId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(link => link.TargetId)
                .Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);
        var index = 0;
        var indexes = new Dictionary<string, int>(StringComparer.Ordinal);
        var low = new Dictionary<string, int>(StringComparer.Ordinal);
        var stack = new Stack<string>();
        var onStack = new HashSet<string>(StringComparer.Ordinal);
        var components = new List<string[]>();

        foreach (var id in ids)
            if (!indexes.ContainsKey(id)) StrongConnect(id);

        var componentByNode = components.SelectMany((component, componentIndex) =>
                component.Select(node => new { node, componentIndex }))
            .ToDictionary(item => item.node, item => item.componentIndex, StringComparer.Ordinal);
        var incoming = new int[components.Count];
        foreach (var link in links)
        {
            var source = componentByNode[link.SourceId];
            var target = componentByNode[link.TargetId];
            if (source != target) incoming[target]++;
        }
        return components.Select((component, componentIndex) => new { component, componentIndex })
            .Where(item => incoming[item.componentIndex] == 0)
            .Select(item => item.component.OrderBy(id => id, StringComparer.Ordinal).First())
            .OrderBy(id => id, StringComparer.Ordinal).ToArray();

        void StrongConnect(string node)
        {
            indexes[node] = low[node] = index++;
            stack.Push(node);
            onStack.Add(node);
            if (outgoing.TryGetValue(node, out var targets))
            {
                foreach (var target in targets)
                {
                    if (!indexes.ContainsKey(target))
                    {
                        StrongConnect(target);
                        low[node] = Math.Min(low[node], low[target]);
                    }
                    else if (onStack.Contains(target))
                    {
                        low[node] = Math.Min(low[node], indexes[target]);
                    }
                }
            }
            if (low[node] != indexes[node]) return;
            var component = new List<string>();
            string current;
            do
            {
                current = stack.Pop();
                onStack.Remove(current);
                component.Add(current);
            } while (!string.Equals(current, node, StringComparison.Ordinal));
            components.Add(component.OrderBy(id => id, StringComparer.Ordinal).ToArray());
        }
    }

    private static string SafeId(string value) => string.Concat(value.Select(character =>
        char.IsLetterOrDigit(character) || character == '_' || character == '-' ? character : '_'));

    private sealed record SemanticNode(
        string Id, string? ProjectId, string Name, string SemanticTypeIdentity, string Kind,
        bool IsExternal, string ExternalTag, InterfaceResolutionStatus InterfaceResolution,
        string? InterfaceIdentity, string? ImplementationIdentity, int ImplementationCount);

    private sealed class DuplicationPolicy
    {
        private readonly bool allowAll;
        private readonly Regex[] exceptions;

        private DuplicationPolicy(bool allowAll, Regex[] exceptions)
        {
            this.allowAll = allowAll;
            this.exceptions = exceptions;
        }

        public static DuplicationPolicy Create(NodeDuplicationSettings settings)
        {
            var patterns = (settings.DuplicationExceptionPatterns ?? new List<string>())
                .Where(pattern => !string.IsNullOrWhiteSpace(pattern)).Select((pattern, index) =>
                {
                    try { return new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant); }
                    catch (ArgumentException exception)
                    {
                        throw new InvalidDataException(
                            $"Node duplication exception pattern at index {index} is not a valid regular expression: {pattern}", exception);
                    }
                }).ToArray();
            return new DuplicationPolicy(settings.AllowDuplicateNodes, patterns);
        }

        public bool AllowAll => allowAll;

        public ArchitectureDuplicationReason Reason =>
            allowAll ? ArchitectureDuplicationReason.GlobalPolicy : ArchitectureDuplicationReason.ExceptionPattern;

        public bool Matches(string name, string identity) => exceptions.Any(pattern =>
            pattern.IsMatch(name ?? string.Empty) || pattern.IsMatch(identity ?? string.Empty));
    }
}
