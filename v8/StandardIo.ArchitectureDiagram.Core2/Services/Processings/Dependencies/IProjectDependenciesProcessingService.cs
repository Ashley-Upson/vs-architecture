// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System.Threading;
using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.Core2.Models;

namespace StandardIo.ArchitectureDiagram.Core2.Services.Processings.Dependencies;
internal interface IProjectDependenciesProcessingService
{
    Task PopulateDependenciesAsync(ProjectModel project, CancellationToken cancellationToken);
}