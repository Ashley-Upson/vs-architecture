namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal static class ContactInteractionPolicy
{
    public static bool CreatesFinalGeometryEdge(CanonicalContactKind kind) => kind switch
    {
        CanonicalContactKind.NearParallelSpacingConflict => true,
        CanonicalContactKind.PositiveCollinearOverlap => true,
        CanonicalContactKind.EndpointToInterior => true,
        CanonicalContactKind.SharedBend => true,
        CanonicalContactKind.BendInvolvedPerpendicularContact => true,
        _ => false
    };

    public static bool CreatesUnassignedRailEdge(RailDemand first, RailDemand second) =>
        first.Orientation == second.Orientation &&
        first.OccupiedInterval.PositiveLengthOverlap(second.OccupiedInterval) > 0 &&
        first.AllowedAxisRange.ClosedIntersects(second.AllowedAxisRange);

    public static bool CreatesAssignedRailEdge(AssignedRail first, AssignedRail second, int requiredSpacing)
    {
        if (first.Orientation != second.Orientation ||
            first.OccupiedInterval.PositiveLengthOverlap(second.OccupiedInterval) <= 0)
            return false;

        var separation = System.Math.Abs(first.AxisCoordinate - second.AxisCoordinate);
        return separation == 0 || separation < requiredSpacing;
    }
}
