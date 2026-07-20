using StandardIo.ArchitectureDiagram.Core.Models;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core.Tests;

public sealed class HierarchyAnalyzerTests
{
    [Fact]
    public void Analyze_builds_chain_and_sibling_layout_relationships()
    {
        var hierarchy = Analyze(
            Nodes("root", "left", "right", "leaf"),
            Edges(("root", "left"), ("root", "right"), ("left", "leaf")));

        Assert.Equal(new[] { "left", "right" }, hierarchy.ChildrenByNode["root"]);
        Assert.Equal("root", hierarchy.ParentByNode["left"]);
        Assert.Equal("left", hierarchy.ParentByNode["leaf"]);
        Assert.Equal(new[] { "root" }, hierarchy.RootNodeIds);
    }

    [Fact]
    public void Analyze_uses_first_stable_parent_for_diamond()
    {
        var hierarchy = Analyze(
            Nodes("root", "left", "right", "shared"),
            Edges(("root", "left"), ("root", "right"), ("left", "shared"), ("right", "shared")));

        Assert.Equal("left", hierarchy.ParentByNode["shared"]);
        Assert.DoesNotContain("shared", hierarchy.ChildrenByNode["right"]);
    }

    [Fact]
    public void Analyze_condenses_cycles_and_keeps_layout_hierarchy_acyclic()
    {
        var hierarchy = Analyze(
            Nodes("a", "b", "c", "d"),
            Edges(("a", "b"), ("b", "a"), ("b", "c"), ("c", "d"), ("d", "c")));

        Assert.Equal(hierarchy.ComponentByNode["a"], hierarchy.ComponentByNode["b"]);
        Assert.Equal(hierarchy.ComponentByNode["c"], hierarchy.ComponentByNode["d"]);
        Assert.NotEqual(hierarchy.ComponentByNode["a"], hierarchy.ComponentByNode["c"]);
        Assert.All(hierarchy.ParentByNode, item => Assert.NotEqual(
            hierarchy.ComponentByNode[item.Key], hierarchy.ComponentByNode[item.Value]));
        AssertAcyclic(hierarchy);
    }

    [Fact]
    public void Analyze_is_deterministic_when_input_enumeration_is_reversed()
    {
        var nodes = Nodes("root", "left", "right", "shared");
        var edges = Edges(("root", "left"), ("root", "right"), ("left", "shared"), ("right", "shared"));

        var forward = Analyze(nodes, edges);
        var reverse = Analyze(nodes, edges.AsEnumerable().Reverse().ToArray());

        Assert.Equal(forward.StableNodeOrder, reverse.StableNodeOrder);
        Assert.Equal(forward.ParentByNode, reverse.ParentByNode);
        Assert.Equal(forward.VisualLayerByNode, reverse.VisualLayerByNode);
    }

    [Fact]
    public void Analyze_records_canonical_first_placement_parent()
    {
        var settings = DiagramSettings.CreateDefault();
        settings.Layout.ExposureTreeLayoutThreshold = 1;
        settings.NodeDuplication.AllowDuplicateNodes = false;
        settings.NodeDuplication.DuplicationExceptionPatterns.Clear();
        var graph = RenderGraph.From(DiagramWithRoots(
            Nodes("RootController", "OtherController", "SharedService"),
            new[] { "RootController", "OtherController" },
            Edges(("RootController", "SharedService"), ("OtherController", "SharedService"))), settings);

        var hierarchy = HierarchyAnalyzer.Analyze(graph, new LayoutRevision(0));
        var shared = graph.Nodes.Single(node => node.Name == "SharedService");

        Assert.Equal(LayoutNodeProvenance.CanonicalFirstPlacement, hierarchy.ProvenanceByNode[shared.Id]);
        Assert.Equal(graph.PlacementParentByNode[shared.Id], hierarchy.ParentByNode[shared.Id]);
    }

    [Fact]
    public void Analyze_records_duplicated_exposure_parent_and_regex_exception_provenance()
    {
        var duplicated = DiagramSettings.CreateDefault();
        duplicated.Layout.ExposureTreeLayoutThreshold = 1;
        var duplicatedGraph = RenderGraph.From(DiagramWithRoots(
            Nodes("RootController", "Service"),
            new[] { "RootController" },
            Edges(("RootController", "Service"))), duplicated);
        var duplicatedHierarchy = HierarchyAnalyzer.Analyze(duplicatedGraph, new LayoutRevision(0));
        var clonedService = duplicatedGraph.Nodes.Single(node => node.Name == "Service");
        Assert.Equal(LayoutNodeProvenance.DuplicatedExposureClone, duplicatedHierarchy.ProvenanceByNode[clonedService.Id]);
        Assert.NotNull(duplicatedHierarchy.ParentByNode[clonedService.Id]);

        var canonical = DiagramSettings.CreateDefault();
        canonical.Layout.ExposureTreeLayoutThreshold = 1;
        canonical.NodeDuplication.AllowDuplicateNodes = false;
        canonical.NodeDuplication.DuplicationExceptionPatterns.Add("Service$");
        var exceptionGraph = RenderGraph.From(DiagramWithRoots(
            Nodes("RootController", "OtherController", "Service"),
            new[] { "RootController", "OtherController" },
            Edges(("RootController", "Service"), ("OtherController", "Service"))), canonical);
        Assert.Equal(2, exceptionGraph.Nodes.Count(node => node.Name == "Service"));
    }

    [Fact]
    public void Analyze_honours_cancellation_before_returning_a_hierarchy()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var graph = RenderGraph.From(Diagram(Nodes("root"), Array.Empty<DependencyEdge>()));

        Assert.Throws<OperationCanceledException>(() =>
            HierarchyAnalyzer.Analyze(graph, new LayoutRevision(0), cancellation.Token));
    }

    private static LayoutHierarchy Analyze(TypeNode[] nodes, DependencyEdge[] edges) =>
        HierarchyAnalyzer.Analyze(RenderGraph.From(Diagram(nodes, edges)), new LayoutRevision(0));

    private static DiagramModel Diagram(TypeNode[] nodes, DependencyEdge[] edges) =>
        new(new[] { new ProjectContainer("project", "Project", nodes) }, Array.Empty<ExternalDependencyNode>(), edges);

    private static DiagramModel DiagramWithRoots(TypeNode[] nodes, string[] rootIds, DependencyEdge[] edges) =>
        new(new[] { new ProjectContainer("project", "Project", nodes) }, Array.Empty<ExternalDependencyNode>(), edges,
            new DiagramMetadata(SemanticSelection: new SemanticSelectionReport(
                "ConfiguredRootOutgoingReachability",
                new[] { new RootDiscoveryPatternDefinition(0, 1, "fixture") },
                rootIds.Select(id => new SemanticRootMatch(id, $"Fixture.{id}", 0, 1, "fixture")).ToArray(),
                Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<int>())));

    private static TypeNode[] Nodes(params string[] ids) =>
        ids.Select((id, order) => new TypeNode(id, "project", id, $"Fixture.{id}", "Class")).ToArray();

    private static DependencyEdge[] Edges(params (string Source, string Target)[] edges) =>
        edges.Select((edge, order) => new DependencyEdge($"edge-{order}", edge.Source, edge.Target, "internal")).ToArray();

    private static void AssertAcyclic(LayoutHierarchy hierarchy)
    {
        foreach (var node in hierarchy.StableNodeOrder)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var current = node;
            while (hierarchy.ParentByNode.TryGetValue(current, out var parent))
            {
                Assert.True(seen.Add(current), $"Cycle detected from {node}.");
                current = parent;
            }
        }
    }
}
