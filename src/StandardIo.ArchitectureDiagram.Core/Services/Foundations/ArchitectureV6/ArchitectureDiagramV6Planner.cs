using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using StandardIo.ArchitectureDiagram.Core.Models.Architectures;
using StandardIo.ArchitectureDiagram.Core.Models.ArchitectureV6;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.ArchitectureV6;

public sealed class ArchitectureDiagramV6Planner : IArchitectureDiagramPlanner
{
    public PlannedArchitectureDiagram Plan(ArchitecturePlanningRequest request)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));

        var projection = new ProjectionBuilder(request).Build();
        var placement = new ArchitectureV6LogicalPlacementBuilder(request, projection).Build();
        var projectGrids = placement.ProjectGrids;
        var diagramGrid = EmptyDiagramGrid();
        var findings = projection.Diagnostics.Concat(new[]
        {
            Deferred("V6RoutePlanningDeferred", PlanningDiagnosticSubject.RouteStep, "Abstract grid-route planning is deferred; no coordinate route is generated."),
            Deferred("V6SizingDeferred", PlanningDiagnosticSubject.TrackConstraint, "Row and column sizing is deferred until route demand exists."),
            Deferred("V6GeometryDeferred", PlanningDiagnosticSubject.Grid, "Relative and absolute geometry compilation is deferred."),
        }).ToArray();

        var metrics = new ArchitecturePlanningMetrics(
            request.SemanticModel.Projects.Sum(project => project.Nodes.Count) + request.SemanticModel.ExternalNodes.Count,
            request.SemanticModel.Links.Count,
            projection.PhysicalNodes.Count,
            projection.PhysicalLinks.Count,
            projectGrids.Count,
            projectGrids.Sum(grid => grid.Grid.Rows.Count),
            projectGrids.Sum(grid => grid.Grid.Columns.Count),
            projectGrids.Sum(grid => grid.Grid.Cells.Count(item => item.Value.Occupancy == CellOccupancy.NodeAnchor)),
            0,
            new Dictionary<string, int>(),
            0,
            0,
            0,
            null,
            findings.Length,
            projection.SemanticNodeToPhysicalNodeIds.ToDictionary(item => item.Key, item => item.Value.Count, StringComparer.Ordinal),
            projection.RootPhysicalNodeIds.Count,
            projection.ExternalPhysicalNodeIds.Count,
            projection.StandalonePhysicalNodeIds.Count,
            projection.CycleSemanticNodeIds.Count,
            LogicalLayerCount: placement.NodeMetadata.Select(item => item.LogicalLayer).Distinct().Count(),
            AnchorCellCount: placement.NodePlacements.Count,
            FootprintCellCount: placement.NodePlacements.Sum(item => item.Footprint.Count),
            SubtreeReservationCount: placement.SubtreeReservations.Count,
            PositionalOwnerCount: placement.NodeMetadata.Count(item => item.PositionalOwnerId is not null));

        return new PlannedArchitectureDiagram(
            request,
            projection.PhysicalNodes,
            projection.PhysicalLinks,
            diagramGrid,
            projectGrids,
            placement.NodePlacements,
            Array.Empty<PlannedGridRoute>(),
            new GridTrackSizingPlan(Array.Empty<PlanningGridRow>(), Array.Empty<PlanningGridColumn>(), Array.Empty<GridTrackConstraint>(), null),
            new ArchitecturePlanningDiagnostics(findings, metrics),
            projection,
            placement.NodeMetadata,
            placement.LinkMetadata,
            placement.SubtreeReservations,
            new ArchitecturePlanningStageStatus(true, true, true, true, true, false, false));
    }

    private static ArchitecturePlanningDiagnostic Deferred(string code, PlanningDiagnosticSubject subject, string message) =>
        new(code, message, subject, null);

    private static Regex ToRegex(string? value)
    {
        var pattern = string.IsNullOrWhiteSpace(value) ? ".*" : value!;
        if (pattern.IndexOf(".*", StringComparison.Ordinal) >= 0 || pattern.IndexOf("$", StringComparison.Ordinal) >= 0 || pattern.IndexOf("(", StringComparison.Ordinal) >= 0)
            return new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return new Regex("^" + Regex.Escape(pattern).Replace("\\*", ".*") + "$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static DiagramRoutingGrid EmptyDiagramGrid()
    {
        var gridId = new PlanningGridId("diagram");
        var grid = new PlanningGrid(gridId, Array.Empty<PlanningGridRow>(), Array.Empty<PlanningGridColumn>(),
            new Dictionary<PlanningGridCellId, PlanningGridCell>(), new GridTransform(gridId, new RelativePoint(0, 0)));
        return new DiagramRoutingGrid(grid, Array.Empty<RelativeRectangle>(), Array.Empty<GridTransition>());
    }

    private sealed class ProjectionBuilder
    {
        private readonly ArchitecturePlanningRequest request;
        private readonly Dictionary<string, SemanticInfo> nodes = new(StringComparer.Ordinal);
        private readonly List<string> order = new();
        private readonly List<ArchitecturePlanningDiagnostic> diagnostics = new();

        public ProjectionBuilder(ArchitecturePlanningRequest request) => this.request = request;

        public ArchitectureProjectionResult Build()
        {
            ReadSemanticNodes();
            var links = request.SemanticModel.Links.Where(link => nodes.ContainsKey(link.SourceId) && nodes.ContainsKey(link.TargetId)).ToArray();
            var parents = links.GroupBy(link => link.TargetId).ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
            var children = links.GroupBy(link => link.SourceId).ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
            var roots = FindRoots(parents);
            var physicalNodes = order.Select(id =>
            {
                var info = nodes[id];
                return new PlannedPhysicalNode($"physical:{id}", id, PhysicalNodeProjectionMode.Canonical, null,
                    info.ProjectId ?? FindExternalOwner(id, links), null, info.IsExternal,
                    !parents.ContainsKey(id) && !children.ContainsKey(id))
                {
                    SemanticName = info.Name,
                    SemanticFullName = info.FullName
                };
            }).ToList();
            var bySemantic = physicalNodes.ToDictionary(node => node.SemanticNodeId, StringComparer.Ordinal);
            var nodeMapBuilder = physicalNodes.ToDictionary(node => node.SemanticNodeId,
                node => new List<string> { node.PhysicalNodeId }, StringComparer.Ordinal);
            var duplicatePatterns = request.NodeProjection.DuplicationExceptionPatterns
                .Where(pattern => !string.IsNullOrWhiteSpace(pattern)).Select(ToRegex).ToArray();
            var duplicateCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            var targetUses = new Dictionary<string, int>(StringComparer.Ordinal);
            var physicalLinksBuilder = new List<PlannedPhysicalLink>();
            foreach (var link in links)
            {
                var source = bySemantic[link.SourceId];
                var target = bySemantic[link.TargetId];
                if (request.NodeProjection.Mode == NodeProjectionMode.DuplicateBranches && targetUses.TryGetValue(link.TargetId, out var useCount) && useCount > 0 &&
                    duplicatePatterns.Any(pattern => pattern.IsMatch(nodes[link.TargetId].Name) || pattern.IsMatch(link.TargetId)))
                {
                    var ordinal = duplicateCounts.TryGetValue(link.TargetId, out var current) ? current + 1 : 1;
                    duplicateCounts[link.TargetId] = ordinal;
                    target = new PlannedPhysicalNode($"physical:{link.TargetId}:duplicate:{ordinal}", link.TargetId,
                        PhysicalNodeProjectionMode.DuplicateBranch, source.PhysicalNodeId,
                        nodes[link.TargetId].ProjectId ?? FindExternalOwner(link.TargetId, links),
                        new DuplicationProvenance(link.TargetId, $"Configured duplicate pattern matched '{nodes[link.TargetId].Name}' for link '{link.Id}', ordinal {ordinal}.", source.PhysicalNodeId),
                        nodes[link.TargetId].IsExternal, false)
                    {
                        SemanticName = nodes[link.TargetId].Name,
                        SemanticFullName = nodes[link.TargetId].FullName
                    };
                    physicalNodes.Add(target);
                    nodeMapBuilder[link.TargetId].Add(target.PhysicalNodeId);
                }
                targetUses[link.TargetId] = targetUses.TryGetValue(link.TargetId, out var count) ? count + 1 : 1;
                physicalLinksBuilder.Add(new PlannedPhysicalLink($"physical-link:{link.Id}:{physicalLinksBuilder.Count}", link.Id,
                    source.PhysicalNodeId, target.PhysicalNodeId, source.ProjectId, target.ProjectId) { Kind = link.Kind });
            }
            var physicalLinks = physicalLinksBuilder.ToArray();
            var nodeMap = nodeMapBuilder.ToDictionary(item => item.Key, item => (IReadOnlyList<string>)item.Value.ToArray(), StringComparer.Ordinal);
            var linkMap = links.GroupBy(link => link.Id).ToDictionary(group => group.Key,
                group => (IReadOnlyList<string>)physicalLinks.Where(item => item.SemanticLinkId == group.Key)
                    .Select(item => item.PhysicalLinkId).ToArray(), StringComparer.Ordinal);
            var unaccountedLinks = request.SemanticModel.Links.Where(link => !nodes.ContainsKey(link.SourceId) || !nodes.ContainsKey(link.TargetId))
                .Select(link => link.Id).ToArray();
            foreach (var id in unaccountedLinks)
                diagnostics.Add(new ArchitecturePlanningDiagnostic("UnaccountedSemanticLink", "A semantic link references a node outside the selected semantic scope.", PlanningDiagnosticSubject.SemanticLink, id));
            var unaccountedNodes = request.SemanticModel.Projects.SelectMany(project => project.Nodes).Select(node => node.Id)
                .Where(id => !nodes.ContainsKey(id)).ToArray();
            return new ArchitectureProjectionResult(
                physicalNodes,
                physicalLinks,
                Array.Empty<PhysicalNodePlacementMetadata>(),
                Array.Empty<PlannedPhysicalLinkMetadata>(),
                nodeMap,
                linkMap,
                roots.Select(id => bySemantic[id].PhysicalNodeId).ToArray(),
                physicalNodes.Where(node => node.IsExternal).Select(node => node.PhysicalNodeId).ToArray(),
                physicalNodes.Where(node => node.IsStandalone).Select(node => node.PhysicalNodeId).ToArray(),
                FindCycles(children, roots),
                unaccountedNodes,
                unaccountedLinks,
                diagnostics);
        }

        private void ReadSemanticNodes()
        {
            var selected = new HashSet<string>(request.SelectedScope.SelectedProjectIds ?? Array.Empty<string>(), StringComparer.Ordinal);
            var selectedNodes = new HashSet<string>(request.SemanticModel.Selection?.SelectedNodeIds ?? Array.Empty<string>(), StringComparer.Ordinal);
            foreach (var project in request.SemanticModel.Projects)
            {
                if (selected.Count > 0 && !selected.Contains(project.Id)) continue;
                foreach (var node in project.Nodes)
                {
                    if (selectedNodes.Count > 0 && !selectedNodes.Contains(node.Id)) continue;
                    nodes[node.Id] = new SemanticInfo(node.Id, node.Name, node.FullName, project.Id, false);
                    order.Add(node.Id);
                }
            }
            foreach (var external in request.SemanticModel.ExternalNodes)
            {
                nodes[external.Id] = new SemanticInfo(external.Id, external.Name, external.FullName, null, true);
                order.Add(external.Id);
            }
        }

        private string? FindExternalOwner(string id, IReadOnlyList<ArchitectureLink> links) => links
            .Where(link => link.TargetId == id && nodes.TryGetValue(link.SourceId, out var source) && source.ProjectId is not null)
            .Select(link => nodes[link.SourceId].ProjectId).FirstOrDefault();

        private string[] FindRoots(IReadOnlyDictionary<string, ArchitectureLink[]> parents) => order
            .Where(id => !parents.ContainsKey(id) || parents[id].Length == 0)
            .Concat(request.SemanticModel.Selection?.Roots.Select(root => root.SemanticNodeId) ?? Array.Empty<string>())
            .Where(nodes.ContainsKey).Distinct(StringComparer.Ordinal).ToArray();

        private IReadOnlyList<string> FindCycles(IReadOnlyDictionary<string, ArchitectureLink[]> children, IReadOnlyList<string> roots)
        {
            var cycles = new HashSet<string>(StringComparer.Ordinal);
            var visiting = new HashSet<string>(StringComparer.Ordinal);
            var visited = new HashSet<string>(StringComparer.Ordinal);
            void Visit(string id)
            {
                if (visiting.Contains(id)) { cycles.Add(id); return; }
                if (!visited.Add(id)) return;
                visiting.Add(id);
                if (children.TryGetValue(id, out var outgoing))
                    foreach (var link in outgoing) Visit(link.TargetId);
                visiting.Remove(id);
            }
            foreach (var root in roots) Visit(root);
            foreach (var id in order) Visit(id);
            return cycles.OrderBy(id => order.IndexOf(id)).ToArray();
        }

        private sealed record SemanticInfo(string Id, string Name, string FullName, string? ProjectId, bool IsExternal);
    }
}
