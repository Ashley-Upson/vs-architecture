using System.Text;
using Microsoft.Extensions.DependencyInjection;
using StandardIo.ArchitectureDiagram.Core2;
using StandardIo.ArchitectureDiagram.Core2.Models;

try
{
    var services = new ServiceCollection();
    services.AddArchitectureDiagram();
    using ServiceProvider provider = services.BuildServiceProvider();
    DiagramRenderResult result = await provider.GetRequiredService<DiagramRenderCommand>().ExecuteAsync(command: args);
    if (result.OutputPath is null)
    {
        Console.WriteLine(Encoding.UTF8.GetString(result.Content));
        return 0;
    }
    Directory.CreateDirectory(Path.GetDirectoryName(result.OutputPath)!);
    await File.WriteAllBytesAsync(result.OutputPath, result.Content);
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception.Message);
    return 1;
}
