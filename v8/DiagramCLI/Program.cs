// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------

using System.Text;
using Microsoft.Extensions.DependencyInjection;
using StandardIo.ArchitectureDiagram.Core2;
using StandardIo.ArchitectureDiagram.Core2.Models;
using StandardIo.ArchitectureDiagram.Core2.Exposures;

try
{
    var services = new ServiceCollection();
    services.AddArchitectureDiagram();
    using ServiceProvider provider = services.BuildServiceProvider();
    DiagramRenderResult result = await provider.GetRequiredService<DiagramRenderCommand>()
        .ExecuteAsync(command: args);
    if (result.OutputPath is null)
    {
        Console.WriteLine(value: Encoding.UTF8.GetString(bytes: result.Content));
        return 0;
    }

    Directory.CreateDirectory(Path.GetDirectoryName(path: result.OutputPath)!);
    await File.WriteAllBytesAsync(path: result.OutputPath, bytes: result.Content);
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine(value: exception.Message);
    return 1;
}