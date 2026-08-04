using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core.Models;
using StandardIo.ArchitectureDiagram.Core.Models.Architectures;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal sealed record ArchitecturePlanningNode(
    ArchitectureRenderNode Node,
    int Order,
    int Depth,
    int Width,
    int Height,
    string PlacementGroup);

internal sealed record ArchitecturePlanningLink(
    ArchitectureRenderLink Link,
    int Order,
    string Topology,
    string SourcePlanningNodeId,
    string TargetPlanningNodeId);

internal sealed class ArchitecturePlanningGraph
{
    public ArchitecturePlanningGraph(
        ArchitectureRenderGraph source,
        IReadOnlyList<ArchitecturePlanningNode> nodes,
        IReadOnlyList<ArchitecturePlanningLink> links)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        Nodes = Array.AsReadOnly(nodes?.ToArray() ?? throw new ArgumentNullException(nameof(nodes)));
        Links = Array.AsReadOnly(links?.ToArray() ?? throw new ArgumentNullException(nameof(links)));
    }

    public ArchitectureRenderGraph Source { get; }
    public IReadOnlyList<ArchitecturePlanningNode> Nodes { get; }
    public IReadOnlyList<ArchitecturePlanningLink> Links { get; }
}

internal sealed record ArchitectureTerminal(
    string LinkId,
    string NodeId,
    Point Point,
    bool IsSource,
    string Owner);

internal sealed record ArchitecturePhysicalRoute(
    ArchitecturePlanningLink Link,
    IReadOnlyList<Point> Points,
    string Topology,
    string SlotOwner,
    string ColumnOwner);

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
    int BendCount);

internal sealed record ArchitecturePhysicalScene(
    ArchitectureCandidatePlan Candidate,
    Rect PageBounds,
    IReadOnlyDictionary<string, Rect> ProjectLabelRects,
    IReadOnlyList<ArchitecturePhysicalRoute> Routes,
    IReadOnlyList<string> OwnershipTransitions,
    IReadOnlyList<ValidationFinding> Findings);

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
    string? SemanticTargetId = null);

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
