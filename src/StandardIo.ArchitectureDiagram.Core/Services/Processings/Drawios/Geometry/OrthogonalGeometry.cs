using System;
using System.Collections.Generic;
using System.Linq;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal readonly record struct AxisInterval
{
    public AxisInterval(int first, int second)
    {
        Minimum = Math.Min(first, second);
        Maximum = Math.Max(first, second);
    }

    public int Minimum { get; }
    public int Maximum { get; }
    public int Length => Maximum - Minimum;

    public bool ContainsClosed(int value) => value >= Minimum && value <= Maximum;

    public bool ClosedIntersects(AxisInterval other) =>
        Minimum <= other.Maximum && other.Minimum <= Maximum;

    public int PositiveLengthOverlap(AxisInterval other) =>
        Math.Max(0, Math.Min(Maximum, other.Maximum) - Math.Max(Minimum, other.Minimum));
}

internal readonly record struct Point(int X, int Y)
{
    public Point Translate(int deltaX, int deltaY) => new(X + deltaX, Y + deltaY);
}

internal readonly record struct Rect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
    public int CenterX => X + Width / 2;
    public int CenterY => Y + Height / 2;

    public bool Contains(Point point) =>
        point.X > X && point.X < Right && point.Y > Y && point.Y < Bottom;

    public Rect Inflate(int padding) =>
        new(X - padding, Y - padding, Width + padding * 2, Height + padding * 2);

    public Rect Translate(int deltaX, int deltaY) =>
        new(X + deltaX, Y + deltaY, Width, Height);
}

internal readonly record struct Segment(Point Start, Point End)
{
    public bool IsHorizontal => Start.Y == End.Y;
    public bool IsVertical => Start.X == End.X;
    public bool IsOrthogonal => IsHorizontal || IsVertical;
    public int Length => Math.Abs(Start.X - End.X) + Math.Abs(Start.Y - End.Y);

    public bool ContainsPointClosed(Point point)
    {
        if (IsHorizontal)
        {
            return point.Y == Start.Y && new AxisInterval(Start.X, End.X).ContainsClosed(point.X);
        }

        return IsVertical && point.X == Start.X && new AxisInterval(Start.Y, End.Y).ContainsClosed(point.Y);
    }

    public bool Intersects(Rect rect)
    {
        if (IsHorizontal)
        {
            return Start.Y > rect.Y && Start.Y < rect.Bottom &&
                new AxisInterval(Start.X, End.X).PositiveLengthOverlap(new AxisInterval(rect.X, rect.Right)) > 0;
        }

        if (IsVertical)
        {
            return Start.X > rect.X && Start.X < rect.Right &&
                new AxisInterval(Start.Y, End.Y).PositiveLengthOverlap(new AxisInterval(rect.Y, rect.Bottom)) > 0;
        }

        return rect.Contains(Start) || rect.Contains(End);
    }

    public bool Crosses(Segment other)
    {
        if (!IsOrthogonal || !other.IsOrthogonal || IsHorizontal == other.IsHorizontal)
        {
            return false;
        }

        var horizontal = IsHorizontal ? this : other;
        var vertical = IsHorizontal ? other : this;
        return vertical.Start.X > Math.Min(horizontal.Start.X, horizontal.End.X) &&
            vertical.Start.X < Math.Max(horizontal.Start.X, horizontal.End.X) &&
            horizontal.Start.Y > Math.Min(vertical.Start.Y, vertical.End.Y) &&
            horizontal.Start.Y < Math.Max(vertical.Start.Y, vertical.End.Y);
    }

    public int OverlapLength(Segment other)
    {
        if (IsHorizontal && other.IsHorizontal && Start.Y == other.Start.Y)
        {
            return new AxisInterval(Start.X, End.X)
                .PositiveLengthOverlap(new AxisInterval(other.Start.X, other.End.X));
        }

        if (IsVertical && other.IsVertical && Start.X == other.Start.X)
        {
            return new AxisInterval(Start.Y, End.Y)
                .PositiveLengthOverlap(new AxisInterval(other.Start.Y, other.End.Y));
        }

        return 0;
    }
}

internal static class PolylineGeometry
{
    public static Rect Bounds(IReadOnlyList<Point> points)
    {
        if (points is null || points.Count == 0)
        {
            throw new ArgumentException("A polyline requires at least one point.", nameof(points));
        }

        var minimumX = points.Min(point => point.X);
        var minimumY = points.Min(point => point.Y);
        return new Rect(
            minimumX,
            minimumY,
            points.Max(point => point.X) - minimumX,
            points.Max(point => point.Y) - minimumY);
    }

    public static IReadOnlyList<Point> Translate(
        IReadOnlyList<Point> points,
        int deltaX,
        int deltaY) =>
        points.Select(point => point.Translate(deltaX, deltaY)).ToArray();
}
