using System.Security.Cryptography;
using System.Text;
using StandardIo.ArchitectureDiagram.Core.Models;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.Settings;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core.Tests;

public sealed class DiagramSettingsSourceTests
{
    [Fact]
    public void Preserved_user_config_beats_repository_defaults()
    {
        using var files = new TemporarySettingsFiles();
        var preserved = DiagramSettings.CreateDefault();
        preserved.Layout.HorizontalSpacing = 13;
        files.WritePreserved(SettingsSerializer.Export(preserved));

        var result = DiagramSettingsSourceResolver.Resolve(null, files.PreservedPath);

        Assert.Equal(DiagramSettingsSourceResolver.PreservedUserConfigSource, result.SourceType);
        Assert.Equal(13, result.Settings.Layout.HorizontalSpacing);
        Assert.Equal(Path.GetFullPath(files.PreservedPath), result.Path);
    }

    [Fact]
    public void Explicit_config_beats_preserved_user_config_and_repository_defaults()
    {
        using var files = new TemporarySettingsFiles();
        var preserved = DiagramSettings.CreateDefault();
        preserved.Layout.HorizontalSpacing = 13;
        files.WritePreserved(SettingsSerializer.Export(preserved));

        var explicitOverlay = "{ \"layout\": { \"horizontalSpacing\": 29 } }";
        files.WriteExplicit(explicitOverlay);

        var result = DiagramSettingsSourceResolver.Resolve(files.ExplicitPath, files.PreservedPath);

        Assert.Equal(DiagramSettingsSourceResolver.ExplicitConfigSource, result.SourceType);
        Assert.Equal(29, result.Settings.Layout.HorizontalSpacing);
        Assert.Equal(Path.GetFullPath(files.ExplicitPath), result.Path);
    }

    [Fact]
    public void Repository_defaults_are_used_only_when_no_config_file_exists()
    {
        using var files = new TemporarySettingsFiles();

        var result = DiagramSettingsSourceResolver.Resolve(null, files.PreservedPath);

        Assert.Equal(DiagramSettingsSourceResolver.RepositoryDefaultSource, result.SourceType);
        Assert.Equal(80, result.Settings.Layout.HorizontalSpacing);
        Assert.Equal(DiagramSettingsSourceResolver.RepositoryDefaultSource, result.Path);
        Assert.NotEmpty(result.Sha256);
    }

    [Fact]
    public void Source_diagnostics_record_file_hash_and_schema_version()
    {
        using var files = new TemporarySettingsFiles();
        var json = SettingsSerializer.Export(DiagramSettings.CreateDefault());
        files.WritePreserved(json);

        var result = DiagramSettingsSourceResolver.Resolve(null, files.PreservedPath);
        using var sha256 = SHA256.Create();
        var expectedHash = BitConverter.ToString(sha256.ComputeHash(Encoding.UTF8.GetBytes(json)))
            .Replace("-", string.Empty)
            .ToLowerInvariant();

        Assert.Equal(expectedHash, result.Sha256);
        Assert.Equal(SettingsSchemaVersion.Current, result.SourceVersion);
        Assert.Equal(SettingsSchemaVersion.Current, result.Version);
        Assert.Equal(DiagramSettingsSourceResolver.PreservedUserConfigSource, result.SourceType);
    }

    [Fact]
    public void An_available_preserved_config_is_not_silently_replaced_by_defaults()
    {
        using var files = new TemporarySettingsFiles();
        files.WritePreserved(SettingsSerializer.Export(new DiagramSettings
        {
            Layout = new LayoutSettings { HorizontalSpacing = 7 }
        }));

        var result = DiagramSettingsSourceResolver.Resolve(null, files.PreservedPath);

        Assert.Equal(7, result.Settings.Layout.HorizontalSpacing);
        Assert.NotEqual(DiagramSettingsSourceResolver.RepositoryDefaultSource, result.SourceType);
    }

    private sealed class TemporarySettingsFiles : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "architecture-settings-" + Guid.NewGuid().ToString("N"));

        public TemporarySettingsFiles()
        {
            Directory.CreateDirectory(_directory);
        }

        public string PreservedPath => Path.Combine(_directory, "preserved.json");
        public string ExplicitPath => Path.Combine(_directory, "explicit.json");

        public void WritePreserved(string json) => File.WriteAllText(PreservedPath, json);
        public void WriteExplicit(string json) => File.WriteAllText(ExplicitPath, json);

        public void Dispose()
        {
            if (Directory.Exists(_directory))
                Directory.Delete(_directory, recursive: true);
        }
    }
}
