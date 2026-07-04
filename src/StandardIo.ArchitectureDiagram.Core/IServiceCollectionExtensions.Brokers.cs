using Microsoft.Extensions.DependencyInjection;
using StandardIo.ArchitectureDiagram.Core.Brokers.Files;
using StandardIo.ArchitectureDiagram.Core.Brokers.Roslyn;
using StandardIo.ArchitectureDiagram.Core.Brokers.Workspaces;

namespace StandardIo.ArchitectureDiagram.Core;

public static partial class IServiceCollectionExtensions
{
    private static void AddArchitectureDiagramBrokers(IServiceCollection services)
    {
        services.AddTransient<IDiagramFileBroker, DiagramFileBroker>();
        services.AddTransient<IRoslynBroker, RoslynBroker>();
        services.AddTransient<IWorkspacePathBroker, WorkspacePathBroker>();
    }
}
