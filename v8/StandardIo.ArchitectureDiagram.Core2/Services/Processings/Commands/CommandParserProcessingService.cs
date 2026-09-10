using System;
using System.Collections.Generic;
using System.IO;
using StandardIo.ArchitectureDiagram.Core2.Models;
namespace StandardIo.ArchitectureDiagram.Core2.Services.Processings.Commands;
internal sealed class CommandParserProcessingService : ICommandParserProcessingService
{
    public DiagramRenderRequest Parse(string[] command)
    {
        ArgumentNullException.ThrowIfNull(argument: command);
        if (command.Length == 1 && command[0] is "--help" or "-h") return new DiagramRenderRequest { ShowHelp = true };
        if (command.Length < 2 || !(string.Equals(command[0], "Architecture", StringComparison.OrdinalIgnoreCase)
            || string.Equals(command[0], "Data", StringComparison.OrdinalIgnoreCase))) throw new ArgumentException(DiagramRenderCommand.Usage);
        string? output = null, format = null;
        var projects = new List<string>();
        for (int index = 1; index < command.Length; index++)
        {
            string argument = command[index];
            if (argument is "--output" or "-o" or "--format" or "-f")
            {
                if (++index >= command.Length || string.IsNullOrWhiteSpace(command[index]) || command[index].StartsWith('-'))
                    throw new ArgumentException("Missing value for " + argument);
                if (argument is "--output" or "-o")
                {
                    if (output is not null) throw new ArgumentException("Supply output only once.");
                    output = Path.GetFullPath(command[index]);
                }
                else
                {
                    if (format is not null) throw new ArgumentException("Supply format only once.");
                    format = command[index];
                }
            }
            else
            {
                if (string.IsNullOrWhiteSpace(argument) || argument.StartsWith('-')) throw new ArgumentException("Unknown argument: " + argument);
                if (!string.Equals(Path.GetExtension(argument), ".csproj", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Expected a .csproj path: " + argument);
                projects.Add(Path.GetFullPath(argument));
            }
        }
        if (output is null || projects.Count == 0) throw new ArgumentException(DiagramRenderCommand.Usage);
        return new DiagramRenderRequest { ProjectPaths = projects.ToArray(), OutputPath = output, Format = format,
            DiagramType = Enum.Parse<DiagramTypes>(command[0], ignoreCase: true) };
    }
}
