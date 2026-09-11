// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System.Threading;
using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.Core2.Models;

namespace StandardIo.ArchitectureDiagram.Core2.Services.Processings.Exports;
internal interface IRawJsonExportProcessingService
{
    Task<byte[]> ExportAsync(DiagramRenderRequest request, CancellationToken cancellationToken);
}
