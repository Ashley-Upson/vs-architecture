using System;
using System.Collections.Generic;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core.Models;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

/// <summary>
/// Experimental typed-renderer policy: project-owned external nodes are placed after the internal hierarchy
/// on one terminal layer. This is deliberately not a settings or compatibility contract.
/// </summary>
internal static class ExperimentalExternalTerminalLayerPlacement
{
    internal static Dictionary<string, NodeLayout> Apply(
        RenderGraph projectGraph,
        IReadOnlyDictionary<string, NodeLayout> internalNodes,
        DiagramSettings settings)
    {
        var incidentNodeIds = new HashSet<string>(
            projectGraph.Links.SelectMany(link => new[] { link.SourceId, link.TargetId }),
            StringComparer.Ordinal);
        var result = internalNodes.ToDictionary(
            item => item.Key,
            item => incidentNodeIds.Contains(item.Key) ? item.Value with { IsStandalone = false } : item.Value,
            StringComparer.Ordinal);
        var externalNodes = projectGraph.Nodes.Where(node => node.IsExternal)
            .OrderBy(node => node.Id, StringComparer.Ordinal).ToArray();
        if (externalNodes.Length == 0) return result;

        var deepestInternalDepth = internalNodes.Values.Select(node => node.Depth).DefaultIfEmpty(-1).Max();
        var externalDepth = deepestInternalDepth + 1;
        var externalY = internalNodes.Values.Select(node => node.Rect.Bottom)
            .DefaultIfEmpty(settings.Layout.ContainerPadding * 2 - settings.Layout.VerticalSpacing).Max() +
            settings.Layout.VerticalSpacing;
        var widths = PlacementPipeline.CalculateWidths(projectGraph, settings);
        var internalById = result.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        var ordering = externalNodes.Select(node => Order(node, projectGraph.Links, internalById))
            .OrderBy(item => item.Connected ? 0 : 1)
            .ThenBy(item => item.MedianX)
            .ThenBy(item => item.MinimumX)
            .ThenBy(item => item.Node.Id, StringComparer.Ordinal)
            .ToArray();

        var cursorX = internalNodes.Values.Select(node => node.Rect.X)
            .DefaultIfEmpty(settings.Layout.ContainerPadding * 2).Min();
        foreach (var item in ordering)
        {
            var node = item.Node;
            var preferredX = item.Connected
                ? (int)Math.Round(item.MedianX - widths[node.Id] / 2.0, MidpointRounding.AwayFromZero)
                : cursorX;
            var x = Math.Max(cursorX, preferredX);
            result[node.Id] = new NodeLayout(node,
                new Rect(x, externalY, widths[node.Id], settings.Layout.NodeHeight), externalDepth, false);
            cursorX = x + widths[node.Id] + settings.Layout.HorizontalSpacing;
        }
        return result;
    }

    private static ExternalOrder Order(
        RenderNode external,
        IReadOnlyList<RenderLink> links,
        IReadOnlyDictionary<string, NodeLayout> internalNodes)
    {
        var centres = links.Where(link =>
                string.Equals(link.SourceId, external.Id, StringComparison.Ordinal) ||
                string.Equals(link.TargetId, external.Id, StringComparison.Ordinal))
            .Select(link => string.Equals(link.SourceId, external.Id, StringComparison.Ordinal)
                ? link.TargetId : link.SourceId)
            .Where(internalNodes.ContainsKey)
            .Distinct(StringComparer.Ordinal)
            .Select(id => internalNodes[id].Rect.CenterX)
            .OrderBy(value => value)
            .ToArray();
        if (centres.Length == 0) return new ExternalOrder(external, false, 0, 0);
        var middle = centres.Length / 2;
        var median = centres.Length % 2 == 1
            ? centres[middle]
            : (centres[middle - 1] + centres[middle]) / 2.0;
        return new ExternalOrder(external, true, median, centres[0]);
    }

    internal sealed record ExternalOrder(RenderNode Node, bool Connected, double MedianX, int MinimumX);
}
