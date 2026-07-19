using System.Collections.Generic;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal static class ChangedIntervalSlotReassignment
{
    public static ChangedIntervalReassignmentResult ReassignOnce(
        LinkSegmentAllocationRegionIdentity region,
        IReadOnlyList<LinkSegmentDemand> initialDemands,
        IReadOnlyList<LinkSegmentDemand> changedDemands,
        LinkSegmentAssignmentOptions options)
    {
        var initial = DeterministicSlotAllocator.Assign(region, initialDemands, options);
        var final = DeterministicSlotAllocator.Assign(region, changedDemands, options);
        return new ChangedIntervalReassignmentResult(
            initial, final, initial.RequiredExtent != final.RequiredExtent,
            final.RequiredExtent > region.AllowedAxisRange.Length, 1);
    }
}
