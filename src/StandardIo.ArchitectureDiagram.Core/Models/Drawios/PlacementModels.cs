using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal sealed record NodeBasePlacement(
    string NodeId,
    Rect Rect,
    int Depth,
    bool IsStandalone);

internal sealed record NodeTranslation(int DeltaX, int DeltaY)
{
    public static NodeTranslation None { get; } = new(0, 0);
}

internal sealed class LayoutTranslations
{
    public LayoutTranslations(IReadOnlyDictionary<string, NodeTranslation> byNode)
    {
        ByNode = new ReadOnlyDictionary<string, NodeTranslation>(
            byNode.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal));
    }

    public IReadOnlyDictionary<string, NodeTranslation> ByNode { get; }

    public Rect Materialize(string nodeId, NodeBasePlacement placement)
    {
        var translation = ByNode.TryGetValue(nodeId, out var value) ? value : NodeTranslation.None;
        return placement.Rect.Translate(translation.DeltaX, translation.DeltaY);
    }

    public static LayoutTranslations Between(
        IReadOnlyDictionary<string, NodeBasePlacement> bases,
        IReadOnlyDictionary<string, NodeLayout> materialized) =>
        new(materialized.ToDictionary(
            item => item.Key,
            item =>
            {
                if (!bases.TryGetValue(item.Key, out var basePlacement))
                {
                    throw new InvalidOperationException($"Node {item.Key} has no base placement.");
                }

                return new NodeTranslation(
                    item.Value.Rect.X - basePlacement.Rect.X,
                    item.Value.Rect.Y - basePlacement.Rect.Y);
            },
            StringComparer.Ordinal));
}

internal sealed class NodeOwnership
{
    public NodeOwnership(
        IReadOnlyDictionary<string, string> projectByNode,
        IReadOnlyList<string> rootOwnedNodeIds,
        IReadOnlyDictionary<string, string> externalOwnerProjectByNode)
    {
        ProjectByNode = Snapshot(projectByNode);
        RootOwnedNodeIds = Array.AsReadOnly(rootOwnedNodeIds.ToArray());
        ExternalOwnerProjectByNode = Snapshot(externalOwnerProjectByNode);
    }

    public IReadOnlyDictionary<string, string> ProjectByNode { get; }
    public IReadOnlyList<string> RootOwnedNodeIds { get; }
    public IReadOnlyDictionary<string, string> ExternalOwnerProjectByNode { get; }

    public static NodeOwnership From(RenderGraph graph)
    {
        var projectByNode = graph.Nodes
            .Where(node => node.ProjectId is not null)
            .ToDictionary(node => node.Id, node => node.ProjectId!, StringComparer.Ordinal);
        return new NodeOwnership(
            projectByNode,
            graph.Nodes.Where(node => node.ProjectId is null).Select(node => node.Id).ToArray(),
            graph.Nodes.Where(node => node.IsExternal && node.ProjectId is not null)
                .ToDictionary(node => node.Id, node => node.ProjectId!, StringComparer.Ordinal));
    }

    private static IReadOnlyDictionary<string, string> Snapshot(IReadOnlyDictionary<string, string> source) =>
        new ReadOnlyDictionary<string, string>(
            source.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal));
}

internal sealed class ProjectPlacementResult
{
    public ProjectPlacementResult(
        IReadOnlyDictionary<string, ProjectLayout> layouts,
        IReadOnlyList<string> stableProjectOrder,
        IReadOnlyDictionary<string, IReadOnlyList<string>> nodeIdsByProject,
        NodeOwnership nodeOwnership)
    {
        Layouts = new ReadOnlyDictionary<string, ProjectLayout>(
            layouts.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal));
        StableProjectOrder = Array.AsReadOnly(stableProjectOrder.ToArray());
        NodeIdsByProject = new ReadOnlyDictionary<string, IReadOnlyList<string>>(
            nodeIdsByProject.ToDictionary(
                item => item.Key,
                item => (IReadOnlyList<string>)Array.AsReadOnly(item.Value.ToArray()),
                StringComparer.Ordinal));
        NodeOwnership = nodeOwnership;
    }

    public IReadOnlyDictionary<string, ProjectLayout> Layouts { get; }
    public IReadOnlyList<string> StableProjectOrder { get; }
    public IReadOnlyDictionary<string, IReadOnlyList<string>> NodeIdsByProject { get; }
    public NodeOwnership NodeOwnership { get; }

    public ProjectPlacementResult WithLayouts(IReadOnlyDictionary<string, ProjectLayout> layouts) =>
        new(layouts, StableProjectOrder, NodeIdsByProject, NodeOwnership);

    public static ProjectPlacementResult Create(
        RenderGraph graph,
        IReadOnlyDictionary<string, ProjectLayout> layouts) =>
        new(
            layouts,
            graph.Projects.OrderBy(project => project.Order).ThenBy(project => project.Id, StringComparer.Ordinal)
                .Select(project => project.Id).ToArray(),
            graph.Projects.ToDictionary(
                project => project.Id,
                project => (IReadOnlyList<string>)graph.Nodes
                    .Where(node => string.Equals(node.ProjectId, project.Id, StringComparison.Ordinal))
                    .OrderBy(node => node.Order).ThenBy(node => node.Id, StringComparer.Ordinal)
                    .Select(node => node.Id).ToArray(),
                StringComparer.Ordinal),
            NodeOwnership.From(graph));
}
