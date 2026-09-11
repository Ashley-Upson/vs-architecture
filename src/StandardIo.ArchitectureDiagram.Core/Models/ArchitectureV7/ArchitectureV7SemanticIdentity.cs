using System;
using System.Collections.Generic;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core.Models.Architectures;
using ArchitectureDiagramModel = StandardIo.ArchitectureDiagram.Core.Models.Architectures.ArchitectureDiagram;

namespace StandardIo.ArchitectureDiagram.Core.Models.ArchitectureV7;

/// <summary>
/// Deterministic semantic comparison keys. These are audit identities only;
/// physical/projected IDs remain owned by their existing authorities.
/// </summary>
public static class ArchitectureV7SemanticIdentity
{
    public static string NodeKey(ArchitectureNode node) =>
        string.Join("|", node.ProjectId, node.FullName, node.Kind);

    public static string ExternalNodeKey(ArchitectureExternalNode node) =>
        node.FullName;

    public static string RelationshipKey(ArchitectureLink link, IReadOnlyDictionary<string, string> nodeKeys) =>
        string.Join("|", Resolve(nodeKeys, link.SourceId), Resolve(nodeKeys, link.TargetId), link.Kind, link.AnalyserOrdinal);

    public static IReadOnlyList<string> SortedNodeKeys(ArchitectureDiagramModel diagram) =>
        diagram.Projects.SelectMany(project => project.Nodes).Select(NodeKey)
            .OrderBy(value => value, StringComparer.Ordinal).ToArray();

    public static IReadOnlyList<string> SortedRelationshipKeys(ArchitectureDiagramModel diagram)
    {
        var nodeKeys = diagram.Projects.SelectMany(project => project.Nodes)
            .ToDictionary(node => node.Id, node => node.FullName, StringComparer.Ordinal);
        foreach (var external in diagram.ExternalNodes)
            nodeKeys[external.Id] = ExternalNodeKey(external);
        return diagram.Links.Select(link => RelationshipKey(link, nodeKeys))
            .OrderBy(value => value, StringComparer.Ordinal).ToArray();
    }

    private static string Resolve(IReadOnlyDictionary<string, string> nodeKeys, string id) =>
        nodeKeys.TryGetValue(id, out var value) ? value : "unresolved:" + id;
}
