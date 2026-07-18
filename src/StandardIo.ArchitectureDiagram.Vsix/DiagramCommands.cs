using System;
using System.ComponentModel.Design;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using EnvDTE;
using EnvDTE80;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.LanguageServices;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.Extensions.DependencyInjection;
using StandardIo.ArchitectureDiagram.Core;
using StandardIo.ArchitectureDiagram.Core.Brokers.Files;
using StandardIo.ArchitectureDiagram.Core.Exposures.Diagrams;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.Renderers;
using StandardIo.ArchitectureDiagram.Core.Services.Processings.Diagrams;
using StandardIo.ArchitectureDiagram.Core.Models;
using RoslynProject = Microsoft.CodeAnalysis.Project;
using Task = System.Threading.Tasks.Task;

namespace StandardIo.ArchitectureDiagram.Vsix;

internal sealed class DiagramCommands
{
    private readonly AsyncPackage _package;
    private static DiagramCommands? _instance;

    private DiagramCommands(AsyncPackage package, OleMenuCommandService commandService)
    {
        _package = package;
        commandService.AddCommand(new OleMenuCommand(GenerateDiagram, new CommandID(Guids.CommandSet, CommandIds.GenerateDiagram)));
        commandService.AddCommand(new OleMenuCommand(OpenSettings, new CommandID(Guids.CommandSet, CommandIds.OpenSettings)));
        commandService.AddCommand(new OleMenuCommand(ExportSettings, new CommandID(Guids.CommandSet, CommandIds.ExportSettings)));
        commandService.AddCommand(new OleMenuCommand(ImportSettings, new CommandID(Guids.CommandSet, CommandIds.ImportSettings)));
    }

    public static async Task InitializeAsync(AsyncPackage package)
    {
        var commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService
            ?? throw new InvalidOperationException("Could not acquire Visual Studio command service.");

        _instance = new DiagramCommands(package, commandService);
    }

    private void GenerateDiagram(object sender, EventArgs e)
    {
        _ = ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            using var threadTelemetry = IsThreadTelemetryEnabled()
                ? GenerationThreadTelemetrySession.Start(() => ThreadHelper.CheckAccess())
                : null;
            threadTelemetry?.Mark("command entry");

            try
            {
                DiagramTarget? target;
                using (threadTelemetry?.Measure("selection operation"))
                {
                    target = await GetSelectedDiagramTargetAsync();
                }
                if (target is null)
                {
                    ShowMessage("Select a C# project or solution node before generating a diagram.", OLEMSGICON.OLEMSGICON_WARNING);
                    return;
                }

                DiagramSettings settings;
                string? outputPath;
                using (threadTelemetry?.Measure("settings and save-dialog operations"))
                {
                    settings = SettingsStore.Load();
                }
                using var provider = new ServiceCollection()
                    .AddArchitectureDiagram()
                    .BuildServiceProvider();
                var renderer = provider
                    .GetRequiredService<IDiagramRendererRegistry>()
                    .Resolve(settings.OutputRenderer);
                using (threadTelemetry?.Measure("settings and save-dialog operations"))
                {
                    outputPath = PromptForSavePath(target.Name, renderer);
                }
                if (string.IsNullOrWhiteSpace(outputPath))
                {
                    return;
                }

                using var cancellation = new CancellationTokenSource();
                using var progress = new DiagramGenerationProgressDialog(cancellation);
                progress.Show();
                progress.SetStage("Analyzing the selected project graph...");

                var analysis = provider.GetRequiredService<IDiagramAnalysisProcessingService>();
                DiagramModel diagram;
                using (threadTelemetry?.Measure("semantic analysis"))
                {
                    diagram = await Task.Run(
                        () => analysis.AnalyzeAsync(target.Projects, settings, cancellation.Token),
                        cancellation.Token);
                }

                cancellation.Token.ThrowIfCancellationRequested();
                progress.SetStage("Constructing layout and routing geometry...");
                string output;
                using (threadTelemetry?.Measure("renderer background execution"))
                {
                    output = await Task.Run(
                        () => renderer.Render(diagram, settings),
                        cancellation.Token);
                }

                cancellation.Token.ThrowIfCancellationRequested();
                progress.SetStage("Writing the generated diagram...");
                using (threadTelemetry?.Measure("file writing"))
                {
                    await provider.GetRequiredService<IDiagramFileBroker>()
                        .WriteTextAsync(outputPath!, output, cancellation.Token);
                }
                progress.Complete();
                using (threadTelemetry?.Measure("completion notification"))
                {
                    ShowMessage($"{renderer.DisplayName} generated:\n{outputPath}", OLEMSGICON.OLEMSGICON_INFO);
                }
            }
            catch (OperationCanceledException)
            {
                ShowMessage("Diagram generation was cancelled.", OLEMSGICON.OLEMSGICON_INFO);
            }
            catch (Exception ex)
            {
                ShowMessage(ex.Message, OLEMSGICON.OLEMSGICON_CRITICAL);
            }
            finally
            {
                foreach (var metric in threadTelemetry?.Snapshot() ?? Array.Empty<GenerationThreadMetric>())
                {
                    DiagnosticLog.Write(
                        $"Generation thread telemetry: stage={metric.Stage}; startThread={metric.StartManagedThreadId}; " +
                        $"endThread={metric.EndManagedThreadId}; startMain={metric.StartIsMainThread}; " +
                        $"endMain={metric.EndIsMainThread}; start={metric.StartedAt:O}; end={metric.EndedAt:O}");
                }
            }
        });
    }

    private static bool IsThreadTelemetryEnabled() =>
        string.Equals(
            Environment.GetEnvironmentVariable("STANDARDIO_THREAD_TELEMETRY"),
            "1",
            StringComparison.Ordinal);

    private void OpenSettings(object sender, EventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        DiagnosticLog.Write("OpenSettings command invoked.");

        try
        {
            if (_package is ArchitectureDiagramPackage architecturePackage)
            {
                architecturePackage.ShowDiagramOptionsPage();
            }
            else
            {
                DiagnosticLog.Write($"OpenSettings command has unexpected package type: {_package.GetType().FullName}.");
                _package.ShowOptionPage(typeof(DiagramOptionsPage));
            }

            DiagnosticLog.Write("OpenSettings command completed.");
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write("OpenSettings command failed.", ex);
            ShowMessage($"Settings page failed to open. Diagnostic log:\n{DiagnosticLog.FilePath}", OLEMSGICON.OLEMSGICON_CRITICAL);
        }
    }

    private void ExportSettings(object sender, EventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        var path = PromptForJsonSavePath("architecture-diagram-settings.json");
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        File.WriteAllText(path, SettingsSerializer.Export(SettingsStore.Load()));
        ShowMessage($"Settings exported:\n{path}", OLEMSGICON.OLEMSGICON_INFO);
    }

    private void ImportSettings(object sender, EventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        var path = PromptForOpenPath();
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            var settings = SettingsSerializer.Import(File.ReadAllText(path));
            SettingsStore.Save(settings);
            ShowMessage("Architecture diagram settings imported.", OLEMSGICON.OLEMSGICON_INFO);
        }
        catch (Exception ex)
        {
            ShowMessage($"Settings were not imported:\n{ex.Message}", OLEMSGICON.OLEMSGICON_CRITICAL);
        }
    }

    private async Task<DiagramTarget?> GetSelectedDiagramTargetAsync()
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

        var dte = await _package.GetServiceAsync(typeof(SDTE)) as DTE2;
        var workspace = await GetWorkspaceAsync();

        if (workspace is null)
        {
            DiagnosticLog.Write("GenerateDiagram target rejected: VisualStudioWorkspace service was unavailable.");
            return null;
        }

        var csharpProjects = workspace.CurrentSolution.Projects
            .Where(p => p.Language == LanguageNames.CSharp)
            .ToArray();
        if (csharpProjects.Length == 0)
        {
            var languages = string.Join(", ", workspace.CurrentSolution.Projects.Select(p => p.Language).Distinct());
            DiagnosticLog.Write($"GenerateDiagram target rejected: no C# projects in Roslyn workspace. Project count: {workspace.CurrentSolution.ProjectIds.Count}; languages: {languages}.");
            return null;
        }

        if (IsSolutionNodeSelected(dte))
        {
            return new DiagramTarget(GetSolutionName(dte), csharpProjects);
        }

        var selectedProject = await GetSelectedDteProjectAsync(dte);
        if (selectedProject is not null)
        {
            var project = FindRoslynProject(csharpProjects, selectedProject, dte);
            if (project is not null)
            {
                return new DiagramTarget(project.Name, new[] { project });
            }

            DiagnosticLog.Write(
                $"GenerateDiagram selected project did not match Roslyn C# projects. " +
                $"Selected name: '{selectedProject.Name}', full name: '{selectedProject.FullName}', unique name: '{selectedProject.UniqueName}'. " +
                $"Roslyn projects: {string.Join("; ", csharpProjects.Select(p => $"{p.Name} | {p.FilePath}"))}.");
        }

        DiagnosticLog.Write("GenerateDiagram target rejected: no selected C# project could be resolved.");
        return null;
    }

    private async Task<VisualStudioWorkspace?> GetWorkspaceAsync()
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

        var componentModel = await _package.GetServiceAsync(typeof(SComponentModel)) as IComponentModel;
        var workspace = componentModel?.GetService<VisualStudioWorkspace>();
        if (workspace is not null)
        {
            return workspace;
        }

        return await _package.GetServiceAsync(typeof(VisualStudioWorkspace)) as VisualStudioWorkspace;
    }

    private async Task<EnvDTE.Project?> GetSelectedDteProjectAsync(DTE2? dte)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

        var selectedProject = await GetSelectedHierarchyProjectAsync();
        if (selectedProject is not null)
        {
            return selectedProject;
        }

        return GetSelectedDteProject(dte);
    }

    private async Task<EnvDTE.Project?> GetSelectedHierarchyProjectAsync()
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

        var monitorSelection = await _package.GetServiceAsync(typeof(SVsShellMonitorSelection)) as IVsMonitorSelection;
        if (monitorSelection is null)
        {
            return null;
        }

        ErrorHandler.ThrowOnFailure(monitorSelection.GetCurrentSelection(
            out var hierarchyPointer,
            out _,
            out _,
            out _));

        if (hierarchyPointer == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            if (Marshal.GetObjectForIUnknown(hierarchyPointer) is not IVsHierarchy hierarchy)
            {
                return null;
            }

            ErrorHandler.ThrowOnFailure(hierarchy.GetProperty(
                VSConstants.VSITEMID_ROOT,
                (int)__VSHPROPID.VSHPROPID_ExtObject,
                out var extObject));

            return extObject as EnvDTE.Project;
        }
        finally
        {
            Marshal.Release(hierarchyPointer);
        }
    }

    private static EnvDTE.Project? GetSelectedDteProject(DTE2? dte)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (dte?.SelectedItems is null || dte.SelectedItems.Count == 0)
        {
            return null;
        }

        var selected = dte.SelectedItems.Item(1);
        if (selected.Project is not null)
        {
            return selected.Project;
        }

        if (selected.ProjectItem?.ContainingProject is not null)
        {
            return selected.ProjectItem.ContainingProject;
        }

        return selected.Collection?.DTE?.ActiveDocument?.ProjectItem?.ContainingProject;
    }

    private static RoslynProject? FindRoslynProject(
        IReadOnlyList<RoslynProject> csharpProjects,
        EnvDTE.Project selectedProject,
        DTE2? dte)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        var projectPaths = GetProjectPathCandidates(selectedProject, dte).ToArray();
        var pathMatch = csharpProjects.FirstOrDefault(project =>
            !string.IsNullOrWhiteSpace(project.FilePath) &&
            projectPaths.Any(path => string.Equals(path, project.FilePath, StringComparison.OrdinalIgnoreCase)));
        if (pathMatch is not null)
        {
            return pathMatch;
        }

        var selectedName = selectedProject.Name;
        return csharpProjects.FirstOrDefault(project =>
            string.Equals(project.Name, selectedName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(project.AssemblyName, selectedName, StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<string> GetProjectPathCandidates(EnvDTE.Project selectedProject, DTE2? dte)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        foreach (var candidate in new[]
        {
            selectedProject.FullName,
            selectedProject.UniqueName,
            GetProjectPropertyValue(selectedProject, "FullPath"),
            GetProjectPropertyValue(selectedProject, "FileName")
        })
        {
            if (TryNormalizeProjectPath(candidate, dte, out var path))
            {
                yield return path;
            }
        }
    }

    private static string? GetProjectPropertyValue(EnvDTE.Project selectedProject, string propertyName)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        try
        {
            return selectedProject.Properties?.Item(propertyName)?.Value as string;
        }
        catch
        {
            return null;
        }
    }

    private static bool TryNormalizeProjectPath(string? candidate, DTE2? dte, out string path)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        path = string.Empty;
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        try
        {
            var solutionPath = dte?.Solution?.FullName;
            if (!Path.IsPathRooted(candidate) && !string.IsNullOrWhiteSpace(solutionPath))
            {
                path = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(solutionPath)!, candidate));
            }
            else
            {
                path = Path.GetFullPath(candidate);
            }

            return Path.HasExtension(path);
        }
        catch
        {
            var solutionPath = dte?.Solution?.FullName;
            if (string.IsNullOrWhiteSpace(solutionPath))
            {
                return false;
            }

            try
            {
                path = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(solutionPath)!, candidate));
                return Path.HasExtension(path);
            }
            catch
            {
                path = string.Empty;
                return false;
            }
        }
    }

    private static bool IsSolutionNodeSelected(DTE2? dte)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (dte?.SelectedItems is null || dte.SelectedItems.Count == 0 || string.IsNullOrWhiteSpace(dte.Solution?.FullName))
        {
            return false;
        }

        var selected = dte.SelectedItems.Item(1);
        if (selected.Project is not null || selected.ProjectItem is not null)
        {
            return false;
        }

        var solutionName = GetSolutionName(dte);
        if (string.Equals(selected.Name, solutionName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return selected.Name?.StartsWith("Solution", StringComparison.OrdinalIgnoreCase) == true
            && selected.Name.IndexOf(solutionName, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string GetSolutionName(DTE2? dte)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        var solutionPath = dte?.Solution?.FullName;
        return string.IsNullOrWhiteSpace(solutionPath)
            ? "Solution"
            : Path.GetFileNameWithoutExtension(solutionPath);
    }

    private static string? PromptForSavePath(string projectName, IDiagramRenderer renderer)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        renderer ??= new DrawioDiagramRenderer();

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = $"Save {renderer.DisplayName}",
            FileName = $"{projectName}.architecture{renderer.FileExtension}",
            Filter = renderer.FileFilter,
            AddExtension = true,
            DefaultExt = renderer.FileExtension
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    private static string? PromptForJsonSavePath(string fileName)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export Architecture Diagram Settings",
            FileName = fileName,
            Filter = "JSON file (*.json)|*.json|All files (*.*)|*.*",
            AddExtension = true,
            DefaultExt = ".json"
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    private static string? PromptForOpenPath()
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Import Architecture Diagram Settings",
            Filter = "JSON file (*.json)|*.json|All files (*.*)|*.*",
            CheckFileExists = true
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    private static void ShowMessage(string message, OLEMSGICON icon)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        VsShellUtilities.ShowMessageBox(
            Microsoft.VisualStudio.Shell.ServiceProvider.GlobalProvider,
            message,
            "Architecture Diagram Generator",
            icon,
            OLEMSGBUTTON.OLEMSGBUTTON_OK,
            OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
    }

    private sealed class DiagramTarget
    {
        public DiagramTarget(string name, IReadOnlyList<RoslynProject> projects)
        {
            Name = name;
            Projects = projects;
        }

        public string Name { get; }

        public IReadOnlyList<RoslynProject> Projects { get; }
    }
}
