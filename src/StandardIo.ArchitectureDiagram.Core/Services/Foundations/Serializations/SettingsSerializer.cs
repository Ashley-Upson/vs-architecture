using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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
        ValidateNodeLayerGroups(settings.Layout?.NodeLayerGroups);
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
        settings.Layout.NodeLayerGroups ??= LayoutSettings.CreateDefaultNodeLayerGroups();
        ValidateNodeLayerGroups(settings.Layout.NodeLayerGroups);

        return settings;
    }

    public static DiagramSettings ApplyOverlay(DiagramSettings baseline, string json)
    {
        if (baseline is null) throw new ArgumentNullException(nameof(baseline));
        if (string.IsNullOrWhiteSpace(json)) throw new InvalidDataException("Settings overlay JSON is empty.");

        using var baselineDocument = JsonDocument.Parse(Export(baseline));
        JsonDocument overlayDocument;
        try
        {
            overlayDocument = JsonDocument.Parse(json);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Settings overlay JSON is invalid.", exception);
        }

        using (overlayDocument)
        {
            if (overlayDocument.RootElement.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("Settings overlay JSON did not contain a settings object.");

            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
                WriteOverlayObject(writer, baselineDocument.RootElement, overlayDocument.RootElement, string.Empty);
            return Import(Encoding.UTF8.GetString(stream.ToArray()));
        }
    }

    private static void WriteOverlayObject(
        Utf8JsonWriter writer,
        JsonElement baseline,
        JsonElement overlay,
        string path)
    {
        writer.WriteStartObject();
        var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var baselineProperty in baseline.EnumerateObject())
        {
            writer.WritePropertyName(baselineProperty.Name);
            if (!TryGetProperty(overlay, baselineProperty.Name, out var overlayValue))
                baselineProperty.Value.WriteTo(writer);
            else
                WriteOverlayValue(writer, baselineProperty.Value, overlayValue,
                    path.Length == 0 ? baselineProperty.Name : $"{path}.{baselineProperty.Name}");
            written.Add(baselineProperty.Name);
        }

        // Preserve the serializer's existing unknown-property policy by passing unknown members through.
        // JsonSerializer currently ignores them during Import.
        foreach (var overlayProperty in overlay.EnumerateObject().Where(property => !written.Contains(property.Name)))
        {
            writer.WritePropertyName(overlayProperty.Name);
            if (overlayProperty.Value.ValueKind == JsonValueKind.Null)
                throw new InvalidDataException($"Settings overlay property '{overlayProperty.Name}' cannot be null.");
            overlayProperty.Value.WriteTo(writer);
        }
        writer.WriteEndObject();
    }

    private static bool TryGetProperty(JsonElement source, string name, out JsonElement value)
    {
        foreach (var property in source.EnumerateObject())
        {
            if (!string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
            value = property.Value;
            return true;
        }
        value = default;
        return false;
    }

    private static void WriteOverlayValue(
        Utf8JsonWriter writer,
        JsonElement baseline,
        JsonElement overlay,
        string path)
    {
        if (overlay.ValueKind == JsonValueKind.Null)
        {
            if (baseline.ValueKind == JsonValueKind.Null)
            {
                overlay.WriteTo(writer);
                return;
            }

            throw new InvalidDataException($"Settings overlay property '{path}' cannot be null.");
        }
        if (baseline.ValueKind == JsonValueKind.Object && overlay.ValueKind == JsonValueKind.Object)
            WriteOverlayObject(writer, baseline, overlay, path);
        else
            overlay.WriteTo(writer);
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

    private static void ValidateNodeLayerGroups(IReadOnlyList<NodeLayerGroupRule>? rules)
    {
        if (rules is null) throw new InvalidDataException("Layout node layer groups cannot be null.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < rules.Count; index++)
        {
            var rule = rules[index] ?? throw new InvalidDataException(
                $"Node layer group rule at index {index} cannot be null.");
            rule.Name = rule.Name?.Trim() ?? string.Empty;
            rule.Pattern = rule.Pattern?.Trim() ?? string.Empty;
            if (rule.Name.Length == 0)
                throw new InvalidDataException($"Node layer group rule at index {index} has a blank name.");
            if (rule.Pattern.Length == 0)
                throw new InvalidDataException($"Node layer group rule '{rule.Name}' has a blank pattern.");
            if (!names.Add(rule.Name))
                throw new InvalidDataException($"Node layer group name '{rule.Name}' is duplicated.");
            try
            {
                _ = new Regex(rule.Pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
            }
            catch (ArgumentException exception)
            {
                throw new InvalidDataException(
                    $"Node layer group rule '{rule.Name}' has an invalid pattern '{rule.Pattern}'.", exception);
            }
        }
    }
}
