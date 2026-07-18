using System;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal enum CanonicalContactKind
{
    Disjoint,
    NearParallelSpacingConflict,
    PositiveCollinearOverlap,
    EndpointToEndpoint,
    EndpointToInterior,
    SharedBend,
    StraightContinuation,
    CleanPerpendicularCrossover,
    BendInvolvedPerpendicularContact,
    TerminalContact,
    IntentionalSemanticJunction
}

internal readonly record struct ContactSegment(
    Segment Segment,
    Segment? Previous = null,
    Segment? Next = null,
    bool StartIsTerminal = false,
    bool EndIsTerminal = false)
{
    public bool BendsAt(Point point)
    {
        var adjacent = point == Segment.Start ? Previous : point == Segment.End ? Next : null;
        return adjacent is not null && adjacent.Value.IsOrthogonal &&
            adjacent.Value.IsHorizontal != Segment.IsHorizontal;
    }

    public bool IsTerminal(Point point) =>
        point == Segment.Start && StartIsTerminal || point == Segment.End && EndIsTerminal;
}

internal sealed record CanonicalContact(
    CanonicalContactKind Kind,
    Point? Point = null,
    int PositiveOverlap = 0,
    int Separation = int.MaxValue);

internal static class CanonicalContactClassifier
{
    public static CanonicalContact Classify(
        ContactSegment first,
        ContactSegment second,
        int requiredParallelSpacing = 0,
        bool intentionalSemanticJunction = false)
    {
        if (intentionalSemanticJunction)
            return new CanonicalContact(CanonicalContactKind.IntentionalSemanticJunction);

        var left = first.Segment;
        var right = second.Segment;
        if (!left.IsOrthogonal || !right.IsOrthogonal)
            return new CanonicalContact(CanonicalContactKind.Disjoint);

        if (left.IsHorizontal == right.IsHorizontal)
            return ClassifyParallel(first, second, requiredParallelSpacing);

        var horizontal = left.IsHorizontal ? left : right;
        var vertical = left.IsVertical ? left : right;
        var point = new Point(vertical.Start.X, horizontal.Start.Y);
        if (!horizontal.ContainsPointClosed(point) || !vertical.ContainsPointClosed(point))
            return new CanonicalContact(CanonicalContactKind.Disjoint);

        if (first.IsTerminal(point) || second.IsTerminal(point))
            return new CanonicalContact(CanonicalContactKind.TerminalContact, point);

        var firstEndpoint = IsEndpoint(left, point);
        var secondEndpoint = IsEndpoint(right, point);
        var bend = first.BendsAt(point) || second.BendsAt(point);
        if (!firstEndpoint && !secondEndpoint && !bend)
            return new CanonicalContact(CanonicalContactKind.CleanPerpendicularCrossover, point);
        if (bend)
            return new CanonicalContact(CanonicalContactKind.BendInvolvedPerpendicularContact, point);
        return new CanonicalContact(
            firstEndpoint && secondEndpoint
                ? CanonicalContactKind.EndpointToEndpoint
                : CanonicalContactKind.EndpointToInterior,
            point);
    }

    private static CanonicalContact ClassifyParallel(
        ContactSegment first,
        ContactSegment second,
        int requiredSpacing)
    {
        var left = first.Segment;
        var right = second.Segment;
        var leftInterval = left.IsHorizontal
            ? new AxisInterval(left.Start.X, left.End.X)
            : new AxisInterval(left.Start.Y, left.End.Y);
        var rightInterval = right.IsHorizontal
            ? new AxisInterval(right.Start.X, right.End.X)
            : new AxisInterval(right.Start.Y, right.End.Y);
        var separation = left.IsHorizontal
            ? Math.Abs(left.Start.Y - right.Start.Y)
            : Math.Abs(left.Start.X - right.Start.X);
        var overlap = leftInterval.PositiveLengthOverlap(rightInterval);

        if (separation > 0)
        {
            return overlap > 0 && separation < requiredSpacing
                ? new CanonicalContact(CanonicalContactKind.NearParallelSpacingConflict, null, overlap, separation)
                : new CanonicalContact(CanonicalContactKind.Disjoint, null, 0, separation);
        }

        if (overlap > 0)
            return new CanonicalContact(CanonicalContactKind.PositiveCollinearOverlap, null, overlap, 0);

        var contact = SharedEndpoint(left, right);
        if (contact is null)
            return new CanonicalContact(CanonicalContactKind.Disjoint, null, 0, 0);
        if (first.IsTerminal(contact.Value) || second.IsTerminal(contact.Value))
            return new CanonicalContact(CanonicalContactKind.TerminalContact, contact);
        var firstBends = first.BendsAt(contact.Value);
        var secondBends = second.BendsAt(contact.Value);
        if (firstBends && secondBends)
            return new CanonicalContact(CanonicalContactKind.SharedBend, contact);
        if (!firstBends && !secondBends)
            return new CanonicalContact(CanonicalContactKind.StraightContinuation, contact);
        return new CanonicalContact(CanonicalContactKind.EndpointToEndpoint, contact);
    }

    private static bool IsEndpoint(Segment segment, Point point) =>
        segment.Start == point || segment.End == point;

    private static Point? SharedEndpoint(Segment first, Segment second)
    {
        if (first.Start == second.Start || first.Start == second.End) return first.Start;
        if (first.End == second.Start || first.End == second.End) return first.End;
        return null;
    }
}
