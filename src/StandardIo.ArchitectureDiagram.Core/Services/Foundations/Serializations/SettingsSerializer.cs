using System;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace StandardIo.ArchitectureDiagram.Core.Models;

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
        settings.ExternalDependencyTag = string.IsNullOrWhiteSpace(settings.ExternalDependencyTag)
            ? "[External]"
            : settings.ExternalDependencyTag.Trim();
        settings.ProjectContainerStyle ??= NodeStyle.ProjectContainer();
        settings.ExternalDependencyStyle ??= NodeStyle.External();
        settings.Connector ??= new ConnectorStyle();
        settings.NodeDuplication ??= new NodeDuplicationSettings();
        settings.NodeDuplication.DuplicationExceptionPatterns ??= new();
        for (var index = 0; index < settings.NodeDuplication.DuplicationExceptionPatterns.Count; index++)
        {
            var pattern = settings.NodeDuplication.DuplicationExceptionPatterns[index]?.Trim() ?? string.Empty;
            if (pattern.Length == 0)
            {
                settings.NodeDuplication.DuplicationExceptionPatterns.RemoveAt(index--);
                continue;
            }

            try
            {
                _ = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            }
            catch (ArgumentException exception)
            {
                throw new InvalidDataException(
                    $"Node duplication exception pattern at index {index} is not a valid regular expression: {pattern}",
                    exception);
            }

            settings.NodeDuplication.DuplicationExceptionPatterns[index] = pattern;
        }
        settings.Layout.BaselineAlignmentPattern = string.IsNullOrWhiteSpace(settings.Layout.BaselineAlignmentPattern)
            ? LayoutSettings.DefaultBaselineAlignmentPattern
            : settings.Layout.BaselineAlignmentPattern.Trim();
        settings.Layout.DuplicateHighNoiseNodePatterns ??= new();

        return settings;
    }
}
