// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System.Threading;
using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.Core2.Models;
using StandardIo.ArchitectureDiagram.Core2.Services.Orchestrations.Commands;

namespace StandardIo.ArchitectureDiagram.Core2.Exposures;
public sealed class DiagramRenderCommand
{
    public const string Usage = "Usage: DiagramCLI <Architecture|CallChain|DataModel> <project.csproj> [additional-project.csproj ...] --output <file> [--format <DrawIO|Html>] [--config <json-file>] [--noduplicates] [--horizontal-offset <pixels>] [--colour-lines] [--max-layout-iterations <count>]\nHorizontal offset defaults to 10 pixels. Aliases: -o output, -f format, -c config. Data is an alias for DataModel. Explicit flags override JSON settings. Without --format, Draw.io is used. The output path does not select the renderer. --noduplicates keeps each project intact instead of splitting it into duplicated trees.";
    private readonly IDiagramRenderOrchestrationService service;
    internal DiagramRenderCommand(IDiagramRenderOrchestrationService service) => this.service = service;
    public Task<DiagramRenderResult> ExecuteAsync(string[] command, CancellationToken cancellationToken = default) =>
        this.service.ExecuteAsync(command: command, cancellationToken: cancellationToken);
}