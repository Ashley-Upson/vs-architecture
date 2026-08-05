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
        var projectGrids = BuildProjectGrids(request, projection);
        var diagramGrid = EmptyDiagramGrid();
        var findings = projection.Diagnostics.Concat(new[]
        {
            Deferred("V6PlacementDeferred", PlanningDiagnosticSubject.Grid, "Node placement is deferred until the grid planner is implemented."),
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
            0,
            0,
            0,
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
            projection.CycleSemanticNodeIds.Count);

        return new PlannedArchitectureDiagram(
            request,
            projection.PhysicalNodes,
            projection.PhysicalLinks,
            diagramGrid,
            projectGrids,
            Array.Empty<PlannedNodePlacement>(),
            Array.Empty<PlannedGridRoute>(),
            new GridTrackSizingPlan(Array.Empty<PlanningGridRow>(), Array.Empty<PlanningGridColumn>(), Array.Empty<GridTrackConstraint>(), null),
            new ArchitecturePlanningDiagnostics(findings, metrics),
            projection,
            Array.Empty<PhysicalNodePlacementMetadata>(),
            BuildLinkMetadata(projection),
            Array.Empty<SubtreeReservation>(),
            new ArchitecturePlanningStageStatus(true, false, true, true, true, false, false));
    }

    private static ArchitecturePlanningDiagnostic Deferred(string code, PlanningDiagnosticSubject subject, string message) =>
        new(code, message, subject, null);

    private static IReadOnlyList<PlannedPhysicalLinkMetadata> BuildLinkMetadata(ArchitectureProjectionResult projection) =>
        projection.PhysicalLinks.Select(link => new PlannedPhysicalLinkMetadata(
            link.PhysicalLinkId,
            link.SemanticLinkId,
            link.SourcePhysicalNodeId,
            link.DestinationPhysicalNodeId,
            link.SourceProjectId,
            link.DestinationProjectId,
            0,
            0,
            link.SourceProjectId != link.DestinationProjectId ? "CrossProject" : "Unclassified",
            link.SourceProjectId != link.DestinationProjectId,
            projection.ExternalPhysicalNodeIds.Contains(link.DestinationPhysicalNodeId, StringComparer.Ordinal))).ToArray();

    private static IReadOnlyList<ProjectRoutingGrid> BuildProjectGrids(
        ArchitecturePlanningRequest request,
        ArchitectureProjectionResult projection)
    {
        var selected = request.SelectedScope.SelectedProjectIds ?? Array.Empty<string>();
        var discovered = projection.PhysicalNodes
            .Select(node => node.ProjectId ?? "external")
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var projectIds = selected.Concat(discovered).Distinct(StringComparer.Ordinal).ToArray();
        return projectIds.Select(projectId =>
        {
            var gridId = new PlanningGridId($"project:{projectId}");
            var grid = new PlanningGrid(gridId, Array.Empty<PlanningGridRow>(), Array.Empty<PlanningGridColumn>(),
                new Dictionary<PlanningGridCellId, PlanningGridCell>(), new GridTransform(gridId, new RelativePoint(0, 0)));
            var owned = projection.PhysicalNodes.Where(node => (node.ProjectId ?? "external") == projectId)
                .Select(node => node.PhysicalNodeId).ToArray();
            var external = projection.PhysicalNodes.Where(node => (node.ProjectId ?? "external") == projectId && node.IsExternal)
                .Select(node => node.PhysicalNodeId).ToArray();
            return new ProjectRoutingGrid(projectId, grid, Array.Empty<SubtreeReservation>(), null, owned, external, "project");
        }).ToArray();
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
            var physicalLinks = links.Select((link, index) => new PlannedPhysicalLink(
                $"physical-link:{link.Id}:{index}", link.Id, bySemantic[link.SourceId].PhysicalNodeId,
                bySemantic[link.TargetId].PhysicalNodeId, bySemantic[link.SourceId].ProjectId,
                bySemantic[link.TargetId].ProjectId) { Kind = link.Kind }).ToArray();
            var nodeMap = physicalNodes.ToDictionary(node => node.SemanticNodeId,
                node => (IReadOnlyList<string>)new[] { node.PhysicalNodeId }, StringComparer.Ordinal);
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
