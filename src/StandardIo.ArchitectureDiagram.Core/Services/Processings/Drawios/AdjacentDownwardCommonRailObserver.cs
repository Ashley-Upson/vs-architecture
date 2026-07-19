using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal static class AdjacentDownwardCommonRailObserver
{
    public static CommonRailParityReport Observe(AdjacentDownwardObservationReport source, int separation, int padding)
    {
        var eligible = source.Routes.Where(item => item.Eligible).OrderBy(item => item.LogicalRouteId, StringComparer.Ordinal).ToArray();
        var through = eligible.Select(item => item.Demands.Single(demand => demand.Role == LinkSegmentRole.Through)).ToArray();
        var assignmentTimer = Stopwatch.StartNew();
        var regions = through.GroupBy(RegionKey).OrderBy(item => item.Key, StringComparer.Ordinal).Select(group =>
        {
            var sample = group.First();
            var region = new LinkSegmentAllocationRegionIdentity(sample.Orientation, sample.AllowedAxisRange,
                group.Key, sample.MovementScope, sample.PlacementRevision);
            return new CommonRailRegionObservation(region,
                DeterministicSlotAllocator.Assign(region, group, new LinkSegmentAssignmentOptions(separation, padding)), null);
        }).ToArray();
        assignmentTimer.Stop();

        var constraintTimer = Stopwatch.StartNew();
        regions = regions.Select(item => item with
        {
            ConstraintProposal = item.Assignment.RequiredExtent <= item.Region.AllowedAxisRange.Length ||
                item.Region.MovementScope is null
                ? null
                : new GenerationConstraint(
                    new GenerationConstraintKey(item.Region.MovementScope.Value, GenerationConstraintKind.MinimumHeight),
                    item.Assignment.RequiredExtent,
                    $"Common rail allocation {item.Region.EnvelopeIdentity}")
        }).ToArray();
        var store = new GenerationConstraintStore();
        foreach (var proposal in regions.Select(item => item.ConstraintProposal).Where(item => item is not null))
            store.Merge(proposal!);
        constraintTimer.Stop();

        var byDemand = regions.SelectMany(item => item.Assignment.SegmentsByDemandId)
            .ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        var reconstructionTicks = 0L;
        var parityTicks = 0L;
        var routes = eligible.Select(route =>
        {
            var demand = route.Demands.Single(item => item.Role == LinkSegmentRole.Through);
            if (!byDemand.TryGetValue(demand.Id, out var common))
                return new CommonRailRouteComparison(route.LogicalRouteId, null,
                    new Dictionary<ExistingLaneMappingSource, CommonAssignmentParity>(), Array.Empty<Point>(),
                    CommonRouteReconstructionParity.UnableToReconstruct, new[] { "Common through rail was not assigned." });
            var reconstructionStarted = Stopwatch.GetTimestamp();
            var existingDeparture = route.SelectedAssignedLinkSegments.Single(item => item.Role == LinkSegmentRole.ConnectionDeparture);
            var existingArrival = route.SelectedAssignedLinkSegments.Single(item => item.Role == LinkSegmentRole.ConnectionArrival);
            var departure = existingDeparture with
            {
                Id = $"{route.LogicalRouteId}:common-departure:assigned",
                OccupiedInterval = new AxisInterval(route.CanonicalAuthoritativePoints[0].Y, common.AxisCoordinate)
            };
            var arrival = existingArrival with
            {
                Id = $"{route.LogicalRouteId}:common-arrival:assigned",
                OccupiedInterval = new AxisInterval(common.AxisCoordinate,
                    route.CanonicalAuthoritativePoints[route.CanonicalAuthoritativePoints.Count - 1].Y)
            };
            var transitions = new[]
            {
                new LinkTransition($"{route.LogicalRouteId}:common-transition:0", route.LogicalRouteId,
                    departure.Id, common.Id, new Point(departure.AxisCoordinate, common.AxisCoordinate), 0,
                    common.PlacementRevision, common.RouteRevision),
                new LinkTransition($"{route.LogicalRouteId}:common-transition:1", route.LogicalRouteId,
                    common.Id, arrival.Id, new Point(arrival.AxisCoordinate, common.AxisCoordinate), 1,
                    common.PlacementRevision, common.RouteRevision)
            };
            var assigned = new[] { departure, common, arrival };
            var reconstructed = AdjacentDownwardLinkSegmentDemandObserver.Reconstruct(
                route.CanonicalAuthoritativePoints[0],
                route.CanonicalAuthoritativePoints[route.CanonicalAuthoritativePoints.Count - 1], assigned, transitions);
            reconstructionTicks += Stopwatch.GetTimestamp() - reconstructionStarted;
            var parityStarted = Stopwatch.GetTimestamp();
            var routeParity = reconstructed.Count == 0
                ? CommonRouteReconstructionParity.UnableToReconstruct
                : reconstructed.Zip(reconstructed.Skip(1), (a, b) => new Segment(a, b)).Any(item => !item.IsOrthogonal)
                    ? CommonRouteReconstructionParity.HardInvariantRegression
                    : reconstructed.SequenceEqual(route.CanonicalAuthoritativePoints)
                        ? CommonRouteReconstructionParity.ExactGeometry
                        : CommonRouteReconstructionParity.ValidDifferentGeometry;
            var existingParity = route.ExistingLaneMappings.GroupBy(item => item.Source).ToDictionary(
                item => item.Key,
                item => Compare(common, item.First().Rail));
            parityTicks += Stopwatch.GetTimestamp() - parityStarted;
            return new CommonRailRouteComparison(route.LogicalRouteId, common, existingParity, reconstructed, routeParity,
                routeParity is CommonRouteReconstructionParity.ExactGeometry
                    ? Array.Empty<string>()
                    : new[] { "Common assignment changes the observational through-rail coordinate or lane ordering." });
        }).ToArray();
        return new CommonRailParityReport(regions, routes, Microseconds(assignmentTimer),
            Microseconds(constraintTimer), Microseconds(reconstructionTicks), Microseconds(parityTicks));
    }

    private static string RegionKey(LinkSegmentDemand demand) =>
        $"{demand.Orientation}:{demand.AllowedAxisRange.Minimum}:{demand.AllowedAxisRange.Maximum}:" +
        $"{demand.MovementScope?.Kind}:{demand.MovementScope?.Id}:{demand.PlacementRevision.Value}";

    private static CommonAssignmentParity Compare(AssignedLinkSegment common, AssignedLinkSegment existing) =>
        common.SlotIndex == existing.SlotIndex && common.AxisCoordinate == existing.AxisCoordinate
            ? CommonAssignmentParity.ExactLaneAndCoordinate
            : common.Orientation == existing.Orientation && common.OccupiedInterval == existing.OccupiedInterval
                ? CommonAssignmentParity.EquivalentValidDifferentOrdering
                : CommonAssignmentParity.UnableToMap;

    private static long Microseconds(Stopwatch timer) => timer.ElapsedTicks * 1000000 / Stopwatch.Frequency;
    private static long Microseconds(long ticks) => ticks * 1000000 / Stopwatch.Frequency;
}
