using System;
using System.Collections.Generic;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core.Models;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal static class ProjectTerminalAllocator
{
    public static IReadOnlyDictionary<string, LinkLayout> Allocate(
        RenderGraph graph,
        IReadOnlyDictionary<string, NodeLayout> nodes,
        DiagramSettings settings)
    {
        var sourceGroups = graph.Links.GroupBy(link => link.SourceId)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        var targetGroups = graph.Links.GroupBy(link => link.TargetId)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        var spacing = Math.Max(settings.Layout.EdgePortSpacing, settings.Layout.ParallelLaneSpacing * 2);
        var allocated = graph.Links.OrderBy(link => link.Order).ThenBy(link => link.Id, StringComparer.Ordinal)
            .ToDictionary(link => link.Id, link =>
            {
                var source = nodes[link.SourceId].Rect;
                var target = nodes[link.TargetId].Rect;
                var sourceX = Attachment(sourceGroups[link.SourceId], link, nodes, source,
                    LinkConnectionSide.OutgoingBottom, settings.Layout.LinkNodeWidthPadding, spacing, true);
                var targetX = Attachment(targetGroups[link.TargetId], link, nodes, target,
                    LinkConnectionSide.IncomingTop, settings.Layout.LinkNodeWidthPadding, spacing, false);
                return new LinkLayout(link,
                    new Point(sourceX, source.Bottom), new Point(targetX, target.Y), Array.Empty<Point>(),
                    Ratio(sourceX, source), Ratio(targetX, target));
            }, StringComparer.Ordinal);
        return SeparateOpposingTerminals(graph, nodes,
            SeparateOpposingBoundaryTerminals(graph, nodes, allocated, settings), settings);
    }

    private static IReadOnlyDictionary<string, LinkLayout> SeparateOpposingTerminals(
        RenderGraph graph,
        IReadOnlyDictionary<string, NodeLayout> nodes,
        IReadOnlyDictionary<string, LinkLayout> allocated,
        DiagramSettings settings)
    {
        var separation = Math.Max(settings.Layout.EdgePortSpacing, settings.Layout.ParallelLaneSpacing);
        if (separation <= 0) return allocated;

        var result = allocated.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        foreach (var node in nodes.Values.OrderBy(item => item.Node.Order))
        {
            var terminals = graph.Links
                .Where(link => link.SourceId == node.Node.Id || link.TargetId == node.Node.Id)
                .Select(link => (Link: link, IsSource: link.SourceId == node.Node.Id,
                    X: link.SourceId == node.Node.Id
                        ? result[link.Id].SourcePoint.X
                        : result[link.Id].TargetPoint.X))
                .OrderBy(item => item.IsSource ? 0 : 1)
                .ThenBy(item => item.X)
                .ThenBy(item => item.Link.Order)
                .ThenBy(item => item.Link.Id, StringComparer.Ordinal)
                .ToArray();
            var occupied = new List<int>();
            foreach (var terminal in terminals)
            {
                var coordinate = ClosestAvailable(
                    terminal.X,
                    node.Rect.X + settings.Layout.LinkNodeWidthPadding,
                    node.Rect.Right - settings.Layout.LinkNodeWidthPadding,
                    separation,
                    occupied.Select(value => (Id: string.Empty, X: value)).ToArray());
                occupied.Add(coordinate);
                if (coordinate == terminal.X) continue;

                var layout = result[terminal.Link.Id];
                result[terminal.Link.Id] = terminal.IsSource
                    ? layout with
                    {
                        SourcePoint = new Point(coordinate, layout.SourcePoint.Y),
                        ExitX = Ratio(coordinate, node.Rect)
                    }
                    : layout with
                    {
                        TargetPoint = new Point(coordinate, layout.TargetPoint.Y),
                        EntryX = Ratio(coordinate, node.Rect)
                    };
            }
        }
        return result;
    }

    private static IReadOnlyDictionary<string, LinkLayout> SeparateOpposingBoundaryTerminals(
        RenderGraph graph,
        IReadOnlyDictionary<string, NodeLayout> nodes,
        IReadOnlyDictionary<string, LinkLayout> allocated,
        DiagramSettings settings)
    {
        var separation = settings.Layout.ParallelLaneSpacing;
        if (separation <= 0) return allocated;
        var result = allocated.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        var internalLinks = graph.Links.Where(link =>
                !nodes[link.SourceId].IsStandalone && !nodes[link.TargetId].IsStandalone)
            .ToArray();
        foreach (var boundary in internalLinks.Select(link => nodes[link.SourceId].Depth)
                     .Concat(internalLinks.Select(link => nodes[link.TargetId].Depth - 1)).Distinct().OrderBy(item => item))
        {
            var fixedDepartures = internalLinks.Where(link => nodes[link.SourceId].Depth == boundary)
                .Select(link => (link.Id, X: result[link.Id].SourcePoint.X)).ToArray();
            var acceptedArrivals = new List<(string Id, int X)>();
            foreach (var link in internalLinks.Where(link => nodes[link.TargetId].Depth - 1 == boundary)
                         .OrderBy(link => result[link.Id].TargetPoint.X)
                         .ThenBy(link => link.Id, StringComparer.Ordinal))
            {
                var layout = result[link.Id];
                var target = nodes[link.TargetId].Rect;
                var minimum = target.X + settings.Layout.LinkNodeWidthPadding;
                var maximum = target.Right - settings.Layout.LinkNodeWidthPadding;
                var occupied = fixedDepartures.Where(item => item.Id != link.Id)
                    .Concat(acceptedArrivals.Where(item => item.Id != link.Id)).ToArray();
                var targetX = ClosestAvailable(layout.TargetPoint.X, minimum, maximum, separation, occupied);
                acceptedArrivals.Add((link.Id, targetX));
                if (targetX == layout.TargetPoint.X) continue;
                result[link.Id] = layout with
                {
                    TargetPoint = new Point(targetX, layout.TargetPoint.Y),
                    EntryX = Ratio(targetX, target)
                };
            }
        }
        return result;
    }

    private static int ClosestAvailable(
        int preferred,
        int minimum,
        int maximum,
        int separation,
        IReadOnlyList<(string Id, int X)> occupied)
    {
        bool Available(int candidate) => occupied.All(item => Math.Abs(item.X - candidate) >= separation);
        if (preferred >= minimum && preferred <= maximum && Available(preferred)) return preferred;
        var extent = Math.Max(preferred - minimum, maximum - preferred);
        for (var offset = 1; offset <= extent; offset++)
        {
            var left = preferred - offset;
            if (left >= minimum && Available(left)) return left;
            var right = preferred + offset;
            if (right <= maximum && Available(right)) return right;
        }
        return preferred;
    }

    private static int Attachment(
        IReadOnlyList<RenderLink> group,
        RenderLink selected,
        IReadOnlyDictionary<string, NodeLayout> nodes,
        Rect terminal,
        LinkConnectionSide side,
        int padding,
        int spacing,
        bool source) =>
        LinkConnectionDemandCalculator.Allocate(
                terminal,
                group.Select(link => new LinkConnectionRequest(
                    link.Id, source ? nodes[link.TargetId].Rect.CenterX : nodes[link.SourceId].Rect.CenterX, side)),
                padding,
                spacing)
            .Single(item => item.RouteId == selected.Id).AxisCoordinate;

    private static double Ratio(int x, Rect rect) =>
        Math.Max(0, Math.Min(1, (x - rect.X) / (double)rect.Width));
}
