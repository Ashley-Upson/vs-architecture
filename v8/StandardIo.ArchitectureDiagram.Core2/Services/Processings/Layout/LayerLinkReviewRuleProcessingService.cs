using System;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core2.Models;
namespace StandardIo.ArchitectureDiagram.Core2.Services.Processings.Layout;
internal sealed class LayerLinkReviewRuleProcessingService : ILayoutRuleProcessingService
{
    public void ApplyRule(RenderModel model)
    {
        if (model.DiagramType != DiagramTypes.Architecture) return;
        var owners = model.Projects.SelectMany(p => p.Nodes.Select(n => (n.Id, Node: n, Project: p))).ToDictionary(x => x.Id);
        RenderConnection Review(RenderConnection edge)
        {
            var source = owners[edge.SourceId]; var target = owners[edge.TargetId];
            var from = source.Node.Category ?? RenderNodeCategory.Other;
            var to = target.Node.Category ?? RenderNodeCategory.Other;
            var order = source.Project.ArchitecturalLayers;
            int a = Array.IndexOf(order, from), b = Array.IndexOf(order, to);
            // Unknown categories and structural/composition links carry no runtime-layer verdict.
            bool bad = false;
            if (!edge.IsComposition && !edge.Inheritance && from != RenderNodeCategory.Other && to != RenderNodeCategory.Other)
            {
                // Exposures start another stack. Brokers may hand off regardless of physical row/project.
                if (from == to && source.Project.Id == target.Project.Id) bad = false;
                else if (to == RenderNodeCategory.Exposure)
                    bad = from != RenderNodeCategory.Broker && a >= 0 && a < order.Length - 1;
                else if (a >= 0 && b >= 0)
                    bad = from != to && b != a + 1;
            }
            return edge with { Stroke = bad ? "#ef4444" : model.Configuration.ColourLines ? target.Node.Fill : "#d1d5db" };
        }
        foreach (var project in model.Projects)
            for (int i = 0; i < project.Connections.Length; i++) project.Connections[i] = Review(project.Connections[i]);
        for (int i = 0; i < model.CrossProjectConnections.Length; i++) model.CrossProjectConnections[i] = Review(model.CrossProjectConnections[i]);
    }
}
