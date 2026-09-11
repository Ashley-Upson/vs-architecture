// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core2.Models;
using StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Rendering;
namespace StandardIo.ArchitectureDiagram.Core2.Services.Processings.Layout;
internal sealed class CrossProjectRoutingLayoutRuleProcessingService : ILayoutRuleProcessingService
{
    public void ApplyRule(RenderModel renderModel)
    {
        if (renderModel.CrossProjectConnections.Length == 0) return;
        var nodes = renderModel.Projects.SelectMany(project => project.Nodes.Select(node =>
            new DrawingNode(node.Id, new DefinedType { Name = node.Id }, node.Label,
                project.X + node.X, project.Y + node.Y, node.Width, node.Height))).ToArray();
        var projectModel = new ProjectModel
        {
            Dependencies = renderModel.CrossProjectConnections.Select(edge => new TypeRelationship
            {
                FromType = edge.SourceId, ToType = edge.TargetId,
                DependencyType = edge.Inheritance ? DependencyType.Inheritance : DependencyType.Consumed
            }).ToArray()
        };
        var drawing = new ProjectModelDrawing("cross-project", projectModel, 0, renderModel.Width, renderModel.Height, nodes);
        var reservedRoutes = renderModel.Projects.SelectMany(project => project.Connections.Select(edge =>
            (edge.TargetId, edge.Points.Select(point => new DrawingPoint(project.X + point.X, project.Y + point.Y)).ToArray())));
        var routes = DiagramRouting.CreateRoutes(drawing, renderModel.Configuration, reservedRoutes);
        for (int index = 0; index < routes.Length; index++)
            renderModel.CrossProjectConnections[index] = renderModel.CrossProjectConnections[index] with { Points = routes[index].Points };
    }
}
