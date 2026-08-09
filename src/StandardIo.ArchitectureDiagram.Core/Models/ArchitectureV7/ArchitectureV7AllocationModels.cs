using System;
using System.Collections.Generic;
using System.Linq;

namespace StandardIo.ArchitectureDiagram.Core.Models.ArchitectureV7;

public enum ArchitectureV7RunOrientation { Horizontal, Vertical }
public enum ArchitectureV7EndpointKind { SourceDeparture, DestinationArrival }
public enum ArchitectureV7EndpointDirection { Left, Down, Right, Up }

public sealed record ArchitectureV7AllocationConfiguration(
    int ParallelLaneSpacing,
    int TerminalPortSpacing,
    int TerminalInset);

public sealed class ArchitectureV7StraightRun
{
    public ArchitectureV7StraightRun(string runId, string physicalLinkId, ArchitectureV7RunOrientation orientation,
        IReadOnlyList<ArchitectureV7RouteCell> cells, int startRouteIndex, int endRouteIndex, string endpointContext, string provenance)
    {
        RunId = runId; PhysicalLinkId = physicalLinkId; Orientation = orientation;
        Cells = Array.AsReadOnly((cells ?? Array.Empty<ArchitectureV7RouteCell>()).ToArray());
        StartRouteIndex = startRouteIndex; EndRouteIndex = endRouteIndex; EndpointContext = endpointContext; Provenance = provenance;
    }
    public string RunId { get; }
    public string PhysicalLinkId { get; }
    public ArchitectureV7RunOrientation Orientation { get; }
    public IReadOnlyList<ArchitectureV7RouteCell> Cells { get; }
    public int StartRouteIndex { get; }
    public int EndRouteIndex { get; }
    public string EndpointContext { get; }
    public string Provenance { get; }
}

public sealed record ArchitectureV7PhysicalLane(string LaneId, ArchitectureV7RunOrientation Orientation, int LaneOrdinal, int ParallelLaneSpacing, IReadOnlyList<string> RunIds);
public sealed record ArchitectureV7RunLaneAssignment(string RunId, string LaneId, int LaneOrdinal);

public sealed record ArchitectureV7TerminalSlotAssignment(
    string PhysicalLinkId,
    string PhysicalNodeId,
    ArchitectureV7EndpointKind EndpointKind,
    ArchitectureV7EndpointDirection Direction,
    int SlotOrdinal,
    double RelativeOffset,
    int TerminalCapacityRequirement,
    string Provenance);

public sealed record ArchitectureV7EndpointApproachReservation(
    string PhysicalLinkId,
    string PhysicalNodeId,
    ArchitectureV7EndpointKind EndpointKind,
    int TerminalSlotOrdinal,
    int LaneOrdinal,
    int RequiredVerticalLane,
    int RequiredHorizontalLane,
    IReadOnlyList<ArchitectureV7RouteCell> ApproachCells,
    string Provenance);

public sealed record ArchitectureV7EndpointHandoff(
    string PhysicalLinkId,
    string PhysicalNodeId,
    ArchitectureV7EndpointKind EndpointKind,
    int TerminalSlotOrdinal,
    IReadOnlyList<ArchitectureV7RouteCell> AuthoritativeCells,
    string Reason,
    string Provenance);

public sealed record ArchitectureV7BendAllocation(
    string BendId,
    string PhysicalLinkId,
    int RouteIndex,
    ArchitectureV7RouteCell Cell,
    ArchitectureV7RunOrientation IncomingOrientation,
    ArchitectureV7RunOrientation OutgoingOrientation,
    string IncomingRunId,
    string OutgoingRunId,
    string Provenance);

public sealed record ArchitectureV7CrossingAllocation(
    string CrossingId,
    ArchitectureV7RouteCell Cell,
    string HorizontalPhysicalLinkId,
    string VerticalPhysicalLinkId,
    string Provenance);

public sealed record ArchitectureV7AllocationDiagnostic(string Code, string Message, bool IsHardFailure, string? PhysicalLinkId = null, string? RunId = null);

public sealed class ArchitectureV7CollectiveAllocationFreeze
{
    public ArchitectureV7CollectiveAllocationFreeze(
        IReadOnlyList<ArchitectureV7StraightRun> runs,
        IReadOnlyList<ArchitectureV7PhysicalLane> lanes,
        IReadOnlyList<ArchitectureV7RunLaneAssignment> runAssignments,
        IReadOnlyList<ArchitectureV7TerminalSlotAssignment> terminals,
        IReadOnlyList<ArchitectureV7EndpointApproachReservation> approaches,
        IReadOnlyList<ArchitectureV7EndpointHandoff> handoffs,
        IReadOnlyList<ArchitectureV7BendAllocation> bends,
        IReadOnlyList<ArchitectureV7CrossingAllocation> crossings,
        IReadOnlyList<ArchitectureV7AllocationDiagnostic> diagnostics,
        string placementFingerprint, string routeFingerprint, string allocationFingerprint)
    {
        Runs = Array.AsReadOnly((runs ?? Array.Empty<ArchitectureV7StraightRun>()).OrderBy(x => x.RunId, StringComparer.Ordinal).ToArray());
        Lanes = Array.AsReadOnly((lanes ?? Array.Empty<ArchitectureV7PhysicalLane>()).OrderBy(x => x.LaneId, StringComparer.Ordinal).ToArray());
        RunAssignments = Array.AsReadOnly((runAssignments ?? Array.Empty<ArchitectureV7RunLaneAssignment>()).OrderBy(x => x.RunId, StringComparer.Ordinal).ToArray());
        Terminals = Array.AsReadOnly((terminals ?? Array.Empty<ArchitectureV7TerminalSlotAssignment>()).OrderBy(x => x.PhysicalLinkId, StringComparer.Ordinal).ThenBy(x => x.EndpointKind).ThenBy(x => x.SlotOrdinal).ToArray());
        Approaches = Array.AsReadOnly((approaches ?? Array.Empty<ArchitectureV7EndpointApproachReservation>()).OrderBy(x => x.PhysicalLinkId, StringComparer.Ordinal).ThenBy(x => x.EndpointKind).ToArray());
        Handoffs = Array.AsReadOnly((handoffs ?? Array.Empty<ArchitectureV7EndpointHandoff>()).OrderBy(x => x.PhysicalLinkId, StringComparer.Ordinal).ThenBy(x => x.EndpointKind).ToArray());
        Bends = Array.AsReadOnly((bends ?? Array.Empty<ArchitectureV7BendAllocation>()).OrderBy(x => x.BendId, StringComparer.Ordinal).ToArray());
        Crossings = Array.AsReadOnly((crossings ?? Array.Empty<ArchitectureV7CrossingAllocation>()).OrderBy(x => x.CrossingId, StringComparer.Ordinal).ToArray());
        Diagnostics = Array.AsReadOnly((diagnostics ?? Array.Empty<ArchitectureV7AllocationDiagnostic>()).ToArray());
        PlacementFingerprint = placementFingerprint; RouteFingerprint = routeFingerprint; AllocationFingerprint = allocationFingerprint;
    }
    public IReadOnlyList<ArchitectureV7StraightRun> Runs { get; }
    public IReadOnlyList<ArchitectureV7PhysicalLane> Lanes { get; }
    public IReadOnlyList<ArchitectureV7RunLaneAssignment> RunAssignments { get; }
    public IReadOnlyList<ArchitectureV7TerminalSlotAssignment> Terminals { get; }
    public IReadOnlyList<ArchitectureV7EndpointApproachReservation> Approaches { get; }
    public IReadOnlyList<ArchitectureV7EndpointHandoff> Handoffs { get; }
    public IReadOnlyList<ArchitectureV7BendAllocation> Bends { get; }
    public IReadOnlyList<ArchitectureV7CrossingAllocation> Crossings { get; }
    public IReadOnlyList<ArchitectureV7AllocationDiagnostic> Diagnostics { get; }
    public string PlacementFingerprint { get; }
    public string RouteFingerprint { get; }
    public string AllocationFingerprint { get; }
    public bool IsComplete => !Diagnostics.Any(x => x.IsHardFailure);
}
