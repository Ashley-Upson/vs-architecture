using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

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
    public bool IsEndpointTransition => EndpointContext.StartsWith("endpoint-transition:", StringComparison.Ordinal);
    public int? ConflictStart { get; init; }
    public int? ConflictEnd { get; init; }
}

/// <summary>A node-span-owned vertical corridor and its collectively allocated routing-row transition.</summary>
public sealed record ArchitectureV7EndpointCorridor(
    string ResourceId, string PhysicalLinkId, string PhysicalNodeId,
    ArchitectureV7EndpointKind EndpointKind, int RouteIndex, int RoutingRow,
    string OrdinaryRunId, string HorizontalRunId, double SignedSlot,
    double X, double NodeEdgeY, double RoutingY, double OrdinaryX,
    double SpanLeft, double SpanRight, string Provenance);

public sealed record ArchitectureV7PhysicalLane(string LaneId, ArchitectureV7RunOrientation Orientation, int LaneOrdinal, int ParallelLaneSpacing, IReadOnlyList<string> RunIds);
public sealed record ArchitectureV7RunLaneAssignment(string RunId, string LaneId, int LaneOrdinal)
{
    /// <summary>
    /// The domain-local midpoint-relative slot. LaneOrdinal remains the stable
    /// allocation identity; this value is the physical signed authority.
    /// </summary>
    public double SignedLaneOrdinal { get; init; } = LaneOrdinal;
}

public sealed record ArchitectureV7EndpointLaneCoordinate(
    string PhysicalLinkId,
    string PhysicalNodeId,
    ArchitectureV7EndpointKind EndpointKind,
    string RunId,
    double RelativeXOffset,
    string Provenance)
{
    public double SignedLaneOrdinal { get; init; }
}

/// <summary>Allocator-owned local orthogonal bend for a fixed incoming X and packed terminal X.</summary>
public sealed record ArchitectureV7EndpointZBend(
    string PhysicalLinkId,
    string PhysicalNodeId,
    ArchitectureV7EndpointKind EndpointKind,
    string FixedRunId,
    string Provenance);

public sealed record ArchitectureV7SharedVerticalRunConstraint(
    string PhysicalLinkId,
    string RunId,
    string SourcePhysicalNodeId,
    string DestinationPhysicalNodeId,
    string Provenance);

public sealed record ArchitectureV7TerminalSlotAssignment(
    string PhysicalLinkId,
    string PhysicalNodeId,
    ArchitectureV7EndpointKind EndpointKind,
    ArchitectureV7EndpointDirection Direction,
    int SlotOrdinal,
    double RelativeOffset,
    int TerminalCapacityRequirement,
    string Provenance)
{
    public double SignedSlotOrdinal { get; init; }
}

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
    ArchitectureV7PhysicalRelativePosition? RelativePosition = null,
    IReadOnlyList<string>? InteractionIds = null)
{
    public ArchitectureV7PhysicalRelativePosition EffectiveRelativePosition => RelativePosition ?? new(0, 0);
    public IReadOnlyList<string> AssociatedInteractionIds =>
        Array.AsReadOnly((InteractionIds ?? Array.Empty<string>()).OrderBy(x => x, StringComparer.Ordinal).ToArray());
}

/// <summary>
/// Immutable evidence that routes interact at one dense logical cell. This is
/// deliberately separate from the physical resource allocated for that geometry.
/// </summary>
public sealed record ArchitectureV7CrossingInteraction(
    string InteractionId,
    ArchitectureV7RouteCell Cell,
    string Classification,
    string HorizontalPhysicalLinkId,
    string VerticalPhysicalLinkId,
    string HorizontalRunId,
    string VerticalRunId,
    string HorizontalLaneId,
    string VerticalLaneId,
    int HorizontalRouteIndex,
    int VerticalRouteIndex,
    string Provenance,
    string ResourceId = "",
    string BendResourceId = "");

public sealed record ArchitectureV7PhysicalCrossingResource(
    string ResourceId,
    ArchitectureV7RouteCell Cell,
    string Classification,
    string HorizontalLaneId,
    string VerticalLaneId,
    ArchitectureV7PhysicalRelativePosition RelativePosition,
    double RequiredClearance,
    IReadOnlyList<string> InteractionIds,
    string Provenance);

public sealed record ArchitectureV7PhysicalTrackDemand(
    int LogicalRow,
    int LogicalColumn,
    double RequiredRowExtent,
    double RequiredColumnExtent,
    IReadOnlyList<string> ResourceIds,
    string Provenance,
    double RequiredRowMinimumOffset = 0,
    double RequiredRowMaximumOffset = 0,
    double RequiredColumnMinimumOffset = 0,
    double RequiredColumnMaximumOffset = 0);

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
        string placementFingerprint, string routeFingerprint, string allocationFingerprint,
        IReadOnlyList<ArchitectureV7CrossingInteraction>? crossingInteractions = null,
        ArchitectureV7AllocationConfiguration? allocationConfiguration = null,
        IReadOnlyList<ArchitectureV7EndpointLaneCoordinate>? endpointLaneCoordinates = null,
        IReadOnlyList<ArchitectureV7SharedVerticalRunConstraint>? sharedVerticalRunConstraints = null,
        IReadOnlyList<ArchitectureV7EndpointZBend>? endpointZBends = null,
        IReadOnlyList<ArchitectureV7EndpointCorridor>? endpointCorridors = null)
    {
        Runs = Array.AsReadOnly((runs ?? Array.Empty<ArchitectureV7StraightRun>()).OrderBy(x => x.RunId, StringComparer.Ordinal).ToArray());
        EndpointCorridors = Array.AsReadOnly((endpointCorridors ?? Array.Empty<ArchitectureV7EndpointCorridor>()).ToArray());
        Lanes = Array.AsReadOnly((lanes ?? Array.Empty<ArchitectureV7PhysicalLane>()).OrderBy(x => x.LaneId, StringComparer.Ordinal).ToArray());
        RunAssignments = Array.AsReadOnly((runAssignments ?? Array.Empty<ArchitectureV7RunLaneAssignment>()).OrderBy(x => x.RunId, StringComparer.Ordinal).ToArray());
        Terminals = Array.AsReadOnly((terminals ?? Array.Empty<ArchitectureV7TerminalSlotAssignment>()).OrderBy(x => x.PhysicalLinkId, StringComparer.Ordinal).ThenBy(x => x.EndpointKind).ThenBy(x => x.SlotOrdinal).ToArray());
        Approaches = Array.AsReadOnly((approaches ?? Array.Empty<ArchitectureV7EndpointApproachReservation>()).OrderBy(x => x.PhysicalLinkId, StringComparer.Ordinal).ThenBy(x => x.EndpointKind).ToArray());
        Handoffs = Array.AsReadOnly((handoffs ?? Array.Empty<ArchitectureV7EndpointHandoff>()).OrderBy(x => x.PhysicalLinkId, StringComparer.Ordinal).ThenBy(x => x.EndpointKind).ToArray());
        Bends = Array.AsReadOnly((bends ?? Array.Empty<ArchitectureV7BendAllocation>()).OrderBy(x => x.BendId, StringComparer.Ordinal).ToArray());
        Crossings = Array.AsReadOnly((crossings ?? Array.Empty<ArchitectureV7CrossingAllocation>()).OrderBy(x => x.CrossingId, StringComparer.Ordinal).ToArray());
        CrossingInteractions = Array.AsReadOnly((crossingInteractions ?? Array.Empty<ArchitectureV7CrossingInteraction>()).OrderBy(x => x.InteractionId, StringComparer.Ordinal).ToArray());
        EndpointLaneCoordinates = Array.AsReadOnly((endpointLaneCoordinates ?? Array.Empty<ArchitectureV7EndpointLaneCoordinate>())
            .OrderBy(x => x.PhysicalLinkId, StringComparer.Ordinal).ThenBy(x => x.EndpointKind).ToArray());
        SharedVerticalRunConstraints = Array.AsReadOnly((sharedVerticalRunConstraints ?? Array.Empty<ArchitectureV7SharedVerticalRunConstraint>())
            .OrderBy(x => x.PhysicalLinkId, StringComparer.Ordinal).ToArray());
        EndpointZBends = Array.AsReadOnly((endpointZBends ?? Array.Empty<ArchitectureV7EndpointZBend>())
            .OrderBy(x => x.PhysicalLinkId, StringComparer.Ordinal).ThenBy(x => x.EndpointKind).ToArray());
        CrossingResources = Array.AsReadOnly(Crossings.GroupBy(crossing => GeometryKey(crossing.Cell, crossing.Classification,
                crossing.HorizontalLaneId, crossing.VerticalLaneId, crossing.EffectiveRelativePosition, crossing.RequiredClearance), StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group =>
            {
                var crossing = group.First();
                return new ArchitectureV7PhysicalCrossingResource(
                    crossing.CrossingId, crossing.Cell, crossing.Classification, crossing.HorizontalLaneId, crossing.VerticalLaneId,
                    crossing.EffectiveRelativePosition, group.Max(item => item.RequiredClearance),
                    group.SelectMany(item => item.AssociatedInteractionIds).Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal).ToArray(),
                    "physical-crossing-resource;geometry-keyed;" + group.Key);
            }).OrderBy(resource => resource.ResourceId, StringComparer.Ordinal).ToArray());
        TrackDemands = Array.AsReadOnly(BuildTrackDemands(Runs, RunAssignments, Lanes, Handoffs, Bends, allocationConfiguration));
        CrossingInteractionFingerprint = Fingerprint(CrossingInteractions.Select(interaction => interaction.InteractionId + ":" + interaction.Classification + ":" + interaction.ResourceId + ":" + interaction.BendResourceId));
        CrossingResourceFingerprint = Fingerprint(CrossingResources.Select(resource => resource.ResourceId + ":" + string.Join(",", resource.InteractionIds)));
        TrackDemandFingerprint = Fingerprint(TrackDemands.Select(demand => demand.LogicalRow + ":" + demand.LogicalColumn + ":" + demand.RequiredRowExtent + ":" + demand.RequiredColumnExtent + ":" + string.Join(",", demand.ResourceIds)));
        Diagnostics = Array.AsReadOnly((diagnostics ?? Array.Empty<ArchitectureV7AllocationDiagnostic>()).ToArray());
        PlacementFingerprint = placementFingerprint; RouteFingerprint = routeFingerprint; AllocationFingerprint = allocationFingerprint;
        AllocationConfiguration = allocationConfiguration;
    }
    public IReadOnlyList<ArchitectureV7StraightRun> Runs { get; }
    public IReadOnlyList<ArchitectureV7PhysicalLane> Lanes { get; }
    public IReadOnlyList<ArchitectureV7RunLaneAssignment> RunAssignments { get; }
    public IReadOnlyList<ArchitectureV7TerminalSlotAssignment> Terminals { get; }
    public IReadOnlyList<ArchitectureV7EndpointApproachReservation> Approaches { get; }
    public IReadOnlyList<ArchitectureV7EndpointHandoff> Handoffs { get; }
    public IReadOnlyList<ArchitectureV7BendAllocation> Bends { get; }
    public IReadOnlyList<ArchitectureV7CrossingAllocation> Crossings { get; }
    public IReadOnlyList<ArchitectureV7CrossingInteraction> CrossingInteractions { get; }
    public IReadOnlyList<ArchitectureV7EndpointLaneCoordinate> EndpointLaneCoordinates { get; }
    public IReadOnlyList<ArchitectureV7SharedVerticalRunConstraint> SharedVerticalRunConstraints { get; }
    public IReadOnlyList<ArchitectureV7EndpointZBend> EndpointZBends { get; }
    public IReadOnlyList<ArchitectureV7EndpointCorridor> EndpointCorridors { get; }
    public IReadOnlyList<ArchitectureV7PhysicalCrossingResource> CrossingResources { get; }
    public IReadOnlyList<ArchitectureV7PhysicalTrackDemand> TrackDemands { get; }
    public string CrossingInteractionFingerprint { get; }
    public string CrossingResourceFingerprint { get; }
    public string TrackDemandFingerprint { get; }
    public IReadOnlyList<ArchitectureV7AllocationDiagnostic> Diagnostics { get; }
    public string PlacementFingerprint { get; }
    public string RouteFingerprint { get; }
    public string AllocationFingerprint { get; }
    public ArchitectureV7AllocationConfiguration? AllocationConfiguration { get; }
    public bool IsComplete => !Diagnostics.Any(x => x.IsHardFailure);

    public ArchitectureV7CollectiveAllocationFreeze WithEndpointGeometry(
        IReadOnlyList<ArchitectureV7TerminalSlotAssignment> terminals,
        IReadOnlyList<ArchitectureV7EndpointApproachReservation> approaches,
        IReadOnlyList<ArchitectureV7EndpointLaneCoordinate> endpointLaneCoordinates,
        IReadOnlyList<ArchitectureV7EndpointZBend>? endpointZBends = null,
        IReadOnlyList<ArchitectureV7EndpointCorridor>? endpointCorridors = null,
        IReadOnlyList<ArchitectureV7AllocationDiagnostic>? endpointDiagnostics = null)
    {
        var terminalFingerprint = string.Join(";", terminals.OrderBy(x => x.PhysicalLinkId, StringComparer.Ordinal)
            .ThenBy(x => x.EndpointKind).Select(x => x.PhysicalLinkId + ":" + x.EndpointKind + ":" + x.SlotOrdinal + ":" + x.RelativeOffset.ToString("R", System.Globalization.CultureInfo.InvariantCulture)));
        return new ArchitectureV7CollectiveAllocationFreeze(
            Runs, Lanes, RunAssignments, terminals, approaches, Handoffs, Bends, Crossings, endpointDiagnostics ?? Diagnostics,
            PlacementFingerprint, RouteFingerprint, AllocationFingerprint + "|endpoint-geometry:" + terminalFingerprint,
            CrossingInteractions, AllocationConfiguration, endpointLaneCoordinates, SharedVerticalRunConstraints,
            endpointZBends ?? EndpointZBends, endpointCorridors ?? EndpointCorridors);
    }

    private static ArchitectureV7PhysicalTrackDemand[] BuildTrackDemands(
        IReadOnlyList<ArchitectureV7StraightRun> runs,
        IReadOnlyList<ArchitectureV7RunLaneAssignment> assignments,
        IReadOnlyList<ArchitectureV7PhysicalLane> lanes,
        IReadOnlyList<ArchitectureV7EndpointHandoff> handoffs,
        IReadOnlyList<ArchitectureV7BendAllocation> bends,
        ArchitectureV7AllocationConfiguration? configuration)
    {
        var demands = new Dictionary<(int Row, int Column), MutableTrackDemand>();

        if (configuration is not null)
        {
            foreach (var group in runs.GroupBy(run => (run.Orientation, FixedCoordinate(run)))
                         .OrderBy(group => group.Key.Orientation)
                         .ThenBy(group => group.Key.Item2))
            {
                var laneIds = group.Select(run => assignments.First(assignment => assignment.RunId == run.RunId).LaneId)
                    .Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal).ToArray();
                if (laneIds.Length == 0) continue;
                var spacing = group.Select(run => lanes.First(lane => lane.RunIds.Contains(run.RunId, StringComparer.Ordinal)).ParallelLaneSpacing)
                    .DefaultIfEmpty(configuration.ParallelLaneSpacing).Max();
                var row = group.Key.Orientation == ArchitectureV7RunOrientation.Horizontal ? group.Key.Item2 : -1;
                var column = group.Key.Orientation == ArchitectureV7RunOrientation.Vertical ? group.Key.Item2 : -1;
                var offsets = Enumerable.Range(0, laneIds.Length).Select(index => (double)index * spacing).ToArray();
                AddDemand(demands, row, column,
                    group.Key.Orientation == ArchitectureV7RunOrientation.Horizontal ? offsets[0] : 0,
                    group.Key.Orientation == ArchitectureV7RunOrientation.Horizontal ? offsets[offsets.Length - 1] : null,
                    group.Key.Orientation == ArchitectureV7RunOrientation.Vertical ? offsets[0] : 0,
                    group.Key.Orientation == ArchitectureV7RunOrientation.Vertical ? offsets[offsets.Length - 1] : null,
                    configuration.ResourceClearance, laneIds, "lane-envelope;lane-count=" + laneIds.Length);
            }
        }

        foreach (var handoff in handoffs)
        {
            if (handoff.LogicalCell is not { } cell) continue;
            AddDemand(demands, cell.Row, cell.Column,
                handoff.HandoffOrientation == ArchitectureV7RunOrientation.Horizontal ? handoff.RelativePhysicalOffset : null,
                handoff.HandoffOrientation == ArchitectureV7RunOrientation.Horizontal ? handoff.RelativePhysicalOffset : null,
                handoff.HandoffOrientation == ArchitectureV7RunOrientation.Vertical ? handoff.RelativePhysicalOffset : null,
                handoff.HandoffOrientation == ArchitectureV7RunOrientation.Vertical ? handoff.RelativePhysicalOffset : null,
                handoff.RequiredClearance, new[] { handoff.ResourceId }, "endpoint-handoff;cell-centre-relative-offset");
        }
        foreach (var bend in bends)
        {
            var position = bend.EffectiveRelativePosition;
            AddDemand(demands, bend.Cell.Row, bend.Cell.Column,
                position.YOffset, position.YOffset, position.XOffset, position.XOffset,
                bend.RequiredClearance, new[] { bend.BendId }, "bend-envelope;cell-centre-relative-offset");
        }

        return demands.OrderBy(item => item.Key.Row).ThenBy(item => item.Key.Column)
            .Select(item => item.Value.Freeze(item.Key.Row, item.Key.Column)).ToArray();

        static void AddDemand(Dictionary<(int Row, int Column), MutableTrackDemand> demands, int row, int column,
            double? rowMinimumOffset, double? rowMaximumOffset, double? columnMinimumOffset, double? columnMaximumOffset,
            double clearance, IEnumerable<string> resourceIds, string provenance)
        {
            var key = (row, column);
            if (!demands.TryGetValue(key, out var demand)) demands[key] = demand = new();
            if (rowMinimumOffset.HasValue && rowMaximumOffset.HasValue)
            {
                demand.RowMinimumOffset = Math.Min(demand.RowMinimumOffset, rowMinimumOffset.Value);
                demand.RowMaximumOffset = Math.Max(demand.RowMaximumOffset, rowMaximumOffset.Value);
                demand.RowClearance = Math.Max(demand.RowClearance, clearance);
            }
            if (columnMinimumOffset.HasValue && columnMaximumOffset.HasValue)
            {
                demand.ColumnMinimumOffset = Math.Min(demand.ColumnMinimumOffset, columnMinimumOffset.Value);
                demand.ColumnMaximumOffset = Math.Max(demand.ColumnMaximumOffset, columnMaximumOffset.Value);
                demand.ColumnClearance = Math.Max(demand.ColumnClearance, clearance);
            }
            demand.ResourceIds.UnionWith(resourceIds);
            demand.Provenance.Add(provenance);
        }

        static int FixedCoordinate(ArchitectureV7StraightRun run) => run.Orientation == ArchitectureV7RunOrientation.Horizontal
            ? run.Cells[0].Row : run.Cells[0].Column;
    }

    private sealed class MutableTrackDemand
    {
        public double RowMinimumOffset { get; set; } = double.PositiveInfinity;
        public double RowMaximumOffset { get; set; } = double.NegativeInfinity;
        public double RowClearance { get; set; }
        public double ColumnMinimumOffset { get; set; } = double.PositiveInfinity;
        public double ColumnMaximumOffset { get; set; } = double.NegativeInfinity;
        public double ColumnClearance { get; set; }
        public HashSet<string> ResourceIds { get; } = new(StringComparer.Ordinal);
        public HashSet<string> Provenance { get; } = new(StringComparer.Ordinal);

        public ArchitectureV7PhysicalTrackDemand Freeze(int row, int column)
        {
            var rowExtent = double.IsPositiveInfinity(RowMinimumOffset) ? 0 : RowMaximumOffset - RowMinimumOffset + 2 * RowClearance;
            var columnExtent = double.IsPositiveInfinity(ColumnMinimumOffset) ? 0 : ColumnMaximumOffset - ColumnMinimumOffset + 2 * ColumnClearance;
            return new(row, column, rowExtent, columnExtent,
                ResourceIds.OrderBy(id => id, StringComparer.Ordinal).ToArray(), string.Join(";", Provenance.OrderBy(value => value, StringComparer.Ordinal)),
                double.IsPositiveInfinity(RowMinimumOffset) ? 0 : RowMinimumOffset,
                double.IsNegativeInfinity(RowMaximumOffset) ? 0 : RowMaximumOffset,
                double.IsPositiveInfinity(ColumnMinimumOffset) ? 0 : ColumnMinimumOffset,
                double.IsNegativeInfinity(ColumnMaximumOffset) ? 0 : ColumnMaximumOffset);
        }
    }

    private static string Fingerprint(IEnumerable<string> values)
    {
        using var sha = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(string.Join("|", values));
        return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", string.Empty);
    }

    private static string GeometryKey(ArchitectureV7RouteCell cell, string classification, string horizontalLaneId,
        string verticalLaneId, ArchitectureV7PhysicalRelativePosition position, double clearance) =>
        cell.Row + ":" + cell.Column + ":" + classification + ":" + horizontalLaneId + ":" + verticalLaneId + ":" +
        position.XOffset.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + ":" +
        position.YOffset.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + ":" +
        clearance.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
}
