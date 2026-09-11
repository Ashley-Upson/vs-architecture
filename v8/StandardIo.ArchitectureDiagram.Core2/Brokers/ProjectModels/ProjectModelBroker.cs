// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System.Threading;
using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.Core2.Models;
using StandardIo.ArchitectureDiagram.Core2.Exposures;

namespace StandardIo.ArchitectureDiagram.Core2.Brokers.ProjectModels;
internal sealed class ProjectModelBroker(ProjectModelBuilder exposure) : IProjectModelBroker
{
    public Task<ProjectModel> BuildAsync(string projectFilePath, CancellationToken cancellationToken) =>
        exposure.BuildAsync(projectFilePath: projectFilePath, cancellationToken: cancellationToken);
}