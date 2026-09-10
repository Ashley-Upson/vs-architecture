// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core2.Models;
using StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Rendering;
namespace StandardIo.ArchitectureDiagram.Core2.Services.Processings.Layout;
internal sealed class RoutingLayoutRuleProcessingService : ILayoutRuleProcessingService
{
    public void ApplyRule(RenderModel renderModel)
    {
        foreach (var project in renderModel.Projects)
        {
            var model = LayoutGraph.ToProjectModel(project);
            var drawing = new ProjectModelDrawing(project.Id, model, project.X, project.Width, project.Height,
                project.Nodes.Select(node => new DrawingNode(node.Id, new DefinedType { Name = node.TypeName }, node.Label, node.X, node.Y, node.Width, node.Height)).ToArray());
            var routes = DiagramRouting.CreateRoutes(drawing, renderModel.Configuration);
            for (int index = 0; index < routes.Length; index++)
                project.Connections[index] = project.Connections[index] with { Points = routes[index].Points };
        }
    }
}
