using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using StandardIo.ArchitectureDiagram.Core.Graph;
using StandardIo.ArchitectureDiagram.Core.Settings;

namespace StandardIo.ArchitectureDiagram.Core.Drawio;

public sealed partial class DeterministicDrawioExporter
{
    private sealed record RenderProject(string Id, string Name, int Order);
    private sealed record RenderNode(
        string Id,
        string? ProjectId,
        string Name,
        string FullName,
        string Kind,
        bool IsExternal,
        string Tag,
        int Order,
        IReadOnlyList<string> Interfaces,
        IReadOnlyList<TypeProperty> Properties,
        int MethodCount);
    private sealed record RenderLink(string Id, string SourceId, string TargetId, string Kind, int Order);
    private sealed record NodeLayout(RenderNode Node, Rect Rect, int Depth, bool IsStandalone);
    private sealed record ProjectLayout(RenderProject Project, Rect Rect);
    private sealed record LinkLayout(RenderLink Link, Point SourcePoint, Point TargetPoint, IReadOnlyList<Point> Points, double ExitX, double EntryX, double ExitY = 1, double EntryY = 0);
    private sealed record SubtreeMeasure(int Width, int Height);
    private sealed record DataModelRelationship(string SourceId, string TargetId, string PropertyName);

    private readonly record struct Point(int X, int Y);

    private readonly record struct Rect(int X, int Y, int Width, int Height)
    {
        public int Right => X + Width;

        public int Bottom => Y + Height;

        public int CenterX => X + Width / 2;

        public int CenterY => Y + Height / 2;

        public bool Contains(Point point)
        {
            return point.X > X && point.X < Right && point.Y > Y && point.Y < Bottom;
        }

        public Rect Inflate(int padding)
        {
            return new Rect(X - padding, Y - padding, Width + padding * 2, Height + padding * 2);
        }
    }

    private readonly record struct Segment(Point Start, Point End)
    {
        public bool IsHorizontal => Start.Y == End.Y;

        public bool IsVertical => Start.X == End.X;

        public int Length => Math.Abs(Start.X - End.X) + Math.Abs(Start.Y - End.Y);

        public bool Intersects(Rect rect)
        {
            if (IsHorizontal)
            {
                return Start.Y > rect.Y &&
                    Start.Y < rect.Bottom &&
                    Math.Max(Start.X, End.X) > rect.X &&
                    Math.Min(Start.X, End.X) < rect.Right;
            }

            if (IsVertical)
            {
                return Start.X > rect.X &&
                    Start.X < rect.Right &&
                    Math.Max(Start.Y, End.Y) > rect.Y &&
                    Math.Min(Start.Y, End.Y) < rect.Bottom;
            }

            return rect.Contains(Start) || rect.Contains(End);
        }

        public bool Crosses(Segment other)
        {
            if (IsHorizontal == other.IsHorizontal)
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
                return OverlapLength(Start.X, End.X, other.Start.X, other.End.X);
            }

            if (IsVertical && other.IsVertical && Start.X == other.Start.X)
            {
                return OverlapLength(Start.Y, End.Y, other.Start.Y, other.End.Y);
            }

            return 0;
        }

        private static int OverlapLength(int firstStart, int firstEnd, int secondStart, int secondEnd)
        {
            var start = Math.Max(Math.Min(firstStart, firstEnd), Math.Min(secondStart, secondEnd));
            var end = Math.Min(Math.Max(firstStart, firstEnd), Math.Max(secondStart, secondEnd));
            return Math.Max(0, end - start);
        }
    }
}
