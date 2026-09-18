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
    DiagramRenderCommand renderCommand = provider.GetRequiredService<DiagramRenderCommand>();
    int exitCode = 0;

    foreach (string[] command in DiagramRenderCommand.SplitBatchCommands(command: args))
    {
        try
        {
            DiagramRenderResult result = await renderCommand.ExecuteAsync(command: command);

            if (result.OutputPath is null)
            {
                Console.WriteLine(value: Encoding.UTF8.GetString(bytes: result.Content));
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path: result.OutputPath)!);
            await File.WriteAllBytesAsync(path: result.OutputPath, bytes: result.Content);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(value: exception.Message);
            exitCode = 1;
        }
    }

    return exitCode;
}
catch (Exception exception)
{
    Console.Error.WriteLine(value: exception.Message);
    return 1;
}
