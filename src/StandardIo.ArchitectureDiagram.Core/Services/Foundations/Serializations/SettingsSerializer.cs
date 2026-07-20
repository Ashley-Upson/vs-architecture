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

        _ = StandardIo.ArchitectureDiagram.Core.Services.Foundations.Analyses.RootDiscoveryPatternParser
            .Parse(settings.RootDiscoveryPatternsText ?? string.Empty);
        settings.Version = SettingsSchemaVersion.Current;
        return JsonSerializer.Serialize(settings, Options);
    }

    public static DiagramSettings Import(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new InvalidDataException("Settings JSON is empty.");
        }

        var sourceVersion = ReadSourceVersion(json);
        if (sourceVersion < SettingsSchemaVersion.LegacyUnversioned ||
            sourceVersion > SettingsSchemaVersion.Current)
        {
            throw new NotSupportedException($"Settings version {sourceVersion} is not supported.");
        }

        var settings = JsonSerializer.Deserialize<DiagramSettings>(json, Options)
            ?? throw new InvalidDataException("Settings JSON did not contain a settings object.");
        settings.Version = SettingsSchemaVersion.Current;

        settings.Canvas ??= new CanvasSettings();
        settings.Layout ??= new LayoutSettings();
        settings.ExcludedNamespaces ??= new();
        settings.ExcludedNames ??= new();
        settings.RootDiscoveryPatternsText ??= string.Empty;
        _ = StandardIo.ArchitectureDiagram.Core.Services.Foundations.Analyses.RootDiscoveryPatternParser
            .Parse(settings.RootDiscoveryPatternsText);
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

    private static int ReadSourceVersion(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException("Settings JSON did not contain a settings object.");
            }

            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (!string.Equals(property.Name, "version", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (property.Value.ValueKind != JsonValueKind.Number || !property.Value.TryGetInt32(out var version))
                {
                    throw new InvalidDataException("Settings version must be an integer.");
                }

                return version;
            }

            return SettingsSchemaVersion.LegacyUnversioned;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Settings JSON is invalid.", exception);
        }
    }
}
