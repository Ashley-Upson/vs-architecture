using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using StandardIo.ArchitectureDiagram.Core.Models;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal static class InterLayerDemandDiscovery
{
    public static InterLayerReport Observe(
        PlacedGraph placement,
        GeneratedLogicalRoutes routes,
        DiagramSettings settings,
        TraceabilityValidationResult? findings = null,
        CancellationToken cancellationToken = default,
        RouteRevision? expectedRouteRevision = null)
    {
        if (placement is null) throw new ArgumentNullException(nameof(placement));
        if (routes is null) throw new ArgumentNullException(nameof(routes));
        if (settings is null) throw new ArgumentNullException(nameof(settings));
        routes.EnsureCompatible(placement);
        if (expectedRouteRevision is { } expected && routes.Revision != expected)
        {
            throw new InvalidOperationException($"generated routes revision {routes.Revision.Value} does not match expected route revision {expected.Value}.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        var timer = Stopwatch.StartNew();
        var layers = placement.Nodes.Values.Where(node => !node.IsStandalone)
            .GroupBy(node => node.Depth)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var maximumLayer = layers.Keys.DefaultIfEmpty(0).Max();
        var builders = Enumerable.Range(0, maximumLayer)
            .Where(upper => layers.ContainsKey(upper) && layers.ContainsKey(upper + 1))
            .Select(upper => new BandBuilder(
                new InterLayerId(upper, upper + 1, placement.Revision),
                layers[upper].Max(node => node.Rect.Bottom),
                layers[upper + 1].Min(node => node.Rect.Y)))
            .ToDictionary(builder => builder.Id.UpperLayer);

        foreach (var link in routes.Links.Values.OrderBy(link => link.Link.Order).ThenBy(link => link.Link.Id, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!placement.Nodes.TryGetValue(link.Link.SourceId, out var source) ||
                !placement.Nodes.TryGetValue(link.Link.TargetId, out var target)) continue;
            var points = new[] { link.SourcePoint }.Concat(link.Points).Concat(new[] { link.TargetPoint }).ToArray();
            var touched = new List<(BandBuilder Band, int First, int Last)>();
            foreach (var band in builders.Values.OrderBy(item => item.Id.UpperLayer))
            {
                var indices = Enumerable.Range(0, points.Length - 1)
                    .Where(index => IntersectsVerticalRange(points[index], points[index + 1], band.UpperBoundary, band.LowerBoundary))
                    .ToArray();
                if (indices.Length == 0) continue;
                foreach (var group in Contiguous(indices))
                {
                    touched.Add((band, group.First(), group.Last()));
                }
            }

            var orderedTouched = touched.OrderBy(item => item.First).ThenBy(item => item.Band.Id.UpperLayer).ToArray();
            for (var membershipIndex = 0; membershipIndex < orderedTouched.Length; membershipIndex++)
            {
                var item = orderedTouched[membershipIndex];
                var role = Role(source.Depth, target.Depth, membershipIndex, orderedTouched.Length);
                var membershipId = $"{link.Link.Id}:band-{item.Band.Id.UpperLayer}:segments-{item.First}-{item.Last}";
                item.Band.Memberships.Add(new InterLayerLinkMembership(
                    membershipId, link.Link.Id, routes.Revision, item.Band.Id, item.First, item.Last, role));
                for (var segmentIndex = item.First; segmentIndex <= item.Last; segmentIndex++)
                {
                    var start = points[segmentIndex];
                    var end = points[segmentIndex + 1];
                    if (start.X != end.X && start.Y != end.Y)
                    {
                        item.Band.UnsupportedShapes.Add($"{link.Link.Id}:segment-{segmentIndex}:non-orthogonal");
                    }
                    if (start.Y != end.Y || start.Y < item.Band.UpperBoundary || start.Y > item.Band.LowerBoundary) continue;
                    item.Band.PendingDemands.Add(new PendingDemand(
                        $"{membershipId}:segment-{segmentIndex}", link.Link.Id, routes.Revision, item.Band.Id,
                        segmentIndex, role, Math.Min(start.X, end.X), Math.Max(start.X, end.X),
                        link.Link.Order, end.X >= start.X ? InterLayerLinkDirection.Right : InterLayerLinkDirection.Left));
                }
            }
        }

        long comparisons = 0;
        var observations = builders.Values.OrderBy(builder => builder.Id.UpperLayer)
            .Select(builder => builder.Build(settings.Layout.ParallelLaneSpacing, settings.Layout.LinkPadding, ref comparisons))
            .ToArray();
        var correlations = Correlate(findings, observations);
        timer.Stop();
        var bandsByEdge = observations.SelectMany(band => band.Memberships)
            .GroupBy(item => item.LogicalEdgeIdentity, StringComparer.Ordinal)
            .Select(group => group.Select(item => item.BandId).Distinct().Count())
            .DefaultIfEmpty(0).Max();
        var telemetry = new InterLayerTelemetry(
            placement.Revision, routes.Revision, placement.Nodes.Count, routes.Links.Count,
            routes.Links.Values.Sum(link => link.Points.Count + 1), observations.Length,
            observations.Sum(band => band.Memberships.Count), bandsByEdge,
            observations.Sum(band => band.Demands.Count), observations.Sum(band => band.OverlapGroupCount),
            observations.Select(band => band.MaximumSimultaneousOverlap).DefaultIfEmpty(0).Max(),
            observations.Sum(band => band.Demands.Count(demand => demand.Role == InterLayerMembershipRole.Return)),
            observations.Sum(band => band.UnsupportedShapes.Count), comparisons,
            (long)(timer.ElapsedTicks * 1_000_000d / Stopwatch.Frequency));
        PerformanceAudit.Increment("inter-layer bands observed", telemetry.BandCount);
        PerformanceAudit.Increment("inter-layer band demands", telemetry.HorizontalDemandCount);
        PerformanceAudit.Increment("inter-layer interval comparisons", comparisons);
        return new InterLayerReport(observations, correlations, telemetry);
    }

    private static bool IntersectsVerticalRange(Point start, Point end, int upper, int lower) =>
        Math.Max(Math.Min(start.Y, end.Y), upper) <= Math.Min(Math.Max(start.Y, end.Y), lower);

    private static IEnumerable<int[]> Contiguous(int[] indices)
    {
        var current = new List<int>();
        foreach (var index in indices)
        {
            if (current.Count > 0 && index != current[current.Count - 1] + 1)
            {
                yield return current.ToArray();
                current.Clear();
            }
            current.Add(index);
        }
        if (current.Count > 0) yield return current.ToArray();
    }

    private static InterLayerMembershipRole Role(int sourceLayer, int targetLayer, int index, int count)
    {
        if (targetLayer < sourceLayer) return InterLayerMembershipRole.Return;
        if (index == 0) return InterLayerMembershipRole.SourceTransition;
        if (index == count - 1) return InterLayerMembershipRole.TargetTransition;
        return InterLayerMembershipRole.Through;
    }

    private static IReadOnlyList<InterLayerFindingCorrelation> Correlate(
        TraceabilityValidationResult? findings,
        IReadOnlyList<InterLayerObservation> bands)
    {
        if (findings is null) return Array.Empty<InterLayerFindingCorrelation>();
        return findings.Violations.Select(finding =>
        {
            var relevant = bands.Where(band => band.Memberships.Any(membership =>
                membership.LogicalEdgeIdentity == finding.EdgeId || membership.LogicalEdgeIdentity == finding.OtherEdgeId)).ToArray();
            var underSized = relevant.Any(band => band.MissingExtent > 0);
            return new InterLayerFindingCorrelation(
                finding.Code, finding.EdgeId, finding.OtherEdgeId, relevant.Select(band => band.Id).ToArray(),
                relevant.SelectMany(band => band.Demands).Where(demand =>
                    demand.LogicalEdgeIdentity == finding.EdgeId || demand.LogicalEdgeIdentity == finding.OtherEdgeId)
                    .Select(demand => demand.Id).ToArray(),
                relevant.Length == 0 ? null : underSized,
                relevant.Length == 0 ? "No observed inter-layer demand is associated with the finding." :
                underSized ? "At least one associated band has missing required extent." : "Associated bands have no missing extent; band expansion alone is unlikely to resolve it.");
        }).ToArray();
    }

    private sealed class BandBuilder
    {
        public BandBuilder(InterLayerId id, int upperBoundary, int lowerBoundary)
        { Id = id; UpperBoundary = upperBoundary; LowerBoundary = lowerBoundary; }
        public InterLayerId Id { get; }
        public int UpperBoundary { get; }
        public int LowerBoundary { get; }
        public List<InterLayerLinkMembership> Memberships { get; } = new();
        public List<PendingDemand> PendingDemands { get; } = new();
        public List<string> UnsupportedShapes { get; } = new();

        public InterLayerObservation Build(int clearance, int padding, ref long comparisons)
        {
            var demands = new List<InterLayerLinkDemand>();
            var maximumOverlap = 0;
            var overlapGroups = 0;
            var laneCount = 0;
            var ordinaryLaneCount = 0;
            var returnLaneCount = 0;
            foreach (var roleGroup in PendingDemands.GroupBy(item => item.Role).OrderBy(group => group.Key))
            {
                var pending = roleGroup.ToArray();
                var commonDemands = pending.Select(item => item.ToLinkSegmentDemand(UpperBoundary, LowerBoundary)).ToArray();
                var movement = new MovementScopeIdentity(MovementScopeKind.LayerAndLowerSuffix, $"depth:{Id.LowerLayer}");
                var common = DeterministicSlotAllocator.Assign(
                    new LinkSegmentAllocationRegionIdentity(LinkSegmentOrientation.Horizontal,
                        new AxisInterval(UpperBoundary, LowerBoundary), $"{Id}:{roleGroup.Key}", movement, Id.LayoutRevision),
                    commonDemands, new LinkSegmentAssignmentOptions(clearance, padding));
                comparisons += common.ConflictComparisons;
                overlapGroups += common.Components.Count;
                var createdSlots = common.SegmentsByDemandId.Values.Select(item => item.SlotIndex).DefaultIfEmpty(-1).Max() + 1;
                maximumOverlap = Math.Max(maximumOverlap, createdSlots);
                foreach (var demand in pending)
                    demands.Add(demand.ToDemand(common.SegmentsByDemandId[demand.Id].SlotIndex));
                laneCount = Math.Max(laneCount, createdSlots);
                if (roleGroup.Key == InterLayerMembershipRole.Return) returnLaneCount = createdSlots;
                else ordinaryLaneCount = Math.Max(ordinaryLaneCount, createdSlots);
            }
            var current = Math.Max(0, LowerBoundary - UpperBoundary);
            var ordinary = demands.Where(item => item.Role != InterLayerMembershipRole.Return).ToArray();
            var returns = demands.Where(item => item.Role == InterLayerMembershipRole.Return).ToArray();
            var returnRegions = returns.Select(item => new InterLayerReturnRegionObservation(
                item.Id, item.LogicalEdgeIdentity, item.Direction == InterLayerLinkDirection.Left ? "left" : "right",
                item.XStart, item.XEnd, ordinary.Any(other =>
                    item.XStart < other.XEnd + clearance && other.XStart < item.XEnd + clearance))).ToArray();
            var effectiveLanes = returnRegions.Any(item => item.ConflictsWithDownwardTraffic)
                ? ordinaryLaneCount + returnLaneCount
                : Math.Max(ordinaryLaneCount, returnLaneCount);
            var required = PendingDemands.Count == 0 ? current : padding * 2 + Math.Max(1, effectiveLanes) * clearance;
            return new InterLayerObservation(Id, UpperBoundary, LowerBoundary, current, required,
                Math.Max(0, required - current), Memberships.OrderBy(item => item.Id, StringComparer.Ordinal).ToArray(),
                demands.OrderBy(item => item.Id, StringComparer.Ordinal).ToArray(), overlapGroups,
                maximumOverlap, Math.Max(laneCount, effectiveLanes), returnLaneCount, returnRegions,
                UnsupportedShapes.OrderBy(item => item, StringComparer.Ordinal).ToArray());
        }
    }

    private sealed record PendingDemand(string Id, string EdgeId, RouteRevision Revision, InterLayerId BandId,
        int SegmentIndex, InterLayerMembershipRole Role, int XStart, int XEnd, int ConnectionOrder, InterLayerLinkDirection Direction)
    {
        public InterLayerLinkDemand ToDemand(int lane) => new(Id, EdgeId, Revision, BandId, SegmentIndex, Role,
            XStart, XEnd, ConnectionOrder, Direction, lane);

        public LinkSegmentDemand ToLinkSegmentDemand(int upperBoundary, int lowerBoundary) => new(
            Id, EdgeId, LinkSegmentOrientation.Horizontal, new AxisInterval(XStart, XEnd),
            new AxisInterval(upperBoundary, lowerBoundary), null,
            Role == InterLayerMembershipRole.Return ? LinkSegmentRole.Return : LinkSegmentRole.Through,
            ConnectionOrder, SegmentIndex,
            new MovementScopeIdentity(MovementScopeKind.LayerAndLowerSuffix, $"depth:{BandId.LowerLayer}"),
            BandId.LayoutRevision, Revision);
    }
}
