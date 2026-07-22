using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core.Models;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal static class ProjectInterLayerSlotCompiler
{
    public static ProjectSlotCompilation Compile(
        IReadOnlyDictionary<string, CanonicalTopologyPlan> plans,
        IReadOnlyDictionary<string, NodeLayout> nodes,
        IReadOnlyDictionary<string, LinkLayout> terminalLayouts,
        IReadOnlyDictionary<string, ProjectLabelGeometry> projectLabels,
        LayoutRevision revision,
        int separation,
        int padding)
    {
        var timings = new List<PipelineStageMetric>();
        var timer = Stopwatch.StartNew();
        var bands = Bands(nodes, revision, padding, separation, terminalLayouts.Count);
        var globalHorizontalSpan = new AxisInterval(
            nodes.Values.Min(item => item.Rect.X) - padding,
            nodes.Values.Max(item => item.Rect.Right) + padding);
        var demands = new List<LinkSegmentDemand>();
        foreach (var plan in plans.Values.OrderBy(item => terminalLayouts[item.LogicalRouteId].Link.Order)
                     .ThenBy(item => item.LogicalRouteId, StringComparer.Ordinal))
        {
            var route = terminalLayouts[plan.LogicalRouteId];
            var departureId = BandForDeparture(plan, nodes, bands);
            var horizontalSpan = HorizontalSpan(plan, nodes, padding, globalHorizontalSpan);
            demands.Add(Demand(plan, route, departureId, DepartureRole(plan.Family), 0,
                bands[departureId], revision, horizontalSpan));
            if (plan.RequiresReturnColumn || plan.RequiresDestinationColumn)
            {
                var arrivalId = BandForArrival(plan, nodes, bands);
                demands.Add(Demand(plan, route, arrivalId,
                    plan.RequiresReturnColumn ? LinkSegmentRole.ReturnArrival : LinkSegmentRole.LongArrival,
                    1, bands[arrivalId], revision, horizontalSpan));
            }
        }

        timer.Stop();
        timings.Add(new PipelineStageMetric("project-region InterLayer discovery", timer.ElapsedMilliseconds));

        timer.Restart();
        var preservedRootAssignments = PreservedRootAssignments(
            plans, nodes, terminalLayouts, bands, revision, separation, padding, globalHorizontalSpan);
        var assignments = new Dictionary<string, AssignedLinkSegment>(StringComparer.Ordinal);
        var requiredExpansion = new Dictionary<ProjectLayerExpansionIdentity, int>();
        foreach (var group in demands.GroupBy(item =>
                     $"{item.MovementScope?.Id}:{item.AllowedAxisRange.Minimum}:{item.AllowedAxisRange.Maximum}",
                     StringComparer.Ordinal).OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            var sample = group.First();
            var allowedRange = sample.AllowedAxisRange;
            var identity = new LinkSegmentAllocationRegionIdentity(
                LinkSegmentOrientation.Horizontal, allowedRange,
                $"project-interLayer:{group.Key}",
                sample.MovementScope, revision);
            var assigned = DeterministicSlotAllocator.Assign(identity, group,
                new LinkSegmentAssignmentOptions(separation, padding));
            var selectedAssignments = string.Equals(sample.DemandCategory, "ProjectInternal", StringComparison.Ordinal)
                ? ConstrainProjectAssignments(group.ToArray(), assigned.SegmentsByDemandId, plans, nodes,
                    projectLabels, separation, padding)
                : assigned.SegmentsByDemandId;
            foreach (var item in selectedAssignments)
                assignments.Add(item.Key, string.Equals(sample.DemandCategory, "RootTransition", StringComparison.Ordinal)
                    ? preservedRootAssignments[item.Key]
                    : item.Value);
            var requiredExtent = selectedAssignments.Values.Select(item => item.SlotIndex)
                .DefaultIfEmpty(0).Max() * separation + separation + padding * 2;
            var missing = Math.Max(0, Math.Max(assigned.RequiredExtent, requiredExtent) - allowedRange.Length);
            if (missing > 0 && sample.CoordinateFrameId is not null &&
                string.Equals(sample.DemandCategory, "ProjectInternal", StringComparison.Ordinal))
            {
                var band = bands.Keys.Single(item => string.Equals(item.ToString(), sample.BandId, StringComparison.Ordinal));
                var expansionId = new ProjectLayerExpansionIdentity(sample.CoordinateFrameId, band.LowerLayer);
                requiredExpansion[expansionId] = Math.Max(
                    requiredExpansion.TryGetValue(expansionId, out var existing) ? existing : 0, missing);
            }
        }

        timer.Stop();
        timings.Add(new PipelineStageMetric("project-region horizontal slot allocation", timer.ElapsedMilliseconds));

        timer.Restart();
        var returnOrder = plans.Values.Where(item => item.RequiresReturnColumn)
            .OrderBy(item => item.LogicalRouteId, StringComparer.Ordinal)
            .Select((plan, index) => (plan.LogicalRouteId, index))
            .ToDictionary(item => item.LogicalRouteId, item => item.index, StringComparer.Ordinal);
        var minimumX = nodes.Values.Min(item => item.Rect.X);
        var maximumX = nodes.Values.Max(item => item.Rect.Right);
        var returnSides = plans.Values.Where(item => item.RequiresReturnColumn)
            .OrderBy(item => item.LogicalRouteId, StringComparer.Ordinal)
            .ToDictionary(item => item.LogicalRouteId, item =>
            {
                var route = terminalLayouts[item.LogicalRouteId];
                var leftCost = route.SourcePoint.X - minimumX + route.TargetPoint.X - minimumX;
                var rightCost = maximumX - route.SourcePoint.X + maximumX - route.TargetPoint.X;
                return leftCost <= rightCost ? "Left" : "Right";
            }, StringComparer.Ordinal);
        var verticalDemands = plans.Values.Where(item => item.RequiresDestinationColumn || item.RequiresReturnColumn)
            .OrderBy(item => item.LogicalRouteId, StringComparer.Ordinal).Select(plan =>
            {
                var route = terminalLayouts[plan.LogicalRouteId];
                var routeDemands = demands.Where(item => item.LogicalRouteId == plan.LogicalRouteId)
                    .OrderBy(item => item.TurnOrder).ToArray();
                var departureY = assignments[routeDemands[0].Id].AxisCoordinate;
                var arrivalY = assignments[routeDemands[1].Id].AxisCoordinate;
                var interval = new AxisInterval(departureY, arrivalY);
                if (plan.RequiresReturnColumn)
                {
                    var lane = returnOrder[plan.LogicalRouteId] + 1;
                    var left = returnSides[plan.LogicalRouteId] == "Left";
                    var preferred = left ? minimumX - padding - separation * lane : maximumX + padding + separation * lane;
                    return new VerticalLinkColumnDemand(
                        $"{plan.LogicalRouteId}:return-column", plan.LogicalRouteId, preferred,
                        new AxisInterval(preferred, preferred), nodes[plan.SourceNodeId].Depth, nodes[plan.TargetNodeId].Depth,
                        interval, padding, plan.SourceNodeId, plan.TargetNodeId, nodes[plan.SourceNodeId].Node.ProjectId,
                        null, revision, new RouteRevision(0));
                }
                var allowed = new AxisInterval(minimumX - padding - separation * plans.Count,
                    maximumX + padding + separation * plans.Count);
                var forbidden = nodes.Values.Where(node => node.Node.Id != plan.SourceNodeId && node.Node.Id != plan.TargetNodeId &&
                        PositiveOverlap(interval, new AxisInterval(node.Rect.Y - padding, node.Rect.Bottom + padding)))
                    .Select(node => new AxisInterval(node.Rect.X - padding, node.Rect.Right + padding))
                    .Concat(projectLabels.Values.Where(label => PositiveOverlap(interval,
                            new AxisInterval(label.ProjectLabelObstacleBounds.Y, label.ProjectLabelObstacleBounds.Bottom)))
                        .Select(label => new AxisInterval(
                            label.ProjectLabelObstacleBounds.X, label.ProjectLabelObstacleBounds.Right)))
                    .Concat(FixedColumnExclusions(
                        plan, plans, terminalLayouts, demands, assignments, interval, separation))
                    .ToArray();
                return new VerticalLinkColumnDemand(
                    $"{plan.LogicalRouteId}:destination-column", plan.LogicalRouteId, route.TargetPoint.X,
                    allowed, nodes[plan.SourceNodeId].Depth, nodes[plan.TargetNodeId].Depth, interval, padding,
                    plan.SourceNodeId, plan.TargetNodeId, nodes[plan.TargetNodeId].Node.ProjectId, null,
                    revision, new RouteRevision(0), forbidden);
            }).ToArray();
        var verticalColumns = VerticalLinkColumnAllocator.Assign(verticalDemands, separation);
        timer.Stop();
        timings.Add(new PipelineStageMetric(
            "project-region vertical and return column allocation", timer.ElapsedMilliseconds));

        timer.Restart();
        var links = plans.Values.OrderBy(item => item.LogicalRouteId, StringComparer.Ordinal).ToDictionary(
            plan => plan.LogicalRouteId,
            plan => Materialize(plan, terminalLayouts[plan.LogicalRouteId], demands, assignments, verticalColumns),
            StringComparer.Ordinal);
        timer.Stop();
        timings.Add(new PipelineStageMetric("project-region constrained materialisation", timer.ElapsedMilliseconds));
        return new ProjectSlotCompilation(
            links, demands, assignments, verticalColumns, returnSides, requiredExpansion,
            bands.Count, requiredExpansion.Count, timings);
    }

    private static IReadOnlyDictionary<string, AssignedLinkSegment> ConstrainProjectAssignments(
        IReadOnlyList<LinkSegmentDemand> demands,
        IReadOnlyDictionary<string, AssignedLinkSegment> preferred,
        IReadOnlyDictionary<string, CanonicalTopologyPlan> plans,
        IReadOnlyDictionary<string, NodeLayout> nodes,
        IReadOnlyDictionary<string, ProjectLabelGeometry> labels,
        int separation,
        int padding)
    {
        var result = new Dictionary<string, AssignedLinkSegment>(StringComparer.Ordinal);
        foreach (var demand in demands.OrderBy(item => preferred[item.Id].SlotIndex)
                     .ThenBy(item => item.LogicalRouteId, StringComparer.Ordinal)
                     .ThenBy(item => item.TurnOrder).ThenBy(item => item.Id, StringComparer.Ordinal))
        {
            var plan = plans[demand.LogicalRouteId];
            var slot = preferred[demand.Id].SlotIndex;
            while (ProjectSlotBlocked(demand, slot, result.Values, plan, nodes, labels, separation, padding))
                slot++;
            var coordinate = demand.AllowedAxisRange.Minimum + padding + slot * separation;
            result.Add(demand.Id, preferred[demand.Id] with { AxisCoordinate = coordinate, SlotIndex = slot });
        }
        return result;
    }

    private static bool ProjectSlotBlocked(
        LinkSegmentDemand demand,
        int slot,
        IEnumerable<AssignedLinkSegment> allocated,
        CanonicalTopologyPlan plan,
        IReadOnlyDictionary<string, NodeLayout> nodes,
        IReadOnlyDictionary<string, ProjectLabelGeometry> labels,
        int separation,
        int padding)
    {
        var y = demand.AllowedAxisRange.Minimum + padding + slot * separation;
        if (nodes.Values.Where(node => string.Equals(node.Node.ProjectId, demand.CoordinateFrameId, StringComparison.Ordinal) &&
                node.Node.Id != plan.SourceNodeId && node.Node.Id != plan.TargetNodeId)
            .Any(node => y >= node.Rect.Y - padding && y <= node.Rect.Bottom + padding &&
                         PositiveOverlap(demand.OccupiedInterval,
                             new AxisInterval(node.Rect.X - padding, node.Rect.Right + padding))))
            return true;
        if (labels.TryGetValue(demand.CoordinateFrameId!, out var label) &&
            y >= label.ProjectLabelObstacleBounds.Y && y <= label.ProjectLabelObstacleBounds.Bottom &&
            PositiveOverlap(demand.OccupiedInterval,
                new AxisInterval(label.ProjectLabelObstacleBounds.X, label.ProjectLabelObstacleBounds.Right)))
            return true;
        return allocated.Any(other => Math.Abs(other.AxisCoordinate - y) < separation &&
            PositiveOverlap(demand.OccupiedInterval, other.OccupiedInterval));
    }

    private static IReadOnlyDictionary<string, AssignedLinkSegment> PreservedRootAssignments(
        IReadOnlyDictionary<string, CanonicalTopologyPlan> plans,
        IReadOnlyDictionary<string, NodeLayout> nodes,
        IReadOnlyDictionary<string, LinkLayout> routes,
        IReadOnlyDictionary<InterLayerId, AxisInterval> bands,
        LayoutRevision revision,
        int separation,
        int padding,
        AxisInterval globalHorizontalSpan)
    {
        var legacyDemands = new List<LinkSegmentDemand>();
        foreach (var plan in plans.Values.OrderBy(item => routes[item.LogicalRouteId].Link.Order)
                     .ThenBy(item => item.LogicalRouteId, StringComparer.Ordinal))
        {
            var route = routes[plan.LogicalRouteId];
            var departure = ClosestBand(bands, nodes[plan.SourceNodeId].Depth, null,
                InterLayerBandRole.RootTransition);
            legacyDemands.Add(Demand(plan, route, departure, DepartureRole(plan.Family), 0,
                bands[departure], revision, globalHorizontalSpan) with
            {
                MovementScope = new MovementScopeIdentity(
                    MovementScopeKind.LayerAndLowerSuffix, $"depth:{departure.LowerLayer}")
            });
            if (!plan.RequiresReturnColumn && !plan.RequiresDestinationColumn) continue;
            var arrival = ClosestBand(bands, Math.Max(-1, nodes[plan.TargetNodeId].Depth - 1), null,
                InterLayerBandRole.RootTransition);
            legacyDemands.Add(Demand(plan, route, arrival,
                plan.RequiresReturnColumn ? LinkSegmentRole.ReturnArrival : LinkSegmentRole.LongArrival,
                1, bands[arrival], revision, globalHorizontalSpan) with
            {
                MovementScope = new MovementScopeIdentity(
                    MovementScopeKind.LayerAndLowerSuffix, $"depth:{arrival.LowerLayer}")
            });
        }

        var result = new Dictionary<string, AssignedLinkSegment>(StringComparer.Ordinal);
        foreach (var group in legacyDemands.GroupBy(item =>
                     $"{item.MovementScope?.Id}:{item.AllowedAxisRange.Minimum}:{item.AllowedAxisRange.Maximum}",
                     StringComparer.Ordinal).OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            var sample = group.First();
            var identity = new LinkSegmentAllocationRegionIdentity(
                LinkSegmentOrientation.Horizontal, sample.AllowedAxisRange,
                $"legacy-preserved-interLayer:{group.Key}", sample.MovementScope, revision);
            var assigned = DeterministicSlotAllocator.Assign(identity, group,
                new LinkSegmentAssignmentOptions(separation, padding));
            foreach (var item in assigned.SegmentsByDemandId) result[item.Key] = item.Value;
        }
        return result;
    }

    private static LinkLayout Materialize(
        CanonicalTopologyPlan plan,
        LinkLayout route,
        IReadOnlyList<LinkSegmentDemand> demands,
        IReadOnlyDictionary<string, AssignedLinkSegment> assignments,
        VerticalLinkColumnAssignment verticalColumns)
    {
        var routeDemands = demands.Where(item => item.LogicalRouteId == plan.LogicalRouteId)
            .OrderBy(item => item.TurnOrder).ToArray();
        var departureY = assignments[routeDemands[0].Id].AxisCoordinate;
        IReadOnlyList<Point> points;
        if (plan.RequiresReturnColumn || plan.RequiresDestinationColumn)
        {
            var arrivalY = assignments[routeDemands[1].Id].AxisCoordinate;
            var demandId = plan.RequiresReturnColumn
                ? $"{plan.LogicalRouteId}:return-column"
                : $"{plan.LogicalRouteId}:destination-column";
            var columnX = verticalColumns.ColumnsByDemandId[demandId].X;
            points = new[]
            {
                new Point(route.SourcePoint.X, departureY), new Point(columnX, departureY),
                new Point(columnX, arrivalY), new Point(route.TargetPoint.X, arrivalY)
            };
        }
        else
        {
            points = new[]
            {
                new Point(route.SourcePoint.X, departureY),
                new Point(route.TargetPoint.X, departureY)
            };
        }
        return route.AcceptGeometry(
            new[] { route.SourcePoint }.Concat(points).Concat(new[] { route.TargetPoint }),
            LogicalRouteStage.Allocated, nameof(ProjectInterLayerSlotCompiler));
    }

    private static LinkSegmentDemand Demand(
        CanonicalTopologyPlan plan,
        LinkLayout route,
        InterLayerId band,
        LinkSegmentRole role,
        int order,
        AxisInterval range,
        LayoutRevision revision,
        AxisInterval projectHorizontalSpan) => new(
            $"{plan.LogicalRouteId}:horizontal:{order}", plan.LogicalRouteId,
            LinkSegmentOrientation.Horizontal,
            plan.RequiresDestinationColumn || plan.RequiresReturnColumn
                ? projectHorizontalSpan : new AxisInterval(route.SourcePoint.X, route.TargetPoint.X),
            range, null, role,
            route.Link.Order, order,
            new MovementScopeIdentity(MovementScopeKind.LayerAndLowerSuffix,
                $"{(band.BandRole == InterLayerBandRole.ProjectInternal ? $"project:{band.ProjectId}" : "root-transition")}:depth:{band.LowerLayer}"),
            revision, new RouteRevision(0), null,
            route.SourcePoint.X <= route.TargetPoint.X
                ? LinkSegmentEndpointRole.Departure
                : LinkSegmentEndpointRole.Arrival,
            route.SourcePoint.X <= route.TargetPoint.X
                ? LinkSegmentEndpointRole.Arrival
                : LinkSegmentEndpointRole.Departure,
            band.ToString(), band.ProjectId,
            band.BandRole == InterLayerBandRole.ProjectInternal ? "ProjectInternal" : "RootTransition");

    private static LinkSegmentRole DepartureRole(CanonicalTopologyFamily family) => family switch
    {
        CanonicalTopologyFamily.AdjacentDownward => LinkSegmentRole.AdjacentDeparture,
        CanonicalTopologyFamily.LongDownward => LinkSegmentRole.LongDeparture,
        CanonicalTopologyFamily.SameLayerReturn or CanonicalTopologyFamily.UpwardReturn => LinkSegmentRole.ReturnDeparture,
        _ => LinkSegmentRole.BoundaryHorizontal
    };

    private static InterLayerId BandForDeparture(
        CanonicalTopologyPlan plan,
        IReadOnlyDictionary<string, NodeLayout> nodes,
        IReadOnlyDictionary<InterLayerId, AxisInterval> bands)
    {
        var source = nodes[plan.SourceNodeId];
        return ClosestBand(bands, source.Depth, ProjectId(plan, nodes), BandRole(plan, nodes));
    }

    private static InterLayerId BandForArrival(
        CanonicalTopologyPlan plan,
        IReadOnlyDictionary<string, NodeLayout> nodes,
        IReadOnlyDictionary<InterLayerId, AxisInterval> bands)
    {
        var target = nodes[plan.TargetNodeId];
        return ClosestBand(bands, Math.Max(-1, target.Depth - 1), ProjectId(plan, nodes), BandRole(plan, nodes));
    }

    private static InterLayerId ClosestBand(
        IReadOnlyDictionary<InterLayerId, AxisInterval> bands,
        int upper,
        string? projectId,
        InterLayerBandRole role) =>
        bands.Keys.Where(id => id.BandRole == role && string.Equals(id.ProjectId, projectId, StringComparison.Ordinal))
            .OrderBy(id => Math.Abs(id.UpperLayer - upper)).ThenBy(id => id.UpperLayer).First();

    private static IReadOnlyDictionary<InterLayerId, AxisInterval> Bands(
        IReadOnlyDictionary<string, NodeLayout> nodes,
        LayoutRevision revision,
        int padding,
        int separation,
        int routeCount)
    {
        var result = new Dictionary<InterLayerId, AxisInterval>();
        foreach (var project in nodes.Values.Where(item => item.Node.ProjectId is not null)
                     .GroupBy(item => item.Node.ProjectId!, StringComparer.Ordinal)
                     .OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            AddBands(result, project.GroupBy(item => item.Depth).ToDictionary(item => item.Key, item => item.ToArray()),
                revision, padding, separation, routeCount, project.Key, InterLayerBandRole.ProjectInternal);
        }
        var globalLayers = nodes.Values.Where(item => item.Node.ProjectId is not null).GroupBy(item => item.Depth)
            .ToDictionary(item => item.Key, item => item.ToArray());
        AddBands(result, globalLayers, revision, padding, separation, routeCount, null,
            InterLayerBandRole.RootTransition);
        return result;
    }

    private static void AddBands(
        IDictionary<InterLayerId, AxisInterval> result,
        IReadOnlyDictionary<int, NodeLayout[]> layers,
        LayoutRevision revision,
        int padding,
        int separation,
        int routeCount,
        string? projectId,
        InterLayerBandRole role)
    {
        foreach (var upper in layers.Keys.OrderBy(item => item))
        {
            var upperBottom = layers[upper].Max(item => item.Rect.Bottom);
            var lowerTop = layers.TryGetValue(upper + 1, out var lower)
                ? lower.Min(item => item.Rect.Y)
                : upperBottom + padding * 2 + Math.Max(1, routeCount) * separation;
            result[new InterLayerId(upper, upper + 1, revision, projectId, role)] =
                new AxisInterval(upperBottom, lowerTop);
        }
    }

    private static InterLayerBandRole BandRole(
        CanonicalTopologyPlan plan,
        IReadOnlyDictionary<string, NodeLayout> nodes) =>
        ProjectId(plan, nodes) is null ? InterLayerBandRole.RootTransition : InterLayerBandRole.ProjectInternal;

    private static string? ProjectId(
        CanonicalTopologyPlan plan,
        IReadOnlyDictionary<string, NodeLayout> nodes)
    {
        var sourceProject = nodes[plan.SourceNodeId].Node.ProjectId;
        var targetProject = nodes[plan.TargetNodeId].Node.ProjectId;
        var internalFamily = plan.Family is CanonicalTopologyFamily.AdjacentDownward or
            CanonicalTopologyFamily.LongDownward or CanonicalTopologyFamily.SameLayerReturn or
            CanonicalTopologyFamily.UpwardReturn;
        return internalFamily && sourceProject is not null &&
               string.Equals(sourceProject, targetProject, StringComparison.Ordinal)
            ? sourceProject
            : null;
    }

    private static AxisInterval HorizontalSpan(
        CanonicalTopologyPlan plan,
        IReadOnlyDictionary<string, NodeLayout> nodes,
        int padding,
        AxisInterval globalSpan)
    {
        var projectId = ProjectId(plan, nodes);
        if (projectId is null) return globalSpan;
        var projectNodes = nodes.Values.Where(item =>
            string.Equals(item.Node.ProjectId, projectId, StringComparison.Ordinal)).ToArray();
        return new AxisInterval(projectNodes.Min(item => item.Rect.X) - padding,
            projectNodes.Max(item => item.Rect.Right) + padding);
    }

    private static bool PositiveOverlap(AxisInterval first, AxisInterval second) =>
        Math.Min(first.Maximum, second.Maximum) > Math.Max(first.Minimum, second.Minimum);

    internal static IEnumerable<AxisInterval> FixedColumnExclusions(
        CanonicalTopologyPlan plan,
        IReadOnlyDictionary<string, CanonicalTopologyPlan> plans,
        IReadOnlyDictionary<string, LinkLayout> routes,
        IReadOnlyList<LinkSegmentDemand> demands,
        IReadOnlyDictionary<string, AssignedLinkSegment> assignments,
        AxisInterval verticalInterval,
        int separation)
    {
        foreach (var other in plans.Values.Where(item => item.LogicalRouteId != plan.LogicalRouteId)
                     .OrderBy(item => item.LogicalRouteId, StringComparer.Ordinal))
        {
            var route = routes[other.LogicalRouteId];
            var routeDemands = demands.Where(item => item.LogicalRouteId == other.LogicalRouteId)
                .OrderBy(item => item.TurnOrder).ToArray();
            var departureY = assignments[routeDemands[0].Id].AxisCoordinate;
            var sourceInterval = new AxisInterval(route.SourcePoint.Y, departureY);
            if (PositiveOverlap(verticalInterval, sourceInterval))
                yield return new AxisInterval(route.SourcePoint.X - separation, route.SourcePoint.X + separation);
            var arrivalY = other.RequiresDestinationColumn || other.RequiresReturnColumn
                ? assignments[routeDemands[1].Id].AxisCoordinate
                : departureY;
            var arrivalInterval = new AxisInterval(arrivalY, route.TargetPoint.Y);
            if (PositiveOverlap(verticalInterval, arrivalInterval))
                yield return new AxisInterval(route.TargetPoint.X - separation, route.TargetPoint.X + separation);
        }
    }
}
