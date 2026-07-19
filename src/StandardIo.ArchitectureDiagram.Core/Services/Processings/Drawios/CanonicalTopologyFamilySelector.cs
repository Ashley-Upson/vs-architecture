using System;
using System.Collections.Generic;
using System.Linq;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal static class CanonicalTopologyFamilySelector
{
    public static CanonicalTopologySelection Select(
        RenderGraph graph,
        IReadOnlyDictionary<string, NodeLayout> nodes,
        LayoutRevision revision)
    {
        var plans = new Dictionary<string, CanonicalTopologyPlan>(StringComparer.Ordinal);
        var rejections = new List<string>();
        foreach (var link in graph.Links.OrderBy(item => item.Order).ThenBy(item => item.Id, StringComparer.Ordinal))
        {
            if (!nodes.TryGetValue(link.SourceId, out var source) || !nodes.TryGetValue(link.TargetId, out var target))
            {
                rejections.Add($"MissingEndpoint:{link.Id}");
                continue;
            }
            if (string.Equals(link.SourceId, link.TargetId, StringComparison.Ordinal))
            {
                rejections.Add($"UnsupportedSelfLoop:{link.Id}");
                continue;
            }

            var family = Family(source, target);
            InterLayerId? departure = source.IsStandalone || target.IsStandalone
                ? null : new InterLayerId(source.Depth, source.Depth + 1, revision);
            InterLayerId? arrival = family is CanonicalTopologyFamily.SameLayerReturn or CanonicalTopologyFamily.UpwardReturn
                ? new InterLayerId(Math.Max(0, target.Depth - 1), target.Depth, revision)
                : null;
            var isReturn = family is CanonicalTopologyFamily.SameLayerReturn or CanonicalTopologyFamily.UpwardReturn;
            var destinationColumn = family == CanonicalTopologyFamily.LongDownward;
            var boundary = family is CanonicalTopologyFamily.InternalToExternal or
                CanonicalTopologyFamily.ExternalToInternal or CanonicalTopologyFamily.CrossProjectBoundaryTransition;
            var segments = new List<CanonicalTopologySegmentRequirement>
            {
                new(CanonicalTopologySegmentRole.Departure, LinkSegmentOrientation.Vertical, true, false),
                new(CanonicalTopologySegmentRole.Through, LinkSegmentOrientation.Horizontal, true, true)
            };
            if (destinationColumn)
                segments.Add(new CanonicalTopologySegmentRequirement(
                    CanonicalTopologySegmentRole.DestinationColumn, LinkSegmentOrientation.Vertical, true, true));
            if (isReturn)
                segments.Add(new CanonicalTopologySegmentRequirement(
                    CanonicalTopologySegmentRole.ReturnColumn, LinkSegmentOrientation.Vertical, true, true));
            if (boundary)
                segments.Add(new CanonicalTopologySegmentRequirement(
                    CanonicalTopologySegmentRole.BoundaryTransition, LinkSegmentOrientation.Horizontal, true, true));
            segments.Add(new CanonicalTopologySegmentRequirement(
                CanonicalTopologySegmentRole.Arrival, LinkSegmentOrientation.Vertical, true, false));

            plans.Add(link.Id, new CanonicalTopologyPlan(
                link.Id, link.SourceId, link.TargetId, family, CanonicalTerminal.SourceBottom, CanonicalTerminal.TargetTop,
                segments.Select(item => item.Role).ToArray(), segments, departure, arrival,
                destinationColumn, isReturn,
                new[] { source.Node.ProjectId, target.Node.ProjectId }
                    .Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal).Cast<string>().ToArray(),
                isReturn ? 4 : destinationColumn || boundary ? 3 : 2));
        }
        return new CanonicalTopologySelection(plans, rejections.OrderBy(item => item, StringComparer.Ordinal).ToArray());
    }

    private static CanonicalTopologyFamily Family(NodeLayout source, NodeLayout target)
    {
        if (!source.IsStandalone && target.IsStandalone) return CanonicalTopologyFamily.InternalToExternal;
        if (source.IsStandalone && !target.IsStandalone) return CanonicalTopologyFamily.ExternalToInternal;
        if (!string.Equals(source.Node.ProjectId, target.Node.ProjectId, StringComparison.Ordinal))
            return CanonicalTopologyFamily.CrossProjectBoundaryTransition;
        if (target.Depth == source.Depth + 1) return CanonicalTopologyFamily.AdjacentDownward;
        if (target.Depth > source.Depth + 1) return CanonicalTopologyFamily.LongDownward;
        if (target.Depth == source.Depth) return CanonicalTopologyFamily.SameLayerReturn;
        return CanonicalTopologyFamily.UpwardReturn;
    }
}
