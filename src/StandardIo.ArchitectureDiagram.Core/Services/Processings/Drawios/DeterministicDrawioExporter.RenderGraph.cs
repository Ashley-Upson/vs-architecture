using System;
using System.Collections.Generic;
using System.Globalization;
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
            IReadOnlyList<RenderNode> dataModels)
        {
            Projects = projects;
            Nodes = nodes;
            Links = links;
            DataModels = dataModels;
        }

        public IReadOnlyList<RenderProject> Projects { get; }

        public IReadOnlyList<RenderNode> Nodes { get; }

        public IReadOnlyList<RenderLink> Links { get; }

        public IReadOnlyList<RenderNode> DataModels { get; }

        public static RenderGraph From(DiagramModel diagram)
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
                for (var index = 0; index < externalEdges.Length; index++)
                {
                    var edge = externalEdges[index];
                    var renderNodeId = externalEdges.Length == 1
                        ? external.Id
                        : $"{external.Id}__{SafeId(edge.SourceId)}__{SafeId(edge.Id)}";

                    if (seenNodeIds.Add(renderNodeId))
                    {
                        nodes.Add(ToRenderNode(external, renderNodeId, nodes.Count));
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
            var graph = From(diagram);
            return graph.Nodes.Count >= settings.Layout.ExposureTreeLayoutThreshold
                ? BuildExposureTreeGraph(graph)
                : graph;
        }

        private static RenderGraph BuildExposureTreeGraph(RenderGraph graph)
        {
            var incoming = graph.Nodes.ToDictionary(node => node.Id, _ => 0, StringComparer.Ordinal);
            var outgoing = graph.Links
                .GroupBy(link => link.SourceId, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => group.OrderBy(link => graph.Nodes.Single(node => node.Id == link.TargetId).Order).ThenBy(link => link.Order).ToArray(),
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
                .ToArray();
            var roots = preferredRoots.Length > 0
                ? preferredRoots
                : graph.Nodes
                    .Where(node => !node.IsExternal && outgoing.ContainsKey(node.Id) && incoming[node.Id] == 0)
                    .OrderBy(node => node.Order)
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

        private static RenderNode ToRenderNode(ExternalDependencyNode external, string id, int order)
        {
            var fullName = string.IsNullOrWhiteSpace(external.FullName) ? external.Name : external.FullName;
            var tag = string.IsNullOrWhiteSpace(external.Tag) ? "[External]" : external.Tag;
            return new RenderNode(id, null, external.Name, fullName, "External", true, tag, order, Array.Empty<string>(), Array.Empty<TypeProperty>(), 0);
        }

        private static string SafeId(string value)
        {
            return string.Concat(value.Select(character => char.IsLetterOrDigit(character) || character == '_' || character == '-' ? character : '_'));
        }
    }
