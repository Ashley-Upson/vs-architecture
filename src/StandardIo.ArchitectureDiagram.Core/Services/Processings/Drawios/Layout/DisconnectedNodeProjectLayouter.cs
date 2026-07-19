using System;
using System.Collections.Generic;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core.Models;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal static class DisconnectedNodeProjectLayouter
{
    internal const string ProjectId = "__disconnected_nodes__";
    internal const string ProjectName = "Disconnected Nodes";

    public static DisconnectedNodeProjectLayout? Create(
        RenderGraph graph,
        IReadOnlyDictionary<string, NodeLayout> positionedNodes,
        DiagramSettings settings)
    {
        var incident = new HashSet<string>(graph.Links.SelectMany(link => new[] { link.SourceId, link.TargetId }), StringComparer.Ordinal);
        var disconnected = positionedNodes.Values.Where(node => !incident.Contains(node.Node.Id))
            .OrderBy(node => node.Node.Order).ThenBy(node => node.Node.Id, StringComparer.Ordinal).ToArray();
        if (disconnected.Length == 0) return null;

        var perLayer = (int)Math.Ceiling(Math.Sqrt(disconnected.Length));
        var startX = positionedNodes.Values.Where(node => incident.Contains(node.Node.Id))
            .Select(node => node.Rect.Right).DefaultIfEmpty(settings.Layout.ContainerPadding).Max() +
            settings.Layout.StandaloneGroupSpacing;
        var top = settings.Layout.ContainerPadding * 2 + settings.Layout.ProjectHeaderHeight;
        var rows = disconnected.Select((node, index) => new { node, row = index / perLayer })
            .GroupBy(item => item.row).OrderBy(group => group.Key).ToArray();
        var rowWidths = rows.Select(row => row.Sum(item => item.node.Rect.Width) +
            Math.Max(0, row.Count() - 1) * settings.Layout.HorizontalSpacing).ToArray();
        var contentWidth = rowWidths.Max();
        var nodes = new Dictionary<string, NodeLayout>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            var x = startX + settings.Layout.ContainerPadding + (contentWidth - rowWidths[row.Key]) / 2;
            foreach (var item in row)
            {
                var owned = item.node.Node with { ProjectId = ProjectId };
                nodes[owned.Id] = item.node with
                {
                    Node = owned,
                    Rect = new Rect(x, top + row.Key * (settings.Layout.NodeHeight + settings.Layout.VerticalSpacing),
                        item.node.Rect.Width, item.node.Rect.Height),
                    IsStandalone = true
                };
                x += item.node.Rect.Width + settings.Layout.HorizontalSpacing;
            }
        }
        var project = new RenderProject(ProjectId, ProjectName, graph.Projects.Count);
        var height = rows.Length * settings.Layout.NodeHeight + Math.Max(0, rows.Length - 1) * settings.Layout.VerticalSpacing +
            settings.Layout.ProjectHeaderHeight + settings.Layout.ContainerPadding * 2;
        var layout = new ProjectLayout(project, new Rect(startX, settings.Layout.ContainerPadding,
            contentWidth + settings.Layout.ContainerPadding * 2, height));
        return new DisconnectedNodeProjectLayout(project, layout, nodes, perLayer,
            disconnected.Select(node => node.Node.Id).ToArray());
    }
}
