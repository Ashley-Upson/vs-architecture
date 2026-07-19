using System;
using System.Collections.Generic;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core.Models;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal static class PreAssignmentRouteProjection
{
    public static IReadOnlyDictionary<string, LinkLayout> Project(
        PlacedGraph placement,
        IReadOnlyDictionary<string, LinkLayout> basis,
        DiagramSettings settings)
    {
        var separation = LinkConnectionDemandCalculator.AttachmentSeparation(
            settings.Layout.EdgePortSpacing, settings.Layout.ParallelLaneSpacing);
        var padding = settings.Layout.LinkNodeWidthPadding;
        var assignments = new Dictionary<(string RouteId, LinkConnectionSide Side), LinkConnectionAssignment>();
        foreach (var node in placement.Nodes.Values.OrderBy(item => item.Node.Id, StringComparer.Ordinal))
        {
            var requests = basis.Values.Where(link => link.Link.SourceId == node.Node.Id)
                    .Select(link => new LinkConnectionRequest(link.Link.Id,
                        placement.Nodes[link.Link.TargetId].Rect.CenterX, LinkConnectionSide.OutgoingBottom))
                .Concat(basis.Values.Where(link => link.Link.TargetId == node.Node.Id)
                    .Select(link => new LinkConnectionRequest(link.Link.Id,
                        placement.Nodes[link.Link.SourceId].Rect.CenterX, LinkConnectionSide.IncomingTop))).ToArray();
            foreach (var assignment in LinkConnectionDemandCalculator.Allocate(node.Rect, requests, padding, separation))
                assignments[(assignment.RouteId, assignment.Side)] = assignment;
        }
        return basis.Values.OrderBy(item => item.Link.Id, StringComparer.Ordinal).ToDictionary(link => link.Link.Id, link =>
        {
            var source = placement.Nodes[link.Link.SourceId].Rect;
            var target = placement.Nodes[link.Link.TargetId].Rect;
            var sourceX = assignments[(link.Link.Id, LinkConnectionSide.OutgoingBottom)].AxisCoordinate;
            var targetX = assignments[(link.Link.Id, LinkConnectionSide.IncomingTop)].AxisCoordinate;
            var sourcePoint = new Point(sourceX, source.Bottom);
            var targetPoint = new Point(targetX, target.Y);
            return new LinkLayout(link.Link, sourcePoint, targetPoint,
                new RoutedEdgeGeometry(Array.Empty<Point>()), (sourceX - source.X) / (double)source.Width,
                (targetX - target.X) / (double)target.Width, 1, 0,
                LogicalRouteState.Selected(link.Link.Id, "PreAssignmentRouteProjection", new[] { sourcePoint, targetPoint }));
        }, StringComparer.Ordinal);
    }
}
