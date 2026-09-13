using System;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core2.Models;
namespace StandardIo.ArchitectureDiagram.Core2.Services.Processings.Layout;
internal sealed class LayerLinkReviewRuleProcessingService : ILayoutRuleProcessingService
{
    public void ApplyRule(RenderModel model)
    {
        if (model.DiagramType != DiagramTypes.Architecture) return;
        RenderConnection Review(RenderConnection edge, double sourceY, double targetY, double[] rows, string targetFill) => edge with
        {
            Stroke = Array.IndexOf(rows, targetY) != Array.IndexOf(rows, sourceY) + 1
                ? "#ef4444" : model.Configuration.ColourLines ? targetFill : "#d1d5db"
        };
        foreach (var project in model.Projects)
        {
            var nodes = project.Nodes.ToDictionary(n => n.Id);
            var rows = project.Nodes.Select(n => n.Y).Distinct().OrderBy(y => y).ToArray();
            for (int i = 0; i < project.Connections.Length; i++)
            {
                var edge = project.Connections[i];
                project.Connections[i] = Review(edge, nodes[edge.SourceId].Y, nodes[edge.TargetId].Y, rows, nodes[edge.TargetId].Fill);
            }
        }
        var owners = model.Projects.SelectMany(project => project.Nodes.Select(node => (node.Id, Project: project, Node: node))).ToDictionary(item => item.Id);
        for (int i = 0; i < model.CrossProjectConnections.Length; i++)
        {
            var edge = model.CrossProjectConnections[i];
            var source = owners[edge.SourceId];
            var target = owners[edge.TargetId];
            // Crossing the boundary joins the last source layer to the first destination layer.
            bool adjacent = source.Node.Y == source.Project.Nodes.Max(n => n.Y)
                && target.Node.Y == target.Project.Nodes.Min(n => n.Y)
                && target.Project.Y > source.Project.Y;
            model.CrossProjectConnections[i] = edge with { Stroke = adjacent
                ? model.Configuration.ColourLines ? target.Node.Fill : "#d1d5db" : "#ef4444" };
        }
    }
}
