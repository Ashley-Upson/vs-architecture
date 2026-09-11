// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.Core2.Models;
using StandardIo.ArchitectureDiagram.Core2.Services.Processings.Commands;
using StandardIo.ArchitectureDiagram.Core2.Services.Processings.Exports;
using StandardIo.ArchitectureDiagram.Core2.Exposures;

namespace StandardIo.ArchitectureDiagram.Core2.Services.Orchestrations.Commands;
internal sealed class DiagramRenderOrchestrationService(
    ICommandParserProcessingService parser,
    IDiagramRequestProcessingService requestProcessingService,
    IRawJsonExportProcessingService rawJsonExportProcessingService) : IDiagramRenderOrchestrationService
{
    public async Task<DiagramRenderResult> ExecuteAsync(string[] command, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        DiagramRenderRequest request = parser.Parse(command: command);

        if (request.ShowHelp)
        {
            return new DiagramRenderResult(null, Encoding.UTF8.GetBytes(s: DiagramRenderCommand.Usage));
        }

        byte[] bytes = request.Format == DiagramFormats.Json
            ? await rawJsonExportProcessingService.ExportAsync(request: request, cancellationToken: cancellationToken)
            : await requestProcessingService.RenderDiagramRenderRequestAsync(diagramRenderRequest: request, cancellationToken: cancellationToken);
        return new DiagramRenderResult(request.OutputPath, bytes);
    }
}
