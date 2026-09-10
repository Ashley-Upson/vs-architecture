using System.Threading;
using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.Core2.Models;
namespace StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Commands;
internal interface IDiagramRenderRequestService { Task<byte[]> RenderAsync(DiagramRenderRequest request, CancellationToken cancellationToken); }
