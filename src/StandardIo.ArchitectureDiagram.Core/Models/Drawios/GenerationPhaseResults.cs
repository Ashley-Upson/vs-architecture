using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal readonly record struct LayoutRevision(int Value)
{
    public LayoutRevision Next() => new(checked(Value + 1));
}

internal readonly record struct RouteRevision(int Value)
{
    public RouteRevision Next() => new(checked(Value + 1));
}

internal sealed class PlacedGraph
{
    public PlacedGraph(
        RenderGraph graph,
        IReadOnlyDictionary<string, NodeLayout> nodes,
        IReadOnlyDictionary<string, ProjectLayout> projects,
        LayoutRevision revision)
        : this(
            graph,
            HierarchyAnalyzer.Analyze(graph, revision),
            nodes.ToDictionary(
                item => item.Key,
                item => new NodeBasePlacement(
                    item.Key, item.Value.Rect, item.Value.Depth, item.Value.IsStandalone),
                StringComparer.Ordinal),
            new LayoutTranslations(nodes.ToDictionary(
                item => item.Key,
                _ => NodeTranslation.None,
                StringComparer.Ordinal)),
            nodes,
            ProjectPlacementResult.Create(graph, projects),
            revision)
    {
    }

    public PlacedGraph(
        RenderGraph graph,
        LayoutHierarchy hierarchy,
        IReadOnlyDictionary<string, NodeBasePlacement> nodeBasePlacements,
        LayoutTranslations translations,
        IReadOnlyDictionary<string, NodeLayout> nodes,
        ProjectPlacementResult projectPlacement,
        LayoutRevision revision)
    {
        Graph = graph ?? throw new ArgumentNullException(nameof(graph));
        Hierarchy = hierarchy ?? throw new ArgumentNullException(nameof(hierarchy));
        NodeBasePlacements = Snapshot(nodeBasePlacements);
        Translations = translations ?? throw new ArgumentNullException(nameof(translations));
        Nodes = Snapshot(nodes);
        ProjectPlacement = projectPlacement ?? throw new ArgumentNullException(nameof(projectPlacement));
        Revision = revision;
    }

    public RenderGraph Graph { get; }
    public LayoutHierarchy Hierarchy { get; }
    public IReadOnlyDictionary<string, NodeBasePlacement> NodeBasePlacements { get; }
    public LayoutTranslations Translations { get; }
    public IReadOnlyDictionary<string, NodeLayout> Nodes { get; }
    public ProjectPlacementResult ProjectPlacement { get; }
    public IReadOnlyDictionary<string, ProjectLayout> Projects => ProjectPlacement.Layouts;
    public NodeOwnership NodeOwnership => ProjectPlacement.NodeOwnership;
    public LayoutRevision Revision { get; }

    public PlacedGraph Revise(
        IReadOnlyDictionary<string, NodeLayout> nodes,
        IReadOnlyDictionary<string, ProjectLayout> projects)
    {
        var nextRevision = Revision.Next();
        return new PlacedGraph(
            Graph,
            Hierarchy.WithRevision(nextRevision),
            NodeBasePlacements,
            LayoutTranslations.Between(NodeBasePlacements, nodes),
            nodes,
            ProjectPlacement.WithLayouts(projects),
            nextRevision);
    }

    private static IReadOnlyDictionary<string, TValue> Snapshot<TValue>(
        IReadOnlyDictionary<string, TValue> source) =>
        new ReadOnlyDictionary<string, TValue>(source.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal));
}

internal sealed class GeneratedLogicalRoutes
{
    public GeneratedLogicalRoutes(
        PlacedGraph placement,
        IReadOnlyDictionary<string, LinkLayout> links,
        RouteRevision revision)
    {
        Placement = placement ?? throw new ArgumentNullException(nameof(placement));
        Links = Snapshot(links);
        Revision = revision;
    }

    public PlacedGraph Placement { get; }
    public LayoutRevision LayoutRevision => Placement.Revision;
    public RouteRevision Revision { get; }
    public IReadOnlyDictionary<string, LinkLayout> Links { get; }

    public void EnsureCompatible(PlacedGraph placement)
    {
        if (placement.Revision != LayoutRevision)
        {
            throw RevisionMismatch("generated routes", LayoutRevision, placement.Revision);
        }
    }

    internal static IReadOnlyDictionary<string, LinkLayout> Snapshot(IReadOnlyDictionary<string, LinkLayout> source) =>
        new ReadOnlyDictionary<string, LinkLayout>(source.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal));

    internal static InvalidOperationException RevisionMismatch(
        string state,
        LayoutRevision expected,
        LayoutRevision actual) =>
        new($"{state} belongs to layout revision {expected.Value}, not {actual.Value}.");
}

internal sealed class NormalizedLogicalRoutes
{
    public NormalizedLogicalRoutes(
        GeneratedLogicalRoutes generated,
        IReadOnlyDictionary<string, LinkLayout> links,
        RouteRevision revision)
    {
        Generated = generated ?? throw new ArgumentNullException(nameof(generated));
        Links = GeneratedLogicalRoutes.Snapshot(links);
        Revision = revision;
    }

    public GeneratedLogicalRoutes Generated { get; }
    public LayoutRevision LayoutRevision => Generated.LayoutRevision;
    public RouteRevision Revision { get; }
    public IReadOnlyDictionary<string, LinkLayout> Links { get; }

    public void EnsureCompatible(GeneratedLogicalRoutes generated)
    {
        if (generated.LayoutRevision != LayoutRevision || generated.Revision != Generated.Revision)
        {
            throw new InvalidOperationException(
                $"normalized routes revision {Revision.Value} does not belong to generated route revision {generated.Revision.Value} " +
                $"on layout revision {generated.LayoutRevision.Value}.");
        }
    }
}

internal sealed class ValidatedLogicalRoutes
{
    public ValidatedLogicalRoutes(
        NormalizedLogicalRoutes normalized,
        TraceabilityValidationResult validation)
    {
        Normalized = normalized ?? throw new ArgumentNullException(nameof(normalized));
        Validation = validation ?? throw new ArgumentNullException(nameof(validation));
    }

    public NormalizedLogicalRoutes Normalized { get; }
    public LayoutRevision LayoutRevision => Normalized.LayoutRevision;
    public RouteRevision RouteRevision => Normalized.Revision;
    public IReadOnlyDictionary<string, LinkLayout> Links => Normalized.Links;
    public TraceabilityValidationResult Validation { get; }

    public void EnsureCompatible(NormalizedLogicalRoutes normalized)
    {
        if (normalized.LayoutRevision != LayoutRevision || normalized.Revision != RouteRevision)
        {
            throw new InvalidOperationException(
                $"validation belongs to layout/route revision {LayoutRevision.Value}/{RouteRevision.Value}, not " +
                $"{normalized.LayoutRevision.Value}/{normalized.Revision.Value}.");
        }
    }

    public void ValidatedCompatibilityCheck() => EnsureCompatible(Normalized);
}
