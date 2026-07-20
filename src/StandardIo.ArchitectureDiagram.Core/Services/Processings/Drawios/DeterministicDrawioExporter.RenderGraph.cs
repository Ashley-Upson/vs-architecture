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
            IReadOnlyDictionary<string, string>? placementParentByNode = null)
        {
            Projects = projects;
            Nodes = nodes;
            Links = links;
            PlacementParentByNode = placementParentByNode ?? new Dictionary<string, string>(StringComparer.Ordinal);
        }

        public IReadOnlyList<RenderProject> Projects { get; }

        public IReadOnlyList<RenderNode> Nodes { get; }

        public IReadOnlyList<RenderLink> Links { get; }

        public IReadOnlyDictionary<string, string> PlacementParentByNode { get; }

        internal RenderGraph WithDisconnectedProject(DisconnectedNodeProjectLayout disconnected) =>
            new RenderGraph(
                Projects.Where(project => !string.Equals(project.Id, disconnected.Project.Id, StringComparison.Ordinal))
                    .Concat(new[] { disconnected.Project }).ToArray(),
                Nodes.Select(node => disconnected.Nodes.TryGetValue(node.Id, out var layout) ? layout.Node : node).ToArray(),
                Links, PlacementParentByNode);

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
                    if (type.Kind == "Interface")
                    {
                        continue;
                    }

                    if (seenNodeIds.Add(type.Id))
                    {
                        nodes.Add(ToRenderNode(type, nodes.Count));
                    }
                }
            }

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

            return new RenderGraph(projects, nodes, links);
        }

        public static RenderGraph From(DiagramModel diagram, DiagramSettings settings)
        {
            var duplicationPolicy = NodeDuplicationPolicy.From(settings.NodeDuplication);
            RenderGraph graph;
            using (PerformanceAudit.Measure("base RenderGraph construction"))
            {
                graph = FromBaseDiagram(diagram, duplicationPolicy);
            }

            var configuredRoots = diagram.Metadata?.SemanticSelection?.Roots
                .OrderBy(root => root.PatternIndex).ThenBy(root => root.MatchedCanonicalValue, StringComparer.Ordinal)
                .Select(root => root.SemanticNodeId).Where(id => graph.Nodes.Any(node => node.Id == id))
                .Distinct(StringComparer.Ordinal).ToArray() ?? Array.Empty<string>();
            if (configuredRoots.Length == 0)
            {
                return graph;
            }

            using (PerformanceAudit.Measure(
                "exposure/canonical graph construction",
                graph.Nodes.Count,
                graph.Links.Count))
            {
                var result = settings.NodeDuplication.AllowDuplicateNodes
                    ? BuildExposureTreeGraph(graph, configuredRoots)
                    : BuildCanonicalExposureGraph(graph, duplicationPolicy, configuredRoots);
                PerformanceAudit.Increment("render paths/clones created", result.Nodes.Count);
                PerformanceAudit.Increment("render links created", result.Links.Count);
                return result;
            }
        }

        private static RenderGraph BuildCanonicalExposureGraph(
            RenderGraph graph,
            NodeDuplicationPolicy duplicationPolicy,
            IReadOnlyList<string> configuredRootIds)
        {
            var nodeById = graph.Nodes.ToDictionary(node => node.Id, StringComparer.Ordinal);
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

            var roots = configuredRootIds.Where(nodeById.ContainsKey).Select(id => nodeById[id]).ToArray();

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
                PerformanceAudit.Increment("exposure roots visited");
                Visit(root.Id, root.Id, SafeId(root.Id), null, new HashSet<string>(StringComparer.Ordinal));
            }

            return new RenderGraph(graph.Projects, clonedNodes, clonedLinks, placementParentByNode);

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
                PerformanceAudit.Increment("render clone instances created");
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

        private static RenderGraph BuildExposureTreeGraph(RenderGraph graph, IReadOnlyList<string> configuredRootIds)
        {
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

            var nodeById = graph.Nodes.ToDictionary(node => node.Id, StringComparer.Ordinal);
            var roots = configuredRootIds.Where(nodeById.ContainsKey).Select(id => nodeById[id]).ToArray();

            if (roots.Length == 0)
            {
                return graph;
            }

            var clonedNodes = new List<RenderNode>();
            var clonedLinks = new List<RenderLink>();
            var originalNodesInTrees = new HashSet<string>(StringComparer.Ordinal);

            foreach (var root in roots)
            {
                PerformanceAudit.Increment("exposure roots visited");
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
                    PerformanceAudit.Increment("render clone instances created");

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

            return new RenderGraph(graph.Projects, clonedNodes, clonedLinks);
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
