// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System.Threading;
using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.Core2.Models;

namespace StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Dependencies;
internal interface IProjectDependenciesService
{
    Task PopulateDependenciesAsync(ProjectModel project, CancellationToken cancellationToken);
}