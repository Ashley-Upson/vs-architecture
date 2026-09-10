// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using StandardIo.ArchitectureDiagram.Core2.Models;
namespace StandardIo.ArchitectureDiagram.Core2.Services.Processings.Rendering;
internal interface IProjectModelCompositionProcessingService
{
    ProjectModelPresentation[] Prepare(ProjectModel[] projectModels, DiagramTypes diagramType);
}
