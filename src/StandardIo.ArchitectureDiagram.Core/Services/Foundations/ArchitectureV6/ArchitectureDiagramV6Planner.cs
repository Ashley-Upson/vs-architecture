using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using StandardIo.ArchitectureDiagram.Core.Models;
using StandardIo.ArchitectureDiagram.Core.Models.Architectures;
using StandardIo.ArchitectureDiagram.Core.Models.ArchitectureV6;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.ArchitectureV6;

public sealed class ArchitectureDiagramV6Planner : IArchitectureDiagramPlanner
{
    public PlannedArchitectureDiagram Plan(ArchitecturePlanningRequest request)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));

        var stageTimings = new Dictionary<string, long>(StringComparer.Ordinal);
        var stageInvocations = new Dictionary<string, int>(StringComparer.Ordinal);
        void TimeStage(string name, Action action)
        {
            var timer = Stopwatch.StartNew();
            action();
            timer.Stop();
            stageTimings[name] = stageTimings.TryGetValue(name, out var elapsed) ? elapsed + timer.ElapsedMilliseconds : timer.ElapsedMilliseconds;
            stageInvocations[name] = stageInvocations.TryGetValue(name, out var count) ? count + 1 : 1;
        }
        ArchitectureProjectionResult projection = null!;
        TimeStage("projection", () => projection = new ProjectionBuilder(request).Build());
        IReadOnlyList<ArchitectureV6NodeSpanRequirement> preRoutingSpans = Array.Empty<ArchitectureV6NodeSpanRequirement>();
        IReadOnlyList<ArchitectureV6ReservedDepthRequirement> reservedDepthRequirements = Array.Empty<ArchitectureV6ReservedDepthRequirement>();
        ArchitectureV6ReservedDepthTable reservedDepthTable = null!;
        TimeStage("preRoutingSpanSizing", () => preRoutingSpans = new ArchitectureV6PreRoutingSpanSizer(request, projection).Build());
        TimeStage("reservedDepthInspection", () =>
        {
            var planner = new ArchitectureV6ReservedDepthPlanner(request, projection);
            reservedDepthRequirements = planner.Inspect();
            reservedDepthTable = planner.BuildFrozenTable(reservedDepthRequirements);
        });
        var canonicalPlacement = new ArchitectureV6CanonicalPlacementBuilder(request, projection, preRoutingSpans, reservedDepthTable).Build();
        LogicalPlacementResult placement = canonicalPlacement.Placement;
        ArchitectureV6PlacementFreeze placementFreeze = canonicalPlacement.Freeze;
        return BuildPlacementOnlyResult(request, projection, placement, placementFreeze, reservedDepthTable,
            preRoutingSpans, reservedDepthRequirements, stageTimings, stageInvocations);
    }


    private static PlannedArchitectureDiagram BuildPlacementOnlyResult(
        ArchitecturePlanningRequest request,
        ArchitectureProjectionResult projection,
        LogicalPlacementResult placement,
        ArchitectureV6PlacementFreeze placementFreeze,
        ArchitectureV6ReservedDepthTable reservedDepthTable,
        IReadOnlyList<ArchitectureV6NodeSpanRequirement> preRoutingSpans,
        IReadOnlyList<ArchitectureV6ReservedDepthRequirement> reservedDepthRequirements,
        IReadOnlyDictionary<string, long> stageTimings,
        IReadOnlyDictionary<string, int> stageInvocations)
    {
        var deferredStages = new[]
        {
            Deferred("V6StageDeferred.Routing", PlanningDiagnosticSubject.RouteStep,
                "Capability-driven logical routing is deferred until the placement tranche is accepted."),
            Deferred("V6StageDeferred.LanesAndTerminals", PlanningDiagnosticSubject.Lane,
                "Collective lane and terminal allocation is deferred until logical routing is implemented."),
            Deferred("V6StageDeferred.PhysicalSizing", PlanningDiagnosticSubject.Grid,
                "Physical track sizing and geometry compilation are deferred until routing is implemented."),
            Deferred("V6StageDeferred.Validation", PlanningDiagnosticSubject.Grid,
                "Final physical-scene validation is deferred until physical geometry exists."),
            Deferred("V6StageDeferred.Rendering", PlanningDiagnosticSubject.Grid,
                "Architecture rendering is deferred until a completed physical plan exists.")
        };
        var findings = projection.Diagnostics.Concat(placement.Diagnostics).Concat(deferredStages).ToArray();
        var nodeEvidence = placement.NodePlacements.OrderBy(item => item.PhysicalNodeId, StringComparer.Ordinal).Select(item =>
        {
            var metadata = placement.NodeMetadata.Single(value => value.PhysicalNodeId == item.PhysicalNodeId);
            var physical = projection.PhysicalNodes.Single(value => value.PhysicalNodeId == item.PhysicalNodeId);
            return new ArchitectureNodeGridEvidence(item.PhysicalNodeId, physical.SemanticNodeId,
                item.AnchorCellId.RowId.Value, item.AnchorCellId.ColumnId.Value,
                item.Footprint.Select(cell => cell.ColumnId.Value).ToArray(), metadata.PositionalOwnerId,
                metadata.PositionalChildIds, metadata.SubtreeId);
        }).ToArray();
        var metrics = new ArchitecturePlanningMetrics(
            request.SemanticModel.Projects.Sum(project => project.Nodes.Count) + request.SemanticModel.ExternalNodes.Count,
            request.SemanticModel.Links.Count,
            projection.PhysicalNodes.Count,
            projection.PhysicalLinks.Count,
            placement.ProjectGrids.Count,
            placement.ProjectGrids.Sum(grid => grid.Grid.Rows.Count),
            placement.ProjectGrids.Sum(grid => grid.Grid.Columns.Count),
            placement.NodePlacements.Sum(item => item.Footprint.Count),
            0,
            new Dictionary<string, int>(StringComparer.Ordinal),
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
            PositionalOwnerCount: placement.NodeMetadata.Count(item => item.PositionalOwnerId is not null),
            UnplacedNodeCount: projection.PhysicalNodes.Count - placement.NodePlacements.Count,
            OverlappingFootprintCount: placement.Diagnostics.Count(item => item.Code == "LogicalPlacementFootprintOverlap"),
            IncompatibleReservationCount: placement.Diagnostics.Count(item => item.Code == "LogicalPlacementReservationConflict"),
            StructuralRowCountBeforeRouting: placement.ProjectGrids.Sum(grid => grid.Grid.Rows.Count),
            StructuralColumnCountBeforeRouting: placement.ProjectGrids.Sum(grid => grid.Grid.Columns.Count),
            StructuralRowCountAfterRouting: placement.ProjectGrids.Sum(grid => grid.Grid.Rows.Count),
            StructuralColumnCountAfterRouting: placement.ProjectGrids.Sum(grid => grid.Grid.Columns.Count),
            StructuralRowRoleCounts: placement.ProjectGrids.SelectMany(grid => grid.Grid.Rows).GroupBy(row => row.Role.ToString())
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal),
            StructuralColumnRoleCounts: placement.ProjectGrids.SelectMany(grid => grid.Grid.Columns).GroupBy(column => column.Role.ToString())
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal),
            CellsBeforeRouting: placement.ProjectGrids.Sum(grid => grid.Grid.Cells.Count),
            CellsAfterRouting: placement.ProjectGrids.Sum(grid => grid.Grid.Cells.Count),
            StageTimingMilliseconds: stageTimings,
            StageInvocationCounts: stageInvocations,
            NodeGridEvidence: nodeEvidence);
        var emptySizing = new GridTrackSizingPlan(Array.Empty<PlanningGridRow>(), Array.Empty<PlanningGridColumn>(),
            Array.Empty<GridTrackConstraint>(), null);
        return new PlannedArchitectureDiagram(
            request,
            projection.PhysicalNodes,
            projection.PhysicalLinks,
            placement.DiagramGrid,
            placement.ProjectGrids,
            placement.NodePlacements,
            Array.Empty<PlannedGridRoute>(),
            emptySizing,
            new ArchitecturePlanningDiagnostics(findings, metrics),
            projection,
            placement.NodeMetadata,
            placement.LinkMetadata,
            placement.SubtreeReservations,
            new ArchitecturePlanningStageStatus(
                ProjectionCompleted: true,
                LogicalPlacementCompleted: true,
                AbstractRoutingCompleted: false,
                LaneAllocationDeferred: true,
                SizingDeferred: true,
                AbsoluteGeometryDeferred: true,
                SizingCompleted: false,
                AbsoluteGeometryCompleted: false,
                CapacityConstraintsCompleted: false,
                PhysicalSizingDeferred: true),
            reservedDepthTable: reservedDepthTable,
            preRoutingSpanRequirements: preRoutingSpans,
            reservedDepthRequirements: reservedDepthRequirements,
            placementFreeze: placementFreeze);
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

    internal sealed class ProjectionBuilder
    {
        private readonly ArchitecturePlanningRequest request;
        private readonly Dictionary<string, SemanticInfo> nodes = new(StringComparer.Ordinal);
        private readonly List<string> order = new();
        private readonly List<ArchitecturePlanningDiagnostic> diagnostics = new();

        public ProjectionBuilder(ArchitecturePlanningRequest request) => this.request = request;

        public ArchitectureProjectionResult Build()
        {
            ReadSemanticNodes();
            var discoveredOrder = order.Distinct(StringComparer.Ordinal).ToArray();
            var links = request.SemanticModel.Links.Where(link => nodes.ContainsKey(link.SourceId) && nodes.ContainsKey(link.TargetId)).ToArray();
            var parents = links.GroupBy(link => link.TargetId).ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
            var children = links.GroupBy(link => link.SourceId).ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
            var roots = FindRoots(parents);
            var physicalNodes = discoveredOrder.Select(id =>
            {
                var info = nodes[id];
                var semanticPositionalOwnerId = parents.TryGetValue(id, out var incoming)
                    ? incoming.Select(link => link.SourceId)
                        .Where(nodes.ContainsKey)
                        .Where(parentId => Array.IndexOf(discoveredOrder, parentId) < Array.IndexOf(discoveredOrder, id))
                        .OrderBy(parentId => Array.IndexOf(discoveredOrder, parentId))
                        .FirstOrDefault()
                    : null;
                var positionalOwnerId = semanticPositionalOwnerId is null ? null : $"physical:{semanticPositionalOwnerId}";
                return new PlannedPhysicalNode($"physical:{id}", id, PhysicalNodeProjectionMode.Canonical, positionalOwnerId,
                    info.ProjectId ?? FindExternalOwner(id, links), null, info.IsExternal,
                    !parents.ContainsKey(id) && !children.ContainsKey(id))
                {
                    SemanticName = info.Name,
                    SemanticFullName = info.FullName,
                    Interfaces = info.Interfaces,
                    ImplementationCount = info.ImplementationCount,
                    DisplayLabel = ResolveDisplayLabel(info.Name, info.Interfaces, info.ImplementationCount, info.IsExternal),
                    ResolvedRole = info.IsExternal ? "External" : ArchitectureV6RoleResolver.Resolve(info.Name, request.NodePlacement.RoleRules),
                    ResolvedStyle = ResolveNodeStyle(info.Name, info.FullName, info.IsExternal)
                };
            }).ToList();
            var bySemantic = physicalNodes.ToDictionary(node => node.SemanticNodeId, StringComparer.Ordinal);
            var nodeMapBuilder = bySemantic.Values.ToDictionary(node => node.SemanticNodeId,
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
                        SemanticFullName = nodes[link.TargetId].FullName,
                        Interfaces = nodes[link.TargetId].Interfaces,
                        ImplementationCount = nodes[link.TargetId].ImplementationCount,
                        DisplayLabel = ResolveDisplayLabel(nodes[link.TargetId].Name, nodes[link.TargetId].Interfaces,
                            nodes[link.TargetId].ImplementationCount, nodes[link.TargetId].IsExternal),
                        ResolvedRole = nodes[link.TargetId].IsExternal ? "External" : ArchitectureV6RoleResolver.Resolve(nodes[link.TargetId].Name, request.NodePlacement.RoleRules),
                        ResolvedStyle = ResolveNodeStyle(nodes[link.TargetId].Name, nodes[link.TargetId].FullName, nodes[link.TargetId].IsExternal)
                    };
                    physicalNodes.Add(target);
                    nodeMapBuilder[link.TargetId].Add(target.PhysicalNodeId);
                }
                targetUses[link.TargetId] = targetUses.TryGetValue(link.TargetId, out var count) ? count + 1 : 1;
                physicalLinksBuilder.Add(new PlannedPhysicalLink($"physical-link:{link.Id}:{physicalLinksBuilder.Count}", link.Id,
                    source.PhysicalNodeId, target.PhysicalNodeId, source.ProjectId, target.ProjectId)
                {
                    Kind = link.Kind,
                    ResolvedStyle = ResolveConnectorStyle(target.ResolvedStyle),
                    ResolvedStyleSource = "connector-target-background"
                });
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

        private ArchitectureV6StyleRule ResolveNodeStyle(string name, string fullName, bool external)
        {
            var exact = request.StyleOverridesWithValues?.FirstOrDefault(item =>
                string.Equals(item.FullName, fullName, StringComparison.Ordinal));
            if (exact is not null) return exact.Style;
            if (external && request.ExternalDependencyStyle is not null) return request.ExternalDependencyStyle;
            return request.StylePolicies?.FirstOrDefault(rule =>
                GlobMatcher.IsMatch(name, rule.Match) || GlobMatcher.IsMatch(fullName, rule.Match))
                ?? new ArchitectureV6StyleRule("<fallback>", "#dae8fc", "#6c8ebf", "#111111", "rounded", true, null);
        }

        private ArchitectureV6ConnectorStyle? ResolveConnectorStyle(ArchitectureV6StyleRule? destinationStyle)
        {
            if (request.ConnectorStyle is null) return null;
            if (destinationStyle is null || string.IsNullOrWhiteSpace(destinationStyle.FillColor)) return request.ConnectorStyle;
            return request.ConnectorStyle with { StrokeColor = destinationStyle.FillColor };
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
                    if (nodes.ContainsKey(node.Id)) continue;
                    nodes.Add(node.Id, new SemanticInfo(node.Id, node.Name, node.FullName, project.Id, false,
                        node.Interfaces ?? Array.Empty<string>(), node.ImplementationCount));
                    order.Add(node.Id);
                }
            }
            foreach (var external in request.SemanticModel.ExternalNodes)
            {
                if (nodes.ContainsKey(external.Id)) continue;
                nodes.Add(external.Id, new SemanticInfo(external.Id, external.Name, external.FullName, null, true,
                    Array.Empty<string>(), 0));
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

        private static string ResolveDisplayLabel(string name, IReadOnlyList<string> interfaces, int implementationCount, bool external)
        {
            var simpleName = SimpleName(name);
            if (external) return "[External]\n" + simpleName;
            if (implementationCount > 1 && interfaces.Count == 0)
                return $"{simpleName} ({implementationCount} implementations)";
            if (interfaces.Count == 1)
                return simpleName + ":" + SimpleName(interfaces[0]);
            return simpleName;
        }

        private static string SimpleName(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            var lineBreak = value.IndexOf('\n');
            if (lineBreak >= 0) value = value.Substring(0, lineBreak);
            var displaySeparator = value.IndexOf(" : ", StringComparison.Ordinal);
            if (displaySeparator >= 0) value = value.Substring(0, displaySeparator);
            var lastDot = value.LastIndexOf('.');
            return (lastDot >= 0 ? value.Substring(lastDot + 1) : value).Trim();
        }

        private sealed record SemanticInfo(string Id, string Name, string FullName, string? ProjectId, bool IsExternal,
            IReadOnlyList<string> Interfaces, int ImplementationCount);
    }
}
