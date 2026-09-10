using System.Threading;
using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.Core2.Models;
using StandardIo.ArchitectureDiagram.Core2.Services.Orchestrations.Commands;
namespace StandardIo.ArchitectureDiagram.Core2;
public sealed class DiagramRenderCommand
{
    public const string Usage = "Usage: DiagramCLI <Architecture|Data> <project.csproj> [additional-project.csproj ...] --output <file> [--format <name>]\nAliases: -o output, -f format. Without --format, the output extension selects a registered renderer.";
    private readonly IDiagramRenderOrchestrationService service;
    internal DiagramRenderCommand(IDiagramRenderOrchestrationService service) => this.service = service;
    public Task<DiagramRenderResult> ExecuteAsync(string[] command, CancellationToken cancellationToken = default) =>
        this.service.ExecuteAsync(command: command, cancellationToken: cancellationToken);
}
