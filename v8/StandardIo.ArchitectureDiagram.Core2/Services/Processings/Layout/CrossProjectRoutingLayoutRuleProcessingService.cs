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
        if (renderModel.DiagramType == DiagramTypes.Architecture)
        {
            var local = renderModel.Projects.SelectMany(project => project.Connections.Select((edge,index)=>(Project:project,Index:index,Edge:edge))).ToArray();
            var allEdges = local.Select(item=>item.Edge).Concat(renderModel.CrossProjectConnections).ToArray();
            var combined = new ProjectModelDrawing("combined",new ProjectModel
            {
                Dependencies=allEdges.Select(edge=>new TypeRelationship {FromType=edge.SourceId,ToType=edge.TargetId}).ToArray()
            },0,renderModel.Width,renderModel.Height,nodes);
            var combinedRoutes=DiagramRouting.CreateRoutes(combined,renderModel.Configuration);
            for(int index=0;index<local.Length;index++)
            {
                var item=local[index];
                item.Project.Connections[item.Index]=item.Edge with { Points=combinedRoutes[index].Points
                    .Select(point=>new DrawingPoint(point.X-item.Project.X,point.Y-item.Project.Y)).ToArray() };
            }
            for(int index=0;index<renderModel.CrossProjectConnections.Length;index++)
                renderModel.CrossProjectConnections[index]=renderModel.CrossProjectConnections[index] with {Points=combinedRoutes[local.Length+index].Points};
            return;
        }
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

        // Local and cross-project links leave the same physical node. Reconcile
        // their exit order together once both sets have their final gutter bends.
        var localEdges = renderModel.Projects.SelectMany(project => project.Connections.Select((edge,index) =>
            (Project:project,Index:index,Route:new DrawingRoute(new TypeRelationship {FromType=edge.SourceId,ToType=edge.TargetId},
                edge.Points.Select(point=>new DrawingPoint(project.X+point.X,project.Y+point.Y)).ToArray())))).ToArray();
        var crossRoutes = renderModel.CrossProjectConnections.Select(edge=>new DrawingRoute(
            new TypeRelationship {FromType=edge.SourceId,ToType=edge.TargetId},edge.Points)).ToArray();
        DiagramRouting.OrderSourceExits(localEdges.Select(edge=>edge.Route).Concat(crossRoutes).ToArray(),
            nodes.ToDictionary(node=>node.Id),renderModel.Configuration.HorizontalOffset);
        foreach (var edge in localEdges)
            edge.Project.Connections[edge.Index] = edge.Project.Connections[edge.Index] with
            { Points=edge.Route.Points.Select(point=>new DrawingPoint(point.X-edge.Project.X,point.Y-edge.Project.Y)).ToArray() };
    }
}
