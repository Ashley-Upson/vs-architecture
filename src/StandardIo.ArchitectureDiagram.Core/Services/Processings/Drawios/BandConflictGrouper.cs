using System;
using System.Collections.Generic;
using System.Linq;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal static class BandConflictGrouper
{
    public static IReadOnlyList<BandConflictGroup> Group(
        InterLayerBandObservation band,
        int clearance,
        int padding,
        out long comparisons)
    {
        var commonDemands = band.Demands.Select(item => new LinkSegmentDemand(
            item.Id, item.LogicalEdgeIdentity, LinkSegmentOrientation.Horizontal,
            new AxisInterval(item.XStart, item.XEnd), new AxisInterval(band.UpperBoundary, band.LowerBoundary),
            null, item.Role == BandMembershipRole.Return ? LinkSegmentRole.Return : LinkSegmentRole.Through,
            item.ConnectionOrder, item.SegmentIndex,
            new MovementScopeIdentity(MovementScopeKind.LayerAndLowerSuffix, $"depth:{band.Id.LowerLayer}"),
            band.Id.LayoutRevision, item.RouteRevision)).ToArray();
        var region = new LinkSegmentAllocationRegionIdentity(
            LinkSegmentOrientation.Horizontal, new AxisInterval(band.UpperBoundary, band.LowerBoundary),
            band.Id.ToString(), new MovementScopeIdentity(MovementScopeKind.LayerAndLowerSuffix, $"depth:{band.Id.LowerLayer}"),
            band.Id.LayoutRevision);
        var common = DeterministicSlotAllocator.Assign(region, commonDemands,
            new LinkSegmentAssignmentOptions(clearance, padding, EndpointContactCreatesComponent: true));
        comparisons = common.ConflictComparisons;
        var byId = band.Demands.ToDictionary(item => item.Id, StringComparer.Ordinal);
        var groups = new List<BandConflictGroup>();
        foreach (var commonComponent in common.Components)
        {
            var component = commonComponent.Demands.Select(item => byId[item.Id]).ToArray();
            var assigned = commonComponent.Segments.ToDictionary(item => item.DemandId, item => item.SlotIndex, StringComparer.Ordinal);
            var laneCount = commonComponent.Segments.Select(item => item.SlotIndex).DefaultIfEmpty(-1).Max() + 1;
            var identity = string.Join("+", component.Select(item => item.Id).OrderBy(item => item, StringComparer.Ordinal));
            groups.Add(new BandConflictGroup(
                $"group:{band.Id}:{identity}", band.Id,
                component.OrderBy(item => item.Id, StringComparer.Ordinal).ToArray(),
                assigned,
                component.Select(item => item.SlotIndex).DefaultIfEmpty(-1).Max() + 1,
                laneCount, band.CurrentExtent, commonComponent.RequiredExtent, commonComponent.MissingExtent,
                SpacingConstraintScope.LayerBoundary));
        }
        return groups.OrderBy(item => item.Id, StringComparer.Ordinal).ToArray();
    }

    public static IntervalContactKind Contact(int firstStart, int firstEnd, int secondStart, int secondEnd, int clearance = 0)
    {
        var left = Math.Max(Math.Min(firstStart, firstEnd), Math.Min(secondStart, secondEnd));
        var right = Math.Min(Math.Max(firstStart, firstEnd), Math.Max(secondStart, secondEnd));
        if (right > left) return IntervalContactKind.PositiveOverlap;
        if (right == left) return IntervalContactKind.EndpointContact;
        var gap = left - right;
        return gap < clearance ? IntervalContactKind.EndpointContact : IntervalContactKind.Disjoint;
    }

    public static RoutePointContactKind ClassifyContact(
        Segment first,
        Segment? firstContinuation,
        Segment second,
        Segment? secondContinuation)
    {
        // Preserve the existing Stage C policy: any caller-supplied continuation makes
        // the contact ambiguous. The canonical classifier provides the geometry fact;
        // this adapter retains the current consumer policy and therefore route parity.
        if (firstContinuation is not null || secondContinuation is not null)
            return RoutePointContactKind.AmbiguousBend;
        var contact = CanonicalContactClassifier.Classify(
            new ContactSegment(first, Next: firstContinuation),
            new ContactSegment(second, Next: secondContinuation));
        return contact.Kind switch
        {
            CanonicalContactKind.CleanPerpendicularCrossover => RoutePointContactKind.CleanCrossover,
            CanonicalContactKind.SharedBend or
            CanonicalContactKind.BendInvolvedPerpendicularContact => RoutePointContactKind.AmbiguousBend,
            _ => RoutePointContactKind.StraightContinuation
        };
    }

}
