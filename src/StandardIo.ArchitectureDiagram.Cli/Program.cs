using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using StandardIo.ArchitectureDiagram.Core;
using StandardIo.ArchitectureDiagram.Core.Brokers.Files;
using StandardIo.ArchitectureDiagram.Core.Brokers.Workspaces;
using StandardIo.ArchitectureDiagram.Core.Exposures.Diagrams;
using StandardIo.ArchitectureDiagram.Core.Models;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;
using StandardIo.ArchitectureDiagram.Core.Services.Processings.Diagrams;

namespace StandardIo.ArchitectureDiagram.Cli;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            var options = CliOptions.Parse(args);
            if (options.ShowHelp)
            {
                WriteUsage();
                return 0;
            }

            var settings = string.IsNullOrWhiteSpace(options.SettingsPath)
                ? DiagramSettings.CreateDefault()
                : SettingsSerializer.Import(File.ReadAllText(options.SettingsPath));

            if (!string.IsNullOrWhiteSpace(options.RendererId))
            {
                settings.OutputRenderer = options.RendererId;
            }

            using var provider = new ServiceCollection()
                .AddArchitectureDiagram()
                .BuildServiceProvider();
            if (options.EmitOnValidationFailure)
            {
                return await GenerateDiagnosticAsync(provider, options, settings).ConfigureAwait(false);
            }

            var result = await provider
                .GetRequiredService<IDiagramGenerationExposure>()
                .GenerateAsync(options.InputPath!, settings, options.OutputPath, options.ProjectFilter)
                .ConfigureAwait(false);
            await provider
                .GetRequiredService<IDiagramFileBroker>()
                .WriteTextAsync(result.OutputPath, result.Content)
                .ConfigureAwait(false);
            Console.WriteLine(result.OutputPath);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static void WriteUsage()
    {
        Console.WriteLine("Usage: StandardIo.ArchitectureDiagram.Cli <path> [--settings <json>] [--renderer <drawio|json>] [--output <path>] [--project <name-or-path>] [--emit-on-validation-failure] [--diagnostics-output <json>]");
    }

    private static async Task<int> GenerateDiagnosticAsync(
        ServiceProvider provider,
        CliOptions options,
        DiagramSettings settings)
    {
        if (!string.Equals(settings.OutputRenderer, "drawio", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("--emit-on-validation-failure is supported only by the drawio renderer.");
        }

        var target = await provider.GetRequiredService<IWorkspacePathBroker>()
            .LoadAsync(
                options.InputPath!,
                new WorkspacePathLoadOptions { ProjectFilter = options.ProjectFilter })
            .ConfigureAwait(false);
        var diagram = await provider.GetRequiredService<IDiagramAnalysisProcessingService>()
            .AnalyzeAsync(target.Projects, settings)
            .ConfigureAwait(false);
        var diagnostic = provider.GetRequiredService<IDeterministicDrawioExporter>()
            .ExportDiagnostic(diagram, settings);
        var outputPath = string.IsNullOrWhiteSpace(options.OutputPath)
            ? Path.Combine(Directory.GetCurrentDirectory(), $"{target.Name}.architecture.drawio")
            : Path.GetFullPath(options.OutputPath);
        var reportPath = string.IsNullOrWhiteSpace(options.DiagnosticsOutputPath)
            ? Path.ChangeExtension(outputPath, ".validation.json")
            : Path.GetFullPath(options.DiagnosticsOutputPath);
        var broker = provider.GetRequiredService<IDiagramFileBroker>();
        await broker.WriteTextAsync(outputPath, diagnostic.Content).ConfigureAwait(false);
        await broker.WriteTextAsync(reportPath, diagnostic.ReportJson).ConfigureAwait(false);

        var focusedDirectory = Path.Combine(
            Path.GetDirectoryName(outputPath) ?? Directory.GetCurrentDirectory(),
            $"{Path.GetFileNameWithoutExtension(outputPath)}-focused");
        foreach (var focused in diagnostic.FocusedOutputs.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            var focusedPath = Path.Combine(focusedDirectory, $"{focused.Key}.drawio");
            await broker.WriteTextAsync(focusedPath, focused.Value).ConfigureAwait(false);
            Console.WriteLine(focusedPath);
        }

        Console.WriteLine(outputPath);
        Console.WriteLine(reportPath);
        if (diagnostic.EnforcedFindingCount == 0)
        {
            return 0;
        }

        Console.Error.WriteLine(
            $"Diagnostic output emitted after strict validation failure: " +
            $"{diagnostic.EnforcedFindingCount} finding(s), {diagnostic.UniqueRejectedRouteCount} unique route(s).");
        return 1;
    }

    private sealed class CliOptions
    {
        public string? InputPath { get; private set; }

        public string? SettingsPath { get; private set; }

        public string? RendererId { get; private set; }

        public string? OutputPath { get; private set; }

        public string? ProjectFilter { get; private set; }

        public string? DiagnosticsOutputPath { get; private set; }

        public bool EmitOnValidationFailure { get; private set; }

        public bool ShowHelp { get; private set; }

        public static CliOptions Parse(string[] args)
        {
            var options = new CliOptions();
            for (var index = 0; index < args.Length; index++)
            {
                var arg = args[index];
                switch (arg)
                {
                    case "-h":
                    case "--help":
                        options.ShowHelp = true;
                        break;
                    case "--settings":
                        options.SettingsPath = ReadValue(args, ref index, arg);
                        break;
                    case "--renderer":
                        options.RendererId = ReadValue(args, ref index, arg);
                        break;
                    case "--output":
                        options.OutputPath = ReadValue(args, ref index, arg);
                        break;
                    case "--project":
                        options.ProjectFilter = ReadValue(args, ref index, arg);
                        break;
                    case "--emit-on-validation-failure":
                        options.EmitOnValidationFailure = true;
                        break;
                    case "--diagnostics-output":
                        options.DiagnosticsOutputPath = ReadValue(args, ref index, arg);
                        break;
                    default:
                        if (arg.StartsWith("-", StringComparison.Ordinal))
                        {
                            throw new ArgumentException($"Unknown option: {arg}");
                        }

                        if (!string.IsNullOrWhiteSpace(options.InputPath))
                        {
                            throw new ArgumentException("Only one input path can be supplied.");
                        }

                        options.InputPath = arg;
                        break;
                }
            }

            if (!options.ShowHelp && string.IsNullOrWhiteSpace(options.InputPath))
            {
                throw new ArgumentException("Input path is required.");
            }

            return options;
        }

        private static string ReadValue(string[] args, ref int index, string option)
        {
            if (index + 1 >= args.Length || args[index + 1].StartsWith("-", StringComparison.Ordinal))
            {
                throw new ArgumentException($"{option} requires a value.");
            }

            index++;
            return args[index];
        }
    }
}
