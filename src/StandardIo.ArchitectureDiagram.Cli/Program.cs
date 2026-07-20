using System;
using System.IO;
using System.Diagnostics;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using StandardIo.ArchitectureDiagram.Core;
using StandardIo.ArchitectureDiagram.Core.Brokers.Files;
using StandardIo.ArchitectureDiagram.Core.Brokers.Workspaces;
using StandardIo.ArchitectureDiagram.Core.Exposures.Diagrams;
using StandardIo.ArchitectureDiagram.Core.Models;
using StandardIo.ArchitectureDiagram.Core.Models.Generation;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.Settings;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.DataModels;
using StandardIo.ArchitectureDiagram.Core.Services.Orchestrations.Diagrams;
using StandardIo.ArchitectureDiagram.Core.Services.Processings.Diagrams;
using StandardIo.ArchitectureDiagram.Core.Services.Processings.Drawios;

namespace StandardIo.ArchitectureDiagram.Cli;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        GenerationPerformanceSession? performance = null;
        try
        {
            var options = CliOptions.Parse(args);
            if (options.ShowHelp)
            {
                WriteUsage();
                return 0;
            }

            if (!string.IsNullOrWhiteSpace(options.PerformanceOutputPath))
            {
                performance = GenerationPerformanceSession.Start(options.SerializationRepeatCount);
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
            if (string.Equals(settings.OutputRenderer, "drawio", StringComparison.OrdinalIgnoreCase))
            {
                var exitCode = await GenerateUnifiedDrawioAsync(provider, options, settings).ConfigureAwait(false);
                if (performance is not null)
                {
                    await WritePerformanceReportAsync(performance, options.PerformanceOutputPath!).ConfigureAwait(false);
                }

                return exitCode;
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
        finally
        {
            performance?.Dispose();
        }
    }

    private static void WriteUsage()
    {
        Console.WriteLine("Usage: StandardIo.ArchitectureDiagram.Cli <path> [--diagram-types <architecture|data-model|architecture,data-model>] [--architecture-analysis-output <directory>] [--data-model-snapshot-output <json>] [--diagram-manifest <json>] [--settings <json>] [--renderer <drawio|json>] [--output <path>] [--project <name-or-path>] [--strict-validation] [--diagnostics-output <json>] [--performance-output <json>] [--serialization-repeat <count>] [--development-project-region <directory>]");
    }

    private static async Task<int> GenerateUnifiedDrawioAsync(
        ServiceProvider provider,
        CliOptions options,
        DiagramSettings settings)
    {
        WorkspacePathLoadResult? target = null;
        DiagramModel? manifest = null;
        if (string.IsNullOrWhiteSpace(options.DiagramManifestPath))
            target = await provider.GetRequiredService<IWorkspacePathBroker>()
                .LoadAsync(options.InputPath!, new WorkspacePathLoadOptions { ProjectFilter = options.ProjectFilter })
                .ConfigureAwait(false);
        else
            manifest = DiagramModelSerializer.Import(File.ReadAllText(options.DiagramManifestPath));
        var pages = new List<StandardIo.ArchitectureDiagram.Core.Models.Drawios.DrawioPage>();
        TypedArchitectureGenerationResult? architecture = null;
        foreach (var diagramType in options.DiagramTypes)
        {
            switch (diagramType)
            {
                case DiagramType.Architecture:
                    var architectureJob = new ArchitectureGenerationJob(
                        LegacyDiagramSettingsAdapter.ToArchitectureAnalysis(settings),
                        LegacyDiagramSettingsAdapter.ToArchitectureRendering(settings));
                    var mode = string.IsNullOrWhiteSpace(options.ProjectRegionDirectory)
                        ? ArchitectureRenderingMode.Production
                        : ArchitectureRenderingMode.DevelopmentProjectRegion;
                    architecture = manifest is null
                        ? await provider.GetRequiredService<IArchitectureGenerationService>()
                            .GenerateAsync(target!.Projects, architectureJob, mode, options.SerializationRepeatCount).ConfigureAwait(false)
                        : await provider.GetRequiredService<IArchitectureGenerationService>()
                            .GenerateAsync(LegacyArchitectureModelAdapter.ToArchitecture(manifest), architectureJob,
                                mode, options.SerializationRepeatCount).ConfigureAwait(false);
                    pages.Add(architecture.Page);
                    break;
                case DiagramType.DataModel:
                    if (target is null)
                        throw new InvalidOperationException("A legacy Architecture manifest cannot supply a Data Model job. Select Architecture only or use source input.");
                    var dataResult = await provider.GetRequiredService<ITypedDiagramGenerationOrchestrator>()
                        .GenerateAsync(target.Projects, new DiagramGenerationRequest(
                            [new DataModelGenerationJob(
                                LegacyDiagramSettingsAdapter.ToDataModelAnalysis(settings),
                                LegacyDiagramSettingsAdapter.ToDataModelRendering(settings))],
                            new StandardIo.ArchitectureDiagram.Core.Models.Drawios.DrawioDocumentSettings()))
                        .ConfigureAwait(false);
                    pages.AddRange(dataResult.Pages);
                    break;
            }
        }
        var document = provider.GetRequiredService<IDrawioDocumentComposer>()
            .Compose(pages, new StandardIo.ArchitectureDiagram.Core.Models.Drawios.DrawioDocumentSettings());
        if (!string.IsNullOrWhiteSpace(options.DataModelSnapshotOutputPath) && target is not null)
        {
            var dataModel = await provider.GetRequiredService<IDataModelAnalyser>()
                .AnalyseAsync(target.Projects, LegacyDiagramSettingsAdapter.ToDataModelAnalysis(settings))
                .ConfigureAwait(false);
            var snapshotPath = Path.GetFullPath(options.DataModelSnapshotOutputPath);
            await provider.GetRequiredService<IDiagramFileBroker>()
                .WriteTextAsync(snapshotPath, DataModelDiagramSerializer.Export(dataModel)).ConfigureAwait(false);
            Console.WriteLine($"Data Model snapshot: {snapshotPath}");
        }
        var targetName = target?.Name ?? "diagram";
        var outputPath = string.IsNullOrWhiteSpace(options.OutputPath)
            ? Path.Combine(Directory.GetCurrentDirectory(), options.DiagramTypes.Count == 1 && options.DiagramTypes[0] == DiagramType.DataModel
                ? $"{targetName}.data-model.drawio"
                : options.DiagramTypes.Count == 1 ? $"{targetName}.architecture.drawio" : $"{targetName}.diagrams.drawio")
            : Path.GetFullPath(options.OutputPath);
        await provider.GetRequiredService<IDiagramFileBroker>()
            .WriteTextAsync(outputPath, document.Content).ConfigureAwait(false);

        if (architecture is not null && !string.IsNullOrWhiteSpace(options.ArchitectureAnalysisOutputDirectory))
        {
            var directory = Path.GetFullPath(options.ArchitectureAnalysisOutputDirectory);
            Directory.CreateDirectory(directory);
            var analyser = provider.GetRequiredService<IArchitectureGeometryAnalyser>();
            var analysis = analyser.Analyse(architecture);
            var broker = provider.GetRequiredService<IDiagramFileBroker>();
            await broker.WriteTextAsync(Path.Combine(directory, "architecture-analysis.json"), analyser.ToJson(analysis)).ConfigureAwait(false);
            await broker.WriteTextAsync(Path.Combine(directory, "architecture-analysis.md"), analyser.ToMarkdown(analysis)).ConfigureAwait(false);
            await broker.WriteTextAsync(Path.Combine(directory, "logical-findings.json"), JsonSerializer.Serialize(
                architecture.LogicalFindings, new JsonSerializerOptions { WriteIndented = true })).ConfigureAwait(false);
            await broker.WriteTextAsync(Path.Combine(directory, "physical-findings.json"), JsonSerializer.Serialize(
                architecture.PhysicalFindings, new JsonSerializerOptions { WriteIndented = true })).ConfigureAwait(false);
            await broker.WriteTextAsync(Path.Combine(directory, "placement-analysis.json"), JsonSerializer.Serialize(new
            {
                analysis.Summary.PageBounds,
                analysis.Projects,
                analysis.Nodes
            }, new JsonSerializerOptions { WriteIndented = true })).ConfigureAwait(false);
            await broker.WriteTextAsync(Path.Combine(directory, "routing-analysis.json"), JsonSerializer.Serialize(new
            {
                analysis.Summary.TotalRouteLength,
                analysis.Summary.MaximumRouteLength,
                analysis.Summary.MaximumDetourRatio,
                analysis.Summary.TotalBends,
                analysis.Summary.MaximumBendsPerRoute,
                analysis.Routes
            }, new JsonSerializerOptions { WriteIndented = true })).ConfigureAwait(false);
            Console.WriteLine($"Architecture analysis: {directory}");
        }

        if (architecture?.SerializationRepeat is { IsDeterministic: false })
            throw new InvalidOperationException("Typed Architecture page serialization was not deterministic.");
        if (architecture is not null && !string.IsNullOrWhiteSpace(options.DiagnosticsOutputPath))
        {
            var diagnosticsPath = Path.GetFullPath(options.DiagnosticsOutputPath);
            await provider.GetRequiredService<IDiagramFileBroker>()
                .WriteTextAsync(diagnosticsPath, architecture.Diagnostics.ReportJson).ConfigureAwait(false);
            Console.WriteLine($"Diagnostics: {diagnosticsPath}");
        }
        if (architecture?.DevelopmentArtifacts is not null && !string.IsNullOrWhiteSpace(options.ProjectRegionDirectory))
        {
            var directory = Path.GetFullPath(options.ProjectRegionDirectory);
            Directory.CreateDirectory(directory);
            foreach (var artifact in architecture.DevelopmentArtifacts.NamedJsonArtifacts)
                await provider.GetRequiredService<IDiagramFileBroker>()
                    .WriteTextAsync(Path.Combine(directory, artifact.Key), artifact.Value).ConfigureAwait(false);
            await provider.GetRequiredService<IDiagramFileBroker>()
                .WriteTextAsync(Path.Combine(directory, "common-after.drawio"), document.Content).ConfigureAwait(false);
            Console.WriteLine($"Project region evidence: {directory}");
        }
        Console.WriteLine($"Output: {outputPath}");
        Console.WriteLine($"Pages: {string.Join(", ", document.PageNames)}");
        if (options.StrictValidation && architecture is { StrictValidationPassed: false })
        {
            var enforced = architecture.LogicalFindings.Concat(architecture.PhysicalFindings)
                .Where(finding => finding.IsStrictlyEnforced).ToArray();
            Console.Error.WriteLine($"Strict validation failed with {enforced.Length} finding(s); the diagram was still written.");
            foreach (var category in enforced.GroupBy(finding => finding.Category).OrderBy(group => group.Key, StringComparer.Ordinal))
                Console.Error.WriteLine($"  {category.Key}: {category.Count()}");
            return 1;
        }
        return 0;
    }

    private static async Task WritePerformanceReportAsync(
        GenerationPerformanceSession performance,
        string outputPath)
    {
        var fullPath = Path.GetFullPath(outputPath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(performance.Snapshot(), new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(fullPath, json).ConfigureAwait(false);
        Console.WriteLine($"Performance: {fullPath}");
    }

    private sealed class CliOptions
    {
        public string? InputPath { get; private set; }

        public string? SettingsPath { get; private set; }

        public string? RendererId { get; private set; }

        public string? OutputPath { get; private set; }

        public string? ProjectFilter { get; private set; }

        public string? DiagnosticsOutputPath { get; private set; }

        public string? PerformanceOutputPath { get; private set; }

        public string? ProjectRegionDirectory { get; private set; }

        public string? DiagramManifestPath { get; private set; }

        public string? DataModelSnapshotOutputPath { get; private set; }

        public string? ArchitectureAnalysisOutputDirectory { get; private set; }

        public int SerializationRepeatCount { get; private set; }

        public bool StrictValidation { get; private set; }

        public bool ShowHelp { get; private set; }

        public IReadOnlyList<DiagramType> DiagramTypes { get; private set; } =
            new[] { DiagramType.Architecture, DiagramType.DataModel };

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
                    case "--diagram-types":
                        options.DiagramTypes = ParseDiagramTypes(ReadValue(args, ref index, arg));
                        break;
                    case "--data-model-snapshot-output":
                        options.DataModelSnapshotOutputPath = ReadValue(args, ref index, arg);
                        break;
                    case "--architecture-analysis-output":
                        options.ArchitectureAnalysisOutputDirectory = ReadValue(args, ref index, arg);
                        break;
                    case "--emit-on-validation-failure":
                        Console.Error.WriteLine("--emit-on-validation-failure is deprecated; use --strict-validation.");
                        options.StrictValidation = true;
                        break;
                    case "--strict-validation":
                        options.StrictValidation = true;
                        break;
                    case "--diagnostics-output":
                        options.DiagnosticsOutputPath = ReadValue(args, ref index, arg);
                        break;
                    case "--performance-output":
                        options.PerformanceOutputPath = ReadValue(args, ref index, arg);
                        break;
                    case "--serialization-repeat":
                        if (!int.TryParse(ReadValue(args, ref index, arg), out var repeatCount) || repeatCount < 0)
                        {
                            throw new ArgumentException("--serialization-repeat requires a non-negative integer.");
                        }

                        options.SerializationRepeatCount = repeatCount;
                        break;
                    case "--development-project-region":
                        options.ProjectRegionDirectory = ReadValue(args, ref index, arg);
                        break;
                    case "--diagram-manifest":
                        options.DiagramManifestPath = ReadValue(args, ref index, arg);
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

        private static IReadOnlyList<DiagramType> ParseDiagramTypes(string value)
        {
            var result = new List<DiagramType>();
            foreach (var item in value.Split(',').Select(item => item.Trim()).Where(item => item.Length > 0))
            {
                var type = item.ToLowerInvariant() switch
                {
                    "architecture" => DiagramType.Architecture,
                    "data-model" or "datamodel" => DiagramType.DataModel,
                    _ => throw new ArgumentException($"Unknown diagram type: {item}")
                };
                if (!result.Contains(type)) result.Add(type);
            }
            if (result.Count == 0) throw new ArgumentException("--diagram-types requires at least one diagram type.");
            return result;
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
