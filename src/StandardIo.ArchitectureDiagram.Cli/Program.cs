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
using StandardIo.ArchitectureDiagram.Core.Services.Orchestrations.Diagrams;
using StandardIo.ArchitectureDiagram.Core.Services.Processings.Diagrams;

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
                if (CanUseTypedPipeline(options))
                {
                    return await GenerateTypedDrawioAsync(provider, options, settings).ConfigureAwait(false);
                }
                var exitCode = await GenerateDrawioAsync(provider, options, settings).ConfigureAwait(false);
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
        Console.WriteLine("Usage: StandardIo.ArchitectureDiagram.Cli <path> [--diagram-types <architecture|data-model|architecture,data-model>] [--diagram-manifest <json>] [--settings <json>] [--renderer <drawio|json>] [--output <path>] [--project <name-or-path>] [--strict-validation] [--diagnostics-output <json>] [--performance-output <json>] [--serialization-repeat <count>] [--development-project-region <directory>]");
    }

    private static bool CanUseTypedPipeline(CliOptions options) =>
        string.IsNullOrWhiteSpace(options.DiagramManifestPath) &&
        string.IsNullOrWhiteSpace(options.ProjectRegionDirectory) &&
        string.IsNullOrWhiteSpace(options.DiagnosticsOutputPath) &&
        string.IsNullOrWhiteSpace(options.PerformanceOutputPath) &&
        options.SerializationRepeatCount == 0 &&
        !options.StrictValidation;

    private static async Task<int> GenerateTypedDrawioAsync(
        ServiceProvider provider,
        CliOptions options,
        DiagramSettings settings)
    {
        var target = await provider.GetRequiredService<IWorkspacePathBroker>()
            .LoadAsync(options.InputPath!, new WorkspacePathLoadOptions { ProjectFilter = options.ProjectFilter })
            .ConfigureAwait(false);
        var jobs = new List<DiagramGenerationJob>();
        foreach (var diagramType in options.DiagramTypes)
        {
            switch (diagramType)
            {
                case DiagramType.Architecture:
                    jobs.Add(new ArchitectureGenerationJob(
                        LegacyDiagramSettingsAdapter.ToArchitectureAnalysis(settings),
                        LegacyDiagramSettingsAdapter.ToArchitectureRendering(settings)));
                    break;
                case DiagramType.DataModel:
                    jobs.Add(new DataModelGenerationJob(
                        LegacyDiagramSettingsAdapter.ToDataModelAnalysis(settings),
                        LegacyDiagramSettingsAdapter.ToDataModelRendering(settings)));
                    break;
            }
        }
        var result = await provider.GetRequiredService<ITypedDiagramGenerationOrchestrator>()
            .GenerateAsync(target.Projects, new DiagramGenerationRequest(jobs, new StandardIo.ArchitectureDiagram.Core.Models.Drawios.DrawioDocumentSettings()))
            .ConfigureAwait(false);
        var outputPath = string.IsNullOrWhiteSpace(options.OutputPath)
            ? Path.Combine(Directory.GetCurrentDirectory(), $"{target.Name}.architecture.drawio")
            : Path.GetFullPath(options.OutputPath);
        await provider.GetRequiredService<IDiagramFileBroker>()
            .WriteTextAsync(outputPath, result.Document.Content).ConfigureAwait(false);
        Console.WriteLine($"Output: {outputPath}");
        Console.WriteLine($"Pages: {string.Join(", ", result.Document.PageNames)}");
        return 0;
    }

    private static async Task<int> GenerateDrawioAsync(
        ServiceProvider provider,
        CliOptions options,
        DiagramSettings settings)
    {
        var workspaceTimer = Stopwatch.StartNew();
        WorkspacePathLoadResult? target = null;
        DiagramModel diagram = null!;
        if (!string.IsNullOrWhiteSpace(options.DiagramManifestPath))
        {
            diagram = DiagramModelSerializer.Import(File.ReadAllText(options.DiagramManifestPath));
        }
        else
        {
            using (GenerationPerformanceSession.Current?.Measure("project/workspace acquisition"))
            {
                target = await provider.GetRequiredService<IWorkspacePathBroker>()
                    .LoadAsync(
                        options.InputPath!,
                        new WorkspacePathLoadOptions { ProjectFilter = options.ProjectFilter })
                    .ConfigureAwait(false);
            }
        }
        workspaceTimer.Stop();
        var analysisTimer = Stopwatch.StartNew();
        if (target is not null)
            using (GenerationPerformanceSession.Current?.Measure("semantic analysis", outputObjects: target.Projects.Count))
            {
                diagram = await provider.GetRequiredService<IDiagramAnalysisProcessingService>()
                    .AnalyzeAsync(target.Projects, settings)
                    .ConfigureAwait(false);
            }
        analysisTimer.Stop();
        var exporter = provider.GetRequiredService<IDeterministicDrawioExporter>();
        if (!string.IsNullOrWhiteSpace(options.ProjectRegionDirectory))
        {
            var concrete = (DeterministicDrawioExporter)exporter;
            var legacy = concrete.GenerateResult(diagram, settings).Document;
            var region = concrete.GenerateProjectRegion(diagram, settings);
            var directory = Path.GetFullPath(options.ProjectRegionDirectory);
            Directory.CreateDirectory(directory);
            var evidenceBroker = provider.GetRequiredService<IDiagramFileBroker>();
            await evidenceBroker.WriteTextAsync(Path.Combine(directory, "legacy-before.drawio"), legacy).ConfigureAwait(false);
            await evidenceBroker.WriteTextAsync(Path.Combine(directory, "common-after.drawio"), region.Document).ConfigureAwait(false);
            await evidenceBroker.WriteTextAsync(Path.Combine(directory, "invariants.json"), region.InvariantJson).ConfigureAwait(false);
            using var invariantDocument = JsonDocument.Parse(region.InvariantJson);
            var invariantRoot = invariantDocument.RootElement;
            await evidenceBroker.WriteTextAsync(Path.Combine(directory, "logical-invariants.json"), JsonSerializer.Serialize(new
            {
                eligible = invariantRoot.GetProperty("eligible"),
                fallbackReasons = invariantRoot.GetProperty("fallbackReasons"),
                logicalRoutes = invariantRoot.GetProperty("logicalRoutes"),
                findings = invariantRoot.GetProperty("logicalFindings")
            }, new JsonSerializerOptions { WriteIndented = true })).ConfigureAwait(false);
            await evidenceBroker.WriteTextAsync(Path.Combine(directory, "physical-invariants.json"), JsonSerializer.Serialize(new
            {
                eligible = invariantRoot.GetProperty("eligible"),
                physicalGeometryAuthority = "CoordinateOwnershipCompiler",
                findings = invariantRoot.GetProperty("physicalFindings")
            }, new JsonSerializerOptions { WriteIndented = true })).ConfigureAwait(false);
            await evidenceBroker.WriteTextAsync(Path.Combine(directory, "authority-trace.json"), JsonSerializer.Serialize(new
            {
                terminalAuthority = "ProjectTerminalAllocator",
                topologyAuthority = invariantRoot.GetProperty("topologySelectionAuthority"),
                horizontalYAuthority = invariantRoot.GetProperty("horizontalSegmentYAuthority"),
                verticalXAuthority = invariantRoot.GetProperty("verticalColumnXAuthority"),
                obstacleCompilationAuthority = invariantRoot.GetProperty("obstacleCompilationAuthority"),
                physicalGeometryAuthority = "CoordinateOwnershipCompiler",
                legacyCandidateSelectionInvoked = invariantRoot.GetProperty("legacyCandidateSelectionInvoked"),
                traversalTopologyReplacementRemaining = invariantRoot.GetProperty("traversalTopologyReplacementRemaining"),
                repairTopologyMutationRemaining = invariantRoot.GetProperty("repairTopologyMutationRemaining")
            }, new JsonSerializerOptions { WriteIndented = true })).ConfigureAwait(false);
            Console.WriteLine($"Project region evidence: {directory}");
            Console.WriteLine(region.Eligible ? "Project region eligible." :
                $"Project region fallback: {string.Join(",", region.FallbackReasons)}");
            return 0;
        }
        var generation = exporter.GenerateResult(diagram, settings, new[]
        {
            new PipelineStageMetric("workspace acquisition", workspaceTimer.ElapsedMilliseconds),
            new PipelineStageMetric("semantic analysis", analysisTimer.ElapsedMilliseconds)
        });
        var outputPath = string.IsNullOrWhiteSpace(options.OutputPath)
            ? Path.Combine(Directory.GetCurrentDirectory(), $"{target?.Name ?? "diagram"}.architecture.drawio")
            : Path.GetFullPath(options.OutputPath);
        var broker = provider.GetRequiredService<IDiagramFileBroker>();
        using (GenerationPerformanceSession.Current?.Measure("file write", outputObjects: generation.Document.Length))
        {
            await broker.WriteTextAsync(outputPath, generation.Document).ConfigureAwait(false);
        }

        string? reportPath = null;
        if (options.StrictValidation || !string.IsNullOrWhiteSpace(options.DiagnosticsOutputPath))
        {
            DrawioDiagnosticExportResult diagnostic;
            using (GenerationPerformanceSession.Current?.Measure("optional diagnostic materialization"))
            {
                diagnostic = exporter.ExportDiagnostic(generation);
            }
            reportPath = string.IsNullOrWhiteSpace(options.DiagnosticsOutputPath)
                ? Path.ChangeExtension(outputPath, ".validation.json")
                : Path.GetFullPath(options.DiagnosticsOutputPath);
            using (GenerationPerformanceSession.Current?.Measure("diagnostic JSON file write", outputObjects: diagnostic.ReportJson.Length))
            {
                await broker.WriteTextAsync(reportPath, diagnostic.ReportJson).ConfigureAwait(false);
            }
            var focusedDirectory = Path.Combine(
                Path.GetDirectoryName(outputPath) ?? Directory.GetCurrentDirectory(),
                $"{Path.GetFileNameWithoutExtension(outputPath)}-focused");
            foreach (var focused in diagnostic.FocusedOutputs.OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                var focusedPath = Path.Combine(focusedDirectory, $"{focused.Key}.drawio");
                using (GenerationPerformanceSession.Current?.Measure("focused diagnostic file write", outputObjects: focused.Value.Length))
                {
                    await broker.WriteTextAsync(focusedPath, focused.Value).ConfigureAwait(false);
                }
            }
        }

        var unresolved = generation.ValidationFindings.Where(finding => finding.IsStrictlyEnforced).ToArray();
        if (unresolved.Length > 0)
        {
            Console.WriteLine("Diagram generated with geometry advisories:");
            foreach (var category in unresolved.GroupBy(finding => finding.Category).OrderBy(group => group.Key, StringComparer.Ordinal))
            {
                Console.WriteLine($"  {category.Key}: {category.Count()}");
            }
        }

        Console.WriteLine($"Output: {outputPath}");
        if (reportPath is not null) Console.WriteLine($"Diagnostics: {reportPath}");
        if (options.StrictValidation && unresolved.Length > 0)
        {
            Console.Error.WriteLine($"Strict validation failed with {unresolved.Length} unresolved finding(s); the diagram was still written.");
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
