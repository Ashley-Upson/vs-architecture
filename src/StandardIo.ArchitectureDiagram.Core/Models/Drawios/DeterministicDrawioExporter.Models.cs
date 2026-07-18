using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using StandardIo.ArchitectureDiagram.Core.Models;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal sealed record RenderProject(string Id, string Name, int Order);
    internal sealed record RenderNode(
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
    internal sealed record RenderLink(
        string Id,
        string SourceId,
        string TargetId,
        string Kind,
        int Order,
        string? SemanticSourceId = null,
        string? SemanticTargetId = null);
    internal sealed record NodeLayout(RenderNode Node, Rect Rect, int Depth, bool IsStandalone);
    internal sealed record ProjectLayout(RenderProject Project, Rect Rect);
    internal enum LogicalRouteStage
    {
        Selected,
        Allocated,
        Compiled,
        Normalized,
        Validated
    }

    internal enum LogicalRouteCompilationStatus
    {
        Pending,
        Accepted,
        Rejected
    }

    internal sealed record LogicalRouteSnapshot(
        int Revision,
        LogicalRouteStage Stage,
        string Producer,
        IReadOnlyList<Point> Points,
        LogicalRouteCompilationStatus CompilationStatus,
        IReadOnlyList<string> Diagnostics);

    internal sealed class LogicalRouteState
    {
        private readonly Point[] _authoritativePoints;
        private readonly LogicalRouteSnapshot[] _history;
        private readonly string[] _diagnostics;

        private LogicalRouteState(
            string logicalEdgeId,
            string selectedCandidate,
            int revision,
            LogicalRouteStage stage,
            string producer,
            IEnumerable<Point> authoritativePoints,
            LogicalRouteCompilationStatus compilationStatus,
            IEnumerable<string> diagnostics,
            IEnumerable<LogicalRouteSnapshot> history)
        {
            LogicalEdgeId = logicalEdgeId;
            SelectedCandidate = selectedCandidate;
            Revision = revision;
            Stage = stage;
            Producer = producer;
            _authoritativePoints = authoritativePoints.ToArray();
            CompilationStatus = compilationStatus;
            _diagnostics = diagnostics.ToArray();
            _history = history.ToArray();
        }

        public string LogicalEdgeId { get; }

        public string SelectedCandidate { get; }

        public IReadOnlyList<Point> AuthoritativePoints => _authoritativePoints;

        public int Revision { get; }

        public LogicalRouteStage Stage { get; }

        public string Producer { get; }

        public LogicalRouteCompilationStatus CompilationStatus { get; }

        public IReadOnlyList<string> Diagnostics => _diagnostics;

        public IReadOnlyList<LogicalRouteSnapshot> History => _history;

        public static LogicalRouteState Selected(
            string logicalEdgeId,
            string selectedCandidate,
            IEnumerable<Point> points,
            string producer = "RouteCandidateSelection") =>
            new(
                logicalEdgeId,
                selectedCandidate,
                0,
                LogicalRouteStage.Selected,
                producer,
                points,
                LogicalRouteCompilationStatus.Pending,
                Array.Empty<string>(),
                Array.Empty<LogicalRouteSnapshot>());

        public LogicalRouteState Accept(
            LogicalRouteStage stage,
            string producer,
            IEnumerable<Point> points,
            LogicalRouteCompilationStatus compilationStatus = LogicalRouteCompilationStatus.Accepted,
            IEnumerable<string>? diagnostics = null)
        {
            if (stage < Stage)
            {
                throw new InvalidOperationException($"Route {LogicalEdgeId} cannot move backwards from {Stage} to {stage}.");
            }

            var history = _history.Concat(new[]
            {
                new LogicalRouteSnapshot(
                    Revision,
                    Stage,
                    Producer,
                    _authoritativePoints.ToArray(),
                    CompilationStatus,
                    _diagnostics.ToArray())
            });
            return new LogicalRouteState(
                LogicalEdgeId,
                SelectedCandidate,
                Revision + 1,
                stage,
                producer,
                points,
                compilationStatus,
                diagnostics ?? Array.Empty<string>(),
                history);
        }

        public LogicalRouteState Reject(string producer, IEnumerable<string> diagnostics) =>
            new(
                LogicalEdgeId,
                SelectedCandidate,
                Revision,
                Stage,
                Producer,
                _authoritativePoints,
                LogicalRouteCompilationStatus.Rejected,
                _diagnostics.Concat(diagnostics),
                _history.Concat(new[]
                {
                    new LogicalRouteSnapshot(
                        Revision,
                        Stage,
                        producer,
                        _authoritativePoints.ToArray(),
                        LogicalRouteCompilationStatus.Rejected,
                        diagnostics.ToArray())
                }));

        public LogicalRouteState Reselect(string producer, IEnumerable<Point> points) =>
            new(
                LogicalEdgeId,
                SelectedCandidate,
                Revision + 1,
                LogicalRouteStage.Selected,
                producer,
                points,
                LogicalRouteCompilationStatus.Pending,
                Array.Empty<string>(),
                _history.Concat(new[]
                {
                    new LogicalRouteSnapshot(
                        Revision,
                        Stage,
                        Producer,
                        _authoritativePoints.ToArray(),
                        CompilationStatus,
                        _diagnostics.ToArray())
                }));
    }

    internal sealed record LinkLayout(
        RenderLink Link,
        Point SourcePoint,
        Point TargetPoint,
        RoutedEdgeGeometry Geometry,
        double ExitX,
        double EntryX,
        double ExitY = 1,
        double EntryY = 0,
        LogicalRouteState? State = null)
    {
        public LinkLayout(
            RenderLink link,
            Point sourcePoint,
            Point targetPoint,
            IEnumerable<Point> points,
            double exitX,
            double entryX,
            double exitY = 1,
            double entryY = 0)
            : this(
                link,
                sourcePoint,
                targetPoint,
                new RoutedEdgeGeometry(points),
                exitX,
                entryX,
                exitY,
                entryY,
                LogicalRouteState.Selected(
                    link.Id,
                    "initial",
                    new[] { sourcePoint }.Concat(points).Concat(new[] { targetPoint })))
        {
        }

        public IReadOnlyList<Point> Points => Geometry.Points;

        public LogicalRouteState RouteState => State ?? LogicalRouteState.Selected(
            Link.Id,
            "legacy",
            new[] { SourcePoint }.Concat(Points).Concat(new[] { TargetPoint }),
            "LegacyLinkLayoutAdapter");

        public LinkLayout AcceptGeometry(
            IEnumerable<Point> completePoints,
            LogicalRouteStage stage,
            string producer,
            LogicalRouteCompilationStatus status = LogicalRouteCompilationStatus.Accepted,
            IEnumerable<string>? diagnostics = null)
        {
            var points = completePoints.ToArray();
            if (points.Length < 2)
            {
                throw new InvalidOperationException($"Route {Link.Id} must contain terminal points.");
            }

            return new LinkLayout(
                Link,
                points[0],
                points[points.Length - 1],
                new RoutedEdgeGeometry(points.Skip(1).Take(points.Length - 2)),
                ExitX,
                EntryX,
                ExitY,
                EntryY,
                RouteState.Accept(stage, producer, points, status, diagnostics));
        }

        public LinkLayout RejectGeometry(string producer, IEnumerable<string> diagnostics) =>
            this with { State = RouteState.Reject(producer, diagnostics) };

        public LinkLayout ProposeSelectedGeometry(IEnumerable<Point> completePoints, string producer)
        {
            var points = completePoints.ToArray();
            return new LinkLayout(
                Link,
                points[0],
                points[points.Length - 1],
                new RoutedEdgeGeometry(points.Skip(1).Take(points.Length - 2)),
                ExitX,
                EntryX,
                ExitY,
                EntryY,
                RouteState.Reselect(producer, points));
        }
    }

    internal sealed class RoutedEdgeGeometry
    {
        private readonly Point[] _points;
        private readonly Segment[] _segments;

        public RoutedEdgeGeometry(IEnumerable<Point> points)
        {
            if (points is null)
            {
                throw new ArgumentNullException(nameof(points));
            }

            _points = points.ToArray();
            _segments = Enumerable.Range(0, Math.Max(0, _points.Length - 1))
                .Select(index => new Segment(_points[index], _points[index + 1]))
                .ToArray();
        }

        public IReadOnlyList<Point> Points => _points;

        public IReadOnlyList<Segment> Segments => _segments;
    }
    internal sealed record SubtreeMeasure(int Width, int Height);
    internal sealed record DataModelRelationship(string SourceId, string TargetId, string PropertyName);

