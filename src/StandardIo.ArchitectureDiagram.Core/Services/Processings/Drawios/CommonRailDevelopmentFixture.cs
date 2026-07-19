using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Xml.Linq;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal static class CommonRailDevelopmentFixture
{
    private const int Separation = 12;
    private static readonly LayoutRevision PlacementRevision = new(3);
    private static readonly RouteRevision RouteRevision = new(79);

    public static CommonRailDevelopmentFixtureResult Build()
    {
        var timer = Stopwatch.StartNew();
        var nodes = Nodes();
        var before = Routes();
        var demands = before.Select(route => new LinkSegmentDemand(
            $"{route.Id}:through", route.Id, LinkSegmentOrientation.Horizontal,
            new AxisInterval(route.Points[1].X, route.Points[2].X), new AxisInterval(130, 270),
            route.Points[1].Y, LinkSegmentRole.Through, route.Order, 1,
            new MovementScopeIdentity(MovementScopeKind.LayerAndLowerSuffix, "depth:1"),
            PlacementRevision, RouteRevision)).ToArray();
        var region = new LinkSegmentAllocationRegionIdentity(
            LinkSegmentOrientation.Horizontal, new AxisInterval(130, 270), "ccoder:band:0:1:retained-component",
            new MovementScopeIdentity(MovementScopeKind.LayerAndLowerSuffix, "depth:1"), PlacementRevision);
        var assignment = DeterministicSlotAllocator.Assign(region, demands, new LinkSegmentAssignmentOptions(Separation, 10));
        var after = before.Select(route =>
        {
            var rail = assignment.SegmentsByDemandId[$"{route.Id}:through"];
            return route with
            {
                Points = new[]
                {
                    route.Points[0], new Point(route.Points[0].X, rail.AxisCoordinate),
                    new Point(route.Points[3].X, rail.AxisCoordinate), route.Points[3]
                }
            };
        }).ToArray();
        var invalidations = after.Select(route => new LinkInvalidation(
            route.Id, LinkInvalidationCause.AssignedLinkSegmentChanged, RouteRevision,
            PlacementRevision, PlacementRevision,
            new MovementScopeIdentity(MovementScopeKind.LayerAndLowerSuffix, "depth:1"),
            assignment.SegmentsByDemandId[$"{route.Id}:through"].Id)).ToArray();
        var beforeDefects = Validate(nodes, before);
        var afterDefects = Validate(nodes, after);
        timer.Stop();
        return new CommonRailDevelopmentFixtureResult(
            Drawio(nodes, before, "Before: existing shared rail"),
            Drawio(nodes, after, "After: common deterministic rails"),
            beforeDefects, afterDefects, 0, 0,
            Math.Max(0, assignment.RequiredExtent - region.AllowedAxisRange.Length),
            after.Length, assignment.SegmentsByDemandId.Count, after.Length * 2, invalidations,
            timer.ElapsedTicks * 1000000 / Stopwatch.Frequency,
            false, false, false);
    }

    private static DevelopmentFixtureDefects Validate(IReadOnlyList<FixtureNode> nodes, IReadOnlyList<FixtureRoute> routes)
    {
        var nodeCollision = routes.Sum(route => Segments(route.Points).Count(segment => nodes
            .Where(node => node.Id != route.SourceId && node.Id != route.TargetId)
            .Any(node => SegmentIntersectsInterior(segment, node.Rect))));
        var shared = 0;
        var spacing = 0;
        var reusedBend = 0;
        for (var left = 0; left < routes.Count; left++)
        for (var right = left + 1; right < routes.Count; right++)
        {
            var leftHorizontal = Segments(routes[left].Points).Single(item => item.IsHorizontal);
            var rightHorizontal = Segments(routes[right].Points).Single(item => item.IsHorizontal);
            var overlap = Math.Min(MaxX(leftHorizontal), MaxX(rightHorizontal)) - Math.Max(MinX(leftHorizontal), MinX(rightHorizontal));
            if (overlap > 0 && leftHorizontal.Start.Y == rightHorizontal.Start.Y) shared++;
            if (overlap > 0 && Math.Abs(leftHorizontal.Start.Y - rightHorizontal.Start.Y) < Separation) spacing++;
            var leftTurns = routes[left].Points.Skip(1).Take(routes[left].Points.Count - 2);
            var rightTurns = routes[right].Points.Skip(1).Take(routes[right].Points.Count - 2);
            reusedBend += leftTurns.Intersect(rightTurns).Count();
        }
        var nonOrthogonal = routes.Sum(route => Segments(route.Points).Count(item => !item.IsOrthogonal));
        var reversals = routes.Sum(route => ImmediateReversals(route.Points));
        return new DevelopmentFixtureDefects(nodeCollision, shared, spacing, reusedBend, reversals,
            nonOrthogonal, 0, 0);
    }

    private static string Drawio(IReadOnlyList<FixtureNode> nodes, IReadOnlyList<FixtureRoute> routes, string title)
    {
        var root = new XElement("root", new XElement("mxCell", new XAttribute("id", "0")),
            new XElement("mxCell", new XAttribute("id", "1"), new XAttribute("parent", "0")));
        root.Add(new XElement("mxCell", new XAttribute("id", "title"), new XAttribute("value", title),
            new XAttribute("style", "text;html=1;align=left;verticalAlign=middle;fontSize=18;fontStyle=1;fontColor=#ffffff;"),
            new XAttribute("vertex", "1"), new XAttribute("parent", "1"),
            new XElement("mxGeometry", new XAttribute("x", 40), new XAttribute("y", 10),
                new XAttribute("width", 600), new XAttribute("height", 30), new XAttribute("as", "geometry"))));
        foreach (var node in nodes)
            root.Add(new XElement("mxCell", new XAttribute("id", node.Id), new XAttribute("value", node.Label),
                new XAttribute("style", "rounded=1;whiteSpace=wrap;html=1;fillColor=#2d8f22;strokeColor=#61bd4f;fontColor=#ffffff;"),
                new XAttribute("vertex", "1"), new XAttribute("parent", "1"),
                new XElement("mxGeometry", new XAttribute("x", node.Rect.X), new XAttribute("y", node.Rect.Y),
                    new XAttribute("width", node.Rect.Width), new XAttribute("height", node.Rect.Height), new XAttribute("as", "geometry"))));
        foreach (var route in routes.OrderBy(item => item.Id, StringComparer.Ordinal))
            root.Add(new XElement("mxCell", new XAttribute("id", route.Id),
                new XAttribute("value", route.Label), new XAttribute("edge", "1"), new XAttribute("parent", "1"),
                new XAttribute("source", route.SourceId), new XAttribute("target", route.TargetId),
                new XAttribute("style", "edgeStyle=none;orthogonalLoop=0;jettySize=auto;html=1;rounded=0;strokeColor=#d0d0d0;endArrow=block;endFill=1;"),
                new XElement("mxGeometry", new XAttribute("relative", "1"), new XAttribute("as", "geometry"),
                    new XElement("Array", new XAttribute("as", "points"),
                        route.Points.Skip(1).Take(route.Points.Count - 2).Select(point =>
                            new XElement("mxPoint", new XAttribute("x", point.X), new XAttribute("y", point.Y)))))));
        return new XDocument(new XDeclaration("1.0", "UTF-8", null),
            new XElement("mxfile", new XAttribute("host", "app.diagrams.net"), new XAttribute("compressed", "false"),
                new XElement("diagram", new XAttribute("id", "common-rail-real-component"), new XAttribute("name", "Page-1"),
                    new XElement("mxGraphModel", new XAttribute("grid", "1"), new XAttribute("gridSize", "10"),
                        new XAttribute("page", "0"), new XAttribute("background", "#101010"), root)))).ToString();
    }

    private static IReadOnlyList<FixtureNode> Nodes() => new[]
    {
        new FixtureNode("app-manager", "AppManager", new Rect(1351, 50, 200, 80)),
        new FixtureNode("app-orchestration", "AppOrchestrationService", new Rect(1297, 270, 308, 80)),
        new FixtureNode("metadata-cache", "MetadataCache", new Rect(4274, 50, 244, 80)),
        new FixtureNode("metadata-type-cache", "IMetadataTypeCache", new Rect(4296, 270, 200, 80)),
        new FixtureNode("app-controller", "AppController", new Rect(12302, 50, 260, 80))
    };

    private static IReadOnlyList<FixtureRoute> Routes() => new[]
    {
        new FixtureRoute("route-app-controller", "AppController -> AppOrchestrationService", "app-controller", "app-orchestration", 0,
            new[] { new Point(12432,130), new Point(12432,200), new Point(1451,200), new Point(1451,270) }),
        new FixtureRoute("route-app-manager", "AppManager -> AppOrchestrationService", "app-manager", "app-orchestration", 1,
            new[] { new Point(1451,130), new Point(1451,200), new Point(1427,200), new Point(1427,270) }),
        new FixtureRoute("route-metadata-cache", "MetadataCache -> IMetadataTypeCache", "metadata-cache", "metadata-type-cache", 2,
            new[] { new Point(4384,130), new Point(4384,200), new Point(4396,200), new Point(4396,270) })
    };

    private static IEnumerable<Segment> Segments(IReadOnlyList<Point> points) =>
        Enumerable.Range(0, points.Count - 1).Select(index => new Segment(points[index], points[index + 1]));

    private static int ImmediateReversals(IReadOnlyList<Point> points) => Enumerable.Range(0, points.Count - 2).Count(index =>
    {
        var first = new Segment(points[index], points[index + 1]);
        var second = new Segment(points[index + 1], points[index + 2]);
        return first.IsHorizontal && second.IsHorizontal || first.IsVertical && second.IsVertical;
    });

    private static bool SegmentIntersectsInterior(Segment segment, Rect rect) => segment.IsHorizontal
        ? segment.Start.Y > rect.Y && segment.Start.Y < rect.Bottom && MaxX(segment) > rect.X && MinX(segment) < rect.Right
        : segment.IsVertical && segment.Start.X > rect.X && segment.Start.X < rect.Right && MaxY(segment) > rect.Y && MinY(segment) < rect.Bottom;

    private static int MinX(Segment segment) => Math.Min(segment.Start.X, segment.End.X);
    private static int MaxX(Segment segment) => Math.Max(segment.Start.X, segment.End.X);
    private static int MinY(Segment segment) => Math.Min(segment.Start.Y, segment.End.Y);
    private static int MaxY(Segment segment) => Math.Max(segment.Start.Y, segment.End.Y);

    private sealed record FixtureNode(string Id, string Label, Rect Rect);
    private sealed record FixtureRoute(string Id, string Label, string SourceId, string TargetId, int Order, IReadOnlyList<Point> Points);
}
