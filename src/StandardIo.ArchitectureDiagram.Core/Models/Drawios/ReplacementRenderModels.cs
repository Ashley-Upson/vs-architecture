using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core.Models;
using StandardIo.ArchitectureDiagram.Core.Models.Architectures;
using StandardIo.ArchitectureDiagram.Core.Models.Generation;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal sealed record ArchitecturePlacementNode(
    ArchitectureRenderNode Node,
    string RenderInstanceId,
    string SemanticNodeId,
    string? ProjectId,
    bool IsExternal,
    ArchitectureRenderNodeOccurrence Occurrence,
    ArchitectureDuplicationReason DuplicationReason,
    int Order,
    int DiscoveryOrder,
    int Depth,
    int Width,
    int Height,
    string PlacementGroup,
    bool IsBaseline,
    bool IsRoot,
    string? PositionalOwnerId,
    string? PlacementParentRenderId);

internal sealed record ArchitecturePlacementLink(
    ArchitectureRenderLink Link,
    int Order,
    string SourcePlanningNodeId,
    string TargetPlanningNodeId);

internal sealed class ArchitecturePlacementGraph
{
    private readonly ArchitectureRenderGraph source;

    public ArchitecturePlacementGraph(
        ArchitectureRenderGraph source,
        IReadOnlyList<ArchitecturePlacementNode> nodes,
        IReadOnlyList<ArchitecturePlacementLink> links)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));
        this.source = source;
        Projects = Array.AsReadOnly(source.Projects.ToArray());
        RenderNodes = Array.AsReadOnly(source.Nodes.ToArray());
        TraversalRootSemanticIds = Array.AsReadOnly(source.TraversalRootSemanticIds.ToArray());
        RenderInstancesBySemanticNodeId = new ReadOnlyDictionary<string, IReadOnlyList<string>>(
            source.RenderInstancesBySemanticNodeId.ToDictionary(
                item => item.Key,
                item => item.Value,
                StringComparer.Ordinal));
        Nodes = Array.AsReadOnly(nodes?.ToArray() ?? throw new ArgumentNullException(nameof(nodes)));
        Links = Array.AsReadOnly(links?.ToArray() ?? throw new ArgumentNullException(nameof(links)));
    }

    public IReadOnlyList<ArchitectureRenderProject> Projects { get; }
    public IReadOnlyList<ArchitectureRenderNode> RenderNodes { get; }
    public IReadOnlyList<string> TraversalRootSemanticIds { get; }
    public IReadOnlyDictionary<string, IReadOnlyList<string>> RenderInstancesBySemanticNodeId { get; }
    public IReadOnlyList<ArchitecturePlacementNode> Nodes { get; }
    public IReadOnlyList<ArchitecturePlacementLink> Links { get; }
    public ArchitectureRenderGraph Source => source;
}

internal sealed record ArchitectureTerminal(
    string LinkId,
    string NodeId,
    Point Point,
    bool IsSource,
    string Owner);

internal sealed record ArchitecturePhysicalRoute(
    ArchitecturePlacementLink Link,
    IReadOnlyList<Point> Points,
    string Topology,
    string SlotOwner,
    string ColumnOwner,
    int LaneX = 0,
    int SourceChannelY = 0,
    int TargetChannelY = 0);

internal sealed record ArchitectureExpansionEvent(
    string Kind,
    string Owner,
    string NodeId,
    int DeltaX,
    int FromX,
    int ToX,
    string Reason);

internal sealed record ArchitectureExpansionDiagnostics(
    IReadOnlyList<ArchitectureExpansionEvent> Events,
    IReadOnlyList<object> WidestGaps,
    int MaximumX,
    double MedianX,
    int LocalColumnCount,
    int MaximumColumnX,
    IReadOnlyDictionary<string, int> WidthContributions);

internal sealed record ArchitectureCandidatePlan(
    string CandidateId,
    IReadOnlyDictionary<string, Rect> NodeRects,
    IReadOnlyDictionary<string, Rect> ProjectRects,
    IReadOnlyList<ArchitectureTerminal> Terminals,
    IReadOnlyList<ArchitecturePhysicalRoute> Routes,
    IReadOnlyList<string> OwnershipTransitions,
    IReadOnlyList<string> Provenance,
    IReadOnlyList<ValidationFinding> Findings,
    int RouteLength,
    int BendCount,
    ArchitectureExpansionDiagnostics ExpansionDiagnostics,
    ArchitectureRoutingEvidence RoutingEvidence);

internal sealed record ArchitecturePhysicalScene(
    ArchitectureCandidatePlan Candidate,
    Rect PageBounds,
    IReadOnlyDictionary<string, Rect> ProjectLabelRects,
    IReadOnlyList<ArchitecturePhysicalRoute> Routes,
    IReadOnlyList<string> OwnershipTransitions,
    IReadOnlyList<ValidationFinding> Findings);

internal sealed record CanonicalRoutingAllocation(
    IReadOnlyList<ArchitectureTerminal> Terminals,
    IReadOnlyList<ArchitecturePhysicalRoute> Routes,
    IReadOnlyList<ValidationFinding> Findings,
    ArchitectureRoutingEvidence Evidence,
    IReadOnlyDictionary<ProjectLayerExpansionIdentity, int>? RequiredLayerExpansion = null,
    IReadOnlyDictionary<ProjectLayerExpansionIdentity, int>? RequiredLayerExtents = null);

internal sealed record DrawioPageCell(
    string Id,
    string ParentId,
    string? SourceId,
    string? TargetId,
    string Value,
    string Style,
    Rect? Bounds,
    IReadOnlyList<Point> Waypoints,
    bool IsEdge,
    string? SemanticSourceId = null,
    string? SemanticTargetId = null,
    string? SemanticNodeId = null,
    double? ExitX = null,
    double? EntryX = null);

internal sealed class DrawioPageModel
{
    public DrawioPageModel(
        IReadOnlyList<DrawioPageCell> cells,
        Rect pageBounds,
        IReadOnlyList<string> provenance)
    {
        Cells = new ReadOnlyCollection<DrawioPageCell>(cells?.ToArray() ?? throw new ArgumentNullException(nameof(cells)));
        PageBounds = pageBounds;
        Provenance = new ReadOnlyCollection<string>(provenance?.ToArray() ?? throw new ArgumentNullException(nameof(provenance)));
    }

    public IReadOnlyList<DrawioPageCell> Cells { get; }
    public Rect PageBounds { get; }
    public IReadOnlyList<string> Provenance { get; }
}
