using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal static class AdjacentDownwardLinkDemandDiscovery
{
    public static AdjacentDownwardObservationReport Observe(IEnumerable<AdjacentDownwardLinkContext> contexts)
    {
        var demandTicks = 0L;
        var adaptationTicks = 0L;
        var reconstructionTicks = 0L;
        var parityTicks = 0L;
        var observations = new List<AdjacentDownwardLinkObservation>();
        foreach (var context in contexts.OrderBy(item => item.Route.Link.Id, StringComparer.Ordinal))
        {
            var demandStarted = Stopwatch.GetTimestamp();
            var rejection = Rejection(context, out var authoritative);
            if (rejection is not null)
            {
                demandTicks += Stopwatch.GetTimestamp() - demandStarted;
                observations.Add(Rejected(context.Route.Link.Id, rejection.Value, authoritative));
                continue;
            }

            var interLayerMembership = context.InterLayerMemberships
                .OrderBy(item => item.FirstSegmentIndex)
                .ThenBy(item => item.LastSegmentIndex)
                .ThenBy(item => item.Id, StringComparer.Ordinal)
                .First();
            var throughSegment = authoritative[1] == authoritative[2]
                ? new Segment(authoritative[1], authoritative[1])
                : new Segment(authoritative[1], authoritative[2]);
            var demands = Demands(context, interLayerMembership, authoritative);
            demandTicks += Stopwatch.GetTimestamp() - demandStarted;

            var adaptationStarted = Stopwatch.GetTimestamp();
            var mappings = ExistingMappings(context, interLayerMembership, throughSegment, demands[1]);
            var selected = SelectAssignedLinkSegments(context, demands, mappings, authoritative);
            adaptationTicks += Stopwatch.GetTimestamp() - adaptationStarted;

            var reconstructionStarted = Stopwatch.GetTimestamp();
            var transitions = Transitions(context, selected, authoritative);
            var reconstructed = Reconstruct(context.Route.SourcePoint, context.Route.TargetPoint, selected, transitions);
            reconstructionTicks += Stopwatch.GetTimestamp() - reconstructionStarted;

            var parityStarted = Stopwatch.GetTimestamp();
            var parity = reconstructed.Count == 0
                ? ObservationalLinkPathParity.UnableToMap
                : reconstructed.SequenceEqual(authoritative)
                    ? ObservationalLinkPathParity.ExactPointParity
                    : SameTopology(reconstructed, authoritative)
                        ? ObservationalLinkPathParity.TopologyParityCoordinateDifference
                        : ObservationalLinkPathParity.UnableToMap;
            parityTicks += Stopwatch.GetTimestamp() - parityStarted;
            observations.Add(new AdjacentDownwardLinkObservation(
                context.Route.Link.Id, true, null, demands, mappings, selected, transitions,
                reconstructed, parity, authoritative,
                parity == ObservationalLinkPathParity.ExactPointParity
                    ? Array.Empty<string>()
                    : new[] { "Common reconstruction does not exactly match the canonical authoritative route." }));
        }

        return new AdjacentDownwardObservationReport(
            observations,
            Microseconds(demandTicks),
            Microseconds(adaptationTicks),
            Microseconds(reconstructionTicks),
            Microseconds(parityTicks));
    }

    private static AdjacentDownwardRejectionReason? Rejection(
        AdjacentDownwardLinkContext context,
        out IReadOnlyList<Point> canonical)
    {
        canonical = Normalize(CompletePoints(context.Route));
        if (context.ExposureTreeSpecific) return AdjacentDownwardRejectionReason.ExposureTreeSpecific;
        if (context.InterLayerMemberships.Any(item => item.RouteRevision != context.RouteRevision ||
                item.InterLayerId.LayoutRevision != context.LayoutRevision) ||
            context.InterLayerDemands.Any(item => item.RouteRevision != context.RouteRevision ||
                item.InterLayerId.LayoutRevision != context.LayoutRevision))
            return AdjacentDownwardRejectionReason.RevisionMismatch;
        if (canonical.Zip(canonical.Skip(1), (a, b) => new Segment(a, b)).Any(item => !item.IsOrthogonal))
            return AdjacentDownwardRejectionReason.NonOrthogonal;
        if (context.Target.Depth == context.Source.Depth) return AdjacentDownwardRejectionReason.SameLayer;
        if (context.Target.Depth < context.Source.Depth) return AdjacentDownwardRejectionReason.UpwardOrReturn;
        if (context.Target.Depth > context.Source.Depth + 1) return AdjacentDownwardRejectionReason.SkippedLayer;
        if (!string.Equals(context.Source.Node.ProjectId, context.Target.Node.ProjectId, StringComparison.Ordinal))
            return AdjacentDownwardRejectionReason.CrossProject;
        if (context.InterLayerMemberships.Select(item => item.InterLayerId).Distinct().Count() != 1)
            return AdjacentDownwardRejectionReason.MultipleInterLayer;
        if (context.Route.ExitY != 1 || context.Route.EntryY != 0 ||
            context.Route.SourcePoint.Y != context.Source.Rect.Bottom ||
            context.Route.TargetPoint.Y != context.Target.Rect.Y ||
            canonical.Count != 4 ||
            canonical[0].X != canonical[1].X ||
            canonical[1].Y != canonical[2].Y ||
            canonical[2].X != canonical[3].X)
            return AdjacentDownwardRejectionReason.UnsupportedConnectionTopology;
        return null;
    }

    private static IReadOnlyList<LinkSegmentDemand> Demands(
        AdjacentDownwardLinkContext context,
        InterLayerLinkMembership membership,
        IReadOnlyList<Point> points)
    {
        return DownwardLinkSegmentDemandFactory.Create(context, new[] { membership.InterLayerId }).SegmentDemands;
    }

    private static IReadOnlyList<ExistingSegmentMapping> ExistingMappings(
        AdjacentDownwardLinkContext context,
        InterLayerLinkMembership membership,
        Segment through,
        LinkSegmentDemand demand)
    {
        var result = new List<ExistingSegmentMapping>();
        foreach (var mapping in context.Corridors.SegmentMappings.Where(item =>
                     item.EdgeId == context.Route.Link.Id && item.Segment.IsHorizontal &&
                     item.Segment.OverlapLength(through) == through.Length))
        {
            if (!context.CorridorLanes.TryGetLane(mapping.CorridorId, context.Route.Link.Id, out var slot)) continue;
            var corridor = context.Corridors.Corridors[mapping.CorridorId];
            result.Add(Mapping(ExistingSegmentMappingSource.LegacyCorridor, demand, slot.LaneIndex, through.Start.Y,
                ("corridorId", corridor.Id), ("role", corridor.Role.ToString()),
                ("regionKey", corridor.RegionKey), ("obstacleBoundaryKey", corridor.ObstacleBoundaryKey)));
        }

        foreach (var interLayerDemand in context.InterLayerDemands.Where(item => item.InterLayerId == membership.InterLayerId))
            result.Add(Mapping(ExistingSegmentMappingSource.InterLayerObservation, demand, interLayerDemand.SlotIndex, through.Start.Y,
                ("interLayerId", interLayerDemand.InterLayerId.ToString()), ("direction", interLayerDemand.Direction.ToString()),
                ("membershipRole", interLayerDemand.Role.ToString())));

        if (context.GroupedPlan is not null)
        {
            foreach (var group in context.GroupedPlan.Groups.Where(item => item.InterLayerId == membership.InterLayerId))
            foreach (var groupDemand in group.Demands.Where(item => item.LogicalEdgeIdentity == context.Route.Link.Id))
                result.Add(Mapping(ExistingSegmentMappingSource.InterLayerSpacingConstraint, demand,
                    group.AssignedSlots[groupDemand.Id], through.Start.Y,
                    ("groupId", group.Id), ("movementScope", group.MovementScope.ToString()),
                    ("currentExtent", group.CurrentExtent.ToString()), ("requiredExtent", group.RequiredExtent.ToString())));
        }

        return result.OrderBy(item => item.Source).ThenBy(item => item.Segment.SlotIndex)
            .ThenBy(item => item.Segment.Id, StringComparer.Ordinal).ToArray();
    }

    private static ExistingSegmentMapping Mapping(
        ExistingSegmentMappingSource source,
        LinkSegmentDemand demand,
        int slotIndex,
        int axis,
        params (string Key, string Value)[] metadata) =>
        new(source, new AssignedLinkSegment(
            $"{demand.LogicalRouteId}:assigned:through:{source}", demand.Id, demand.LogicalRouteId,
            demand.Orientation, axis, slotIndex, demand.OccupiedInterval, demand.Role,
            demand.PlacementRevision, demand.RouteRevision),
            metadata.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal));

    private static IReadOnlyList<AssignedLinkSegment> SelectAssignedLinkSegments(
        AdjacentDownwardLinkContext context,
        IReadOnlyList<LinkSegmentDemand> demands,
        IReadOnlyList<ExistingSegmentMapping> mappings,
        IReadOnlyList<Point> points)
    {
        var through = mappings.OrderBy(item => item.Source).Select(item => item.Segment).FirstOrDefault();
        if (through is null) return Array.Empty<AssignedLinkSegment>();
        return new[]
        {
            AssignedConnection(context, demands[0], points[0].X, 0),
            through,
            AssignedConnection(context, demands[2], points[3].X, 0)
        };
    }

    private static AssignedLinkSegment AssignedConnection(AdjacentDownwardLinkContext context, LinkSegmentDemand demand, int axis, int slot) =>
        new($"{context.Route.Link.Id}:assigned:{demand.Role}", demand.Id, context.Route.Link.Id,
            demand.Orientation, axis, slot, demand.OccupiedInterval, demand.Role,
            context.LayoutRevision, context.RouteRevision);

    private static IReadOnlyList<LinkTransition> Transitions(
        AdjacentDownwardLinkContext context,
        IReadOnlyList<AssignedLinkSegment> segments,
        IReadOnlyList<Point> points)
    {
        if (segments.Count != 3) return Array.Empty<LinkTransition>();
        return new[]
        {
            new LinkTransition($"{context.Route.Link.Id}:transition:0", context.Route.Link.Id,
                segments[0].Id, segments[1].Id, points[1], 0, context.LayoutRevision, context.RouteRevision),
            new LinkTransition($"{context.Route.Link.Id}:transition:1", context.Route.Link.Id,
                segments[1].Id, segments[2].Id, points[2], 1, context.LayoutRevision, context.RouteRevision)
        };
    }

    internal static IReadOnlyList<Point> Reconstruct(
        Point source,
        Point target,
        IReadOnlyList<AssignedLinkSegment> segments,
        IReadOnlyList<LinkTransition> transitions)
    {
        if (segments.Count != 3 || transitions.Count != 2) return Array.Empty<Point>();
        var byId = segments.ToDictionary(item => item.Id, StringComparer.Ordinal);
        foreach (var transition in transitions.OrderBy(item => item.Order))
        {
            if (!byId.TryGetValue(transition.FromAssignedLinkSegmentId, out var from) ||
                !byId.TryGetValue(transition.ToAssignedLinkSegmentId, out var to) ||
                !OnSegment(transition.Turn, from) || !OnSegment(transition.Turn, to))
                return Array.Empty<Point>();
        }
        return Normalize(new[] { source }.Concat(transitions.OrderBy(item => item.Order).Select(item => item.Turn)).Concat(new[] { target }));
    }

    private static bool OnSegment(Point point, AssignedLinkSegment segment) => segment.Orientation == LinkSegmentOrientation.Horizontal
        ? point.Y == segment.AxisCoordinate && segment.OccupiedInterval.ContainsClosed(point.X)
        : point.X == segment.AxisCoordinate && segment.OccupiedInterval.ContainsClosed(point.Y);

    private static AdjacentDownwardLinkObservation Rejected(
        string routeId,
        AdjacentDownwardRejectionReason reason,
        IReadOnlyList<Point> authoritative) =>
        new(routeId, false, reason, Array.Empty<LinkSegmentDemand>(), Array.Empty<ExistingSegmentMapping>(),
            Array.Empty<AssignedLinkSegment>(), Array.Empty<LinkTransition>(), Array.Empty<Point>(),
            ObservationalLinkPathParity.UnableToMap, authoritative, new[] { reason.ToString() });

    private static IReadOnlyList<Point> CompletePoints(LinkLayout route) =>
        new[] { route.SourcePoint }.Concat(route.Points).Concat(new[] { route.TargetPoint }).ToArray();

    internal static IReadOnlyList<Point> Normalize(IEnumerable<Point> points)
    {
        var result = new List<Point>();
        foreach (var point in points)
        {
            if (result.Count > 0 && result[result.Count - 1] == point) continue;
            while (result.Count >= 2 && Collinear(result[result.Count - 2], result[result.Count - 1], point))
                result.RemoveAt(result.Count - 1);
            result.Add(point);
        }
        return result;
    }

    private static bool SameTopology(IReadOnlyList<Point> first, IReadOnlyList<Point> second) =>
        Directions(first).SequenceEqual(Directions(second));

    private static IEnumerable<LinkSegmentOrientation> Directions(IReadOnlyList<Point> points) =>
        points.Zip(points.Skip(1), (a, b) => a.Y == b.Y ? LinkSegmentOrientation.Horizontal : LinkSegmentOrientation.Vertical);

    private static bool Collinear(Point a, Point b, Point c) =>
        a.X == b.X && b.X == c.X || a.Y == b.Y && b.Y == c.Y;

    private static long Microseconds(long ticks) => ticks * 1_000_000 / Stopwatch.Frequency;
}
