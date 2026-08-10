using System;
using System.Collections.Generic;
using System.Linq;

namespace StandardIo.ArchitectureDiagram.Core.Models.ArchitectureV7;

public enum ArchitectureV7RunOrientation { Horizontal, Vertical }
public enum ArchitectureV7EndpointKind { SourceDeparture, DestinationArrival }
public enum ArchitectureV7EndpointDirection { Left, Down, Right, Up }

/// <summary>Physical offset from the centre of a frozen logical cell, in pixels.</summary>
public sealed record ArchitectureV7PhysicalRelativePosition(double XOffset, double YOffset);

public sealed record ArchitectureV7AllocationConfiguration(
    int ParallelLaneSpacing,
    int TerminalPortSpacing,
    int TerminalInset,
    int BaseCellWidth = 100,
    int ResourceClearance = 0);

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

public sealed class ArchitectureV7EndpointHandoff
{
    public ArchitectureV7EndpointHandoff(
        string physicalLinkId,
        string physicalNodeId,
        ArchitectureV7EndpointKind endpointKind,
        int terminalSlotOrdinal,
        IReadOnlyList<ArchitectureV7RouteCell> authoritativeCells,
        string reason,
        string provenance,
        string handoffId = "",
        ArchitectureV7RouteCell? logicalCell = null,
        int startRouteIndex = -1,
        int endRouteIndex = -1,
        string adjacentRunId = "",
        string adjacentLaneId = "",
        ArchitectureV7RunOrientation handoffOrientation = ArchitectureV7RunOrientation.Vertical,
        ArchitectureV7EndpointDirection endpointDirection = ArchitectureV7EndpointDirection.Down,
        double terminalAxisOffset = 0,
        double laneAxisOffset = 0,
        double relativePhysicalOffset = 0,
        double requiredClearance = 0,
        ArchitectureV7PhysicalRelativePosition? relativePosition = null)
    {
        PhysicalLinkId = physicalLinkId;
        PhysicalNodeId = physicalNodeId;
        EndpointKind = endpointKind;
        TerminalSlotOrdinal = terminalSlotOrdinal;
        AuthoritativeCells = Array.AsReadOnly((authoritativeCells ?? Array.Empty<ArchitectureV7RouteCell>()).ToArray());
        Reason = reason;
        Provenance = provenance;
        HandoffId = handoffId;
        LogicalCell = logicalCell;
        StartRouteIndex = startRouteIndex;
        EndRouteIndex = endRouteIndex;
        AdjacentRunId = adjacentRunId;
        AdjacentLaneId = adjacentLaneId;
        HandoffOrientation = handoffOrientation;
        EndpointDirection = endpointDirection;
        TerminalAxisOffset = terminalAxisOffset;
        LaneAxisOffset = laneAxisOffset;
        RelativePhysicalOffset = relativePhysicalOffset;
        RequiredClearance = requiredClearance;
        RelativePosition = relativePosition ?? new(0, 0);
    }

    public string PhysicalLinkId { get; }
    public string PhysicalNodeId { get; }
    public ArchitectureV7EndpointKind EndpointKind { get; }
    public int TerminalSlotOrdinal { get; }
    public IReadOnlyList<ArchitectureV7RouteCell> AuthoritativeCells { get; }
    public string Reason { get; }
    public string Provenance { get; }
    public string HandoffId { get; }
    public ArchitectureV7RouteCell? LogicalCell { get; }
    public int StartRouteIndex { get; }
    public int EndRouteIndex { get; }
    public string AdjacentRunId { get; }
    public string AdjacentLaneId { get; }
    public ArchitectureV7RunOrientation HandoffOrientation { get; }
    public ArchitectureV7EndpointDirection EndpointDirection { get; }
    public double TerminalAxisOffset { get; }
    public double LaneAxisOffset { get; }
    public double RelativePhysicalOffset { get; }
    public double RequiredClearance { get; }
    public ArchitectureV7PhysicalRelativePosition RelativePosition { get; }

    public string ResourceId => string.IsNullOrEmpty(HandoffId)
        ? $"handoff:{PhysicalLinkId}:{EndpointKind}:{TerminalSlotOrdinal}"
        : HandoffId;
}

public sealed record ArchitectureV7BendAllocation(
    string BendId,
    string PhysicalLinkId,
    int RouteIndex,
    ArchitectureV7RouteCell Cell,
    ArchitectureV7RunOrientation IncomingOrientation,
    ArchitectureV7RunOrientation OutgoingOrientation,
    string IncomingRunId,
    string OutgoingRunId,
    string Provenance,
    string IncomingLaneId = "",
    string OutgoingLaneId = "",
    double RelativePhysicalOffset = 0,
    double RequiredClearance = 0,
    ArchitectureV7PhysicalRelativePosition? RelativePosition = null)
{
    public ArchitectureV7PhysicalRelativePosition EffectiveRelativePosition => RelativePosition ?? new(0, 0);
}

public sealed record ArchitectureV7CrossingAllocation(
    string CrossingId,
    ArchitectureV7RouteCell Cell,
    string HorizontalPhysicalLinkId,
    string VerticalPhysicalLinkId,
    string Provenance,
    string HorizontalRunId = "",
    string VerticalRunId = "",
    string HorizontalLaneId = "",
    string VerticalLaneId = "",
    double RelativePhysicalOffset = 0,
    double RequiredClearance = 0,
    string Classification = "clean-crossing",
    int HorizontalRouteIndex = -1,
    int VerticalRouteIndex = -1,
    ArchitectureV7PhysicalRelativePosition? RelativePosition = null)
{
    public ArchitectureV7PhysicalRelativePosition EffectiveRelativePosition => RelativePosition ?? new(0, 0);
}

public sealed record ArchitectureV7PhysicalTrackDemand(
    int LogicalRow,
    int LogicalColumn,
    double RequiredRowExtent,
    double RequiredColumnExtent,
    IReadOnlyList<string> ResourceIds,
    string Provenance);

public sealed record ArchitectureV7AllocationDiagnostic(string Code, string Message, bool IsHardFailure, string? PhysicalLinkId = null, string? RunId = null,
    string? PhysicalNodeId = null, string? EndpointKind = null, int? RequiredWidth = null, int? AvailableWidth = null, int? LogicalSpan = null,
    IReadOnlyList<string>? ConflictingPhysicalLinkIds = null, IReadOnlyList<string>? ConflictingRunIds = null);

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
        TrackDemands = Array.AsReadOnly(BuildTrackDemands(Handoffs, Bends, Crossings));
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
    public IReadOnlyList<ArchitectureV7PhysicalTrackDemand> TrackDemands { get; }
    public IReadOnlyList<ArchitectureV7AllocationDiagnostic> Diagnostics { get; }
    public string PlacementFingerprint { get; }
    public string RouteFingerprint { get; }
    public string AllocationFingerprint { get; }
    public bool IsComplete => !Diagnostics.Any(x => x.IsHardFailure);

    private static ArchitectureV7PhysicalTrackDemand[] BuildTrackDemands(
        IReadOnlyList<ArchitectureV7EndpointHandoff> handoffs,
        IReadOnlyList<ArchitectureV7BendAllocation> bends,
        IReadOnlyList<ArchitectureV7CrossingAllocation> crossings)
    {
        var result = new List<ArchitectureV7PhysicalTrackDemand>();
        foreach (var handoff in handoffs)
        {
            if (handoff.LogicalCell is not { } cell) continue;
            var extent = Math.Max(0, 2 * handoff.RequiredClearance + 2 * Math.Abs(handoff.RelativePhysicalOffset));
            result.Add(new(cell.Row, cell.Column,
                handoff.HandoffOrientation == ArchitectureV7RunOrientation.Vertical ? extent : 0,
                handoff.HandoffOrientation == ArchitectureV7RunOrientation.Horizontal ? extent : 0,
                new[] { handoff.ResourceId }, "endpoint-handoff;cell-centre-relative-offset"));
        }
        foreach (var bend in bends)
        {
            var position = bend.EffectiveRelativePosition;
            var extent = Math.Max(0, 2 * bend.RequiredClearance);
            result.Add(new(bend.Cell.Row, bend.Cell.Column,
                Math.Max(extent, 2 * Math.Abs(position.YOffset) + extent),
                Math.Max(extent, 2 * Math.Abs(position.XOffset) + extent),
                new[] { bend.BendId }, "bend-resource;cell-centre-relative-offset"));
        }
        foreach (var crossing in crossings)
        {
            var position = crossing.EffectiveRelativePosition;
            var extent = Math.Max(0, 2 * crossing.RequiredClearance);
            result.Add(new(crossing.Cell.Row, crossing.Cell.Column,
                Math.Max(extent, 2 * Math.Abs(position.YOffset) + extent),
                Math.Max(extent, 2 * Math.Abs(position.XOffset) + extent),
                new[] { crossing.CrossingId }, "crossing-resource;cell-centre-relative-offset"));
        }
        return result.ToArray();
    }
}
