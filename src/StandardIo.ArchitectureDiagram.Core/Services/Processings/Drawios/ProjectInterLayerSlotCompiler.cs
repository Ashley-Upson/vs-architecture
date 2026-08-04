using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core.Models;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal static class ProjectInterLayerSlotCompiler
{
    private const int MaximumRefinementIterations = 4;

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
        var horizontal = AllocateHorizontal(demands, bands, plans, nodes, projectLabels, revision,
            separation, padding, preservedRootAssignments);
        var assignments = horizontal.Assignments;
        var requiredExpansion = horizontal.RequiredExpansion;
        var requiredExtentByBand = horizontal.RequiredExtentByBand;

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
                if (IsDirectTargetLaneReturn(item, nodes))
                    return route.TargetPoint.X < route.SourcePoint.X ? "Left" :
                        route.TargetPoint.X > route.SourcePoint.X ? "Right" : "Left";
                var leftCost = route.SourcePoint.X - minimumX + route.TargetPoint.X - minimumX;
                var rightCost = maximumX - route.SourcePoint.X + maximumX - route.TargetPoint.X;
                return leftCost <= rightCost ? "Left" : "Right";
            }, StringComparer.Ordinal);
        var verticalColumns = AllocateVertical(plans, nodes, terminalLayouts, projectLabels, demands, assignments,
            returnOrder, returnSides, minimumX, maximumX, revision, separation, padding);
        var refinementIterations = 0;
        var refinementFallbackUsed = false;
        for (var iteration = 1; iteration <= MaximumRefinementIterations; iteration++)
        {
            var refinedDemands = ActualSpanDemands(demands, plans, terminalLayouts, verticalColumns);
            try
            {
                var refinedHorizontal = AllocateHorizontal(refinedDemands, bands, plans, nodes, projectLabels,
                    revision, separation, padding, preservedRootAssignments);
                var refinedColumns = AllocateVertical(plans, nodes, terminalLayouts, projectLabels, refinedDemands,
                    refinedHorizontal.Assignments, returnOrder, returnSides, minimumX, maximumX, revision,
                    separation, padding);
                refinementIterations = iteration;
                var stable = SameAssignments(assignments, refinedHorizontal.Assignments) &&
                    SameColumns(verticalColumns, refinedColumns);
                demands = refinedDemands.ToList();
                assignments = refinedHorizontal.Assignments;
                requiredExpansion = refinedHorizontal.RequiredExpansion;
                requiredExtentByBand = refinedHorizontal.RequiredExtentByBand;
                verticalColumns = refinedColumns;
                if (stable) break;
                if (iteration == MaximumRefinementIterations) refinementFallbackUsed = true;
            }
            catch (InvalidOperationException)
            {
                refinementFallbackUsed = true;
                break;
            }
        }
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
            requiredExtentByBand,
            bands.Count, requiredExpansion.Count, refinementIterations, refinementFallbackUsed, timings);
    }

    private static HorizontalAllocation AllocateHorizontal(
        IReadOnlyList<LinkSegmentDemand> demands,
        IReadOnlyDictionary<InterLayerId, AxisInterval> bands,
        IReadOnlyDictionary<string, CanonicalTopologyPlan> plans,
        IReadOnlyDictionary<string, NodeLayout> nodes,
        IReadOnlyDictionary<string, ProjectLabelGeometry> projectLabels,
        LayoutRevision revision,
        int separation,
        int padding,
        IReadOnlyDictionary<string, AssignedLinkSegment> preservedRootAssignments)
    {
        var assignments = new Dictionary<string, AssignedLinkSegment>(StringComparer.Ordinal);
        var requiredExpansion = new Dictionary<ProjectLayerExpansionIdentity, int>();
        var requiredExtentByBand = bands.Keys
            .Where(band => band.BandRole == InterLayerBandRole.ProjectInternal && band.ProjectId is not null)
            .ToDictionary(
                band => new ProjectLayerExpansionIdentity(band.ProjectId!, band.LowerLayer),
                _ => 0);
        foreach (var group in demands.GroupBy(item =>
                     $"{item.MovementScope?.Id}:{item.AllowedAxisRange.Minimum}:{item.AllowedAxisRange.Maximum}",
                     StringComparer.Ordinal).OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            var sample = group.First();
            var allowedRange = sample.AllowedAxisRange;
            var identity = new LinkSegmentAllocationRegionIdentity(
                LinkSegmentOrientation.Horizontal, allowedRange,
                $"project-interLayer:{group.Key}", sample.MovementScope, revision);
            var assigned = DeterministicSlotAllocator.Assign(identity, group,
                new LinkSegmentAssignmentOptions(separation, padding));
            var selected = string.Equals(sample.DemandCategory, "ProjectInternal", StringComparison.Ordinal)
                ? ConstrainProjectAssignments(group.ToArray(), assigned.SegmentsByDemandId, plans, nodes,
                    projectLabels, separation, padding)
                : assigned.SegmentsByDemandId;
            foreach (var item in selected)
                assignments.Add(item.Key, string.Equals(sample.DemandCategory, "RootTransition", StringComparison.Ordinal)
                    ? preservedRootAssignments[item.Key] : item.Value);
            var requiredExtent = selected.Values.Select(item => item.SlotIndex).DefaultIfEmpty(0).Max() * separation +
                separation + padding * 2;
            var finalRequiredExtent = Math.Max(assigned.RequiredExtent, requiredExtent);
            var missing = Math.Max(0, finalRequiredExtent - allowedRange.Length);
            if (sample.CoordinateFrameId is null ||
                !string.Equals(sample.DemandCategory, "ProjectInternal", StringComparison.Ordinal)) continue;
            var band = bands.Keys.Single(item => string.Equals(item.ToString(), sample.BandId, StringComparison.Ordinal));
            var expansionId = new ProjectLayerExpansionIdentity(sample.CoordinateFrameId, band.LowerLayer);
            requiredExtentByBand[expansionId] = Math.Max(requiredExtentByBand[expansionId], finalRequiredExtent);
            if (missing <= 0) continue;
            requiredExpansion[expansionId] = Math.Max(
                requiredExpansion.TryGetValue(expansionId, out var existing) ? existing : 0, missing);
        }
        return new HorizontalAllocation(assignments, requiredExpansion, requiredExtentByBand);
    }

    private static VerticalLinkColumnAssignment AllocateVertical(
        IReadOnlyDictionary<string, CanonicalTopologyPlan> plans,
        IReadOnlyDictionary<string, NodeLayout> nodes,
        IReadOnlyDictionary<string, LinkLayout> routes,
        IReadOnlyDictionary<string, ProjectLabelGeometry> labels,
        IReadOnlyList<LinkSegmentDemand> demands,
        IReadOnlyDictionary<string, AssignedLinkSegment> assignments,
        IReadOnlyDictionary<string, int> returnOrder,
        IReadOnlyDictionary<string, string> returnSides,
        int minimumX,
        int maximumX,
        LayoutRevision revision,
        int separation,
        int padding)
    {
        var verticalDemands = plans.Values.Where(item => item.RequiresDestinationColumn || item.RequiresReturnColumn)
            .OrderBy(item => item.LogicalRouteId, StringComparer.Ordinal).Select(plan =>
            {
                var route = routes[plan.LogicalRouteId];
                var routeDemands = demands.Where(item => item.LogicalRouteId == plan.LogicalRouteId)
                    .OrderBy(item => item.TurnOrder).ToArray();
                var departureY = assignments[routeDemands[0].Id].AxisCoordinate;
                var arrivalY = assignments[routeDemands[1].Id].AxisCoordinate;
                var interval = new AxisInterval(departureY, arrivalY);
                if (plan.RequiresReturnColumn)
                {
                    if (IsDirectTargetLaneReturn(plan, nodes))
                    {
                        var source = nodes[plan.SourceNodeId];
                        var directLeft = returnSides[plan.LogicalRouteId] == "Left";
                        var directPreferred = directLeft
                            ? source.Rect.X - padding - separation
                            : source.Rect.Right + padding + separation;
                        var directAllowed = directLeft
                            ? new AxisInterval(minimumX - padding - separation * plans.Count, directPreferred)
                            : new AxisInterval(directPreferred, maximumX + padding + separation * plans.Count);
                        var directForbidden = VerticalColumnExclusions(
                            plan, plans, nodes, routes, labels, demands, assignments, interval, separation, padding);
                        return new VerticalLinkColumnDemand(
                            $"{plan.LogicalRouteId}:return-column", plan.LogicalRouteId, directPreferred,
                            directAllowed, source.Depth, nodes[plan.TargetNodeId].Depth, interval, padding,
                            plan.SourceNodeId, plan.TargetNodeId, source.Node.ProjectId, null,
                            revision, new RouteRevision(0), directForbidden);
                    }
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
                var forbidden = VerticalColumnExclusions(
                    plan, plans, nodes, routes, labels, demands, assignments, interval, separation, padding);
                return new VerticalLinkColumnDemand(
                    $"{plan.LogicalRouteId}:destination-column", plan.LogicalRouteId, route.TargetPoint.X,
                    allowed, nodes[plan.SourceNodeId].Depth, nodes[plan.TargetNodeId].Depth, interval, padding,
                    plan.SourceNodeId, plan.TargetNodeId, nodes[plan.TargetNodeId].Node.ProjectId, null,
                    revision, new RouteRevision(0), forbidden);
            }).ToArray();
        return VerticalLinkColumnAllocator.Assign(verticalDemands, separation);
    }

    private static IReadOnlyList<LinkSegmentDemand> ActualSpanDemands(
        IReadOnlyList<LinkSegmentDemand> source,
        IReadOnlyDictionary<string, CanonicalTopologyPlan> plans,
        IReadOnlyDictionary<string, LinkLayout> routes,
        VerticalLinkColumnAssignment columns)
    {
        return source.Select(demand =>
        {
            var plan = plans[demand.LogicalRouteId];
            if (!plan.RequiresDestinationColumn && !plan.RequiresReturnColumn) return demand;
            var columnId = plan.RequiresReturnColumn
                ? $"{plan.LogicalRouteId}:return-column"
                : $"{plan.LogicalRouteId}:destination-column";
            var columnX = columns.ColumnsByDemandId[columnId].X;
            var route = routes[plan.LogicalRouteId];
            var interval = demand.TurnOrder == 0
                ? new AxisInterval(route.SourcePoint.X, columnX)
                : new AxisInterval(columnX, route.TargetPoint.X);
            return demand with { OccupiedInterval = interval };
        }).ToArray();
    }

    private static bool SameAssignments(
        IReadOnlyDictionary<string, AssignedLinkSegment> left,
        IReadOnlyDictionary<string, AssignedLinkSegment> right) =>
        left.Count == right.Count && left.All(item => right.TryGetValue(item.Key, out var value) &&
            item.Value.AxisCoordinate == value.AxisCoordinate && item.Value.SlotIndex == value.SlotIndex);

    private static bool SameColumns(VerticalLinkColumnAssignment left, VerticalLinkColumnAssignment right) =>
        left.ColumnsByDemandId.Count == right.ColumnsByDemandId.Count && left.ColumnsByDemandId.All(item =>
            right.ColumnsByDemandId.TryGetValue(item.Key, out var value) && item.Value.X == value.X);

    private sealed record HorizontalAllocation(
        Dictionary<string, AssignedLinkSegment> Assignments,
        Dictionary<ProjectLayerExpansionIdentity, int> RequiredExpansion,
        Dictionary<ProjectLayerExpansionIdentity, int> RequiredExtentByBand);

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

    internal static bool IsDirectTargetLaneReturn(
        CanonicalTopologyPlan plan,
        IReadOnlyDictionary<string, NodeLayout> nodes)
    {
        // A return link's physical route should be chosen from its endpoint geometry,
        // not from the number of semantic layers between those endpoints. A deep
        // return can still have a clear local lane; sending it to the project edge
        // creates the large detours seen when baseline-aligned nodes have different
        // semantic depths.
        return plan.RequiresReturnColumn &&
            nodes[plan.SourceNodeId].Node.ProjectId == nodes[plan.TargetNodeId].Node.ProjectId;
    }

    private static AxisInterval[] VerticalColumnExclusions(
        CanonicalTopologyPlan plan,
        IReadOnlyDictionary<string, CanonicalTopologyPlan> plans,
        IReadOnlyDictionary<string, NodeLayout> nodes,
        IReadOnlyDictionary<string, LinkLayout> routes,
        IReadOnlyDictionary<string, ProjectLabelGeometry> labels,
        IReadOnlyList<LinkSegmentDemand> demands,
        IReadOnlyDictionary<string, AssignedLinkSegment> assignments,
        AxisInterval interval,
        int separation,
        int padding) =>
        nodes.Values.Where(node => node.Node.Id != plan.SourceNodeId && node.Node.Id != plan.TargetNodeId &&
                PositiveOverlap(interval, new AxisInterval(node.Rect.Y - padding, node.Rect.Bottom + padding)))
            .Select(node => new AxisInterval(node.Rect.X - padding, node.Rect.Right + padding))
            .Concat(labels.Values.Where(label => PositiveOverlap(interval,
                    new AxisInterval(label.ProjectLabelObstacleBounds.Y, label.ProjectLabelObstacleBounds.Bottom)))
                .Select(label => new AxisInterval(label.ProjectLabelObstacleBounds.X,
                    label.ProjectLabelObstacleBounds.Right)))
            .Concat(FixedColumnExclusions(plan, plans, routes, demands, assignments, interval, separation))
            .ToArray();

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
