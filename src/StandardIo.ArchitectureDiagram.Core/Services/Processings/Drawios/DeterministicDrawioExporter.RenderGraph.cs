using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using StandardIo.ArchitectureDiagram.Core.Models;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal sealed class RenderGraph
{
    private const string ExposureTreeIdPrefix = "tree_";

    private RenderGraph(
            IReadOnlyList<RenderProject> projects,
            IReadOnlyList<RenderNode> nodes,
            IReadOnlyList<RenderLink> links,
            IReadOnlyList<RenderNode> dataModels,
            IReadOnlyDictionary<string, string>? placementParentByNode = null)
        {
            Projects = projects;
            Nodes = nodes;
            Links = links;
            DataModels = dataModels;
            PlacementParentByNode = placementParentByNode ?? new Dictionary<string, string>(StringComparer.Ordinal);
        }

        public IReadOnlyList<RenderProject> Projects { get; }

        public IReadOnlyList<RenderNode> Nodes { get; }

        public IReadOnlyList<RenderLink> Links { get; }

        public IReadOnlyList<RenderNode> DataModels { get; }

        public IReadOnlyDictionary<string, string> PlacementParentByNode { get; }

        public static RenderGraph From(DiagramModel diagram)
        {
            return FromBaseDiagram(diagram, NodeDuplicationPolicy.AllowAll);
        }

        private static RenderGraph FromBaseDiagram(DiagramModel diagram, NodeDuplicationPolicy duplicationPolicy)
        {
            var projects = diagram.Projects
                .Select((project, order) => new RenderProject(project.Id, project.Name, order))
                .ToArray();
            var nodes = new List<RenderNode>();
            var seenNodeIds = new HashSet<string>(StringComparer.Ordinal);

            foreach (var project in diagram.Projects)
            {
                foreach (var type in project.Types)
                {
                    if (type.Kind == "Interface" || IsModelType(type))
                    {
                        continue;
                    }

                    if (seenNodeIds.Add(type.Id))
                    {
                        nodes.Add(ToRenderNode(type, nodes.Count));
                    }
                }
            }

            var dataModels = diagram.Projects
                .SelectMany(project => project.Types)
                .Where(type => type.Kind != "Interface" && IsModelType(type))
                .Select((type, order) => ToRenderNode(type, order))
                .ToArray();

            var knownSourceIds = new HashSet<string>(nodes.Select(node => node.Id), StringComparer.Ordinal);
            var projectBySourceId = nodes.ToDictionary(node => node.Id, node => node.ProjectId, StringComparer.Ordinal);
            var externalById = diagram.ExternalDependencies.ToDictionary(external => external.Id, StringComparer.Ordinal);
            var externalTargetIds = new Dictionary<string, string>(StringComparer.Ordinal);
            var externalEdgesByTarget = diagram.Edges
                .Where(edge => knownSourceIds.Contains(edge.SourceId) && externalById.ContainsKey(edge.TargetId))
                .GroupBy(edge => edge.TargetId, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.OrderBy(edge => edge.Id, StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);

            foreach (var externalGroup in externalEdgesByTarget)
            {
                var external = externalById[externalGroup.Key];
                var externalEdges = externalGroup.Value;

                if (!duplicationPolicy.AllowsDuplication(external.Name, external.FullName))
                {
                    var edgesByOwner = externalEdges
                        .GroupBy(
                            edge => projectBySourceId.TryGetValue(edge.SourceId, out var projectId) ? projectId ?? string.Empty : string.Empty,
                            StringComparer.Ordinal)
                        .OrderBy(group => group.Key, StringComparer.Ordinal)
                        .ToArray();

                    foreach (var ownerGroup in edgesByOwner)
                    {
                        var ownerProjectId = ownerGroup.Key.Length == 0 ? null : ownerGroup.Key;
                        var renderNodeId = edgesByOwner.Length == 1
                            ? external.Id
                            : $"{external.Id}__project__{SafeId(ownerGroup.Key)}";

                        if (seenNodeIds.Add(renderNodeId))
                        {
                            nodes.Add(ToRenderNode(external, renderNodeId, ownerProjectId, nodes.Count));
                        }

                        foreach (var edge in ownerGroup.OrderBy(edge => edge.Id, StringComparer.Ordinal))
                        {
                            externalTargetIds[edge.Id] = renderNodeId;
                        }
                    }

                    continue;
                }

                for (var index = 0; index < externalEdges.Length; index++)
                {
                    var edge = externalEdges[index];
                    var renderNodeId = externalEdges.Length == 1
                        ? external.Id
                        : $"{external.Id}__{SafeId(edge.SourceId)}__{SafeId(edge.Id)}";

                    if (seenNodeIds.Add(renderNodeId))
                    {
                        projectBySourceId.TryGetValue(edge.SourceId, out var ownerProjectId);
                        nodes.Add(ToRenderNode(external, renderNodeId, ownerProjectId, nodes.Count));
                    }

                    externalTargetIds[edge.Id] = renderNodeId;
                }
            }

            var nodeIds = new HashSet<string>(nodes.Select(node => node.Id), StringComparer.Ordinal);
            var links = diagram.Edges
                .Select(edge => new
                {
                    Edge = edge,
                    RenderTargetId = externalTargetIds.TryGetValue(edge.Id, out var targetId) ? targetId : edge.TargetId
                })
                .Where(item => nodeIds.Contains(item.Edge.SourceId) && nodeIds.Contains(item.RenderTargetId))
                .Select((item, order) => new RenderLink(
                    item.Edge.Id,
                    item.Edge.SourceId,
                    item.RenderTargetId,
                    item.Edge.Kind,
                    order,
                    item.Edge.SourceId,
                    item.Edge.TargetId))
                .ToArray();

            return new RenderGraph(projects, nodes, links, dataModels);
        }

        public static RenderGraph From(DiagramModel diagram, DiagramSettings settings)
        {
            var duplicationPolicy = NodeDuplicationPolicy.From(settings.NodeDuplication);
            var graph = FromBaseDiagram(diagram, duplicationPolicy);
            return graph.Nodes.Count >= settings.Layout.ExposureTreeLayoutThreshold
                ? settings.NodeDuplication.AllowDuplicateNodes
                    ? BuildExposureTreeGraph(graph)
                    : BuildCanonicalExposureGraph(graph, duplicationPolicy)
                : graph;
        }

        private static RenderGraph BuildCanonicalExposureGraph(
            RenderGraph graph,
            NodeDuplicationPolicy duplicationPolicy)
        {
            var nodeById = graph.Nodes.ToDictionary(node => node.Id, StringComparer.Ordinal);
            var incoming = graph.Nodes.ToDictionary(node => node.Id, _ => 0, StringComparer.Ordinal);
            var outgoing = graph.Links
                .GroupBy(link => link.SourceId, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => group
                        .OrderBy(link => nodeById[link.TargetId].FullName, StringComparer.Ordinal)
                        .ThenBy(link => link.SemanticTargetId ?? link.TargetId, StringComparer.Ordinal)
                        .ThenBy(link => link.Id, StringComparer.Ordinal)
                        .ToArray(),
                    StringComparer.Ordinal);

            foreach (var link in graph.Links)
            {
                if (incoming.ContainsKey(link.TargetId))
                {
                    incoming[link.TargetId]++;
                }
            }

            var preferredRoots = graph.Nodes
                .Where(node => !node.IsExternal && outgoing.ContainsKey(node.Id) && IsExposureNode(node))
                .OrderBy(node => node.FullName, StringComparer.Ordinal)
                .ThenBy(node => node.Id, StringComparer.Ordinal)
                .ToArray();
            var roots = preferredRoots.Length > 0
                ? preferredRoots
                : graph.Nodes
                    .Where(node => !node.IsExternal && outgoing.ContainsKey(node.Id) && incoming[node.Id] == 0)
                    .OrderBy(node => node.FullName, StringComparer.Ordinal)
                    .ThenBy(node => node.Id, StringComparer.Ordinal)
                    .ToArray();

            if (roots.Length == 0)
            {
                return graph;
            }

            var clonedNodes = new List<RenderNode>();
            var clonedLinks = new List<RenderLink>();
            var canonicalCloneByOriginalId = new Dictionary<string, string>(StringComparer.Ordinal);
            var placementParentByNode = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var root in roots)
            {
                Visit(root.Id, root.Id, SafeId(root.Id), null, new HashSet<string>(StringComparer.Ordinal));
            }

            return new RenderGraph(graph.Projects, clonedNodes, clonedLinks, graph.DataModels, placementParentByNode);

            string Visit(
                string originalNodeId,
                string rootId,
                string path,
                string? placementParentId,
                HashSet<string> ancestors)
            {
                var original = nodeById[originalNodeId];
                var mayDuplicate = duplicationPolicy.AllowsDuplication(original.Name, original.FullName);
                if (!mayDuplicate && canonicalCloneByOriginalId.TryGetValue(originalNodeId, out var existingCloneId))
                {
                    return existingCloneId;
                }

                var cloneId = CloneNodeId(rootId, path, originalNodeId);
                if (!ancestors.Add(originalNodeId))
                {
                    return cloneId;
                }

                clonedNodes.Add(original with { Id = cloneId, Order = clonedNodes.Count });
                if (placementParentId is not null)
                {
                    placementParentByNode[cloneId] = placementParentId;
                }
                if (!mayDuplicate)
                {
                    canonicalCloneByOriginalId[originalNodeId] = cloneId;
                }

                if (!outgoing.TryGetValue(originalNodeId, out var childLinks))
                {
                    return cloneId;
                }

                for (var index = 0; index < childLinks.Length; index++)
                {
                    var link = childLinks[index];
                    var childPath = $"{path}_{index}_{SafeId(link.TargetId)}";
                    var childCloneId = Visit(
                        link.TargetId,
                        rootId,
                        childPath,
                        cloneId,
                        new HashSet<string>(ancestors, StringComparer.Ordinal));
                    clonedLinks.Add(link with
                    {
                        Id = $"{ExposureTreeIdPrefix}{SafeId(rootId)}__{SafeId(path)}__{SafeId(link.Id)}",
                        SourceId = cloneId,
                        TargetId = childCloneId,
                        Order = clonedLinks.Count
                    });
                }

                return cloneId;
            }
        }

        private static RenderGraph BuildExposureTreeGraph(RenderGraph graph)
        {
            var incoming = graph.Nodes.ToDictionary(node => node.Id, _ => 0, StringComparer.Ordinal);
            var outgoing = graph.Links
                .GroupBy(link => link.SourceId, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => group
                        .OrderBy(link => graph.Nodes.Single(node => node.Id == link.TargetId).Order)
                        .ThenBy(link => link.Order)
                        .ThenBy(link => link.Id, StringComparer.Ordinal)
                        .ToArray(),
                    StringComparer.Ordinal);

            foreach (var link in graph.Links)
            {
                if (incoming.ContainsKey(link.TargetId))
                {
                    incoming[link.TargetId]++;
                }
            }

            var preferredRoots = graph.Nodes
                .Where(node => !node.IsExternal && outgoing.ContainsKey(node.Id) && IsExposureNode(node))
                .OrderBy(node => node.Order)
                .ThenBy(node => node.Id, StringComparer.Ordinal)
                .ToArray();
            var roots = preferredRoots.Length > 0
                ? preferredRoots
                : graph.Nodes
                    .Where(node => !node.IsExternal && outgoing.ContainsKey(node.Id) && incoming[node.Id] == 0)
                    .OrderBy(node => node.Order)
                    .ThenBy(node => node.Id, StringComparer.Ordinal)
                    .ToArray();

            if (roots.Length == 0)
            {
                return graph;
            }

            var nodeById = graph.Nodes.ToDictionary(node => node.Id, StringComparer.Ordinal);
            var clonedNodes = new List<RenderNode>();
            var clonedLinks = new List<RenderLink>();
            var originalNodesInTrees = new HashSet<string>(StringComparer.Ordinal);

            foreach (var root in roots)
            {
                Visit(root.Id, root.Id, SafeId(root.Id), new HashSet<string>(StringComparer.Ordinal));

                string Visit(string originalNodeId, string rootId, string path, HashSet<string> ancestors)
                {
                    var cloneId = CloneNodeId(rootId, path, originalNodeId);
                    if (!ancestors.Add(originalNodeId))
                    {
                        return cloneId;
                    }

                    originalNodesInTrees.Add(originalNodeId);
                    var original = nodeById[originalNodeId];
                    clonedNodes.Add(original with { Id = cloneId, Order = clonedNodes.Count });

                    if (!outgoing.TryGetValue(originalNodeId, out var childLinks))
                    {
                        return cloneId;
                    }

                    for (var index = 0; index < childLinks.Length; index++)
                    {
                        var link = childLinks[index];
                        var childPath = $"{path}_{index}_{SafeId(link.TargetId)}";
                        var childCloneId = Visit(link.TargetId, rootId, childPath, new HashSet<string>(ancestors, StringComparer.Ordinal));
                        clonedLinks.Add(link with
                        {
                            Id = $"{ExposureTreeIdPrefix}{SafeId(rootId)}__{SafeId(path)}__{SafeId(link.Id)}",
                            SourceId = cloneId,
                            TargetId = childCloneId,
                            Order = clonedLinks.Count
                        });
                    }

                    return cloneId;
                }
            }

            return new RenderGraph(graph.Projects, clonedNodes, clonedLinks, graph.DataModels);
        }

        private static bool IsExposureNode(RenderNode node)
        {
            return node.FullName.Contains(".Exposures.", StringComparison.OrdinalIgnoreCase) ||
                node.Name.EndsWith("Controller", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsModelType(TypeNode type)
        {
            var hasProperties = type.Properties?.Count > 0;
            return hasProperties && type.MethodCount == 0;
        }

        private static string CloneNodeId(string rootId, string path, string nodeId)
        {
            return $"{ExposureTreeIdPrefix}{SafeId(rootId)}__{SafeId(path)}__{SafeId(nodeId)}";
        }

        private static RenderNode ToRenderNode(TypeNode type, int order)
        {
            return new RenderNode(
                type.Id,
                type.ProjectId,
                type.Name,
                type.FullName,
                type.Kind,
                false,
                string.Empty,
                order,
                type.Interfaces ?? Array.Empty<string>(),
                type.Properties ?? Array.Empty<TypeProperty>(),
                type.MethodCount);
        }

        private static RenderNode ToRenderNode(
            ExternalDependencyNode external,
            string id,
            string? ownerProjectId,
            int order)
        {
            var fullName = string.IsNullOrWhiteSpace(external.FullName) ? external.Name : external.FullName;
            var tag = string.IsNullOrWhiteSpace(external.Tag) ? "[External]" : external.Tag;
            return new RenderNode(id, ownerProjectId, external.Name, fullName, "External", true, tag, order, Array.Empty<string>(), Array.Empty<TypeProperty>(), 0);
        }

        private static string SafeId(string value)
        {
            return string.Concat(value.Select(character => char.IsLetterOrDigit(character) || character == '_' || character == '-' ? character : '_'));
        }

        private sealed class NodeDuplicationPolicy
        {
            private readonly bool _allowDuplicateNodes;
            private readonly Regex[] _exceptionPatterns;

            private NodeDuplicationPolicy(bool allowDuplicateNodes, Regex[] exceptionPatterns)
            {
                _allowDuplicateNodes = allowDuplicateNodes;
                _exceptionPatterns = exceptionPatterns;
            }

            public static NodeDuplicationPolicy AllowAll { get; } = new(true, Array.Empty<Regex>());

            public static NodeDuplicationPolicy From(NodeDuplicationSettings? settings)
            {
                settings ??= new NodeDuplicationSettings();
                var patterns = (settings.DuplicationExceptionPatterns ?? new List<string>())
                    .Where(pattern => !string.IsNullOrWhiteSpace(pattern))
                    .Select((pattern, index) =>
                    {
                        try
                        {
                            return new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                        }
                        catch (ArgumentException exception)
                        {
                            throw new InvalidDataException(
                                $"Node duplication exception pattern at index {index} is not a valid regular expression: {pattern}",
                                exception);
                        }
                    })
                    .ToArray();
                return new NodeDuplicationPolicy(settings.AllowDuplicateNodes, patterns);
            }

            public bool AllowsDuplication(string shortName, string? fullName)
            {
                return _allowDuplicateNodes || _exceptionPatterns.Any(pattern =>
                    pattern.IsMatch(shortName ?? string.Empty) ||
                    pattern.IsMatch(fullName ?? string.Empty));
            }
        }
    }
