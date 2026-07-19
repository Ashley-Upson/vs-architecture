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
        return graph.Links.OrderBy(link => link.Order).ThenBy(link => link.Id, StringComparer.Ordinal)
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
