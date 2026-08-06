using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using StandardIo.ArchitectureDiagram.Core.Models.ArchitectureV6;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.ArchitectureV6;

internal sealed class ArchitectureV6LogicalPlacementBuilder
{
    private const int MinimumFootprintSpan = 3;
    private const int LogicalGap = 1;
    private readonly ArchitecturePlanningRequest request;
    private readonly ArchitectureProjectionResult projection;
    private readonly IReadOnlyDictionary<string, int> requiredSpans;
    private readonly Dictionary<string, PlannedPhysicalNode> nodes;
    private readonly Dictionary<string, int> order;
    private readonly Dictionary<string, string?> ownerByNode = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<string>> childrenByNode = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<string>> semanticParentsByNode = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<string>> semanticChildrenByNode = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> treeRootByNode = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> branchOrderByRoot = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> depthByNode = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> rowRoleByNode = new(StringComparer.Ordinal);
    private readonly Dictionary<string, GridSlot> slotByNode = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SubtreeHorizontalProfile> profileByNode = new(StringComparer.Ordinal);
    private readonly Dictionary<string, LogicalProjectGrid> gridsByProject = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> spanByNode = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> spanReasonByNode = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> rowRankByRole = new(StringComparer.Ordinal);
    private readonly List<ArchitecturePlanningDiagnostic> diagnostics = new();
    private long profileComputationMilliseconds;
    private long profileCompositionMilliseconds;
    private long columnMaterializationMilliseconds;
    private long reservationConstructionMilliseconds;
    private int profileCacheHits;
    private int profileCacheMisses;
    private long intervalCompatibilityChecks;

    public ArchitectureV6LogicalPlacementBuilder(ArchitecturePlanningRequest request, ArchitectureProjectionResult projection,
        IReadOnlyDictionary<string, int>? requiredSpans = null)
    {
        this.request = request ?? throw new ArgumentNullException(nameof(request));
        this.projection = projection ?? throw new ArgumentNullException(nameof(projection));
        this.requiredSpans = requiredSpans ?? new Dictionary<string, int>(StringComparer.Ordinal);
        nodes = projection.PhysicalNodes.ToDictionary(node => node.PhysicalNodeId, StringComparer.Ordinal);
        order = projection.PhysicalNodes.Select((node, index) => (node.PhysicalNodeId, index))
            .ToDictionary(item => item.PhysicalNodeId, item => item.index, StringComparer.Ordinal);
    }

    public LogicalPlacementResult Build()
    {
        BuildSemanticRelationships();
        SelectPositionalOwners();
        BuildPositionalChildren();
        BuildTreeRoots();
        CalculateDepths();
        CalculateRows();
        CalculateSpans();

        var projectPlacements = new Dictionary<string, List<PlannedNodePlacement>>(StringComparer.Ordinal);
        foreach (var projectId in ProjectOrder())
        {
            var projectNodes = nodes.Values.Where(node => ProjectOf(node) == projectId).OrderBy(node => order[node.PhysicalNodeId]).ToArray();
            var roots = projectNodes.Where(node => ownerByNode[node.PhysicalNodeId] is null).ToArray();
            var logicalGrid = new LogicalProjectGrid(new PlanningGridId($"project:{projectId}"), rowRankByRole);
            gridsByProject[projectId] = logicalGrid;
            var occupied = new List<ProfileInterval>();
            var mainRoots = roots.Where(node => !node.IsStandalone).ToArray();
            var rootsPerBand = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(mainRoots.Length)));
            var bandCursors = new Dictionary<int, int>();
            foreach (var pair in mainRoots.Select((root, index) => (root, index)))
            {
                var profile = BuildProfile(pair.root.PhysicalNodeId);
                var band = pair.index / rootsPerBand;
                var shift = bandCursors.TryGetValue(band, out var cursor) ? cursor : 0;
                shift = FindCompatibleShift(profile.Intervals, occupied, shift);
                MaterializeProfile(logicalGrid, profile, shift, occupied);
                var profileWidth = profile.Intervals.Max(interval => interval.End + 1);
                bandCursors[band] = shift + profileWidth + LogicalGap;
            }

            PlaceStandaloneProfiles(logicalGrid, projectNodes.Where(node => node.IsStandalone).ToArray(), occupied);
            logicalGrid.EnsureColumns(occupied.Count == 0 ? 0 : occupied.Max(interval => interval.End + 1));
            var placements = projectNodes.Select(BuildPlacement).OrderBy(item => order[item.PhysicalNodeId]).ToArray();
            projectPlacements[projectId] = placements.ToList();
        }

        var reservationTimer = Stopwatch.StartNew();
        var reservations = BuildReservations();
        reservationTimer.Stop();
        reservationConstructionMilliseconds += reservationTimer.ElapsedMilliseconds;
        var projectGrids = BuildProjectGrids(projectPlacements, reservations);
        var diagramGrid = BuildDiagramGrid(projectGrids);
        ValidatePlacement(projectPlacements.Values.SelectMany(items => items).ToArray(), reservations, projectGrids);
        var nodeMetadata = BuildNodeMetadata(reservations);
        var linkMetadata = BuildLinkMetadata();
        return new LogicalPlacementResult(projectGrids, diagramGrid, nodeMetadata, linkMetadata, reservations, diagnostics,
            projectPlacements.Values.SelectMany(items => items).ToArray(),
            new PlacementPerformance(profileComputationMilliseconds, profileCompositionMilliseconds,
                columnMaterializationMilliseconds, reservationConstructionMilliseconds, profileCacheHits,
                profileCacheMisses, intervalCompatibilityChecks));
    }

    private void ValidatePlacement(
        IReadOnlyList<PlannedNodePlacement> placements,
        IReadOnlyList<SubtreeReservation> reservations,
        IReadOnlyList<ProjectRoutingGrid> projectGrids)
    {
        if (placements.Count != nodes.Count)
            diagnostics.Add(new ArchitecturePlanningDiagnostic("LogicalPlacementNodeAccounting", "Each projected physical node must have exactly one logical placement.", PlanningDiagnosticSubject.PhysicalNode, null));

        var anchorOwners = placements.GroupBy(item => item.AnchorCellId).Where(group => group.Count() > 1).ToArray();
        foreach (var group in anchorOwners)
            diagnostics.Add(new ArchitecturePlanningDiagnostic("LogicalPlacementAnchorOverlap", $"Logical anchor cell is shared by {group.Count()} physical nodes.", PlanningDiagnosticSubject.PhysicalNode, string.Join(",", group.Select(item => item.PhysicalNodeId))));

        var footprintOwners = placements.SelectMany(item => item.Footprint.Select(cell => (cell, item.PhysicalNodeId)))
            .GroupBy(item => item.cell).Where(group => group.Select(item => item.PhysicalNodeId).Distinct(StringComparer.Ordinal).Count() > 1).ToArray();
        foreach (var group in footprintOwners)
            diagnostics.Add(new ArchitecturePlanningDiagnostic("LogicalPlacementFootprintOverlap", "Physical node footprints overlap in the logical grid.", PlanningDiagnosticSubject.PhysicalNode, group.Key.ToString()));

        var placedIds = new HashSet<string>(placements.Select(item => item.PhysicalNodeId), StringComparer.Ordinal);
        foreach (var link in projection.PhysicalLinks)
            if (!placedIds.Contains(link.SourcePhysicalNodeId) || !placedIds.Contains(link.DestinationPhysicalNodeId))
                diagnostics.Add(new ArchitecturePlanningDiagnostic("LogicalPlacementLinkEndpointMissing", "Every projected physical link endpoint must have a logical placement.", PlanningDiagnosticSubject.PhysicalLink, link.PhysicalLinkId));

        foreach (var baselineGroup in rowRoleByNode.Where(item => IsBaseline(item.Key)).GroupBy(item => treeRootByNode[item.Key]))
            if (baselineGroup.Select(item => item.Value).Distinct(StringComparer.Ordinal).Count() > 1)
                diagnostics.Add(new ArchitecturePlanningDiagnostic("LogicalPlacementBaselineMisalignment", "Baseline nodes within one ownership branch must share one logical row.", PlanningDiagnosticSubject.PhysicalNode, baselineGroup.Key));

        var reservationByOwner = reservations.ToDictionary(item => item.PositionalOwnerId, StringComparer.Ordinal);
        foreach (var node in nodes.Values)
        {
            var owner = ownerByNode[node.PhysicalNodeId];
            if (owner is null || !reservationByOwner.TryGetValue(owner, out var ownerReservation)) continue;
            if (slotByNode[node.PhysicalNodeId].Columns.Any(column => !ownerReservation.Cells.Any(cell => cell.ColumnId.Equals(column))))
                diagnostics.Add(new ArchitecturePlanningDiagnostic("LogicalPlacementChildOutsideOwnerEnvelope", "A positional child lies outside its owner subtree envelope.", PlanningDiagnosticSubject.PhysicalNode, node.PhysicalNodeId));
        }

        foreach (var parent in childrenByNode.Keys)
        {
            foreach (var siblingRow in childrenByNode[parent]
                .Where(slotByNode.ContainsKey)
                .GroupBy(child => slotByNode[child].RowId))
            {
                var siblings = siblingRow.OrderBy(child => gridsByProject[ProjectOf(nodes[child])].ColumnOrder(slotByNode[child].Columns[0])).ToArray();
                for (var index = 1; index < siblings.Length; index++)
                {
                    var grid = gridsByProject[ProjectOf(nodes[siblings[index]])];
                    var previous = slotByNode[siblings[index - 1]].Columns.Last();
                    var current = slotByNode[siblings[index]].Columns.First();
                    var gap = grid.ColumnOrder(current) - grid.ColumnOrder(previous) - 1;
                    if (gap > LogicalGap)
                        diagnostics.Add(new ArchitecturePlanningDiagnostic("LogicalPlacementSiblingGap", "A sibling group contains an unexplained gap.", PlanningDiagnosticSubject.PhysicalNode, parent));
                }
            }
        }

        foreach (var node in nodes.Values.Where(item => item.IsExternal))
        {
            var owner = ownerByNode[node.PhysicalNodeId];
            if (owner is null) continue;
            if (CompareRows(slotByNode[node.PhysicalNodeId].RowId, slotByNode[owner].RowId) <= 0 ||
                !slotByNode[node.PhysicalNodeId].AnchorColumn.Equals(slotByNode[owner].AnchorColumn))
                diagnostics.Add(new ArchitecturePlanningDiagnostic("LogicalPlacementExternalNotLocal", "An external node with one positional owner must be directly below and centred on that owner.", PlanningDiagnosticSubject.PhysicalNode, node.PhysicalNodeId));
        }

        if (reservations.Count != nodes.Count)
            diagnostics.Add(new ArchitecturePlanningDiagnostic("LogicalPlacementReservationAccounting", "Each projected physical node must have one subtree reservation.", PlanningDiagnosticSubject.Grid, null));

        foreach (var grid in projectGrids)
            foreach (var cell in grid.Grid.Cells.Values)
                if (cell.Occupancy == CellOccupancy.NodeAnchor && string.IsNullOrWhiteSpace(cell.Id.ColumnId.Value))
                    diagnostics.Add(new ArchitecturePlanningDiagnostic("LogicalPlacementInvalidAnchor", "A node anchor must identify a logical column.", PlanningDiagnosticSubject.Cell, cell.Id.ToString()));
    }

    private void BuildSemanticRelationships()
    {
        foreach (var node in nodes.Values)
        {
            semanticParentsByNode[node.PhysicalNodeId] = new List<string>();
            semanticChildrenByNode[node.PhysicalNodeId] = new List<string>();
            childrenByNode[node.PhysicalNodeId] = new List<string>();
        }

        foreach (var link in projection.PhysicalLinks.OrderBy(item => order[item.SourcePhysicalNodeId]).ThenBy(item => order[item.DestinationPhysicalNodeId]))
        {
            semanticParentsByNode[link.DestinationPhysicalNodeId].Add(link.SourcePhysicalNodeId);
            semanticChildrenByNode[link.SourcePhysicalNodeId].Add(link.DestinationPhysicalNodeId);
        }
    }

    private void SelectPositionalOwners()
    {
        foreach (var node in nodes.Values.OrderBy(item => order[item.PhysicalNodeId]))
        {
            var explicitOwner = node.PositionalOwnerId;
            if (explicitOwner is not null && nodes.ContainsKey(explicitOwner))
            {
                ownerByNode[node.PhysicalNodeId] = explicitOwner;
                continue;
            }

            ownerByNode[node.PhysicalNodeId] = semanticParentsByNode[node.PhysicalNodeId]
                .Where(parent => ProjectOf(nodes[parent]) == ProjectOf(node))
                .Where(parent => order[parent] < order[node.PhysicalNodeId])
                .OrderBy(parent => order[parent])
                .ThenBy(parent => parent, StringComparer.Ordinal)
                .FirstOrDefault();
        }
    }

    private void BuildPositionalChildren()
    {
        foreach (var item in ownerByNode)
            if (item.Value is not null && childrenByNode.ContainsKey(item.Value)) childrenByNode[item.Value].Add(item.Key);
        foreach (var children in childrenByNode.Values)
            children.Sort((left, right) => order[left] != order[right]
                ? order[left].CompareTo(order[right])
                : string.CompareOrdinal(left, right));
    }

    private void BuildTreeRoots()
    {
        treeRootByNode.Clear();
        foreach (var node in nodes.Values.OrderBy(item => order[item.PhysicalNodeId]))
        {
            var current = node.PhysicalNodeId;
            var visited = new HashSet<string>(StringComparer.Ordinal);
            while (ownerByNode.TryGetValue(current, out var owner) && owner is not null && visited.Add(current))
                current = owner;
            treeRootByNode[node.PhysicalNodeId] = current;
        }

        branchOrderByRoot.Clear();
        var next = 0;
        foreach (var projectId in ProjectOrder())
            foreach (var root in nodes.Values
                .Where(node => ProjectOf(node) == projectId && treeRootByNode[node.PhysicalNodeId] == node.PhysicalNodeId)
                .OrderBy(node => order[node.PhysicalNodeId]))
                branchOrderByRoot[root.PhysicalNodeId] = next++;
    }

    private void CalculateDepths()
    {
        foreach (var node in nodes.Values) depthByNode[node.PhysicalNodeId] = 0;
        var visiting = new HashSet<string>(StringComparer.Ordinal);
        void Visit(string id, int depth)
        {
            if (visiting.Contains(id)) return;
            depthByNode[id] = Math.Max(depthByNode[id], depth);
            visiting.Add(id);
            foreach (var child in semanticChildrenByNode[id].OrderBy(child => order[child])) Visit(child, depth + 1);
            visiting.Remove(id);
        }

        foreach (var root in nodes.Values.Where(node => semanticParentsByNode[node.PhysicalNodeId].Count == 0).OrderBy(node => order[node.PhysicalNodeId]))
            Visit(root.PhysicalNodeId, 0);
        foreach (var node in nodes.Values.OrderBy(node => order[node.PhysicalNodeId]))
            Visit(node.PhysicalNodeId, depthByNode[node.PhysicalNodeId]);
    }

    private void CalculateRows()
    {
        var baselinePattern = ToRegex(request.NodePlacement.BaselinePattern);
        var baseline = nodes.Values.Where(node => baselinePattern.IsMatch(node.SemanticName) || baselinePattern.IsMatch(node.SemanticNodeId)).ToArray();
        foreach (var node in nodes.Values)
        {
            var branch = treeRootByNode[node.PhysicalNodeId];
            rowRoleByNode[node.PhysicalNodeId] = baseline.Contains(node)
                ? "baseline"
                : $"depth:{depthByNode[node.PhysicalNodeId]}";
        }

        foreach (var node in nodes.Values.OrderBy(node => order[node.PhysicalNodeId]))
        {
            var owner = ownerByNode[node.PhysicalNodeId];
            if (owner is not null && !node.IsExternal && !IsBaselineNode(node) && rowRoleByNode[node.PhysicalNodeId] == rowRoleByNode[owner])
                rowRoleByNode[node.PhysicalNodeId] = $"depth:{depthByNode[owner] + 1}";
        }

        rowRankByRole.Clear();
        foreach (var role in rowRoleByNode
            .OrderBy(item => item.Value == "baseline" ? -1 : depthByNode[item.Key])
            .ThenBy(item => item.Value, StringComparer.Ordinal)
            .ThenBy(item => order[item.Key])
            .Select(item => item.Value)
            .Distinct(StringComparer.Ordinal))
            rowRankByRole[role] = rowRankByRole.Count;
    }

    private void CalculateSpans()
    {
        foreach (var node in nodes.Values)
        {
            var degree = projection.PhysicalLinks.Count(link => link.SourcePhysicalNodeId == node.PhysicalNodeId || link.DestinationPhysicalNodeId == node.PhysicalNodeId);
            var requested = Math.Max(MinimumFootprintSpan, ((node.SemanticName ?? node.SemanticNodeId).Length + 19) / 20 * 2 + 1);
            if (degree > 4) requested = Math.Max(requested, 5);
            if (degree > 8) requested = Math.Max(requested, 7);
            if (requiredSpans.TryGetValue(node.PhysicalNodeId, out var required)) requested = Math.Max(requested, required);
            spanByNode[node.PhysicalNodeId] = requested % 2 == 0 ? requested + 1 : requested;
            spanReasonByNode[node.PhysicalNodeId] = degree == 0 ? "minimum" : $"label-and-degree:{degree}";
        }
    }

    private SubtreeHorizontalProfile BuildProfile(string id)
    {
        if (profileByNode.TryGetValue(id, out var cached))
        {
            profileCacheHits++;
            return cached;
        }
        profileCacheMisses++;
        var profileTimer = Stopwatch.StartNew();

        var intervals = new List<ProfileInterval>();
        foreach (var child in childrenByNode[id].Where(child => !nodes[child].IsStandalone))
        {
            var childProfile = BuildProfile(child);
            var compositionTimer = Stopwatch.StartNew();
            var shift = FindCompatibleShift(childProfile.Intervals, intervals);
            intervals.AddRange(childProfile.Intervals.Select(interval => interval with
            {
                Start = interval.Start + shift,
                End = interval.End + shift
            }));
            compositionTimer.Stop();
            profileCompositionMilliseconds += compositionTimer.ElapsedMilliseconds;
        }

        var childStart = intervals.Count == 0 ? 0 : intervals.Min(interval => interval.Start);
        var childEnd = intervals.Count == 0 ? spanByNode[id] - 1 : intervals.Max(interval => interval.End);
        var parentStart = intervals.Count == 0
            ? 0
            : childStart + Math.Max(0, ((childEnd - childStart + 1) - spanByNode[id]) / 2);
        var parent = new ProfileInterval(rowRoleByNode[id], parentStart, parentStart + spanByNode[id] - 1,
            id, "node", spanByNode[id]);
        while (intervals.Any(interval => interval.RowRole == parent.RowRole &&
            Intersects(interval.Start, interval.End, parent.Start, parent.End)))
        {
            parent = parent with { Start = parent.Start + parent.Width + LogicalGap,
                End = parent.End + parent.Width + LogicalGap };
        }
        intervals.Add(parent);

        var minimum = intervals.Min(interval => interval.Start);
        if (minimum != 0)
            intervals = intervals.Select(interval => interval with
            {
                Start = interval.Start - minimum,
                End = interval.End - minimum
            }).ToList();

        var profile = new SubtreeHorizontalProfile($"subtree:{id}", id, intervals);
        profileByNode[id] = profile;
        profileTimer.Stop();
        profileComputationMilliseconds += profileTimer.ElapsedMilliseconds;
        return profile;
    }

    private void MaterializeProfile(LogicalProjectGrid grid, SubtreeHorizontalProfile profile, int shift,
        ICollection<ProfileInterval> occupied)
    {
        var materializationTimer = Stopwatch.StartNew();
        foreach (var interval in profile.Intervals)
        {
            var shiftedStart = interval.Start + shift;
            var row = grid.EnsureRow(interval.RowRole);
            grid.ReserveNodeFootprintAt(row, shiftedStart, interval.Width, spanReasonByNode[interval.OwnerId]);
            slotByNode[interval.OwnerId] = new GridSlot(row,
                grid.Columns.Skip(shiftedStart).Take(interval.Width).ToArray());
            occupied.Add(interval with { Start = shiftedStart, End = interval.End + shift });
        }
        materializationTimer.Stop();
        columnMaterializationMilliseconds += materializationTimer.ElapsedMilliseconds;
    }

    private void PlaceStandaloneProfiles(LogicalProjectGrid grid, IReadOnlyList<PlannedPhysicalNode> standalone,
        ICollection<ProfileInterval> occupied)
    {
        if (standalone.Count == 0) return;
        var columnsPerRow = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(standalone.Count)));
        var rowCursors = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var index = 0; index < standalone.Count; index++)
        {
            var rowRole = $"standalone:{index / columnsPerRow}";
            var start = rowCursors.TryGetValue(rowRole, out var cursor) ? cursor : 0;
            var interval = new ProfileInterval(rowRole, start, start + spanByNode[standalone[index].PhysicalNodeId] - 1,
                standalone[index].PhysicalNodeId, "standalone", spanByNode[standalone[index].PhysicalNodeId]);
            rowCursors[rowRole] = interval.End + LogicalGap;
            var profile = new SubtreeHorizontalProfile($"standalone:{standalone[index].PhysicalNodeId}",
                standalone[index].PhysicalNodeId, new[] { interval });
            var shift = FindCompatibleShift(profile.Intervals, occupied);
            MaterializeProfile(grid, profile, shift, occupied);
        }
    }

    private int FindCompatibleShift(IReadOnlyList<ProfileInterval> candidate,
        IEnumerable<ProfileInterval> occupied, int minimumShift = 0)
    {
        var shift = minimumShift;
        while (true)
        {
            var conflict = occupied.FirstOrDefault(existing => candidate.Any(item =>
                item.RowRole == existing.RowRole &&
                CountsAsConflict(item, existing, shift)));
            if (conflict is null) return shift;
            var conflicting = candidate.First(item => item.RowRole == conflict.RowRole &&
                CountsAsConflict(item, conflict, shift));
            shift = conflict.End + LogicalGap + 1 - conflicting.Start;
        }
    }

    private bool CountsAsConflict(ProfileInterval candidate, ProfileInterval existing, int shift)
    {
        intervalCompatibilityChecks++;
        return Intersects(candidate.Start + shift, candidate.End + shift, existing.Start, existing.End, LogicalGap);
    }

    private static bool Intersects(int leftStart, int leftEnd, int rightStart, int rightEnd, int gap = 0) =>
        leftStart <= rightEnd + gap && rightStart <= leftEnd + gap;

    private PlannedNodePlacement BuildPlacement(PlannedPhysicalNode node)
    {
        var slot = slotByNode[node.PhysicalNodeId];
        var gridId = gridsByProject[ProjectOf(node)].Id;
        var footprint = slot.Columns
            .Select(column => new PlanningGridCellId(gridId, slot.RowId, column))
            .ToArray();
        return new PlannedNodePlacement(node.PhysicalNodeId, gridId, footprint[slot.Columns.Count / 2],
            slot.Columns.Count, 1, footprint, slot.AnchorColumn, spanReasonByNode[node.PhysicalNodeId]);
    }

    private IReadOnlyList<SubtreeReservation> BuildReservations()
    {
        var result = new List<SubtreeReservation>();
        foreach (var node in nodes.Values.OrderBy(item => order[item.PhysicalNodeId]))
        {
            var members = SubtreeMembers(node.PhysicalNodeId).Where(slotByNode.ContainsKey).ToArray();
            if (members.Length == 0) continue;
            var grid = gridsByProject[ProjectOf(node)];
            var rowIntervals = members.GroupBy(id => slotByNode[id].RowId)
                .Select(group => new SubtreeReservationRowInterval(group.Key,
                    group.SelectMany(id => slotByNode[id].Columns).Distinct().OrderBy(grid.ColumnOrder).ToArray()))
                .OrderBy(interval => grid.RowOrder(interval.RowId))
                .ToArray();
            var cells = rowIntervals.SelectMany(interval => interval.Columns
                .Select(column => new PlanningGridCellId(grid.Id, interval.RowId, column))).ToArray();
            var owner = ownerByNode[node.PhysicalNodeId];
            result.Add(new SubtreeReservation($"subtree:{node.PhysicalNodeId}", node.PhysicalNodeId, grid.Id, cells,
                owner is null ? null : $"subtree:{owner}", rowIntervals));
        }
        return result;
    }

    private IEnumerable<string> SubtreeMembers(string id)
    {
        yield return id;
        foreach (var child in childrenByNode[id])
            foreach (var member in SubtreeMembers(child)) yield return member;
    }

    private IReadOnlyList<ProjectRoutingGrid> BuildProjectGrids(
        IReadOnlyDictionary<string, List<PlannedNodePlacement>> placementsByProject,
        IReadOnlyList<SubtreeReservation> reservations)
    {
        return ProjectOrder().Select(projectId =>
        {
            var gridId = new PlanningGridId($"project:{projectId}");
            var placements = placementsByProject[projectId];
            var projectNodes = nodes.Values.Where(node => ProjectOf(node) == projectId).ToArray();
            var cells = new Dictionary<PlanningGridCellId, PlanningGridCell>();
            foreach (var reservation in reservations.Where(item => item.GridId.Equals(gridId)))
                foreach (var cell in reservation.Cells)
                    cells[cell] = Cell(cell, cells.TryGetValue(cell, out var existing) ? existing : null, reservation.SubtreeId, null);
            foreach (var placement in placements)
                foreach (var cell in placement.Footprint)
                {
                    var isAnchor = cell.Equals(placement.AnchorCellId);
                    cells[cell] = Cell(cell, cells.TryGetValue(cell, out var existing) ? existing : null,
                        null, isAnchor ? null : placement.PhysicalNodeId, isAnchor ? CellOccupancy.NodeAnchor : CellOccupancy.Empty);
                }
            var logicalGrid = gridsByProject[projectId];
            var placementRows = logicalGrid.Rows.ToArray();
            var topExterior = new PlanningGridRowId("routing:exterior:top");
            var bottomExterior = new PlanningGridRowId("routing:exterior:bottom");
            var rows = new List<PlanningGridRow>
            {
                new(topExterior, 0, 1, 1, 1, 0, 0, PlanningGridTrackRole.InterLayerRouting,
                    "placement", "permanent top exterior routing capacity", projectId)
            };
            rows.AddRange(placementRows.Select((id, index) => new PlanningGridRow(id, index * 2 + 1, 1, 1, 1, index * 2 + 1, index * 2 + 1,
                id.Value.StartsWith("standalone:", StringComparison.Ordinal) ? PlanningGridTrackRole.StandaloneRegion :
                id.Value.Contains(":baseline", StringComparison.Ordinal) ? PlanningGridTrackRole.BaselineNode : PlanningGridTrackRole.NodeBearing,
                "placement", "logical node placement", projectId)));
            for (var index = 0; index < placementRows.Length - 1; index++)
            {
                var id = new PlanningGridRowId($"routing:inter-layer:{index}");
                rows.Add(new PlanningGridRow(id, index * 2 + 2, 1, 1, 1, index * 2 + 2, index * 2 + 2,
                    PlanningGridTrackRole.InterLayerRouting, "placement", "shared inter-layer routing", projectId));
            }
            rows.Add(new PlanningGridRow(bottomExterior, placementRows.Length * 2, 1, 1, 1, placementRows.Length * 2, placementRows.Length * 2,
                PlanningGridTrackRole.InterLayerRouting, "placement", "permanent bottom exterior routing capacity", projectId));
            var columns = logicalGrid.Columns.Select((id, index) => new PlanningGridColumn(id, index, 1, 1, 1, index, index,
                logicalGrid.NodeFootprintColumns.Contains(id) ? PlanningGridTrackRole.NodeFootprint : PlanningGridTrackRole.SubtreeSiblingGap,
                "placement", logicalGrid.NodeFootprintColumns.Contains(id) ? "node footprint" : "sibling gap", projectId)).ToList();
            var placementByNode = placements.ToDictionary(item => item.PhysicalNodeId, StringComparer.Ordinal);
            var projectLinks = projection.PhysicalLinks.Where(link => link.SourceProjectId == projectId || link.DestinationProjectId == projectId).ToArray();
            var hasProjectTransition = projectLinks.Any(link => link.SourceProjectId != link.DestinationProjectId);
            foreach (var destination in projectLinks.Where(link => link.DestinationProjectId == projectId)
                .Select(link => link.DestinationPhysicalNodeId).Distinct(StringComparer.Ordinal).OrderBy(id => order[id]))
                AddRegionColumn(columns, PlanningGridTrackRole.DestinationApproach, $"region:destination-approach:{destination}", destination, projectId);
            foreach (var source in projectLinks.Where(link => link.SourceProjectId == projectId && link.DestinationProjectId == projectId)
                .Where(link => placementByNode.TryGetValue(link.SourcePhysicalNodeId, out var sourcePlacement) &&
                    placementByNode.TryGetValue(link.DestinationPhysicalNodeId, out var destinationPlacement) &&
                    logicalGrid.RowOrder(destinationPlacement.AnchorCellId.RowId) <= logicalGrid.RowOrder(sourcePlacement.AnchorCellId.RowId))
                .Select(link => link.SourcePhysicalNodeId).Distinct(StringComparer.Ordinal).OrderBy(id => order[id]))
                AddRegionColumn(columns, PlanningGridTrackRole.OwnershipLocalReturn, $"region:ownership-local-return:{source}", source, projectId);
            AddRegionColumn(columns, PlanningGridTrackRole.ProjectBoundaryTransition, "region:project-boundary-transition", projectId, projectId, hasProjectTransition);
            var structuralCells = rows.SelectMany(row => columns.Select(column =>
            {
                var id = new PlanningGridCellId(gridId, row.Id, column.Id);
                return new KeyValuePair<PlanningGridCellId, PlanningGridCell>(id,
                    cells.TryGetValue(id, out var existing) ? existing : Cell(id, null, null, null));
            })).ToDictionary(item => item.Key, item => item.Value);
            var grid = new PlanningGrid(gridId, rows, columns, structuralCells, new GridTransform(gridId, new RelativePoint(0, 0)));
            var owned = projectNodes.Select(node => node.PhysicalNodeId).ToArray();
            return new ProjectRoutingGrid(projectId, grid, reservations.Where(item => item.GridId.Equals(gridId)).ToArray(), null,
                owned, projectNodes.Where(node => node.IsExternal).Select(node => node.PhysicalNodeId).ToArray(), "project");
        }).ToArray();
    }

    private static void AddRegionColumn(List<PlanningGridColumn> columns, PlanningGridTrackRole role, string id, string ownerId, string projectId, bool required = true)
    {
        if (!required || columns.Any(column => column.Id.Value == id)) return;
        var index = columns.Count;
        var columnId = new PlanningGridColumnId(id);
        columns.Add(new PlanningGridColumn(columnId, index, 1, 1, 1, index, index, role,
            "placement", "ownership-scoped structural routing region", ownerId));
    }

    private static PlanningGridCell Cell(PlanningGridCellId id, PlanningGridCell? existing, string? reservationId,
        string? footprintOwnerId, CellOccupancy occupancy = CellOccupancy.Empty) =>
        new(id, CellCapability.RoutingAllowed | CellCapability.NodeAllowed, occupancy,
            (existing?.ReservationIds ?? Array.Empty<string>()).Concat(reservationId is null ? Array.Empty<string>() : new[] { reservationId }).Distinct(StringComparer.Ordinal).ToArray(),
            footprintOwnerId ?? existing?.FootprintOwnerId);

    private IReadOnlyList<PhysicalNodePlacementMetadata> BuildNodeMetadata(IReadOnlyList<SubtreeReservation> reservations)
    {
        var roots = nodes.Values.Where(node => ownerByNode[node.PhysicalNodeId] is null).ToArray();
        var rootByNode = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var root in roots) foreach (var member in SubtreeMembers(root.PhysicalNodeId)) rootByNode[member] = root.PhysicalNodeId;
        return nodes.Values.OrderBy(node => order[node.PhysicalNodeId]).Select(node =>
        {
            var owner = ownerByNode[node.PhysicalNodeId];
            var semanticParents = semanticParentsByNode[node.PhysicalNodeId].Distinct(StringComparer.Ordinal).ToArray();
            var semanticChildren = semanticChildrenByNode[node.PhysicalNodeId].Distinct(StringComparer.Ordinal).ToArray();
            var subtree = reservations.First(item => item.PositionalOwnerId == node.PhysicalNodeId);
            var role = node.IsExternal ? "External" : ResolveRole(node.SemanticName);
            var baselineRows = rowRoleByNode.Where(item => IsBaseline(item.Key)).Select(item => item.Value).Distinct().ToArray();
            var baseline = baselineRows.Length > 0 && baselineRows.Length == 1 && rowRoleByNode[node.PhysicalNodeId] == baselineRows[0];
            return new PhysicalNodePlacementMetadata(node.PhysicalNodeId, node.SemanticNodeId, owner,
                childrenByNode[node.PhysicalNodeId].ToArray(), semanticParents, semanticChildren, subtree.SubtreeId,
                reservations.Where(item => item.SubtreeId == subtree.SubtreeId || item.AncestorReservationId == subtree.SubtreeId).Select(item => item.SubtreeId).ToArray(),
                node.ProjectId, depthByNode[node.PhysicalNodeId], baseline, node.IsExternal, node.IsStandalone,
                spanReasonByNode[node.PhysicalNodeId], depthByNode[node.PhysicalNodeId], role, 0, ProjectOf(node), owner is null ? $"root:{ProjectOf(node)}" : $"owner:{owner}",
                "sibling", "logical-row", RowOrder(node.PhysicalNodeId), gridsByProject[ProjectOf(node)].ColumnOrder(slotByNode[node.PhysicalNodeId].AnchorColumn),
                rootByNode.TryGetValue(node.PhysicalNodeId, out var rootId) ? rootId : node.PhysicalNodeId, owner, depthByNode[node.PhysicalNodeId] - (owner is null ? 0 : depthByNode[owner]),
                0, owner is null ? order[node.PhysicalNodeId] : childrenByNode[owner].IndexOf(node.PhysicalNodeId), 0, subtree.SubtreeId);
        }).ToArray();
    }

    private bool IsBaseline(string id) => IsBaselineNode(nodes[id]);
    private bool IsBaselineNode(PlannedPhysicalNode node) => ToRegex(request.NodePlacement.BaselinePattern).IsMatch(node.SemanticName) ||
        ToRegex(request.NodePlacement.BaselinePattern).IsMatch(node.SemanticNodeId);

    private IReadOnlyList<PlannedPhysicalLinkMetadata> BuildLinkMetadata() => projection.PhysicalLinks.Select(link =>
    {
        var sourceNode = nodes[link.SourcePhysicalNodeId];
        var targetNode = nodes[link.DestinationPhysicalNodeId];
        var vertical = RowOrder(link.DestinationPhysicalNodeId).CompareTo(RowOrder(link.SourcePhysicalNodeId));
        var horizontal = gridsByProject[ProjectOf(sourceNode)].ColumnOrder(slotByNode[link.DestinationPhysicalNodeId].AnchorColumn)
            .CompareTo(gridsByProject[ProjectOf(sourceNode)].ColumnOrder(slotByNode[link.SourcePhysicalNodeId].AnchorColumn));
        return new PlannedPhysicalLinkMetadata(link.PhysicalLinkId, link.SemanticLinkId, link.SourcePhysicalNodeId, link.DestinationPhysicalNodeId,
            link.SourceProjectId, link.DestinationProjectId, depthByNode[link.SourcePhysicalNodeId], depthByNode[link.DestinationPhysicalNodeId],
            link.SourceProjectId != link.DestinationProjectId ? "CrossProject" : vertical > 0 ? "Downward" : vertical < 0 ? "Upward" : horizontal == 0 ? "SameColumn" : horizontal < 0 ? "Left" : "Right",
            link.SourceProjectId != link.DestinationProjectId, targetNode.IsExternal);
    }).ToArray();

    private string ResolveRole(string name) => (request.NodePlacement.RoleRules ?? Array.Empty<ArchitectureV6RoleRule>())
        .OrderBy(rule => rule.Order).FirstOrDefault(rule => ToRegex(rule.Pattern).IsMatch(name))?.Name ?? "Unmatched";

    private string ProjectOf(PlannedPhysicalNode node) => node.ProjectId ?? "external";
    private IEnumerable<string> ProjectOrder() => (request.SelectedScope.SelectedProjectIds ?? Array.Empty<string>())
        .Concat(nodes.Values.Select(ProjectOf)).Distinct(StringComparer.Ordinal);
    private int CompareRows(PlanningGridRowId left, PlanningGridRowId right)
    {
        var leftOrder = gridsByProject.Values.Select(grid => grid.RowOrder(left)).FirstOrDefault(value => value >= 0);
        var rightOrder = gridsByProject.Values.Select(grid => grid.RowOrder(right)).FirstOrDefault(value => value >= 0);
        return leftOrder.CompareTo(rightOrder);
    }

    private int RowOrder(string physicalNodeId) => gridsByProject[ProjectOf(nodes[physicalNodeId])].RowOrder(slotByNode[physicalNodeId].RowId);
    private static Regex ToRegex(string? value)
    {
        var pattern = string.IsNullOrWhiteSpace(value) ? ".*" : value!;
        if (pattern.IndexOf(".*", StringComparison.Ordinal) >= 0 || pattern.IndexOf("$", StringComparison.Ordinal) >= 0 || pattern.IndexOf("(", StringComparison.Ordinal) >= 0)
            return new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return new Regex("^" + Regex.Escape(pattern).Replace("\\*", ".*") + "$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private sealed record GridSlot(PlanningGridRowId RowId, IReadOnlyList<PlanningGridColumnId> Columns)
    {
        public PlanningGridColumnId AnchorColumn => Columns[Columns.Count / 2];
    }

    private DiagramRoutingGrid BuildDiagramGrid(IReadOnlyList<ProjectRoutingGrid> projects)
    {
        var gridId = new PlanningGridId("diagram");
        var rowId = new PlanningGridRowId("diagram:projects");
        var columns = projects.Select((project, index) => new PlanningGridColumn(new PlanningGridColumnId($"diagram:project:{index}"), index, 1, 1, 1, index, index,
            PlanningGridTrackRole.DiagramProjectPlacement, "placement", project.ProjectId, project.ProjectId)).ToArray();
        var row = new PlanningGridRow(rowId, 0, 1, 1, 1, 0, 0, PlanningGridTrackRole.DiagramProjectPlacement, "placement", "project placement", "diagram");
        var cells = columns.ToDictionary(column => new PlanningGridCellId(gridId, rowId, column.Id), column =>
            new PlanningGridCell(new PlanningGridCellId(gridId, rowId, column.Id), CellCapability.RoutingAllowed | CellCapability.ProjectBoundary,
                CellOccupancy.ProjectFootprint, Array.Empty<string>(), null));
        return new DiagramRoutingGrid(new PlanningGrid(gridId, new[] { row }, columns, cells, new GridTransform(gridId, new RelativePoint(0, 0))),
            Array.Empty<RelativeRectangle>(), Array.Empty<GridTransition>());
    }

    private sealed class LogicalProjectGrid
    {
        private readonly IReadOnlyDictionary<string, int> rowRankByRole;
        private readonly List<PlanningGridColumnId> columns = new();
        private readonly List<PlanningGridRowId> rows = new();
        private readonly HashSet<PlanningGridCellId> occupiedNodeCells = new();
        private readonly HashSet<PlanningGridColumnId> occupiedNodeColumns = new();
        public IReadOnlyCollection<PlanningGridColumnId> NodeFootprintColumns => occupiedNodeColumns;

        public LogicalProjectGrid(PlanningGridId id, IReadOnlyDictionary<string, int> rowRankByRole)
        {
            Id = id;
            this.rowRankByRole = rowRankByRole;
        }
        public PlanningGridId Id { get; }
        public IReadOnlyList<PlanningGridColumnId> Columns => columns;
        public IReadOnlyList<PlanningGridRowId> Rows => rows;

        public void EnsureColumns(int count)
        {
            if (count <= columns.Count) return;
            var existingCount = columns.Count;
            for (var index = existingCount; index < count; index++)
                columns.Add(new PlanningGridColumnId($"column:{index}"));
        }

        public void ReserveNodeFootprintAt(PlanningGridRowId row, int start, int span, string reason)
        {
            if (start < 0 || span <= 0 || span % 2 == 0) throw new ArgumentException("Node footprint spans must be positive and odd.", nameof(span));
            EnsureColumns(start + span);
            var cells = columns.Skip(start).Take(span).Select(column => new PlanningGridCellId(Id, row, column)).ToArray();
            if (cells.Any(occupiedNodeCells.Contains))
                throw new InvalidOperationException($"A logical node footprint conflicts with an existing node footprint in grid {Id}, row {row}, span {span}.");
            occupiedNodeCells.UnionWith(cells);
            foreach (var cell in cells) occupiedNodeColumns.Add(cell.ColumnId);
        }

        public PlanningGridRowId EnsureRow(string role)
        {
            var row = new PlanningGridRowId($"row:{role}");
            if (rows.Contains(row)) return row;
            var rank = rowRankByRole.TryGetValue(role, out var knownRank) ? knownRank : int.MaxValue;
            var insertAt = rows.Count;
            for (var index = 0; index < rows.Count; index++)
            {
                var existingRole = rows[index].Value.StartsWith("row:", StringComparison.Ordinal)
                    ? rows[index].Value.Substring("row:".Length)
                    : string.Empty;
                var existingRank = rowRankByRole.TryGetValue(existingRole, out var value) ? value : int.MaxValue;
                if (existingRank > rank)
                {
                    insertAt = index;
                    break;
                }
            }
            rows.Insert(insertAt, row);
            return row;
        }

        public int ColumnOrder(PlanningGridColumnId id) => columns.IndexOf(id);
        public int RowOrder(PlanningGridRowId id) => rows.IndexOf(id);
    }

    private sealed record ProfileInterval(string RowRole, int Start, int End, string OwnerId, string Role, int Width);

    private sealed record SubtreeHorizontalProfile(string SubtreeId, string RootNodeId,
        IReadOnlyList<ProfileInterval> Intervals);
}

internal sealed record LogicalPlacementResult(
    IReadOnlyList<ProjectRoutingGrid> ProjectGrids,
    DiagramRoutingGrid DiagramGrid,
    IReadOnlyList<PhysicalNodePlacementMetadata> NodeMetadata,
    IReadOnlyList<PlannedPhysicalLinkMetadata> LinkMetadata,
    IReadOnlyList<SubtreeReservation> SubtreeReservations,
    IReadOnlyList<ArchitecturePlanningDiagnostic> Diagnostics,
    IReadOnlyList<PlannedNodePlacement> NodePlacements,
    PlacementPerformance Performance);

internal sealed record PlacementPerformance(
    long ProfileComputationMilliseconds,
    long ProfileCompositionMilliseconds,
    long ColumnMaterializationMilliseconds,
    long ReservationConstructionMilliseconds,
    int ProfileCacheHits,
    int ProfileCacheMisses,
    long IntervalCompatibilityChecks);
