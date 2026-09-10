// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using StandardIo.ArchitectureDiagram.Core2.Models;

namespace StandardIo.ArchitectureDiagram.Core2.Services.Processings.ProjectModels;
internal interface IProjectModelSplitterProcessingService
{
    ProjectModel[] Split(ProjectModel projectModel);
}