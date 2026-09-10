// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System;
using System.Threading;
using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Commands;
using StandardIo.ArchitectureDiagram.Core2.Models;

namespace StandardIo.ArchitectureDiagram.Core2.Services.Processings.Commands;
internal sealed class DiagramRequestProcessingService(IDiagramRequestService requestService) : IDiagramRequestProcessingService
{
    public Task<byte[]> RenderDiagramRenderRequestAsync(DiagramRenderRequest diagramRenderRequest, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(argument: diagramRenderRequest);
        cancellationToken.ThrowIfCancellationRequested();
        return requestService.RenderDiagramRenderRequestAsync(diagramRenderRequest: diagramRenderRequest, cancellationToken: cancellationToken);
    }
}