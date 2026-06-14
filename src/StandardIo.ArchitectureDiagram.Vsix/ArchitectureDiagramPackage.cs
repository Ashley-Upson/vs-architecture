using System;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Task = System.Threading.Tasks.Task;

namespace StandardIo.ArchitectureDiagram.Vsix;

[PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
[InstalledProductRegistration("Architecture Diagram Generator", "Generates Draw.io dependency diagrams from .NET projects.", "0.1.26")]
[ProvideMenuResource("Menus.ctmenu", 1)]
[ProvideOptionPage(typeof(DiagramOptionsPage), "Architecture Diagram", "Settings", 0, 0, false)]
[ProvideBindingPath]
[ProvideAutoLoad(UIContextGuids80.SolutionExists, PackageAutoLoadFlags.BackgroundLoad)]
[Guid(Guids.PackageString)]
public sealed class ArchitectureDiagramPackage : AsyncPackage
{
    protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
    {
        DiagnosticLog.Write("Package initialization started.");
        DiagnosticLog.WriteLoadedAssembly("Microsoft.VisualStudio.Shell.15.0");
        DiagnosticLog.WriteLoadedAssembly("Microsoft.VisualStudio.Threading");

        await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
        await DiagramCommands.InitializeAsync(this);

        DiagnosticLog.Write("Package initialization completed.");
    }

    public void ShowDiagramOptionsPage()
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        DiagnosticLog.Write("ShowDiagramOptionsPage started.");

        try
        {
            DiagnosticLog.Write("ShowDiagramOptionsPage calling GetDialogPage.");
            var page = GetDialogPage(typeof(DiagramOptionsPage));
            DiagnosticLog.Write($"ShowDiagramOptionsPage GetDialogPage returned: {page?.GetType().FullName ?? "<null>"}.");

            DiagnosticLog.Write("ShowDiagramOptionsPage calling ShowOptionPage.");
            ShowOptionPage(typeof(DiagramOptionsPage));
            DiagnosticLog.Write("ShowDiagramOptionsPage completed.");
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write("ShowDiagramOptionsPage failed.", ex);
            throw;
        }
    }
}
