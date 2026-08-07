using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using StandardIo.ArchitectureDiagram.Core.Models;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Settings;

public sealed class DiagramSettingsSource
{
    public DiagramSettingsSource(
        DiagramSettings settings,
        string path,
        string sourceType,
        string sha256,
        int sourceVersion)
    {
        Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        Path = path ?? throw new ArgumentNullException(nameof(path));
        SourceType = sourceType ?? throw new ArgumentNullException(nameof(sourceType));
        Sha256 = sha256 ?? throw new ArgumentNullException(nameof(sha256));
        SourceVersion = sourceVersion;
    }

    public DiagramSettings Settings { get; }
    public string Path { get; }
    public string SourceType { get; }
    public string Sha256 { get; }
    public int SourceVersion { get; }
    public int Version => Settings.Version;
}

public static class DiagramSettingsSourceResolver
{
    public const string ExplicitConfigSource = "explicit-config";
    public const string PreservedUserConfigSource = "preserved-user-config";
    public const string RepositoryDefaultSource = "repository-default";

    public static DiagramSettingsSource Resolve(
        string? explicitPath,
        string? preservedUserConfigPath)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            var path = GetRequiredPath(explicitPath!, "Explicit settings");
            var bytes = ReadRequiredBytes(path, "Explicit settings");
            var settings = SettingsSerializer.ApplyOverlay(
                DiagramSettings.CreateDefault(),
                Encoding.UTF8.GetString(bytes));
            return Create(settings, path, ExplicitConfigSource, bytes);
        }

        if (!string.IsNullOrWhiteSpace(preservedUserConfigPath))
        {
            var path = Path.GetFullPath(preservedUserConfigPath);
            if (File.Exists(path))
            {
                var bytes = ReadRequiredBytes(path, "Preserved user settings");
                var settings = SettingsSerializer.Import(Encoding.UTF8.GetString(bytes));
                return Create(settings, path, PreservedUserConfigSource, bytes);
            }
        }

        var defaults = DiagramSettings.CreateDefault();
        return new DiagramSettingsSource(
            defaults,
            RepositoryDefaultSource,
            RepositoryDefaultSource,
            ComputeSha256(Encoding.UTF8.GetBytes(SettingsSerializer.Export(defaults))),
            SettingsSchemaVersion.Current);
    }

    private static string GetRequiredPath(string path, string description)
    {
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"{description} file was not found.", fullPath);
        return fullPath;
    }

    private static byte[] ReadRequiredBytes(string path, string description)
    {
        try
        {
            return File.ReadAllBytes(path);
        }
        catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
        {
            throw new InvalidDataException($"{description} file could not be read: {path}", exception);
        }
    }

    private static DiagramSettingsSource Create(
        DiagramSettings settings,
        string path,
        string sourceType,
        byte[] bytes) =>
        new(settings, path, sourceType, ComputeSha256(bytes), ReadSourceVersion(bytes));

    private static int ReadSourceVersion(byte[] bytes)
    {
        using var document = JsonDocument.Parse(Encoding.UTF8.GetString(bytes));
        if (document.RootElement.TryGetProperty("version", out var version) &&
            version.TryGetInt32(out var value))
            return value;
        return SettingsSchemaVersion.LegacyUnversioned;
    }

    private static string ComputeSha256(byte[] bytes)
    {
        using var sha256 = SHA256.Create();
        return BitConverter.ToString(sha256.ComputeHash(bytes)).Replace("-", string.Empty).ToLowerInvariant();
    }
}
