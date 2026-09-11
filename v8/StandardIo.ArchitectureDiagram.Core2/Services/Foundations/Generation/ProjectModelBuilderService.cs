// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System.Threading;
using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.Core2.Models;
using StandardIo.ArchitectureDiagram.Core2.Brokers.ProjectModels;

namespace StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Generation;
internal sealed class ProjectModelBuilderService(IProjectModelBroker broker) : IProjectModelBuilderService
{
    public Task<ProjectModel> BuildAsync(string projectFilePath, CancellationToken cancellationToken) =>
        broker.BuildAsync(projectFilePath: projectFilePath, cancellationToken: cancellationToken);
}