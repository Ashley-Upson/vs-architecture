using System.Threading;
using System.Threading.Tasks;

namespace StandardIo.ArchitectureDiagram.Core.Brokers.Files;

public interface IDiagramFileBroker
{
    Task WriteTextAsync(string path, string content, CancellationToken cancellationToken = default);
}
