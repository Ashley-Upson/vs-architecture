// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.Core2.Models;
using StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Generation;

namespace StandardIo.ArchitectureDiagram.Core2.Services.Orchestrations.Generation;
internal sealed class DiagramGenerationOrchestrationService(IProjectModelBuilderService builderService, IProjectModelTreeService treeService, IDiagramRenderer renderer) : IDiagramGenerationOrchestrationService
{
    public async Task<byte[]> GenerateAsync(DiagramRenderRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(argument: request);
        cancellationToken.ThrowIfCancellationRequested();

        if (request.ProjectPaths is null || request.ProjectPaths.Length == 0)
        {
            throw new ArgumentException(message: "At least one project path is required.", paramName: nameof(request));
        }

        var models = new List<ProjectModel>();

        foreach (string path in request.ProjectPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ProjectModel project = await builderService.BuildAsync(projectFilePath: path, cancellationToken: cancellationToken);

            if (request.RenderConfiguration.NoDuplicates)
            {
                models.Add(item: project);
            }
            else
            {
                models.AddRange(collection: treeService.Split(projectModel: project));
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        return renderer.Render(new RenderModel(models.ToArray(), request.RenderConfiguration, request.DiagramType));
    }
}
