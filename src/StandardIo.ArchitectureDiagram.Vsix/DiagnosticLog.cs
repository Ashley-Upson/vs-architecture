using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;

namespace StandardIo.ArchitectureDiagram.Vsix;

internal static class DiagnosticLog
{
    private static readonly object Gate = new();

    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "StandardIo.ArchitectureDiagram",
        "diagnostics.log");

    public static void Write(string message)
    {
        try
        {
            var directory = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var assembly = typeof(DiagnosticLog).Assembly.GetName();
            var line = $"{DateTimeOffset.Now:O} [pid:{Process.GetCurrentProcess().Id}] [thread:{Thread.CurrentThread.ManagedThreadId}] [{assembly.Name} {assembly.Version}] {message}{Environment.NewLine}";

            lock (Gate)
            {
                File.AppendAllText(FilePath, line);
            }
        }
        catch
        {
            // Diagnostics must never break the extension path being diagnosed.
        }
    }

    public static void Write(string message, Exception exception) =>
        Write(message + Environment.NewLine + exception);

    public static void WriteLoadedAssembly(string assemblyName)
    {
        try
        {
            var assembly = Assembly.Load(assemblyName);
            Write($"Loaded assembly: {assembly.FullName} from {assembly.Location}");
        }
        catch (Exception ex)
        {
            Write($"Failed to inspect assembly '{assemblyName}'.", ex);
        }
    }
}
