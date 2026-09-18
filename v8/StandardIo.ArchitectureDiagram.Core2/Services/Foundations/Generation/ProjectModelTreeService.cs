// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.Core2.Models;
using StandardIo.ArchitectureDiagram.Core2.Brokers.ProjectModels;

namespace StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Generation;
internal sealed class ProjectModelTreeService(IProjectModelSplitterBroker broker) : IProjectModelTreeService
{
    private static readonly ConditionalWeakTable<ProjectModel, ProjectModel[]> projectTrees = new();

    public ProjectModel[] Split(ProjectModel projectModel) =>
        projectTrees.GetValue(
            key: projectModel,
            createValueCallback: model => broker.Split(projectModel: model));
}
