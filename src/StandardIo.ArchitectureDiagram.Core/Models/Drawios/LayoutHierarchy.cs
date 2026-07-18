using System.Collections.Generic;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal enum LayoutNodeProvenance
{
    Ordinary,
    CanonicalFirstPlacement,
    DuplicatedExposureClone,
    ExternalDependency
}

internal enum HierarchyEdgeDirection
{
    Downward,
    Lateral,
    Upward
}

internal sealed record LayoutComponent(
    int Id,
    IReadOnlyList<string> NodeIds,
    int VisualLayer,
    int StableOrder,
    bool IsRoot);

internal sealed class LayoutHierarchy
{
    public LayoutHierarchy(
        IReadOnlyDictionary<string, string> parentByNode,
        IReadOnlyDictionary<string, IReadOnlyList<string>> childrenByNode,
        IReadOnlyList<string> rootNodeIds,
        IReadOnlyDictionary<string, int> componentByNode,
        IReadOnlyList<LayoutComponent> components,
        IReadOnlyList<string> stableNodeOrder,
        IReadOnlyDictionary<string, int> visualLayerByNode,
        IReadOnlyDictionary<string, HierarchyEdgeDirection> edgeDirectionByLink,
        IReadOnlyDictionary<string, LayoutNodeProvenance> provenanceByNode,
        LayoutRevision revision)
    {
        ParentByNode = parentByNode;
        ChildrenByNode = childrenByNode;
        RootNodeIds = rootNodeIds;
        ComponentByNode = componentByNode;
        Components = components;
        StableNodeOrder = stableNodeOrder;
        VisualLayerByNode = visualLayerByNode;
        EdgeDirectionByLink = edgeDirectionByLink;
        ProvenanceByNode = provenanceByNode;
        Revision = revision;
    }

    public IReadOnlyDictionary<string, string> ParentByNode { get; }
    public IReadOnlyDictionary<string, IReadOnlyList<string>> ChildrenByNode { get; }
    public IReadOnlyList<string> RootNodeIds { get; }
    public IReadOnlyDictionary<string, int> ComponentByNode { get; }
    public IReadOnlyList<LayoutComponent> Components { get; }
    public IReadOnlyList<string> StableNodeOrder { get; }
    public IReadOnlyDictionary<string, int> VisualLayerByNode { get; }
    public IReadOnlyDictionary<string, HierarchyEdgeDirection> EdgeDirectionByLink { get; }
    public IReadOnlyDictionary<string, LayoutNodeProvenance> ProvenanceByNode { get; }
    public LayoutRevision Revision { get; }
}
