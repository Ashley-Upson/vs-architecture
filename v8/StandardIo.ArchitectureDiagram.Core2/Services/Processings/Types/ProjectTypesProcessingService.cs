// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.Core2.Models;
using StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Types;

namespace StandardIo.ArchitectureDiagram.Core2.Services.Processings.Types;
internal sealed class ProjectTypesProcessingService : IProjectTypesProcessingService
{
    private readonly IProjectTypesService projectTypesService;
    public ProjectTypesProcessingService(IProjectTypesService projectTypesService) => this.projectTypesService = projectTypesService;
    public async Task PopulateTypesAsync(ProjectModel project, CancellationToken cancellationToken)
    {
        await projectTypesService.PopulateTypesAsync(project: project, cancellationToken: cancellationToken);

        project.Types = project.Types!.OrderBy(keySelector: type => type.Name, comparer: StringComparer.Ordinal)
            .ToArray();
    }
}