// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using StandardIo.ArchitectureDiagram.Core2.Models;

namespace StandardIo.ArchitectureDiagram.Core2.Services.Foundations.ProjectModels;
internal interface IProjectModelService
{
    ProjectModel CreateProjectModel(ProjectModel source, DefinedType[] types, TypeRelationship[] dependencies);
}