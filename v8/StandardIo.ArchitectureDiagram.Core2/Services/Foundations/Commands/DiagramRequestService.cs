// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.Core2.Brokers.Rendering;
using StandardIo.ArchitectureDiagram.Core2.Models;

namespace StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Commands;
internal sealed class DiagramRequestService(IDiagramRequestBroker broker) : IDiagramRequestService
{
    public Task<byte[]> RenderDiagramRenderRequestAsync(DiagramRenderRequest diagramRenderRequest, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(argument: diagramRenderRequest);
        cancellationToken.ThrowIfCancellationRequested();

        if (diagramRenderRequest.ProjectPaths is null || diagramRenderRequest.ProjectPaths.Length == 0 || diagramRenderRequest.ProjectPaths.Any(predicate: string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException(message: "At least one non-empty project path is required.", paramName: nameof(diagramRenderRequest));
        }

        return broker.RenderAsync(request: diagramRenderRequest, cancellationToken: cancellationToken);
    }
}