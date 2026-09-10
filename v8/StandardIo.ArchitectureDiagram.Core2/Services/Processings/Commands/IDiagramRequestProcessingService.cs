// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System.Threading;
using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.Core2.Models;

namespace StandardIo.ArchitectureDiagram.Core2.Services.Processings.Commands;
internal interface IDiagramRequestProcessingService
{
    Task<byte[]> RenderDiagramRenderRequestAsync(DiagramRenderRequest diagramRenderRequest, CancellationToken cancellationToken);
}