using System.Threading;
using System.Threading.Tasks;

namespace StandardIo.ArchitectureDiagram.Core.Brokers.Workspaces;

public interface IWorkspacePathBroker
{
    Task<WorkspacePathLoadResult> LoadAsync(
        string inputPath,
        WorkspacePathLoadOptions options,
        CancellationToken cancellationToken = default);
}
