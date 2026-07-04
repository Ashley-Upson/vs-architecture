using System;
using Microsoft.VisualStudio.Settings;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Settings;
using StandardIo.ArchitectureDiagram.Core.Models;

namespace StandardIo.ArchitectureDiagram.Vsix;

internal static class SettingsStore
{
    private const string CollectionPath = "StandardIo.ArchitectureDiagram";
    private const string SettingsProperty = "SettingsJson";

    public static DiagramSettings Load()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        DiagnosticLog.Write("SettingsStore.Load started.");

        var manager = new ShellSettingsManager(ServiceProvider.GlobalProvider);
        var store = manager.GetReadOnlySettingsStore(SettingsScope.UserSettings);

        if (!store.CollectionExists(CollectionPath) || !store.PropertyExists(CollectionPath, SettingsProperty))
        {
            DiagnosticLog.Write("SettingsStore.Load found no stored settings; using defaults.");
            return DiagramSettings.CreateDefault();
        }

        try
        {
            var json = store.GetString(CollectionPath, SettingsProperty);
            DiagnosticLog.Write($"SettingsStore.Load read {json.Length} characters.");
            var settings = SettingsSerializer.Import(json);
            DiagnosticLog.Write("SettingsStore.Load completed.");
            return settings;
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write("SettingsStore.Load failed; using defaults.", ex);
            return DiagramSettings.CreateDefault();
        }
    }

    public static void Save(DiagramSettings settings)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        DiagnosticLog.Write("SettingsStore.Save started.");

        var manager = new ShellSettingsManager(ServiceProvider.GlobalProvider);
        var store = manager.GetWritableSettingsStore(SettingsScope.UserSettings);

        if (!store.CollectionExists(CollectionPath))
        {
            store.CreateCollection(CollectionPath);
        }

        var json = SettingsSerializer.Export(settings);
        DiagnosticLog.Write($"SettingsStore.Save writing {json.Length} characters.");
        store.SetString(CollectionPath, SettingsProperty, json);
        DiagnosticLog.Write("SettingsStore.Save completed.");
    }
}
