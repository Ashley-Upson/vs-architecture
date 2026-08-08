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
    private readonly Dictionary<string, int> structuralDepthByNode = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> rowRoleByNode = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> categoryByNode = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> finalVisualLayerByNode = new(StringComparer.Ordinal);
    private readonly Dictionary<string, GridSlot> slotByNode = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SubtreeHorizontalProfile> profileByNode = new(StringComparer.Ordinal);
    private readonly Dictionary<string, LogicalProjectGrid> gridsByProject = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> spanByNode = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> spanReasonByNode = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> rowRankByRole = new(StringComparer.Ordinal);
    private readonly HashSet<string> externalAffinityBlocked = new(StringComparer.Ordinal);
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
        CalculateStructuralDepths();
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
            foreach (var root in mainRoots)
            {
                var profile = BuildProfile(root.PhysicalNodeId);
                var shift = FindCompatibleShift(profile.Intervals, occupied);
                MaterializeProfile(logicalGrid, profile, shift, occupied);
            }
            ResolveRootRowConflicts(logicalGrid, mainRoots);

            PlaceStandaloneProfiles(logicalGrid, projectNodes.Where(node => node.IsStandalone).ToArray(), occupied);
            PlaceExternalProfiles(logicalGrid, projectNodes.Where(node => node.IsExternal).ToArray(), occupied);
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

        foreach (var node in nodes.Values)
        {
            var owner = ownerByNode[node.PhysicalNodeId];
            if (owner is null || !finalVisualLayerByNode.TryGetValue(owner, out var parentLayer)) continue;
            if (parentLayer >= finalVisualLayerByNode[node.PhysicalNodeId])
                diagnostics.Add(new ArchitecturePlanningDiagnostic("LogicalPlacementParentNotAboveChild", "Every positional parent must occupy an earlier final visual layer than its child.", PlanningDiagnosticSubject.PhysicalNode, node.PhysicalNodeId));
        }

        var externalLayers = nodes.Values.Where(node => node.IsExternal).Select(node => finalVisualLayerByNode[node.PhysicalNodeId]).Distinct().ToArray();
        var bottomNonExternalLayer = nodes.Values.Where(node => !node.IsExternal).Select(node => finalVisualLayerByNode[node.PhysicalNodeId]).DefaultIfEmpty(-1).Max();
        if (externalLayers.Length > 0 && (externalLayers.Length != 1 || externalLayers[0] <= bottomNonExternalLayer))
            diagnostics.Add(new ArchitecturePlanningDiagnostic("LogicalPlacementExternalLayer", "External nodes must share one final layer below every non-external node.", PlanningDiagnosticSubject.PhysicalNode, null));

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
                .Where(child => !nodes[child].IsExternal && !nodes[child].IsStandalone)
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
            if (!externalAffinityBlocked.Contains(node.PhysicalNodeId) &&
                (CompareRows(slotByNode[node.PhysicalNodeId].RowId, slotByNode[owner].RowId) <= 0 ||
                 !slotByNode[node.PhysicalNodeId].AnchorColumn.Equals(slotByNode[owner].AnchorColumn)))
                diagnostics.Add(new ArchitecturePlanningDiagnostic("LogicalPlacementExternalNotLocal", "An external node with one positional owner must be directly below and centred on that owner.", PlanningDiagnosticSubject.PhysicalNode, node.PhysicalNodeId));
        }

        ValidateFinalParentGeometry();
        ValidateFinalRoleBands();

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

    private void ValidateFinalParentGeometry()
    {
        foreach (var parent in nodes.Values.OrderBy(node => order[node.PhysicalNodeId]))
        {
            var children = childrenByNode[parent.PhysicalNodeId]
                .Where(id => slotByNode.ContainsKey(id) && !nodes[id].IsExternal && !nodes[id].IsStandalone)
                .ToArray();
            if (children.Length == 0 || !slotByNode.ContainsKey(parent.PhysicalNodeId)) continue;

            var grid = gridsByProject[ProjectOf(parent)];
            var parentSlot = slotByNode[parent.PhysicalNodeId];
            var parentCentre = (grid.ColumnOrder(parentSlot.Columns.First()) + grid.ColumnOrder(parentSlot.Columns.Last())) / 2.0;
            var childSlots = children.Select(id => slotByNode[id]).ToArray();
            var groupStart = childSlots.Min(slot => grid.ColumnOrder(slot.Columns.First()));
            var groupEnd = childSlots.Max(slot => grid.ColumnOrder(slot.Columns.Last()));
            var expectedCentre = (groupStart + groupEnd) / 2.0;
            var error = Math.Abs(parentCentre - expectedCentre);

            if (children.Length == 1 && error > 1)
                diagnostics.Add(new ArchitecturePlanningDiagnostic("LogicalPlacementSingleChildNotCentered",
                    $"A single-child parent is {error:0.##} logical columns away from its child centre.", PlanningDiagnosticSubject.PhysicalNode, parent.PhysicalNodeId));
            else if (children.Length > 1 && error > 1)
                diagnostics.Add(new ArchitecturePlanningDiagnostic("LogicalPlacementParentNotCentered",
                    $"A parent is {error:0.##} logical columns away from its immediate-child group centre.", PlanningDiagnosticSubject.PhysicalNode, parent.PhysicalNodeId));

            var childContours = children.SelectMany(child => SubtreeMembers(child)
                    .Where(id => slotByNode.ContainsKey(id) && !nodes[id].IsExternal && !nodes[id].IsStandalone)
                    .SelectMany(id => slotByNode[id].Columns.Select(column =>
                        (Child: child, Row: slotByNode[id].RowId, Column: grid.ColumnOrder(column)))))
                .GroupBy(item => (item.Child, item.Row))
                .Select(group => (group.Key.Child, group.Key.Row,
                    Start: group.Min(item => item.Column), End: group.Max(item => item.Column)))
                .GroupBy(item => item.Row);
            foreach (var rowContours in childContours)
            {
                var siblings = rowContours.OrderBy(item => item.Start).ToArray();
                for (var index = 1; index < siblings.Length; index++)
                    if (Intersects(siblings[index - 1].Start, siblings[index - 1].End, siblings[index].Start, siblings[index].End))
                        diagnostics.Add(new ArchitecturePlanningDiagnostic("LogicalPlacementSiblingInterleave",
                            "Immediate sibling subtree intervals overlap after final placement.", PlanningDiagnosticSubject.PhysicalNode, parent.PhysicalNodeId));
            }
        }

        var roots = nodes.Values.Where(node => ownerByNode[node.PhysicalNodeId] is null && !node.IsStandalone)
            .GroupBy(ProjectOf, StringComparer.Ordinal);
        foreach (var projectRoots in roots)
        {
            var grid = gridsByProject[projectRoots.Key];
            var rootContours = projectRoots.SelectMany(root => SubtreeMembers(root.PhysicalNodeId)
                    .Where(id => slotByNode.ContainsKey(id) && !nodes[id].IsExternal && !nodes[id].IsStandalone)
                    .SelectMany(id => slotByNode[id].Columns.Select(column =>
                        (Root: root.PhysicalNodeId, Row: slotByNode[id].RowId, Column: grid.ColumnOrder(column)))))
                .GroupBy(item => (item.Root, item.Row))
                .Select(group => (group.Key.Root, group.Key.Row,
                    Start: group.Min(item => item.Column), End: group.Max(item => item.Column)))
                .GroupBy(item => item.Row);
            foreach (var rowContours in rootContours)
            {
                var rootsOnRow = rowContours.OrderBy(item => item.Start).ToArray();
                for (var index = 1; index < rootsOnRow.Length; index++)
                    if (Intersects(rootsOnRow[index - 1].Start, rootsOnRow[index - 1].End, rootsOnRow[index].Start, rootsOnRow[index].End))
                        diagnostics.Add(new ArchitecturePlanningDiagnostic("LogicalPlacementSubtreeInterleave",
                            "Completed root subtree intervals overlap on a logical row after final packing.", PlanningDiagnosticSubject.PhysicalNode,
                            rootsOnRow[index].Root));
            }
        }
    }

    private void ValidateFinalRoleBands()
    {
        var rules = (request.NodePlacement.RoleRules ?? Array.Empty<ArchitectureV6RoleRule>())
            .OrderBy(rule => rule.Order).ThenBy(rule => rule.Name, StringComparer.Ordinal).ToArray();
        foreach (var rule in rules)
        {
            var members = nodes.Values.Where(node => !node.IsExternal && !node.IsStandalone &&
                string.Equals(ResolveRole(node.SemanticName), rule.Name, StringComparison.Ordinal)).ToArray();
            var layers = members.Select(node => finalVisualLayerByNode[node.PhysicalNodeId]).Distinct().ToArray();
            if (layers.Length > 1)
                diagnostics.Add(new ArchitecturePlanningDiagnostic("LogicalPlacementRoleLayerSplit",
                    $"Configured role '{rule.Name}' is split across final layers: {string.Join(",", layers.OrderBy(item => item))}.",
                    PlanningDiagnosticSubject.PhysicalNode, rule.Name));
        }

        var roleLayers = rules.ToDictionary(rule => rule.Name,
            rule => nodes.Values.Where(node => !node.IsExternal && !node.IsStandalone &&
                    string.Equals(ResolveRole(node.SemanticName), rule.Name, StringComparison.Ordinal))
                .Select(node => finalVisualLayerByNode[node.PhysicalNodeId]).DefaultIfEmpty(-1).Min(), StringComparer.Ordinal);
        for (var index = 0; index < rules.Length; index++)
            for (var other = index + 1; other < rules.Length; other++)
                if (roleLayers[rules[index].Name] >= 0 && roleLayers[rules[other].Name] >= 0 &&
                    roleLayers[rules[index].Name] == roleLayers[rules[other].Name])
                    diagnostics.Add(new ArchitecturePlanningDiagnostic("LogicalPlacementRoleLayerMerge",
                        $"Configured roles '{rules[index].Name}' and '{rules[other].Name}' share final layer {roleLayers[rules[index].Name]}.",
                        PlanningDiagnosticSubject.PhysicalNode, rules[other].Name));
                else if (roleLayers[rules[index].Name] >= 0 && roleLayers[rules[other].Name] >= 0 &&
                    roleLayers[rules[index].Name] > roleLayers[rules[other].Name])
                    diagnostics.Add(new ArchitecturePlanningDiagnostic("LogicalPlacementRoleOrderViolation",
                        $"Configured role order places '{rules[index].Name}' before '{rules[other].Name}', but final layers reverse them.",
                        PlanningDiagnosticSubject.PhysicalNode, rules[other].Name));
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

    private void CalculateStructuralDepths()
    {
        structuralDepthByNode.Clear();
        foreach (var node in nodes.Values.OrderBy(node => order[node.PhysicalNodeId]))
        {
            var depth = 0;
            var current = node.PhysicalNodeId;
            var visited = new HashSet<string>(StringComparer.Ordinal);
            while (ownerByNode.TryGetValue(current, out var owner) && owner is not null && visited.Add(current))
            {
                depth++;
                current = owner;
            }
            structuralDepthByNode[node.PhysicalNodeId] = depth;
        }
    }

    private void CalculateRows()
    {
        var baselinePattern = ToRegex(request.NodePlacement.BaselinePattern);
        var orderedRules = (request.NodePlacement.RoleRules ?? Array.Empty<ArchitectureV6RoleRule>())
            .OrderBy(rule => rule.Order).ThenBy(rule => rule.Name, StringComparer.Ordinal).ToArray();
        var roleByNode = nodes.Values.ToDictionary(node => node.PhysicalNodeId, node => ResolveRole(node.SemanticName), StringComparer.Ordinal);
        var standaloneNodes = nodes.Values.Where(node => node.IsStandalone).OrderBy(node => order[node.PhysicalNodeId]).ToArray();
        var standaloneColumnsPerRow = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(standaloneNodes.Length)));
        var standaloneRowByNode = standaloneNodes
            .Select((node, index) => (node.PhysicalNodeId, Row: index / standaloneColumnsPerRow))
            .ToDictionary(item => item.PhysicalNodeId, item => item.Row, StringComparer.Ordinal);
        categoryByNode.Clear();
        var reservedRank = orderedRules.Select((rule, index) => (rule.Name, index))
            .ToDictionary(item => item.Name, item => item.index, StringComparer.Ordinal);
        var reservedCount = orderedRules.Length;
        var externalLayer = Math.Max(1, reservedCount * 100 + nodes.Count + 1);
        var layerByNode = new Dictionary<string, int>(StringComparer.Ordinal);
        var reservedLayers = new HashSet<int>(reservedRank.Values.Select(value => value * 100));

        // Reserved role bands are anchors. Ordinary nodes are then solved from
        // their dependency children, with insertion points chosen between those
        // anchors rather than being assigned a stale global analysis depth.
        foreach (var node in nodes.Values.OrderBy(node => order[node.PhysicalNodeId]))
        {
            var role = roleByNode[node.PhysicalNodeId];
            categoryByNode[node.PhysicalNodeId] = node.IsExternal
                ? "external"
                : node.IsStandalone
                ? $"standalone:{order[node.PhysicalNodeId] / standaloneColumnsPerRow}"
                : reservedRank.ContainsKey(role)
                ? $"role:{role}"
                : baselinePattern.IsMatch(node.SemanticName) || baselinePattern.IsMatch(node.SemanticNodeId)
                ? "baseline"
                : "ordinary";
        }

        foreach (var node in nodes.Values.OrderBy(node => order[node.PhysicalNodeId]))
            layerByNode[node.PhysicalNodeId] = node.IsExternal
                ? externalLayer
                : node.IsStandalone
                    ? externalLayer - standaloneColumnsPerRow - standaloneRowByNode[node.PhysicalNodeId]
                : reservedRank.TryGetValue(roleByNode[node.PhysicalNodeId], out var fixedLayer)
                    ? fixedLayer * 100
                    : 0;

        var solving = new HashSet<string>(StringComparer.Ordinal);
        var solved = new HashSet<string>(StringComparer.Ordinal);
        int SolveOrdinaryLayer(string id)
        {
            if (solved.Contains(id)) return layerByNode[id];
            if (!solving.Add(id))
            {
                diagnostics.Add(new ArchitecturePlanningDiagnostic("LogicalPlacementDependencyCycle",
                    "A dependency cycle prevented bottom-up layer solving from reaching a fixed point.", PlanningDiagnosticSubject.PhysicalNode, id));
                return layerByNode[id];
            }
            if (!nodes[id].IsExternal && !nodes[id].IsStandalone && !reservedRank.ContainsKey(roleByNode[id]))
            {
                var childLayers = semanticChildrenByNode[id].OrderBy(child => order[child], Comparer<int>.Default)
                    .Select(child => SolveOrdinaryLayer(child)).ToArray();
                if (childLayers.Length > 0)
                {
                    var candidate = childLayers.Min() - 1;
                    while (reservedLayers.Contains(candidate)) candidate--;
                    layerByNode[id] = candidate;
                }
            }
            solving.Remove(id);
            solved.Add(id);
            return layerByNode[id];
        }
        foreach (var node in nodes.Values.OrderBy(node => order[node.PhysicalNodeId]))
            SolveOrdinaryLayer(node.PhysicalNodeId);

        // A positional owner can be discovered after a semantic child in a
        // cycle. Relax ordinary layers deterministically until all ordinary
        // parent/child constraints are represented, then report any cycle.
        for (var pass = 0; pass < Math.Max(1, nodes.Count * 2); pass++)
        {
            var changed = false;
            foreach (var parent in nodes.Values.OrderBy(node => order[node.PhysicalNodeId]))
                foreach (var childId in semanticChildrenByNode[parent.PhysicalNodeId].OrderBy(id => order[id]))
                {
                    if (parent.IsStandalone || nodes[childId].IsStandalone) continue;
                    if (!layerByNode.TryGetValue(parent.PhysicalNodeId, out var parentLayer) ||
                        !layerByNode.TryGetValue(childId, out var childLayer) || parentLayer < childLayer) continue;
                    if (reservedRank.ContainsKey(roleByNode[parent.PhysicalNodeId]) && reservedRank.ContainsKey(roleByNode[childId]))
                    {
                        if (string.Equals(roleByNode[parent.PhysicalNodeId], roleByNode[childId], StringComparison.Ordinal))
                        {
                            // A role band is shared by unrelated members, but it
                            // cannot collapse a real parent/child chain onto one
                            // row. Insert a deterministic sublayer for the child
                            // while retaining the semantic role for styling and
                            // ordering.
                            var candidate = parentLayer + 1;
                            while (reservedLayers.Contains(candidate)) candidate++;
                            if (childLayer != candidate)
                            {
                                layerByNode[childId] = candidate;
                                changed = true;
                            }
                            continue;
                        }
                        diagnostics.Add(new ArchitecturePlanningDiagnostic("LogicalPlacementReservedOrderConflict",
                            "Configured reserved role order conflicts with a dependency edge.", PlanningDiagnosticSubject.PhysicalLink,
                            parent.PhysicalNodeId + "->" + childId));
                        diagnostics.Add(new ArchitecturePlanningDiagnostic("LogicalPlacementCategoryOrderCycle",
                            "Configured and hierarchy category constraints contain a cycle.", PlanningDiagnosticSubject.Grid,
                            parent.PhysicalNodeId + "->" + childId));
                        continue;
                    }
                    if (!reservedRank.ContainsKey(roleByNode[childId]))
                    {
                        var candidate = parentLayer + 1;
                        while (reservedLayers.Contains(candidate)) candidate++;
                        layerByNode[childId] = candidate;
                        changed = true;
                    }
                    else if (!reservedRank.ContainsKey(roleByNode[parent.PhysicalNodeId]) &&
                             parentLayer >= childLayer)
                    {
                        var candidate = childLayer - 1;
                        while (reservedLayers.Contains(candidate)) candidate--;
                        layerByNode[parent.PhysicalNodeId] = candidate;
                        changed = true;
                    }
                }
            if (!changed) break;
        }

        var layerValues = layerByNode.Values.Concat(new[] { externalLayer }).Distinct().OrderBy(value => value).ToArray();
        var layerMap = layerValues.Select((value, index) => (value, index))
            .ToDictionary(item => item.value, item => item.index);
        finalVisualLayerByNode.Clear();
        foreach (var node in nodes.Values)
            finalVisualLayerByNode[node.PhysicalNodeId] = layerMap[layerByNode[node.PhysicalNodeId]];

        foreach (var node in nodes.Values)
        {
            var layer = finalVisualLayerByNode[node.PhysicalNodeId];
            var role = roleByNode[node.PhysicalNodeId];
            rowRoleByNode[node.PhysicalNodeId] = node.IsExternal
                ? "external"
                : node.IsStandalone
                ? $"standalone:{standaloneRowByNode[node.PhysicalNodeId]}"
                : reservedRank.ContainsKey(role)
                ? layerByNode[node.PhysicalNodeId] == reservedRank[role] * 100
                    ? $"role:{role}"
                    : $"role:{role}:sublayer:{layerByNode[node.PhysicalNodeId]}"
                : baselinePattern.IsMatch(node.SemanticName) || baselinePattern.IsMatch(node.SemanticNodeId)
                ? "baseline"
                : $"depth:{layer}";
        }

        rowRankByRole.Clear();
        foreach (var role in rowRoleByNode
            .OrderBy(item => finalVisualLayerByNode[item.Key])
            .ThenBy(item => item.Value, StringComparer.Ordinal)
            .ThenBy(item => order[item.Key])
            .Select(item => item.Value)
            .Distinct(StringComparer.Ordinal))
            rowRankByRole[role] = rowRankByRole.Count;
    }

    private static void AddCategoryEdge(Dictionary<string, HashSet<string>> edges, string before, string after)
    {
        if (!string.Equals(before, after, StringComparison.Ordinal) && edges.ContainsKey(before) && edges.ContainsKey(after))
            edges[before].Add(after);
    }

    private IReadOnlyList<string> TopologicallyOrderCategories(IReadOnlyList<string> categories,
        IReadOnlyDictionary<string, HashSet<string>> edges, IReadOnlyDictionary<string, string> nodeCategories)
    {
        var incoming = categories.ToDictionary(category => category, _ => 0, StringComparer.Ordinal);
        foreach (var targets in edges.Values)
            foreach (var target in targets) incoming[target]++;

        int Priority(string category)
        {
            if (category == "external") return int.MaxValue;
            if (category.StartsWith("standalone:", StringComparison.Ordinal)) return int.MaxValue - 1000 + int.Parse(category.Substring("standalone:".Length));
            if (category == "baseline") return 500;
            if (category.StartsWith("role:", StringComparison.Ordinal))
            {
                var role = category.Substring("role:".Length);
                return 100 + ((request.NodePlacement.RoleRules ?? Array.Empty<ArchitectureV6RoleRule>())
                    .OrderBy(rule => rule.Order).ThenBy(rule => rule.Name, StringComparer.Ordinal)
                    .Select((rule, index) => (rule.Name, index)).FirstOrDefault(item => string.Equals(item.Name, role, StringComparison.Ordinal)).index);
            }
            return int.Parse(category.Substring("depth:".Length));
        }

        var ready = new SortedSet<string>(Comparer<string>.Create((left, right) =>
        {
            var compare = Priority(left).CompareTo(Priority(right));
            return compare != 0 ? compare : string.CompareOrdinal(left, right);
        }));
        foreach (var category in categories.Where(category => incoming[category] == 0)) ready.Add(category);
        var result = new List<string>(categories.Count);
        while (ready.Count > 0)
        {
            var category = ready.Min!;
            ready.Remove(category);
            result.Add(category);
            foreach (var target in edges[category].OrderBy(item => item, StringComparer.Ordinal))
                if (--incoming[target] == 0) ready.Add(target);
        }

        if (result.Count != categories.Count)
        {
            var remaining = categories.Where(category => !result.Contains(category, StringComparer.Ordinal)).OrderBy(Priority).ThenBy(category => category, StringComparer.Ordinal).ToArray();
            diagnostics.Add(new ArchitecturePlanningDiagnostic("LogicalPlacementCategoryOrderCycle",
                $"Configured and hierarchy category constraints contain a cycle: {string.Join(" -> ", remaining)}.",
                PlanningDiagnosticSubject.Grid, string.Join(",", remaining)));
            result.AddRange(remaining);
        }
        return result;
    }

    private void CalculateSpans()
    {
        foreach (var node in nodes.Values)
        {
            var outgoing = projection.PhysicalLinks.Count(link => link.SourcePhysicalNodeId == node.PhysicalNodeId);
            var incoming = projection.PhysicalLinks.Count(link => link.DestinationPhysicalNodeId == node.PhysicalNodeId);
            var terminalDemand = Math.Max(outgoing, incoming);
            var label = string.IsNullOrWhiteSpace(node.DisplayLabel) ? node.SemanticName ?? node.SemanticNodeId : node.DisplayLabel;
            var requested = Math.Max(MinimumFootprintSpan, (label.Length + 19) / 20 * 2 + 1);
            var portInset = Math.Max(request.RoutePlanning.MinimumPortSpacing, request.GridSizing.NodeToRouteClearance);
            var terminalWidth = terminalDemand == 0 ? 0 : checked(portInset * 2 + Math.Max(0, terminalDemand - 1) * request.RoutePlanning.MinimumPortSpacing);
            var cellWidth = Math.Max(1, request.GridSizing.CellWidth);
            var terminalSpan = terminalWidth == 0 ? 0 : (int)Math.Ceiling((double)terminalWidth / cellWidth);
            requested = Math.Max(requested, terminalSpan);
            if (requiredSpans.TryGetValue(node.PhysicalNodeId, out var required)) requested = Math.Max(requested, required);
            spanByNode[node.PhysicalNodeId] = requested % 2 == 0 ? requested + 1 : requested;
            spanReasonByNode[node.PhysicalNodeId] = terminalDemand == 0 ? "minimum" : $"label-and-terminal-capacity:out={outgoing};in={incoming}";
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
        var childProfiles = new List<(string ChildId, SubtreeHorizontalProfile Profile, int Shift)>();
        foreach (var child in childrenByNode[id].Where(child => !nodes[child].IsStandalone && !nodes[child].IsExternal))
        {
            var childProfile = BuildProfile(child);
            var compositionTimer = Stopwatch.StartNew();
            var shift = FindCompatibleShift(childProfile.Intervals, intervals);
            intervals.AddRange(childProfile.Intervals.Select(interval => interval with
            {
                Start = interval.Start + shift,
                End = interval.End + shift
            }));
            childProfiles.Add((child, childProfile, shift));
            compositionTimer.Stop();
            profileCompositionMilliseconds += compositionTimer.ElapsedMilliseconds;
        }

        var parentStart = 0;
        if (childProfiles.Count == 1)
        {
            var child = childProfiles[0];
            var childNode = child.Profile.Intervals.Single(interval => interval.OwnerId == child.ChildId);
            var childStart = childNode.Start + child.Shift;
            parentStart = childStart + (childNode.Width - spanByNode[id]) / 2;
        }
        else if (childProfiles.Count > 1)
        {
            // Reserve the complete child envelopes, but centre over the
            // immediate child nodes. Descendants determine the space reserved
            // for each child; they do not redefine the parent's visual
            // relationship to its direct children.
            var childNodes = childProfiles.Select(child =>
            {
                var childNode = child.Profile.Intervals.Single(interval => interval.OwnerId == child.ChildId);
                var start = childNode.Start + child.Shift;
                var end = childNode.End + child.Shift;
                return (Start: start, End: end);
            }).ToArray();
            var childStart = childNodes.Min(interval => interval.Start);
            var childEnd = childNodes.Max(interval => interval.End);
            parentStart = childStart + Math.Max(0, ((childEnd - childStart + 1) - spanByNode[id]) / 2);
        }
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

    private void ResolveRootRowConflicts(LogicalProjectGrid grid, IReadOnlyList<PlannedPhysicalNode> roots)
    {
        for (var pass = 0; pass < roots.Count + 1; pass++)
        {
            var contours = roots.SelectMany(root => SubtreeMembers(root.PhysicalNodeId)
                    .Where(id => slotByNode.ContainsKey(id) && !nodes[id].IsExternal && !nodes[id].IsStandalone)
                    .SelectMany(id => slotByNode[id].Columns.Select(column =>
                        (Root: root.PhysicalNodeId, Row: slotByNode[id].RowId, Column: grid.ColumnOrder(column)))))
                .GroupBy(item => (item.Root, item.Row))
                .Select(group => (group.Key.Root, group.Key.Row,
                    Start: group.Min(item => item.Column), End: group.Max(item => item.Column)))
                .GroupBy(item => item.Row);

            var moved = false;
            foreach (var rowContours in contours)
            {
                var ordered = rowContours.OrderBy(item => item.Start).ToArray();
                for (var index = 1; index < ordered.Length; index++)
                {
                    if (!Intersects(ordered[index - 1].Start, ordered[index - 1].End,
                        ordered[index].Start, ordered[index].End)) continue;

                    var delta = ordered[index - 1].End + LogicalGap + 1 - ordered[index].Start;
                    if (delta <= 0) continue;
                    if (!MoveRootSubtree(grid, ordered[index].Root, delta))
                    {
                        diagnostics.Add(new ArchitecturePlanningDiagnostic("LogicalPlacementSubtreeCollisionUnresolved",
                            "A complete root subtree could not be translated to resolve a same-row collision.",
                            PlanningDiagnosticSubject.PhysicalNode, ordered[index].Root));
                        continue;
                    }
                    moved = true;
                    break;
                }
                if (moved) break;
            }
            if (!moved) return;
        }
    }

    private bool MoveRootSubtree(LogicalProjectGrid grid, string rootId, int delta)
    {
        var members = SubtreeMembers(rootId)
            .Where(id => slotByNode.ContainsKey(id) && !nodes[id].IsExternal && !nodes[id].IsStandalone)
            .ToArray();
        var moves = members.Select(id =>
        {
            var slot = slotByNode[id];
            return (NodeId: id, Row: slot.RowId, Start: grid.ColumnOrder(slot.Columns[0]), Span: slot.Columns.Count);
        }).ToArray();
        var maxAdditionalShift = Math.Max(grid.Columns.Count, moves.Length * 8);
        for (var candidateDelta = delta; candidateDelta <= delta + maxAdditionalShift; candidateDelta++)
        {
            if (!grid.TryMoveNodeFootprints(moves.Select(move => (move.Row, move.Start, move.Span)).ToArray(), candidateDelta))
                continue;

            foreach (var move in moves)
            {
                var columns = grid.Columns.Skip(move.Start + candidateDelta).Take(move.Span).ToArray();
                slotByNode[move.NodeId] = new GridSlot(move.Row, columns);
            }
            return true;
        }

        return false;
    }

    private void PlaceExternalProfiles(LogicalProjectGrid grid, IReadOnlyList<PlannedPhysicalNode> externalNodes,
        ICollection<ProfileInterval> occupied)
    {
        foreach (var external in externalNodes.OrderBy(node => order[node.PhysicalNodeId]))
        {
            var owner = ownerByNode[external.PhysicalNodeId];
            var span = spanByNode[external.PhysicalNodeId];
            var preferredStart = owner is not null && slotByNode.TryGetValue(owner, out var ownerSlot)
                ? grid.ColumnOrder(ownerSlot.AnchorColumn) - span / 2
                : 0;
            var rowRole = rowRoleByNode[external.PhysicalNodeId];
            var start = FindNearestFreeStart(preferredStart, span, rowRole, occupied);
            if (start < 0)
            {
                diagnostics.Add(new ArchitecturePlanningDiagnostic("LogicalPlacementExternalAffinityBlocked",
                    "An external node could not occupy a valid owner-local position on the external layer.",
                    PlanningDiagnosticSubject.PhysicalNode, external.PhysicalNodeId));
                continue;
            }

            if (owner is not null && start != preferredStart)
            {
                externalAffinityBlocked.Add(external.PhysicalNodeId);
                var preferred = new ProfileInterval(rowRole, preferredStart, preferredStart + span - 1, external.PhysicalNodeId, "external", span);
                var blockers = occupied.Where(existing => existing.RowRole == rowRole && CountsAsConflict(preferred, existing, 0))
                    .Select(existing => existing.OwnerId).Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal).ToArray();
                var classification = blockers.Length > 0 ? "genuinely blocked" : "boundary constrained";
                diagnostics.Add(new ArchitecturePlanningDiagnostic("LogicalPlacementExternalAffinityBlocked",
                    $"Preferred owner-centred external position was {classification}; selected nearest free start {start} instead of {preferredStart}; blockers={string.Join(",", blockers)}.",
                    PlanningDiagnosticSubject.PhysicalNode, external.PhysicalNodeId));
            }

            var profile = new SubtreeHorizontalProfile($"subtree:{external.PhysicalNodeId}", external.PhysicalNodeId,
                new[] { new ProfileInterval(rowRole, start, start + span - 1, external.PhysicalNodeId, "external", span) });
            MaterializeProfile(grid, profile, 0, occupied);
        }
    }

    private int FindNearestFreeStart(int preferredStart, int span, string rowRole,
        IEnumerable<ProfileInterval> occupied)
    {
        var occupiedIntervals = occupied.Where(item => item.RowRole == rowRole).ToArray();
        var limit = Math.Max(preferredStart + span,
            occupiedIntervals.Select(item => item.End).DefaultIfEmpty(0).Max() + span + 2);
        for (var distance = 0; distance < limit + span + 2; distance++)
        {
            var candidates = distance == 0 ? new[] { preferredStart } : new[] { preferredStart - distance, preferredStart + distance };
            foreach (var start in candidates.Where(start => start >= 0))
            {
                var candidate = new ProfileInterval(rowRole, start, start + span - 1, "candidate", "external", span);
                if (!occupiedIntervals.Any(existing => CountsAsConflict(candidate, existing, 0)))
                    return start;
            }
        }
        return -1;
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
                    "placement", "project header plus content origin", projectId)
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
            var baseline = IsBaseline(node);
            return new PhysicalNodePlacementMetadata(node.PhysicalNodeId, node.SemanticNodeId, owner,
                childrenByNode[node.PhysicalNodeId].ToArray(), semanticParents, semanticChildren, subtree.SubtreeId,
                reservations.Where(item => item.SubtreeId == subtree.SubtreeId || item.AncestorReservationId == subtree.SubtreeId).Select(item => item.SubtreeId).ToArray(),
                node.ProjectId, depthByNode[node.PhysicalNodeId], baseline, node.IsExternal, node.IsStandalone,
                 spanReasonByNode[node.PhysicalNodeId], depthByNode[node.PhysicalNodeId], role, 0, ProjectOf(node), owner is null ? $"root:{ProjectOf(node)}" : $"owner:{owner}",
                 "sibling", rowRoleByNode[node.PhysicalNodeId], RowOrder(node.PhysicalNodeId), gridsByProject[ProjectOf(node)].ColumnOrder(slotByNode[node.PhysicalNodeId].AnchorColumn),
                 rootByNode.TryGetValue(node.PhysicalNodeId, out var rootId) ? rootId : node.PhysicalNodeId, owner, depthByNode[node.PhysicalNodeId] - (owner is null ? 0 : depthByNode[owner]),
                 0, owner is null ? order[node.PhysicalNodeId] : childrenByNode[owner].IndexOf(node.PhysicalNodeId), 0, subtree.SubtreeId,
                 finalVisualLayerByNode[node.PhysicalNodeId]);
        }).ToArray();
    }

    private bool IsBaseline(string id) => IsBaselineNode(nodes[id]);
    private bool IsBaseline(PlannedPhysicalNode node) => IsBaselineNode(node);
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

    private int ConfiguredRoleOrder(PlannedPhysicalNode node) => (request.NodePlacement.RoleRules ?? Array.Empty<ArchitectureV6RoleRule>())
        .Where(rule => ToRegex(rule.Pattern).IsMatch(node.SemanticName))
        .OrderBy(rule => rule.Order)
        .Select(rule => rule.Order)
        .FirstOrDefault();

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

        public bool TryMoveNodeFootprint(PlanningGridRowId row, int oldStart, int newStart, int span)
        {
            if (newStart < 0 || span <= 0 || span % 2 == 0) return false;
            EnsureColumns(newStart + span);
            var oldCells = columns.Skip(oldStart).Take(span).Select(column => new PlanningGridCellId(Id, row, column)).ToArray();
            var newCells = columns.Skip(newStart).Take(span).Select(column => new PlanningGridCellId(Id, row, column)).ToArray();
            foreach (var cell in oldCells) occupiedNodeCells.Remove(cell);
            if (newCells.Any(occupiedNodeCells.Contains))
            {
                occupiedNodeCells.UnionWith(oldCells);
                return false;
            }
            occupiedNodeCells.UnionWith(newCells);
            foreach (var cell in newCells) occupiedNodeColumns.Add(cell.ColumnId);
            return true;
        }

        public bool TryMoveNodeFootprints(IReadOnlyList<(PlanningGridRowId Row, int Start, int Span)> moves, int delta)
        {
            if (moves.Count == 0 || delta <= 0) return false;
            EnsureColumns(moves.Max(move => move.Start + delta + move.Span));
            var oldCells = moves.SelectMany(move => columns.Skip(move.Start).Take(move.Span)
                    .Select(column => new PlanningGridCellId(Id, move.Row, column)))
                .ToArray();
            foreach (var cell in oldCells) occupiedNodeCells.Remove(cell);

            var newCells = moves.SelectMany(move =>
                    columns.Skip(move.Start + delta).Take(move.Span)
                        .Select(column => new PlanningGridCellId(Id, move.Row, column)))
                .ToArray();
            if (newCells.Length != oldCells.Length || newCells.Distinct().Count() != newCells.Length ||
                newCells.Any(occupiedNodeCells.Contains))
            {
                occupiedNodeCells.UnionWith(oldCells);
                return false;
            }

            occupiedNodeCells.UnionWith(newCells);
            foreach (var cell in newCells) occupiedNodeColumns.Add(cell.ColumnId);
            return true;
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
