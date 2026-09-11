// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System.Threading;
using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.Core2.Models;

namespace StandardIo.ArchitectureDiagram.Core2.Brokers.Rendering;
internal interface IDiagramRequestBroker
{
    Task<byte[]> RenderAsync(DiagramRenderRequest request, CancellationToken cancellationToken);
}