using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using StandardIo.ArchitectureDiagram.Core.Models;
using StandardIo.ArchitectureDiagram.Core.Models.ArchitectureV6;
using StandardIo.ArchitectureDiagram.Core.Models.Architectures;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.ArchitectureV6;

public sealed class ArchitectureDiagramV6Planner : IArchitectureDiagramPlanner
{
    public PlannedArchitectureDiagram Plan(ArchitecturePlanningRequest request)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));

        var projection = new ProjectionBuilder(request).Build();
        var placement = new LogicalPlacementBuilder(request, projection).Build();
        var geometry = new ArchitectureV6GeometryBuilder(request, projection.PhysicalNodes, placement.NodePlacements, placement.NodeMetadata, placement.SubtreeReservations, placement.ProjectGrids).Build();
        var routes = ArchitectureV6RouteBuilder.Build(request, projection.PhysicalLinks, geometry.Geometry);
        geometry = geometry with { Geometry = geometry.Geometry.WithRoutes(routes) };
        var routeFindings = routes.SelectMany(route => RouteFindings(route)).ToArray();
        var diagnostics = projection.Diagnostics.Concat(placement.Diagnostics).Concat(geometry.Findings).Concat(routeFindings).ToArray();
        var plan = CreatePlan(request, projection, placement, geometry, diagnostics);
        var validation = new ArchitectureDiagramV6Validator().Validate(plan);
        if (validation.IsValid) return plan;
        var validatedDiagnostics = diagnostics.Concat(validation.Findings).ToArray();
        return CreatePlan(request, projection, placement, geometry, validatedDiagnostics);
    }

    private static PlannedArchitectureDiagram CreatePlan(
        ArchitecturePlanningRequest request,
        ArchitectureProjectionResult projection,
        PlacementResult placement,
        ArchitectureV6GeometryBuilder.GeometryBuildResult geometry,
        IReadOnlyList<ArchitecturePlanningDiagnostic> diagnostics)
    {
        var metrics = BuildMetrics(request, projection, placement, geometry, diagnostics);
        var planDiagnostics = new ArchitecturePlanningDiagnostics(diagnostics, metrics);
        var plan = new PlannedArchitectureDiagram(
            request,
            projection.PhysicalNodes,
            projection.PhysicalLinks,
            geometry.DiagramGrid,
            geometry.ProjectGrids,
            placement.NodePlacements,
            geometry.Geometry.Routes.Select(route => new PlannedGridRoute(route.PhysicalLinkId,
                new NodeEndpoint(projection.PhysicalLinks.First(link => link.PhysicalLinkId == route.PhysicalLinkId).SourcePhysicalNodeId, GridSide.Bottom, $"source:{route.PhysicalLinkId}", 0),
                Array.Empty<PlannedGridRouteStep>(), Array.Empty<GridTransition>(),
                new NodeEndpoint(projection.PhysicalLinks.First(link => link.PhysicalLinkId == route.PhysicalLinkId).DestinationPhysicalNodeId, GridSide.Top, $"target:{route.PhysicalLinkId}", 0), route.TopologyFamily,
                projection.PhysicalLinks.First(link => link.PhysicalLinkId == route.PhysicalLinkId).SourceProjectId,
                projection.PhysicalLinks.First(link => link.PhysicalLinkId == route.PhysicalLinkId).DestinationProjectId)).ToArray(),
            geometry.Sizing,
            planDiagnostics,
            projection,
            placement.NodeMetadata,
            placement.LinkMetadata,
            placement.SubtreeReservations,
            new ArchitecturePlanningStageStatus(true, true, false, false, false, true, true))
        {
            Geometry = geometry.Geometry
        };
        return plan;
    }

    private static IEnumerable<ArchitecturePlanningDiagnostic> RouteFindings(PlannedPhysicalRoute route)
    {
        if (route.HasNodeIntersection) yield return new ArchitecturePlanningDiagnostic("RouteNodeIntersection", "A route intersects an unrelated node.", PlanningDiagnosticSubject.PhysicalLink, route.PhysicalLinkId);
        if (route.HasSharedSegment) yield return new ArchitecturePlanningDiagnostic("RouteSharedSegment", "A route shares a non-zero segment with an earlier route.", PlanningDiagnosticSubject.PhysicalSegment, route.PhysicalLinkId);
        if (route.Segments.Any(segment => segment.Start == segment.End)) yield return new ArchitecturePlanningDiagnostic("RouteZeroLengthSegment", "A route contains a zero-length segment.", PlanningDiagnosticSubject.PhysicalSegment, route.PhysicalLinkId);
    }

    private static ArchitecturePlanningMetrics BuildMetrics(
        ArchitecturePlanningRequest request,
        ArchitectureProjectionResult projection,
        PlacementResult placement,
        ArchitectureV6GeometryBuilder.GeometryBuildResult geometry,
        IReadOnlyList<ArchitecturePlanningDiagnostic> diagnostics)
    {
        var duplicates = projection.SemanticNodeToPhysicalNodeIds.ToDictionary(
            item => item.Key, item => item.Value.Count, StringComparer.Ordinal);
        var layers = placement.NodeMetadata.Select(item => item.LogicalLayer).Distinct().Count();
        var footprintCells = placement.NodePlacements.Sum(item => item.Footprint.Count);
        var routes = geometry.Geometry.Routes;
        var topologyCounts = routes.GroupBy(route => route.TopologyFamily.ToString(), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        return new ArchitecturePlanningMetrics(
            projection.SemanticNodeToPhysicalNodeIds.Count,
            projection.SemanticLinkToPhysicalLinkIds.Count,
            projection.PhysicalNodes.Count,
            projection.PhysicalLinks.Count,
            placement.ProjectGrids.Count,
            placement.ProjectGrids.Sum(item => item.Grid.Rows.Count),
            placement.ProjectGrids.Sum(item => item.Grid.Columns.Count),
            geometry.ProjectGrids.Sum(item => item.Grid.Cells.Count),
            routes.Sum(route => route.Segments.Count),
            topologyCounts,
            routes.Sum(route => route.Segments.Count), routes.Sum(route => route.BendCount), routes.SelectMany(route => route.Segments).Select(segment => segment.Lane).Distinct().Count(), geometry.Geometry.DiagramBounds, diagnostics.Count,
             duplicates,
            projection.RootPhysicalNodeIds.Count,
            projection.ExternalPhysicalNodeIds.Count,
            projection.StandalonePhysicalNodeIds.Count,
            projection.CycleSemanticNodeIds.Count,
            layers,
            placement.NodePlacements.Count,
            footprintCells,
            placement.SubtreeReservations.Count,
            placement.NodeMetadata.Count(item => item.PositionalOwnerId is not null),
            placement.NodePlacements.Count(item => !placement.PlacedNodeIds.Contains(item.PhysicalNodeId)),
            placement.Diagnostics.Count(item => item.Code == "LogicalFootprintOverlap"),
            placement.Diagnostics.Count(item => item.Code == "IncompatibleSubtreeReservation"),
            geometry.Geometry.Nodes.Count,
            geometry.Geometry.Projects.Count,
            geometry.Geometry.Grids.Count,
            geometry.Geometry.AbsoluteDiagramBounds.Width,
            geometry.Geometry.AbsoluteDiagramBounds.Height,
            geometry.Findings.Count(item => item.Code == "GeometryCollision"),
            geometry.Findings.Count(item => item.Code == "GeometryContainmentViolation"),
            geometry.Findings.Count(item => item.Code == "InvalidGeometryDimension"));
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
            var validLinks = request.SemanticModel.Links
                .Where(link => nodes.ContainsKey(link.SourceId) && nodes.ContainsKey(link.TargetId))
                .ToArray();
            var unaccountedNodes = request.SemanticModel.Projects
                .SelectMany(project => project.Nodes.Select(node => node.Id))
                .Where(id => !nodes.ContainsKey(id)).ToArray();
            var unaccountedLinks = request.SemanticModel.Links
                .Where(link => !nodes.ContainsKey(link.SourceId) || !nodes.ContainsKey(link.TargetId))
                .Select(link => link.Id).ToArray();
            foreach (var id in unaccountedLinks)
                diagnostics.Add(new ArchitecturePlanningDiagnostic("UnaccountedSemanticLink", "A semantic link references a node outside the selected semantic scope.", PlanningDiagnosticSubject.SemanticLink, id));

            var parents = validLinks.GroupBy(link => link.TargetId).ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
            var children = validLinks.GroupBy(link => link.SourceId).ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
            var roots = FindRoots(parents);
            var cycleNodes = FindCycles(children, roots);
            var physicalNodes = new List<PlannedPhysicalNode>();
            var semanticToPhysical = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            var physicalBySemantic = new Dictionary<string, PlannedPhysicalNode>(StringComparer.Ordinal);
            foreach (var id in order)
            {
                var info = nodes[id];
                var projectId = info.ProjectId ?? FindExternalOwner(id, validLinks);
                var standalone = !parents.ContainsKey(id) && !children.ContainsKey(id);
                var physicalId = PhysicalId(id, 0);
                var node = new PlannedPhysicalNode(physicalId, id, PhysicalNodeProjectionMode.Canonical, null, projectId, null, info.IsExternal, standalone)
                {
                    SemanticName = info.Name,
                    SemanticFullName = info.FullName
                };
                physicalNodes.Add(node);
                physicalBySemantic[id] = node;
                semanticToPhysical[id] = new List<string> { physicalId };
            }

            var duplicatePatterns = request.NodeProjection.DuplicationExceptionPatterns
                .Where(pattern => !string.IsNullOrWhiteSpace(pattern))
                .Select(ToRegex)
                .ToArray();
            var duplicateOrdinals = new Dictionary<string, int>(StringComparer.Ordinal);
            var targetUseCount = new Dictionary<string, int>(StringComparer.Ordinal);
            var physicalLinks = new List<PlannedPhysicalLink>();
            foreach (var link in validLinks)
            {
                var source = physicalBySemantic[link.SourceId];
                var target = physicalBySemantic[link.TargetId];
                if (request.NodeProjection.Mode == NodeProjectionMode.DuplicateBranches &&
                    MatchesDuplicatePattern(nodes[link.TargetId], duplicatePatterns) &&
                    targetUseCount.TryGetValue(link.TargetId, out var useCount) && useCount > 0)
                {
                    var ordinal = duplicateOrdinals.TryGetValue(link.TargetId, out var current) ? current + 1 : 1;
                    duplicateOrdinals[link.TargetId] = ordinal;
                    var duplicateId = PhysicalId(link.TargetId, ordinal);
                    target = new PlannedPhysicalNode(
                        duplicateId,
                        link.TargetId,
                        PhysicalNodeProjectionMode.DuplicateBranch,
                        source.PhysicalNodeId,
                        nodes[link.TargetId].ProjectId ?? FindExternalOwner(link.TargetId, validLinks),
                        new DuplicationProvenance(link.TargetId, $"Configured pattern matched '{nodes[link.TargetId].Name}' for branch '{source.PhysicalNodeId}'. Originating link '{link.Id}', ordinal {ordinal}.", source.PhysicalNodeId),
                        nodes[link.TargetId].IsExternal,
                        false)
                    {
                        SemanticName = nodes[link.TargetId].Name,
                        SemanticFullName = nodes[link.TargetId].FullName
                    };
                    physicalNodes.Add(target);
                    semanticToPhysical[link.TargetId].Add(duplicateId);
                }
                targetUseCount[link.TargetId] = targetUseCount.TryGetValue(link.TargetId, out var count) ? count + 1 : 1;
                physicalLinks.Add(new PlannedPhysicalLink(
                    $"physical-link:{link.Id}:{physicalLinks.Count}", link.Id, source.PhysicalNodeId, target.PhysicalNodeId,
                    source.ProjectId, target.ProjectId) { Kind = link.Kind });
            }

            var rootsPhysical = roots.Select(id => physicalBySemantic[id].PhysicalNodeId).ToArray();
            var externalPhysical = physicalNodes.Where(node => node.IsExternal).Select(node => node.PhysicalNodeId).ToArray();
            var standalonePhysical = physicalNodes.Where(node => node.IsStandalone).Select(node => node.PhysicalNodeId).ToArray();
            var projection = new ArchitectureProjectionResult(
                physicalNodes,
                physicalLinks,
                Array.Empty<PhysicalNodePlacementMetadata>(),
                Array.Empty<PlannedPhysicalLinkMetadata>(),
                semanticToPhysical.ToDictionary(item => item.Key, item => (IReadOnlyList<string>)item.Value.ToArray(), StringComparer.Ordinal),
                validLinks.GroupBy(link => link.Id).ToDictionary(group => group.Key, group => (IReadOnlyList<string>)physicalLinks.Where(item => item.SemanticLinkId == group.Key).Select(item => item.PhysicalLinkId).ToArray(), StringComparer.Ordinal),
                rootsPhysical,
                externalPhysical,
                standalonePhysical,
                cycleNodes,
                unaccountedNodes,
                unaccountedLinks,
                diagnostics);
            return projection;
        }

        private void ReadSemanticNodes()
        {
            var selectedProjects = new HashSet<string>(request.SelectedScope.SelectedProjectIds ?? Array.Empty<string>(), StringComparer.Ordinal);
            var selectedNodes = new HashSet<string>(request.SemanticModel.Selection?.SelectedNodeIds ?? Array.Empty<string>(), StringComparer.Ordinal);
            foreach (var project in request.SemanticModel.Projects)
            {
                if (selectedProjects.Count > 0 && !selectedProjects.Contains(project.Id)) continue;
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
            var result = new HashSet<string>(StringComparer.Ordinal);
            var visiting = new HashSet<string>(StringComparer.Ordinal);
            var visited = new HashSet<string>(StringComparer.Ordinal);
            void Visit(string id)
            {
                if (visiting.Contains(id)) { result.Add(id); return; }
                if (!visited.Add(id)) return;
                visiting.Add(id);
                if (children.TryGetValue(id, out var outgoing))
                    foreach (var link in outgoing) Visit(link.TargetId);
                visiting.Remove(id);
            }
            foreach (var root in roots) Visit(root);
            foreach (var id in order) Visit(id);
            return result.OrderBy(id => order.IndexOf(id)).ToArray();
        }

        private static bool MatchesDuplicatePattern(SemanticInfo node, IReadOnlyList<Regex> patterns) => patterns.Any(pattern => pattern.IsMatch(node.Name) || pattern.IsMatch(node.Id));
        private static Regex ToRegex(string pattern)
        {
            var expression = pattern.Contains("*") ? "^" + Regex.Escape(pattern).Replace("\\*", ".*") + "$" : pattern;
            return new Regex(expression, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }
        private static string PhysicalId(string semanticId, int ordinal) => ordinal == 0 ? $"physical:{semanticId}" : $"physical:{semanticId}:duplicate:{ordinal}";
        private sealed record SemanticInfo(string Id, string Name, string FullName, string? ProjectId, bool IsExternal);
    }

    private sealed class LogicalPlacementBuilder
    {
        private readonly ArchitecturePlanningRequest request;
        private readonly ArchitectureProjectionResult projection;
        private readonly Dictionary<string, PlannedPhysicalNode> nodes;
        private readonly Dictionary<string, int> order;
        private readonly Dictionary<string, List<string>> children = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string?> owners = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> layers = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> visualRows = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> roleBands = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> roleSelectors = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> siblingGroups = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> spans = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Position> positions = new(StringComparer.Ordinal);
        private readonly List<ArchitecturePlanningDiagnostic> diagnostics = new();
        private readonly List<SubtreeReservation> reservations = new();
        private int nextColumn;

        public LogicalPlacementBuilder(ArchitecturePlanningRequest request, ArchitectureProjectionResult projection)
        {
            this.request = request;
            this.projection = projection;
            nodes = projection.PhysicalNodes.ToDictionary(node => node.PhysicalNodeId, StringComparer.Ordinal);
            order = projection.PhysicalNodes.Select((node, index) => (node.PhysicalNodeId, index)).ToDictionary(item => item.PhysicalNodeId, item => item.index, StringComparer.Ordinal);
        }

        public PlacementResult Build()
        {
            var links = projection.PhysicalLinks;
            var incoming = links.GroupBy(link => link.DestinationPhysicalNodeId).ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
            foreach (var node in projection.PhysicalNodes)
            {
                var parent = incoming.TryGetValue(node.PhysicalNodeId, out var incomingLinks)
                    ? incomingLinks.Select(link => link.SourcePhysicalNodeId).OrderBy(id => order[id]).FirstOrDefault(id => order[id] < order[node.PhysicalNodeId])
                    : null;
                owners[node.PhysicalNodeId] = parent;
                if (parent is not null)
                    children.GetOrAdd(parent).Add(node.PhysicalNodeId);
            }
            foreach (var item in children.Values) item.Sort((left, right) => order[left].CompareTo(order[right]));
            AssignLayers(links);
            var baseline = new Regex(BaselineExpression(request.NodePlacement.BaselinePattern), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            foreach (var node in projection.PhysicalNodes)
                if (layers[node.PhysicalNodeId] == int.MaxValue) layers[node.PhysicalNodeId] = 0;
            foreach (var node in projection.PhysicalNodes)
                if (baseline.IsMatch(node.SemanticNodeId) || baseline.IsMatch(node.PhysicalNodeId)) layers[node.PhysicalNodeId] = 0;
            for (var pass = 0; pass < projection.PhysicalNodes.Count; pass++)
                foreach (var link in links)
                    if (layers[link.DestinationPhysicalNodeId] <= layers[link.SourcePhysicalNodeId] &&
                        !baseline.IsMatch(nodes[link.DestinationPhysicalNodeId].SemanticName))
                        layers[link.DestinationPhysicalNodeId] = layers[link.SourcePhysicalNodeId] + 1;
            AssignRoleBandsAndRows(baseline);
            foreach (var node in projection.PhysicalNodes) spans[node.PhysicalNodeId] = FootprintSpan(node, links);

            var roots = projection.PhysicalNodes.Where(node => owners[node.PhysicalNodeId] is null).OrderBy(node => order[node.PhysicalNodeId]).ToArray();
            foreach (var root in roots)
            {
                var width = SubtreeWidth(root.PhysicalNodeId);
                PlaceSubtree(root.PhysicalNodeId, nextColumn);
                nextColumn += width + 1;
            }
            var unplaced = projection.PhysicalNodes.Where(node => !positions.ContainsKey(node.PhysicalNodeId)).OrderBy(node => order[node.PhysicalNodeId]).ToArray();
            foreach (var node in unplaced)
            {
                PlaceSubtree(node.PhysicalNodeId, nextColumn);
                nextColumn += spans[node.PhysicalNodeId] + 1;
            }

            BuildReservations();
            var placements = BuildPlacements();
            var projectGrids = BuildProjectGrids(placements);
            var diagramGrid = new DiagramRoutingGrid(
                EmptyGrid(new PlanningGridId("diagram"), nextColumn, layers.Values.DefaultIfEmpty(0).Max() + 1),
                Array.Empty<RelativeRectangle>(),
                Array.Empty<GridTransition>());
            var metadata = BuildMetadata(links);
            var linkMetadata = links.Select(link => new PlannedPhysicalLinkMetadata(
                link.PhysicalLinkId, link.SemanticLinkId, link.SourcePhysicalNodeId, link.DestinationPhysicalNodeId,
                link.SourceProjectId, link.DestinationProjectId, layers[link.SourcePhysicalNodeId], layers[link.DestinationPhysicalNodeId],
                link.SourceProjectId != link.DestinationProjectId ? "CrossProject" : layers[link.DestinationPhysicalNodeId] > layers[link.SourcePhysicalNodeId] ? "Downward" : layers[link.DestinationPhysicalNodeId] < layers[link.SourcePhysicalNodeId] ? "Upward" : "SameLayer",
                link.SourceProjectId != link.DestinationProjectId, nodes[link.DestinationPhysicalNodeId].IsExternal)).ToArray();
            return new PlacementResult(projectGrids, diagramGrid, placements, metadata, linkMetadata, reservations, diagnostics, new HashSet<string>(positions.Keys, StringComparer.Ordinal));
        }

        private void AssignLayers(IReadOnlyList<PlannedPhysicalLink> links)
        {
            foreach (var node in projection.PhysicalNodes) layers[node.PhysicalNodeId] = projection.RootPhysicalNodeIds.Contains(node.PhysicalNodeId) ? 0 : int.MaxValue;
            for (var pass = 0; pass < projection.PhysicalNodes.Count; pass++)
            {
                var changed = false;
                foreach (var link in links.OrderBy(link => order[link.SourcePhysicalNodeId]).ThenBy(link => order[link.DestinationPhysicalNodeId]))
                {
                    if (layers[link.SourcePhysicalNodeId] == int.MaxValue) continue;
                    var candidate = layers[link.SourcePhysicalNodeId] + 1;
                    if (candidate >= layers[link.DestinationPhysicalNodeId]) continue;
                    layers[link.DestinationPhysicalNodeId] = candidate;
                    changed = true;
                }
                if (!changed) break;
            }
            foreach (var node in projection.PhysicalNodes)
                if (layers[node.PhysicalNodeId] == int.MaxValue) layers[node.PhysicalNodeId] = 0;
        }

        private void AssignRoleBandsAndRows(Regex baseline)
        {
            var rules = (request.NodePlacement.RoleRules ?? Array.Empty<ArchitectureV6RoleRule>())
                .OrderBy(rule => rule.Order).ToArray();
            foreach (var node in projection.PhysicalNodes)
            {
                var role = node.IsExternal ? new ArchitectureV6RoleRule("External", "", int.MaxValue)
                    : rules.FirstOrDefault(rule => IsMatch(node.SemanticName, rule.Pattern));
                roleSelectors[node.PhysicalNodeId] = role?.Name ?? "Unmatched";
                roleBands[node.PhysicalNodeId] = role?.Order ?? rules.Length;
                siblingGroups[node.PhysicalNodeId] = owners[node.PhysicalNodeId] is null
                    ? $"root:{node.ProjectId ?? "external"}"
                    : $"owner:{owners[node.PhysicalNodeId]}";
            }

            // A configured baseline is a rigid group. Other roles are sublayers within
            // their semantic depth, preserving dependency order without sparse columns.
            foreach (var node in projection.PhysicalNodes.Where(node => baseline.IsMatch(node.SemanticName)))
                visualRows[node.PhysicalNodeId] = 0;
            var nextRow = visualRows.Count == 0 ? 0 : 1;
            foreach (var layer in layers.Values.Distinct().OrderBy(value => value))
            {
                var groups = projection.PhysicalNodes.Where(node => layers[node.PhysicalNodeId] == layer &&
                        !visualRows.ContainsKey(node.PhysicalNodeId))
                    .GroupBy(node => roleBands[node.PhysicalNodeId]).OrderBy(group => group.Key).ToArray();
                foreach (var group in groups)
                {
                    foreach (var node in group.OrderBy(node => order[node.PhysicalNodeId]))
                        visualRows[node.PhysicalNodeId] = nextRow;
                    nextRow++;
                }
            }
            foreach (var node in projection.PhysicalNodes)
                if (!visualRows.ContainsKey(node.PhysicalNodeId)) visualRows[node.PhysicalNodeId] = nextRow++;
        }

        private static bool IsMatch(string value, string pattern)
        {
            if (string.IsNullOrWhiteSpace(pattern)) return false;
            var expression = IsRegexPattern(pattern)
                ? pattern
                : "^" + Regex.Escape(pattern).Replace("\\*", ".*") + "$";
            return Regex.IsMatch(value, expression, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        private int SubtreeWidth(string id)
        {
            if (!children.TryGetValue(id, out var childIds) || childIds.Count == 0) return spans[id];
            var childWidth = childIds.Sum(SubtreeWidth) + Math.Max(0, childIds.Count - 1);
            return Math.Max(spans[id], childWidth);
        }

        private void PlaceSubtree(string id, int start)
        {
            var width = SubtreeWidth(id);
            var childrenWidth = children.TryGetValue(id, out var childIds) && childIds.Count > 0 ? childIds.Sum(SubtreeWidth) + childIds.Count - 1 : 0;
            var centre = start + width / 2;
            positions[id] = new Position(centre, layers[id], start, start + width - 1);
            if (childIds is null || childIds.Count == 0) return;
            var childStart = start + Math.Max(0, (width - childrenWidth) / 2);
            foreach (var child in childIds)
            {
                PlaceSubtree(child, childStart);
                childStart += SubtreeWidth(child) + 1;
            }
        }

        private void BuildReservations()
        {
            reservations.Clear();
            foreach (var node in projection.PhysicalNodes.OrderBy(item => order[item.PhysicalNodeId]))
            {
                var members = SubtreeMembers(node.PhysicalNodeId).ToArray();
                var min = members.Min(item => positions[item].Start);
                var max = members.Max(item => positions[item].End);
                var rows = members.Select(item => visualRows[item]).Distinct().OrderBy(item => item).ToArray();
                var cells = rows.SelectMany(row => Enumerable.Range(min, max - min + 1).Select(column => CellId(nodes[node.PhysicalNodeId].ProjectId, row, column))).ToArray();
                reservations.Add(new SubtreeReservation($"subtree:{node.PhysicalNodeId}", node.PhysicalNodeId,
                    new PlanningGridId($"project:{nodes[node.PhysicalNodeId].ProjectId ?? "external"}"), cells,
                    owners[node.PhysicalNodeId] is null ? null : $"subtree:{owners[node.PhysicalNodeId]}"));
            }
        }

        private IEnumerable<string> SubtreeMembers(string id)
        {
            yield return id;
            if (!children.TryGetValue(id, out var childIds)) yield break;
            foreach (var child in childIds)
                foreach (var member in SubtreeMembers(child)) yield return member;
        }

        private IReadOnlyList<PlannedNodePlacement> BuildPlacements()
        {
            var result = new List<PlannedNodePlacement>();
            foreach (var node in projection.PhysicalNodes.OrderBy(item => order[item.PhysicalNodeId]))
            {
                var position = positions[node.PhysicalNodeId];
                var gridId = new PlanningGridId($"project:{node.ProjectId ?? "external"}");
                var start = position.Centre - spans[node.PhysicalNodeId] / 2;
                var row = visualRows[node.PhysicalNodeId];
                var footprint = Enumerable.Range(start, spans[node.PhysicalNodeId]).Select(column => new PlanningGridCellId(gridId, RowId(row), ColumnId(column))).ToArray();
                result.Add(new PlannedNodePlacement(node.PhysicalNodeId, gridId, new PlanningGridCellId(gridId, RowId(row), ColumnId(position.Centre)), spans[node.PhysicalNodeId], 1, footprint, ColumnId(position.Centre)));
            }
            return result;
        }

        private IReadOnlyList<ProjectRoutingGrid> BuildProjectGrids(IReadOnlyList<PlannedNodePlacement> placements)
        {
            var ids = projection.PhysicalNodes.Select(node => node.ProjectId ?? "external").Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal).ToArray();
            return ids.Select(projectId =>
            {
                var owned = projection.PhysicalNodes.Where(node => (node.ProjectId ?? "external") == projectId).Select(node => node.PhysicalNodeId).ToArray();
                var external = projection.PhysicalNodes.Where(node => (node.ProjectId ?? "external") == projectId && node.IsExternal).Select(node => node.PhysicalNodeId).ToArray();
                var projectPlacements = placements.Where(item => item.GridId.Value == $"project:{projectId}").ToArray();
                var maxColumn = projectPlacements.SelectMany(item => item.Footprint).Select(item => ParseColumn(item.ColumnId)).DefaultIfEmpty(0).Max();
                var maxRow = projectPlacements.Select(item => ParseRow(item.AnchorCellId.RowId)).DefaultIfEmpty(0).Max();
                return new ProjectRoutingGrid(projectId, EmptyGrid(new PlanningGridId($"project:{projectId}"), maxColumn + 1, maxRow + 1, projectPlacements), reservations.Where(item => item.GridId.Value == $"project:{projectId}").ToArray(), null, owned, external, $"project:{projectId}");
            }).ToArray();
        }

        private PlanningGrid EmptyGrid(PlanningGridId id, int columnCount, int rowCount, IReadOnlyList<PlannedNodePlacement>? placements = null)
        {
            var rows = Enumerable.Range(0, Math.Max(1, rowCount)).Select(index => new PlanningGridRow(RowId(index), index, 1, 1, 1, index, index)).ToArray();
            var columns = Enumerable.Range(0, Math.Max(1, columnCount)).Select(index => new PlanningGridColumn(ColumnId(index), index, 1, 1, 1, index, index)).ToArray();
            var cells = new Dictionary<PlanningGridCellId, PlanningGridCell>();
            var covered = new HashSet<PlanningGridCellId>();
            foreach (var placement in placements ?? Array.Empty<PlannedNodePlacement>())
                foreach (var cell in placement.Footprint) covered.Add(cell);
            for (var row = 0; row < rows.Length; row++)
                for (var column = 0; column < columns.Length; column++)
                {
                    var cellId = new PlanningGridCellId(id, rows[row].Id, columns[column].Id);
                    var isAnchor = placements?.Any(placement => placement.AnchorCellId.Equals(cellId)) == true;
                    var reservationIds = reservations.Where(item => item.GridId.Equals(id) && item.Cells.Contains(cellId)).Select(item => item.SubtreeId).ToArray();
                    cells[cellId] = new PlanningGridCell(cellId, CellCapability.RoutingAllowed | CellCapability.NodeAllowed, isAnchor ? CellOccupancy.NodeAnchor : covered.Contains(cellId) ? CellOccupancy.ProjectFootprint : CellOccupancy.Empty, reservationIds);
                }
            return new PlanningGrid(id, rows, columns, cells, new GridTransform(id, new RelativePoint(0, 0)));
        }

        private IReadOnlyList<PhysicalNodePlacementMetadata> BuildMetadata(IReadOnlyList<PlannedPhysicalLink> links)
        {
            return projection.PhysicalNodes.OrderBy(node => order[node.PhysicalNodeId]).Select(node =>
            {
                var semanticParents = links.Where(link => link.DestinationPhysicalNodeId == node.PhysicalNodeId).Select(link => nodes[link.SourcePhysicalNodeId].SemanticNodeId).Distinct(StringComparer.Ordinal).ToArray();
                var semanticChildren = links.Where(link => link.SourcePhysicalNodeId == node.PhysicalNodeId).Select(link => nodes[link.DestinationPhysicalNodeId].SemanticNodeId).Distinct(StringComparer.Ordinal).ToArray();
                var positionalChildren = children.TryGetValue(node.PhysicalNodeId, out var childIds) ? childIds.ToArray() : Array.Empty<string>();
                var subtreeId = $"subtree:{node.PhysicalNodeId}";
                var ancestors = new List<string>();
                var owner = owners[node.PhysicalNodeId];
                while (owner is not null) { ancestors.Add($"subtree:{owner}"); owner = owners[owner]; }
                var baselineMember = Regex.IsMatch(node.SemanticName, BaselineExpression(request.NodePlacement.BaselinePattern), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                return new PhysicalNodePlacementMetadata(node.PhysicalNodeId, node.SemanticNodeId, owners[node.PhysicalNodeId], positionalChildren, semanticParents, semanticChildren, subtreeId, ancestors, node.ProjectId, layers[node.PhysicalNodeId], baselineMember, node.IsExternal, node.IsStandalone, owners[node.PhysicalNodeId] is null ? "root-or-disconnected" : "first-discovered-parent", layers[node.PhysicalNodeId], roleSelectors[node.PhysicalNodeId], roleBands[node.PhysicalNodeId], node.ProjectId ?? "external", siblingGroups[node.PhysicalNodeId], "tiered-horizontal", baselineMember ? "baseline" : "role-band", visualRows[node.PhysicalNodeId], positions[node.PhysicalNodeId].Centre);
            }).ToArray();
        }

        private PlanningGridCellId CellId(string? projectId, int row, int column) => new(new PlanningGridId($"project:{projectId ?? "external"}"), RowId(row), ColumnId(column));
        private static PlanningGridRowId RowId(int row) => new($"row:{row}");
        private static PlanningGridColumnId ColumnId(int column) => new($"column:{column}");
        private static int ParseRow(PlanningGridRowId id) => int.TryParse(id.Value.Substring(id.Value.LastIndexOf(':') + 1), out var value) ? value : 0;
        private static int ParseColumn(PlanningGridColumnId id) => int.TryParse(id.Value.Substring(id.Value.LastIndexOf(':') + 1), out var value) ? value : 0;
        private static int FootprintSpan(PlannedPhysicalNode node, IReadOnlyList<PlannedPhysicalLink> links)
        {
            var degree = links.Count(link => link.SourcePhysicalNodeId == node.PhysicalNodeId || link.DestinationPhysicalNodeId == node.PhysicalNodeId);
            var span = Math.Max(3, ((node.SemanticName ?? node.SemanticNodeId).Length + 19) / 20 * 2 + 1);
            if (degree > 4) span = Math.Max(span, 5);
            if (degree > 8) span = Math.Max(span, 7);
            return span % 2 == 0 ? span + 1 : span;
        }
        private static string WildcardToRegex(string value)
        {
            var pattern = string.IsNullOrWhiteSpace(value) ? ".*" : value;
            return "^" + Regex.Escape(pattern).Replace("\\*", ".*") + "$";
        }
        private static string BaselineExpression(string value) => string.IsNullOrWhiteSpace(value) ? ".*" :
            IsRegexPattern(value) ? value : WildcardToRegex(value);
        private static bool IsRegexPattern(string value) => value.IndexOf(".*", StringComparison.Ordinal) >= 0 ||
            value.IndexOf('(') >= 0 || value.IndexOf('[') >= 0 || value.EndsWith("$", StringComparison.Ordinal);
        private sealed record Position(int Centre, int Layer, int Start, int End);
    }

    private sealed record PlacementResult(
        IReadOnlyList<ProjectRoutingGrid> ProjectGrids,
        DiagramRoutingGrid DiagramGrid,
        IReadOnlyList<PlannedNodePlacement> NodePlacements,
        IReadOnlyList<PhysicalNodePlacementMetadata> NodeMetadata,
        IReadOnlyList<PlannedPhysicalLinkMetadata> LinkMetadata,
        IReadOnlyList<SubtreeReservation> SubtreeReservations,
        IReadOnlyList<ArchitecturePlanningDiagnostic> Diagnostics,
        IReadOnlyCollection<string> PlacedNodeIds);
}

internal static class ArchitectureV6CollectionExtensions
{
    public static List<TValue> GetOrAdd<TKey, TValue>(this IDictionary<TKey, List<TValue>> dictionary, TKey key)
    {
        if (!dictionary.TryGetValue(key, out var values)) { values = new List<TValue>(); dictionary[key] = values; }
        return values;
    }
}
