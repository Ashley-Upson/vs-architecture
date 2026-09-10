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
internal sealed class DiagramGenerationOrchestrationService(
    IProjectModelBuilderService builderService, IProjectModelTreeService treeService) : IDiagramGenerationOrchestrationService
{
    public async Task<byte[]> GenerateAsync(DiagramGenerationRequest request, CancellationToken cancellationToken, IDiagramRenderer renderer)
    {
        ArgumentNullException.ThrowIfNull(argument: request);
        cancellationToken.ThrowIfCancellationRequested();
        if (request.DiagramType != DiagramTypes.Architecture)
            throw new NotSupportedException(message: "Only Architecture diagrams are implemented. Data relationships are not implemented yet.");
        if (request.ProjectPaths is null || request.ProjectPaths.Length == 0)
            throw new ArgumentException(message: "At least one project path is required.", paramName: nameof(request));
        var trees = new List<ProjectModel>();
        foreach (string path in request.ProjectPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ProjectModel project = await builderService.BuildAsync(projectFilePath: path, cancellationToken: cancellationToken);
            trees.AddRange(collection: treeService.Split(projectModel: project));
        }
        cancellationToken.ThrowIfCancellationRequested();
        return renderer.Render(projectModels: trees.ToArray());
    }
}
