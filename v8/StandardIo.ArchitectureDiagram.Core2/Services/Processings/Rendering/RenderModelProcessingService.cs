// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core2.Models;
using StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Rendering;
namespace StandardIo.ArchitectureDiagram.Core2.Services.Processings.Rendering;
internal sealed class RenderModelProcessingService(IProjectModelCompositionProcessingService compositionService) : IRenderModelProcessingService
{
    public RenderModel PrepareRenderModel(RenderModel renderModel)
    {
        renderModel.LayoutInitialized = false;
        renderModel.ProjectLayoutInitialized = false;
        renderModel.SharedParentLayoutInitialized = false;
        var drawings = new List<ProjectModelDrawing>();
        double x = 40;
        var presentations = compositionService.Prepare(renderModel);
        foreach (var presentation in presentations)
        {
            string id = "tree-" + drawings.Count;
            var drawing = new ProjectModelDrawing(id, presentation.Model, x, 300, 120,
                presentation.Model.Types!.Select((type, index) => new DrawingNode(id + "-node-" + index, type, presentation.Labels[type.Name!],
                    40 + index * (renderModel.Configuration.Architecture.NodeWidth + renderModel.Configuration.Architecture.NodeSpacing), 60,
                    renderModel.Configuration.Architecture.NodeWidth, 60)).ToArray());
            drawings.Add(drawing);
            x += drawing.Width + renderModel.Configuration.Architecture.ProjectSpacing;
        }
        var configuration = renderModel.Configuration;
        var projects = drawings.Select(drawing =>
        {
            var nodes = drawing.Nodes.Select(node =>
            {
                string[] lines = node.Label.Split(separator: '\n');
                var textLines = lines.Select((line, index) => new RenderText(line, node.X + node.Width / 2, node.Y + node.Height / 2 + (index - (lines.Length - 1) / 2d) * 16, Bold: index == 0, FontSize: index > 0 && node.Type.BaseTypeName is not null && index == lines.Length - 1 ? 10 : 12)).ToArray();
                return new RenderNode(node.Id, node.Type.Name!, node.Label, DiagramStyles.GetRoleColour(name: node.Type.Name!, isInternal: node.Type.IsInternal, layerColours: configuration.Architecture.LayerColours), node.X, node.Y, node.Width, node.Height, textLines);
            }).ToArray();
            return new RenderProject(drawing.Id, drawing.Model.Name ?? "Project", drawing.X, 40, drawing.Width, drawing.Height, nodes, Array.Empty<RenderConnection>());
        }).ToArray();
        var crossProjectConnections = new List<RenderConnection>();
        for (int projectIndex = 0; projectIndex < projects.Length; projectIndex++)
        {
            var project = projects[projectIndex];
            var local = project.Nodes.ToDictionary(node => node.TypeName, StringComparer.Ordinal);
            var connections = new List<RenderConnection>();
            foreach (var link in drawings[projectIndex].Model.Dependencies ?? Array.Empty<TypeRelationship>())
            {
                var source = local[link.FromType!];
                var target = local.TryGetValue(link.ToType!, out var localTarget) ? localTarget
                    : projects.Where((candidate, index) => presentations[index].SourceTreeIndex is null
                        || presentations[index].SourceTreeIndex == projectIndex)
                        .SelectMany(candidate => candidate.Nodes).First(node => node.TypeName == link.ToType);
                bool reviewExternal = renderModel.DiagramType == DiagramTypes.Architecture && !configuration.NoDuplicates && configuration.Architecture.InlineExternals &&
                    drawings[projectIndex].Model.Types!.Any(type => type.Name == target.TypeName && !type.IsInternal) &&
                    drawings[projectIndex].Model.Dependencies!.Any(dependency => dependency.FromType == link.FromType && dependency.ToType != link.FromType &&
                        drawings[projectIndex].Model.Types!.Any(type => type.Name == dependency.ToType && type.IsInternal));
                var connection = new RenderConnection(project.Id + "-edge-" + (connections.Count + crossProjectConnections.Count),
                    source.Id, target.Id, source.TypeName, target.TypeName, link.DependencyType == DependencyType.Inheritance,
                    Array.Empty<DrawingPoint>(), reviewExternal ? "#ef4444" : configuration.ColourLines ? target.Fill : "#d1d5db");
                if (local.ContainsKey(target.TypeName)) connections.Add(connection);
                else crossProjectConnections.Add(connection);
            }
            projects[projectIndex] = project with { Connections = connections.ToArray() };
        }
        projects = projects.Where((project, index) => project.Nodes.Length > 0 ||
            index < renderModel.ProjectModels.Length && (renderModel.ProjectModels[index].Types?.Length ?? 0) == 0).ToArray();
        renderModel.Projects = projects;
        renderModel.CrossProjectConnections = crossProjectConnections.ToArray();
        renderModel.Width = projects.Select(project => project.X + project.Width + 40).DefaultIfEmpty(300).Max();
        renderModel.Height = projects.Select(project => project.Y + project.Height + 40).DefaultIfEmpty(200).Max();
        return renderModel;
    }
}