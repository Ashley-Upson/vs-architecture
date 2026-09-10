using System.Text;
using System.Threading;
using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.Core2.Models;
using StandardIo.ArchitectureDiagram.Core2.Services.Processings.Commands;
using StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Commands;
namespace StandardIo.ArchitectureDiagram.Core2.Services.Orchestrations.Commands;
internal sealed class DiagramRenderOrchestrationService(ICommandParserProcessingService parser,
    IDiagramRenderRequestService requestService) : IDiagramRenderOrchestrationService
{
    public async Task<DiagramRenderResult> ExecuteAsync(string[] command, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        DiagramRenderRequest request = parser.Parse(command: command);
        if (request.ShowHelp) return new DiagramRenderResult(null, Encoding.UTF8.GetBytes(DiagramRenderCommand.Usage));
        byte[] bytes = await requestService.RenderAsync(request: request, cancellationToken: cancellationToken);
        return new DiagramRenderResult(request.OutputPath, bytes);
    }
}
