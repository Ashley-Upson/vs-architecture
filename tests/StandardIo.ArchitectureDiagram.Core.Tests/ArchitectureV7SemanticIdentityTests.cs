using System;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core.Models.Architectures;
using StandardIo.ArchitectureDiagram.Core.Models.ArchitectureV7;
using Xunit;
using ArchitectureDiagramModel = StandardIo.ArchitectureDiagram.Core.Models.Architectures.ArchitectureDiagram;

namespace StandardIo.ArchitectureDiagram.Core.Tests;

public sealed class ArchitectureV7SemanticIdentityTests
{
    [Fact]
    public void Stable_semantic_keys_are_invariant_under_source_and_relationship_enumeration_order()
    {
        var nodes = new[]
        {
            new ArchitectureNode("z", "project", "Z", "Project.Z", "Class", "z", Array.Empty<string>()),
            new ArchitectureNode("a", "project", "A", "Project.A", "Class", "a", Array.Empty<string>()),
            new ArchitectureNode("child", "project", "Child", "Project.Child", "Class", "child", Array.Empty<string>())
        };
        var links = new[]
        {
            new ArchitectureLink("second", "a", "child", "dependency", 1),
            new ArchitectureLink("first", "z", "child", "dependency", 0)
        };
        var left = Diagram(nodes, links);
        var right = Diagram(nodes.OrderByDescending(node => node.Id).ToArray(), links.OrderByDescending(link => link.Id).ToArray());

        Assert.Equal(ArchitectureV7SemanticIdentity.SortedNodeKeys(left), ArchitectureV7SemanticIdentity.SortedNodeKeys(right));
        Assert.Equal(ArchitectureV7SemanticIdentity.SortedRelationshipKeys(left), ArchitectureV7SemanticIdentity.SortedRelationshipKeys(right));
    }

    private static ArchitectureDiagramModel Diagram(ArchitectureNode[] nodes, ArchitectureLink[] links) =>
        new(new[] { new ArchitectureProject("project", "Project", nodes, "project") }, Array.Empty<ArchitectureExternalNode>(), links, null);
}
