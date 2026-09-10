using System.Threading;
using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.Core2.Models;
namespace StandardIo.ArchitectureDiagram.Core2.Services.Orchestrations.Commands;
internal interface IDiagramRenderOrchestrationService { Task<DiagramRenderResult> ExecuteAsync(string[] command, CancellationToken cancellationToken); }
