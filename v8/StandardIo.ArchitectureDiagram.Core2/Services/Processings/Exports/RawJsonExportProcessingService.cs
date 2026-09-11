// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System;
using System.Threading;
using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.Core2.Models;
using StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Exports;
using StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Generation;

namespace StandardIo.ArchitectureDiagram.Core2.Services.Processings.Exports;
internal sealed class RawJsonExportProcessingService(
    IProjectModelBuilderService projectModelBuilderService,
    IRawJsonExportService rawJsonExportService) : IRawJsonExportProcessingService
{
    public async Task<byte[]> ExportAsync(
        DiagramRenderRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(argument: request);
        var projects = new ProjectModel[request.ProjectPaths.Length];

        for (int index = 0; index < request.ProjectPaths.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            projects[index] = await projectModelBuilderService.BuildAsync(
                projectFilePath: request.ProjectPaths[index],
                cancellationToken: cancellationToken);
        }

        return rawJsonExportService.Export(
            diagramType: request.DiagramType,
            projects: projects);
    }
}
