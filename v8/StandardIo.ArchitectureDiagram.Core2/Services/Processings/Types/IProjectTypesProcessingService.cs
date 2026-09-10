// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.Core2.Models;

namespace StandardIo.ArchitectureDiagram.Core2.Services.Processings.Types;

internal interface IProjectTypesProcessingService
{
    Task PopulateTypesAsync(ProjectModel project, CancellationToken cancellationToken);
}
