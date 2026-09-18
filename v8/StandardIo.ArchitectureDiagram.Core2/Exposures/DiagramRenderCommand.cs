// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System.Threading;
using System.Threading.Tasks;
using System;
using System.Collections.Generic;
using StandardIo.ArchitectureDiagram.Core2.Models;
using StandardIo.ArchitectureDiagram.Core2.Services.Orchestrations.Commands;

namespace StandardIo.ArchitectureDiagram.Core2.Exposures;
public sealed class DiagramRenderCommand
{
    public const string Usage = "Usage: DiagramCLI <Architecture|Composition|CallChain|DataModel|All> <project.csproj> [additional-project.csproj ...] --output <file> [--format <DrawIO|Html>] [--config <json-file>] [--noduplicates] [--horizontal-offset <pixels>] [--colour-lines] [--max-layout-iterations <count>] [--next <another complete command>]\nHorizontal offset defaults to 10 pixels. Aliases: -o output, -f format, -c config. Data is an alias for DataModel. Explicit flags override JSON settings. Without --format, Draw.io is used. The output path does not select the renderer. --noduplicates keeps each project intact instead of splitting it into duplicated trees. Commands separated by --next share the process-lifetime project-model cache.";
    private readonly IDiagramRenderOrchestrationService service;
    internal DiagramRenderCommand(IDiagramRenderOrchestrationService service) => this.service = service;
    public Task<DiagramRenderResult> ExecuteAsync(string[] command, CancellationToken cancellationToken = default) =>
        this.service.ExecuteAsync(command: command, cancellationToken: cancellationToken);

    public static string[][] SplitBatchCommands(string[] command)
    {
        ArgumentNullException.ThrowIfNull(argument: command);
        var commands = new List<string[]>();
        var currentCommand = new List<string>();

        foreach (string argument in command)
        {
            if (argument == "--next")
            {
                AddCurrentCommand();
            }
            else
            {
                currentCommand.Add(item: argument);
            }
        }

        AddCurrentCommand();
        return commands.ToArray();

        void AddCurrentCommand()
        {
            if (currentCommand.Count == 0)
            {
                throw new ArgumentException(
                    message: "Each --next separator must be between complete commands.",
                    paramName: nameof(command));
            }

            commands.Add(item: currentCommand.ToArray());
            currentCommand.Clear();
        }
    }
}
