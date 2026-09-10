// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System.Threading;
using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.Core2.Models;

namespace StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Generation;
internal interface IProjectModelBuilderService
{
    Task<ProjectModel> BuildAsync(string projectFilePath, CancellationToken cancellationToken);
}