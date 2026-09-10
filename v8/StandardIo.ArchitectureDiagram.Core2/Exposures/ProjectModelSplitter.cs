// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------

using StandardIo.ArchitectureDiagram.Core2.Models;
using StandardIo.ArchitectureDiagram.Core2.Services.Foundations.ProjectModels;
using StandardIo.ArchitectureDiagram.Core2.Services.Processings.ProjectModels;

namespace StandardIo.ArchitectureDiagram.Core2;

public sealed class ProjectModelSplitter
{
    private readonly IProjectModelSplitterProcessingService projectModelSplitterProcessingService;

    internal ProjectModelSplitter(IProjectModelSplitterProcessingService service) => this.projectModelSplitterProcessingService = service;

    public ProjectModelSplitter()
    {
        this.projectModelSplitterProcessingService = new ProjectModelSplitterProcessingService(
            projectModelService: new ProjectModelService());
    }

    public ProjectModel[] Split(ProjectModel projectModel) =>
        this.projectModelSplitterProcessingService.Split(projectModel: projectModel);
}
