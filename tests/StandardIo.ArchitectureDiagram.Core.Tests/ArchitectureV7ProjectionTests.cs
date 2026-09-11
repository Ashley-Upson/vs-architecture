using System;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core.Models.Architectures;
using StandardIo.ArchitectureDiagram.Core.Models.ArchitectureV7;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.ArchitectureV7;
using Xunit;
using ArchitectureDiagramModel = StandardIo.ArchitectureDiagram.Core.Models.Architectures.ArchitectureDiagram;

namespace StandardIo.ArchitectureDiagram.Core.Tests;

public sealed class ArchitectureV7ProjectionTests
{
    [Fact]
    public void Canonical_projection_deduplicates_shared_semantics_and_accounts_every_link()
    {
        var diagram = Diagram(
            nodes: new[] { Node("a", "A"), Node("b", "B"), Node("shared", "Shared") },
            links: new[] { Link("z", "a", "shared"), Link("a-link", "b", "shared") });

        var result = new ArchitectureV7PhysicalProjectionStage().Project(diagram,
            new ArchitectureV7ProjectionPolicy(ArchitectureV7ProjectionMode.Canonical, Array.Empty<string>()));

        Assert.Equal(3, result.PhysicalNodes.Count);
        Assert.All(result.PhysicalLinks, link => Assert.Contains(link.SemanticLinkId, result.SemanticLinkToPhysicalLinkIds.Keys));
        Assert.Single(result.SemanticNodeToPhysicalNodeIds["shared"]);
    }

    [Fact]
    public void Configured_duplicate_semantics_follow_stable_link_order_not_input_enumeration_order()
    {
        var first = Diagram(
            nodes: new[] { Node("a", "A"), Node("b", "B"), Node("shared", "SharedUtility") },
            links: new[] { Link("second", "b", "shared"), Link("first", "a", "shared") });
        var reordered = first with { Links = first.Links.Reverse().ToArray() };
        var policy = new ArchitectureV7ProjectionPolicy(ArchitectureV7ProjectionMode.ConfiguredDuplicateBranches, new[] { "*Utility" });

        var left = new ArchitectureV7PhysicalProjectionStage().Project(first, policy);
        var right = new ArchitectureV7PhysicalProjectionStage().Project(reordered, policy);

        Assert.Equal(left.PhysicalNodes.Select(node => (node.PhysicalNodeId, node.SemanticNodeId, node.ProjectionMode)),
            right.PhysicalNodes.Select(node => (node.PhysicalNodeId, node.SemanticNodeId, node.ProjectionMode)));
        Assert.Equal(left.PhysicalLinks.Select(link => (link.PhysicalLinkId, link.SourcePhysicalNodeId, link.DestinationPhysicalNodeId)),
            right.PhysicalLinks.Select(link => (link.PhysicalLinkId, link.SourcePhysicalNodeId, link.DestinationPhysicalNodeId)));
        Assert.Equal(2, left.SemanticNodeToPhysicalNodeIds["shared"].Count);
        Assert.Contains(left.PhysicalNodes, node => node.DuplicationProvenance is not null);
    }

    [Fact]
    public void Canonical_ownership_uses_analyser_fifo_order_not_identifier_order()
    {
        var diagram = Diagram(
            nodes: new[] { Node("z", "ParentZ"), Node("a", "ParentA"), Node("child", "Child") },
            links: new[] { Link("z-link", "z", "child", 0), Link("a-link", "a", "child", 1) });
        var projection = new ArchitectureV7PhysicalProjectionStage().Project(diagram,
            new ArchitectureV7ProjectionPolicy(ArchitectureV7ProjectionMode.Canonical, Array.Empty<string>()));

        var ownership = new ArchitectureV7PositionalOwnershipStage().Resolve(projection);
        var child = Assert.Single(ownership.Decisions.Where(item => item.SemanticNodeId == "child"));
        Assert.Equal("physical:z", child.PositionalParentPhysicalNodeId);
        Assert.Equal(new[] { "physical:a" }, child.AdditionalSemanticParentPhysicalNodeIds);
        Assert.Equal(0, projection.PhysicalLinks.Single(link => link.SemanticLinkId == "z-link").AnalyserOrdinal);
        Assert.Equal(1, projection.PhysicalLinks.Single(link => link.SemanticLinkId == "a-link").AnalyserOrdinal);
        Assert.Equal(projection.FreezeFingerprint, ownership.ProjectionFreezeFingerprint);
    }

    [Fact]
    public void Configured_duplication_creates_independent_physical_nodes_without_reference_ownership()
    {
        var diagram = Diagram(
            nodes: new[] { Node("z", "ParentZ"), Node("a", "ParentA"), Node("shared", "SharedUtility") },
            links: new[] { Link("z-link", "z", "shared"), Link("a-link", "a", "shared") });
        var projection = new ArchitectureV7PhysicalProjectionStage().Project(diagram,
            new ArchitectureV7ProjectionPolicy(ArchitectureV7ProjectionMode.ConfiguredDuplicateBranches, new[] { "*Utility" }));

        var sharedPhysicalNodes = projection.PhysicalNodes.Where(node => node.SemanticNodeId == "shared").ToArray();
        Assert.Equal(2, sharedPhysicalNodes.Length);
        Assert.All(sharedPhysicalNodes, node => Assert.DoesNotContain("reference", node.PhysicalNodeId, StringComparison.OrdinalIgnoreCase));

        var ownership = new ArchitectureV7PositionalOwnershipStage().Resolve(projection);
        Assert.All(sharedPhysicalNodes, node =>
        {
            var decision = Assert.Single(ownership.Decisions.Where(item => item.PhysicalNodeId == node.PhysicalNodeId));
            Assert.NotNull(decision.PositionalParentPhysicalNodeId);
            Assert.Empty(decision.AdditionalSemanticParentPhysicalNodeIds);
        });
    }

    [Fact]
    public void Missing_scope_endpoints_remain_explicitly_unaccounted()
    {
        var diagram = Diagram(new[] { Node("a", "A") }, new[] { Link("missing", "a", "outside") });
        var result = new ArchitectureV7PhysicalProjectionStage().Project(diagram,
            new ArchitectureV7ProjectionPolicy(ArchitectureV7ProjectionMode.Canonical, Array.Empty<string>()));

        Assert.Contains("missing", result.UnaccountedSemanticLinkIds);
        Assert.Empty(result.SemanticLinkToPhysicalLinkIds["missing"]);
    }

    private static ArchitectureDiagramModel Diagram(ArchitectureNode[] nodes, ArchitectureLink[] links) =>
        new(new[] { new ArchitectureProject("project", "Project", nodes, "project") }, Array.Empty<ArchitectureExternalNode>(), links, null);

    private static ArchitectureNode Node(string id, string name) =>
        new(id, "project", name, "Project." + name, "Class", id, Array.Empty<string>());

    private static ArchitectureLink Link(string id, string source, string target, int analyserOrdinal = -1) =>
        new(id, source, target, "dependency", analyserOrdinal);
}
