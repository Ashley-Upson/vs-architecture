// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------

using System;
using System.Collections.Generic;
using StandardIo.ArchitectureDiagram.Core2.Models;
using StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Rendering;
namespace StandardIo.ArchitectureDiagram.Core2.Services.Processings.Rendering;
internal sealed class ProjectModelRenderingProcessingService(IProjectModelPresentationService presentationService, IProjectModelLayoutService layoutService) : IProjectModelRenderingProcessingService
{
    public IReadOnlyList<ProjectModelDrawing> Prepare(ProjectModel[] projectModels)
    {
        ArgumentNullException.ThrowIfNull(argument: projectModels);
        var drawings = new List<ProjectModelDrawing>();
        double x = 40;
        foreach (ProjectModel model in projectModels)
        {
            ProjectModelDrawing drawing = layoutService.Layout(presentation: presentationService.Prepare(model: model), index: drawings.Count, left: x);
            drawings.Add(item: drawing);
            x += drawing.Width + 80;
        }
        return drawings;
    }
}
