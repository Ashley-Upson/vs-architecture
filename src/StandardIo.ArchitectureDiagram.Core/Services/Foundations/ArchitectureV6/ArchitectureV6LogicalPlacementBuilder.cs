using System;
using System.Collections.Generic;
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
    private readonly Dictionary<string, PlannedPhysicalNode> nodes;
    private readonly Dictionary<string, int> order;
    private readonly Dictionary<string, string?> ownerByNode = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<string>> childrenByNode = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<string>> semanticParentsByNode = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<string>> semanticChildrenByNode = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> depthByNode = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> rowByNode = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Position> positionByNode = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> spanByNode = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> spanReasonByNode = new(StringComparer.Ordinal);
    private readonly List<ArchitecturePlanningDiagnostic> diagnostics = new();

    public ArchitectureV6LogicalPlacementBuilder(ArchitecturePlanningRequest request, ArchitectureProjectionResult projection)
    {
        this.request = request ?? throw new ArgumentNullException(nameof(request));
        this.projection = projection ?? throw new ArgumentNullException(nameof(projection));
        nodes = projection.PhysicalNodes.ToDictionary(node => node.PhysicalNodeId, StringComparer.Ordinal);
        order = projection.PhysicalNodes.Select((node, index) => (node.PhysicalNodeId, index))
            .ToDictionary(item => item.PhysicalNodeId, item => item.index, StringComparer.Ordinal);
    }

    public LogicalPlacementResult Build()
    {
        BuildSemanticRelationships();
        SelectPositionalOwners();
        BuildPositionalChildren();
        CalculateDepths();
        CalculateRows();
        CalculateSpans();

        var projectPlacements = new Dictionary<string, List<PlannedNodePlacement>>(StringComparer.Ordinal);
        foreach (var projectId in ProjectOrder())
        {
            var projectNodes = nodes.Values.Where(node => ProjectOf(node) == projectId).OrderBy(node => order[node.PhysicalNodeId]).ToArray();
            var roots = projectNodes.Where(node => ownerByNode[node.PhysicalNodeId] is null).ToArray();
            var cursor = 0;
            foreach (var root in roots.Where(node => !node.IsStandalone))
            {
                var width = SubtreeWidth(root.PhysicalNodeId);
                PlaceSubtree(root.PhysicalNodeId, cursor);
                cursor += width + LogicalGap;
            }

            PlaceStandaloneRegion(projectNodes.Where(node => node.IsStandalone).ToArray(), cursor);
            var placements = projectNodes.Select(BuildPlacement).OrderBy(item => order[item.PhysicalNodeId]).ToArray();
            projectPlacements[projectId] = placements.ToList();
        }

        var reservations = BuildReservations();
        var projectGrids = BuildProjectGrids(projectPlacements, reservations);
        ValidatePlacement(projectPlacements.Values.SelectMany(items => items).ToArray(), reservations, projectGrids);
        var nodeMetadata = BuildNodeMetadata(reservations);
        var linkMetadata = BuildLinkMetadata();
        return new LogicalPlacementResult(projectGrids, nodeMetadata, linkMetadata, reservations, diagnostics,
            projectPlacements.Values.SelectMany(items => items).ToArray());
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

        var baselineRows = rowByNode.Where(item => IsBaseline(item.Key)).Select(item => item.Value).Distinct().ToArray();
        if (baselineRows.Length > 1)
            diagnostics.Add(new ArchitecturePlanningDiagnostic("LogicalPlacementBaselineMisalignment", "Configured baseline nodes must share one logical row.", PlanningDiagnosticSubject.PhysicalNode, null));

        foreach (var node in nodes.Values.Where(item => item.IsExternal))
        {
            var owner = ownerByNode[node.PhysicalNodeId];
            if (owner is null) continue;
            if (rowByNode[node.PhysicalNodeId] <= rowByNode[owner] || positionByNode[node.PhysicalNodeId].Centre != positionByNode[owner].Centre)
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
        var baselineRow = baseline.Length == 0 ? -1 : baseline.Max(node => depthByNode[node.PhysicalNodeId]);
        foreach (var node in nodes.Values)
            rowByNode[node.PhysicalNodeId] = baseline.Contains(node)
                ? baselineRow
                : depthByNode[node.PhysicalNodeId];

        foreach (var node in nodes.Values.OrderBy(node => order[node.PhysicalNodeId]))
        {
            var owner = ownerByNode[node.PhysicalNodeId];
            if (owner is not null && !node.IsExternal && !IsBaselineNode(node) && rowByNode[node.PhysicalNodeId] <= rowByNode[owner])
                rowByNode[node.PhysicalNodeId] = rowByNode[owner] + 1;
        }
    }

    private void CalculateSpans()
    {
        foreach (var node in nodes.Values)
        {
            var degree = projection.PhysicalLinks.Count(link => link.SourcePhysicalNodeId == node.PhysicalNodeId || link.DestinationPhysicalNodeId == node.PhysicalNodeId);
            var requested = Math.Max(MinimumFootprintSpan, ((node.SemanticName ?? node.SemanticNodeId).Length + 19) / 20 * 2 + 1);
            if (degree > 4) requested = Math.Max(requested, 5);
            if (degree > 8) requested = Math.Max(requested, 7);
            spanByNode[node.PhysicalNodeId] = requested % 2 == 0 ? requested + 1 : requested;
            spanReasonByNode[node.PhysicalNodeId] = degree == 0 ? "minimum" : $"label-and-degree:{degree}";
        }
    }

    private int SubtreeWidth(string id)
    {
        var children = childrenByNode[id].Where(child => !nodes[child].IsStandalone).ToArray();
        if (children.Length == 0) return spanByNode[id];
        return Math.Max(spanByNode[id], children.Sum(SubtreeWidth) + (children.Length - 1) * LogicalGap);
    }

    private void PlaceSubtree(string id, int start)
    {
        if (positionByNode.ContainsKey(id)) return;
        var width = SubtreeWidth(id);
        var children = childrenByNode[id].Where(child => !nodes[child].IsStandalone).ToArray();
        var childWidth = children.Length == 0 ? 0 : children.Sum(SubtreeWidth) + (children.Length - 1) * LogicalGap;
        var centre = start + Math.Max(0, width - spanByNode[id]) / 2 + spanByNode[id] / 2;
        positionByNode[id] = new Position(centre, start, start + width - 1);
        var childStart = start + Math.Max(0, (width - childWidth) / 2);
        foreach (var child in children)
        {
            PlaceSubtree(child, childStart);
            childStart += SubtreeWidth(child) + LogicalGap;
        }
    }

    private void PlaceStandaloneRegion(IReadOnlyList<PlannedPhysicalNode> standalone, int hierarchyEnd)
    {
        if (standalone.Count == 0) return;
        var columns = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(standalone.Count)));
        var start = hierarchyEnd + LogicalGap * 2;
        var standaloneRow = rowByNode.Values.DefaultIfEmpty(0).Max() + LogicalGap;
        for (var index = 0; index < standalone.Count; index++)
        {
            var node = standalone[index];
            var x = start + (index % columns) * (spanByNode[node.PhysicalNodeId] + LogicalGap);
            positionByNode[node.PhysicalNodeId] = new Position(x + spanByNode[node.PhysicalNodeId] / 2, x, x + spanByNode[node.PhysicalNodeId] - 1);
            rowByNode[node.PhysicalNodeId] = standaloneRow + index / columns;
        }
    }

    private PlannedNodePlacement BuildPlacement(PlannedPhysicalNode node)
    {
        var position = positionByNode[node.PhysicalNodeId];
        var gridId = new PlanningGridId($"project:{ProjectOf(node)}");
        var row = rowByNode[node.PhysicalNodeId];
        var footprint = Enumerable.Range(position.Start, spanByNode[node.PhysicalNodeId])
            .Select(column => new PlanningGridCellId(gridId, new PlanningGridRowId($"row:{row}"), new PlanningGridColumnId($"column:{column}")))
            .ToArray();
        return new PlannedNodePlacement(node.PhysicalNodeId, gridId, footprint[spanByNode[node.PhysicalNodeId] / 2],
            spanByNode[node.PhysicalNodeId], 1, footprint,
            new PlanningGridColumnId($"column:{position.Centre}"), spanReasonByNode[node.PhysicalNodeId]);
    }

    private IReadOnlyList<SubtreeReservation> BuildReservations()
    {
        var result = new List<SubtreeReservation>();
        foreach (var node in nodes.Values.OrderBy(item => order[item.PhysicalNodeId]))
        {
            var members = SubtreeMembers(node.PhysicalNodeId).Where(positionByNode.ContainsKey).ToArray();
            if (members.Length == 0) continue;
            var min = members.Min(id => positionByNode[id].Start);
            var max = members.Max(id => positionByNode[id].End);
            var rows = members.Select(id => rowByNode[id]).Distinct().OrderBy(row => row).ToArray();
            var gridId = new PlanningGridId($"project:{ProjectOf(node)}");
            var cells = rows.SelectMany(row => Enumerable.Range(min, max - min + 1)
                .Select(column => new PlanningGridCellId(gridId, new PlanningGridRowId($"row:{row}"), new PlanningGridColumnId($"column:{column}"))))
                .ToArray();
            var owner = ownerByNode[node.PhysicalNodeId];
            result.Add(new SubtreeReservation($"subtree:{node.PhysicalNodeId}", node.PhysicalNodeId, gridId, cells,
                owner is null ? null : $"subtree:{owner}"));
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
            var rows = cells.Keys.Select(cell => cell.RowId).Distinct().OrderBy(id => id.Value, StringComparer.Ordinal)
                .Select((id, index) => new PlanningGridRow(id, index, 1, 1, 1, index, index)).ToArray();
            var columns = cells.Keys.Select(cell => cell.ColumnId).Distinct().OrderBy(id => Parse(id.Value))
                .Select((id, index) => new PlanningGridColumn(id, index, 1, 1, 1, index, index)).ToArray();
            var grid = new PlanningGrid(gridId, rows, columns, cells, new GridTransform(gridId, new RelativePoint(0, 0)));
            var owned = projectNodes.Select(node => node.PhysicalNodeId).ToArray();
            return new ProjectRoutingGrid(projectId, grid, reservations.Where(item => item.GridId.Equals(gridId)).ToArray(), null,
                owned, projectNodes.Where(node => node.IsExternal).Select(node => node.PhysicalNodeId).ToArray(), "project");
        }).ToArray();
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
            var baselineRows = rowByNode.Where(item => IsBaseline(item.Key)).Select(item => item.Value).Distinct().ToArray();
            var baseline = baselineRows.Length > 0 && baselineRows.Length == 1 && rowByNode[node.PhysicalNodeId] == baselineRows[0];
            return new PhysicalNodePlacementMetadata(node.PhysicalNodeId, node.SemanticNodeId, owner,
                childrenByNode[node.PhysicalNodeId].ToArray(), semanticParents, semanticChildren, subtree.SubtreeId,
                reservations.Where(item => item.SubtreeId == subtree.SubtreeId || item.AncestorReservationId == subtree.SubtreeId).Select(item => item.SubtreeId).ToArray(),
                node.ProjectId, depthByNode[node.PhysicalNodeId], baseline, node.IsExternal, node.IsStandalone,
                spanReasonByNode[node.PhysicalNodeId], depthByNode[node.PhysicalNodeId], role, 0, ProjectOf(node), owner is null ? $"root:{ProjectOf(node)}" : $"owner:{owner}",
                "sibling", "logical-row", rowByNode[node.PhysicalNodeId], positionByNode[node.PhysicalNodeId].Centre,
                rootByNode.TryGetValue(node.PhysicalNodeId, out var rootId) ? rootId : node.PhysicalNodeId, owner, depthByNode[node.PhysicalNodeId] - (owner is null ? 0 : depthByNode[owner]),
                0, owner is null ? order[node.PhysicalNodeId] : childrenByNode[owner].IndexOf(node.PhysicalNodeId), 0, subtree.SubtreeId);
        }).ToArray();
    }

    private bool IsBaseline(string id) => IsBaselineNode(nodes[id]);
    private bool IsBaselineNode(PlannedPhysicalNode node) => ToRegex(request.NodePlacement.BaselinePattern).IsMatch(node.SemanticName) ||
        ToRegex(request.NodePlacement.BaselinePattern).IsMatch(node.SemanticNodeId);

    private IReadOnlyList<PlannedPhysicalLinkMetadata> BuildLinkMetadata() => projection.PhysicalLinks.Select(link =>
    {
        var source = positionByNode[link.SourcePhysicalNodeId];
        var target = positionByNode[link.DestinationPhysicalNodeId];
        var sourceNode = nodes[link.SourcePhysicalNodeId];
        var targetNode = nodes[link.DestinationPhysicalNodeId];
        var vertical = rowByNode[link.DestinationPhysicalNodeId].CompareTo(rowByNode[link.SourcePhysicalNodeId]);
        var horizontal = target.Centre.CompareTo(source.Centre);
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
    private static int Parse(string value) => int.TryParse(value.Substring(value.LastIndexOf(':') + 1), out var result) ? result : 0;
    private static Regex ToRegex(string? value)
    {
        var pattern = string.IsNullOrWhiteSpace(value) ? ".*" : value!;
        if (pattern.IndexOf(".*", StringComparison.Ordinal) >= 0 || pattern.IndexOf("$", StringComparison.Ordinal) >= 0 || pattern.IndexOf("(", StringComparison.Ordinal) >= 0)
            return new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return new Regex("^" + Regex.Escape(pattern).Replace("\\*", ".*") + "$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private sealed record Position(int Centre, int Start, int End);
}

internal sealed record LogicalPlacementResult(
    IReadOnlyList<ProjectRoutingGrid> ProjectGrids,
    IReadOnlyList<PhysicalNodePlacementMetadata> NodeMetadata,
    IReadOnlyList<PlannedPhysicalLinkMetadata> LinkMetadata,
    IReadOnlyList<SubtreeReservation> SubtreeReservations,
    IReadOnlyList<ArchitecturePlanningDiagnostic> Diagnostics,
    IReadOnlyList<PlannedNodePlacement> NodePlacements);
