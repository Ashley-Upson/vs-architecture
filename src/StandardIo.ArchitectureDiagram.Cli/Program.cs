using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using StandardIo.ArchitectureDiagram.Core;
using StandardIo.ArchitectureDiagram.Core.Brokers.Files;
using StandardIo.ArchitectureDiagram.Core.Exposures.Diagrams;
using StandardIo.ArchitectureDiagram.Core.Models;

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
        Console.WriteLine("Usage: StandardIo.ArchitectureDiagram.Cli <path> [--settings <json>] [--renderer <drawio|json>] [--output <path>] [--project <name-or-path>]");
    }

    private sealed class CliOptions
    {
        public string? InputPath { get; private set; }

        public string? SettingsPath { get; private set; }

        public string? RendererId { get; private set; }

        public string? OutputPath { get; private set; }

        public string? ProjectFilter { get; private set; }

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
