using System;
using System.IO;
using System.Text.Json;

namespace StandardIo.ArchitectureDiagram.Core.Settings;

public static class SettingsSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public static string Export(DiagramSettings settings)
    {
        if (settings is null)
        {
            throw new ArgumentNullException(nameof(settings));
        }

        return JsonSerializer.Serialize(settings, Options);
    }

    public static DiagramSettings Import(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new InvalidDataException("Settings JSON is empty.");
        }

        var settings = JsonSerializer.Deserialize<DiagramSettings>(json, Options)
            ?? throw new InvalidDataException("Settings JSON did not contain a settings object.");

        if (settings.Version != 1)
        {
            throw new NotSupportedException($"Settings version {settings.Version} is not supported.");
        }

        settings.Canvas ??= new CanvasSettings();
        settings.Layout ??= new LayoutSettings();
        settings.ExcludedNamespaces ??= new();
        settings.ExcludedNames ??= new();
        settings.StyleRules ??= new();
        settings.Overrides ??= new();
        settings.OutputRenderer = string.IsNullOrWhiteSpace(settings.OutputRenderer)
            ? "drawio"
            : settings.OutputRenderer.Trim();
        settings.ProjectContainerStyle ??= NodeStyle.ProjectContainer();
        settings.ExternalDependencyStyle ??= NodeStyle.External();
        settings.Connector ??= new ConnectorStyle();

        return settings;
    }
}
