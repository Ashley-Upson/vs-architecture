// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.Core2.Models;

namespace StandardIo.ArchitectureDiagram.Core2.Services.Orchestrations.Projects;

internal interface IProjectModelOrchestrationService
{
    Task<ProjectModel> GenerateProjectModelAsync(string projectFilePath, CancellationToken cancellationToken = default);
}
