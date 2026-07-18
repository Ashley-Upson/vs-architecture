using StandardIo.ArchitectureDiagram.Core.Models;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core.Tests;

public sealed class SettingsTests
{
    [Fact]
    public void Import_migrates_unversioned_settings_without_changing_existing_choices()
    {
        var settings = SettingsSerializer.Import("""
            {
              "showProjectContainers": false,
              "outputRenderer": "json",
              "layout": { "parallelLaneSpacing": 23 },
              "unknownLegacyField": "retained-by-source-but-ignored-by-current-policy"
            }
            """);

        Assert.Equal(SettingsSchemaVersion.Current, settings.Version);
        Assert.False(settings.ShowProjectContainers);
        Assert.Equal("json", settings.OutputRenderer);
        Assert.Equal(23, settings.Layout.ParallelLaneSpacing);
    }

    [Fact]
    public void Import_migrates_version_one_and_export_writes_current_schema_version()
    {
        var settings = SettingsSerializer.Import("""
            {
              "version": 1,
              "externalDependencyTag": "[Outside]",
              "nodeDuplication": { "allowDuplicateNodes": false }
            }
            """);

        var exported = SettingsSerializer.Export(settings);
        var reloaded = SettingsSerializer.Import(exported);

        Assert.Contains($"\"version\": {SettingsSchemaVersion.Current}", exported);
        Assert.Equal("[Outside]", reloaded.ExternalDependencyTag);
        Assert.False(reloaded.NodeDuplication.AllowDuplicateNodes);
    }

    [Fact]
    public void Import_applies_defaults_for_fields_missing_from_legacy_settings()
    {
        var settings = SettingsSerializer.Import("{ \"version\": 1 }");

        Assert.NotNull(settings.Canvas);
        Assert.NotNull(settings.Layout);
        Assert.NotNull(settings.Connector);
        Assert.NotNull(settings.NodeDuplication);
        Assert.Equal("drawio", settings.OutputRenderer);
    }

    [Fact]
    public void Import_rejects_unknown_future_schema_version()
    {
        Assert.Throws<NotSupportedException>(() =>
            SettingsSerializer.Import($"{{ \"version\": {SettingsSchemaVersion.Current + 1} }}"));
    }

    [Fact]
    public void Default_settings_export_and_import_without_data_loss()
    {
        var settings = DiagramSettings.CreateDefault();

        var imported = SettingsSerializer.Import(SettingsSerializer.Export(settings));

        Assert.Equal(settings.StyleRules.Count, imported.StyleRules.Count);
        Assert.Equal(settings.Canvas.BackgroundColor, imported.Canvas.BackgroundColor);
        Assert.Equal(settings.ShowProjectContainers, imported.ShowProjectContainers);
        Assert.Equal(settings.OutputRenderer, imported.OutputRenderer);
        Assert.Equal(settings.ExternalDependencyTag, imported.ExternalDependencyTag);
        Assert.Equal(settings.Layout.BaselineAlignmentPattern, imported.Layout.BaselineAlignmentPattern);
        Assert.Equal(settings.Layout.EdgePortSpacing, imported.Layout.EdgePortSpacing);
        Assert.Equal(settings.ExternalDependencyStyle.Shape, imported.ExternalDependencyStyle.Shape);
        Assert.True(imported.NodeDuplication.AllowDuplicateNodes);
        Assert.Empty(imported.NodeDuplication.DuplicationExceptionPatterns);
    }

    [Fact]
    public void Exact_overrides_take_precedence_over_glob_rules()
    {
        var settings = DiagramSettings.CreateDefault();
        settings.Overrides.Add(new StyleOverride
        {
            FullName = "App.Controllers.HomeController",
            Style = new NodeStyle { FillColor = "#123456", Shape = "rhombus" }
        });

        var style = new StyleResolver(settings).Resolve(new TypeNode(
            "type1",
            "project1",
            "HomeController",
            "App.Controllers.HomeController",
            "Class"));

        Assert.Equal("#123456", style.FillColor);
        Assert.Equal("rhombus", style.Shape);
    }

    [Fact]
    public void Default_fallback_style_uses_readable_text()
    {
        var style = new StyleResolver(DiagramSettings.CreateDefault()).Resolve(new TypeNode(
            "type1",
            "project1",
            "UnmatchedThing",
            "App.UnmatchedThing",
            "Class"));

        Assert.Equal("#111111", style.FontColor);
    }

    [Fact]
    public void Default_service_styles_use_first_specific_matching_rule()
    {
        var resolver = new StyleResolver(DiagramSettings.CreateDefault());

        var coordination = resolver.Resolve(new TypeNode(
            "type1",
            "project1",
            "ComponentRenderCoordinationService",
            "App.ComponentRenderCoordinationService",
            "Class"));
        var generic = resolver.Resolve(new TypeNode(
            "type2",
            "project1",
            "EmailService",
            "App.EmailService",
            "Class"));

        Assert.NotEqual(generic.FillColor, coordination.FillColor);
        Assert.Equal("#2f8f83", coordination.FillColor);
        Assert.Equal("#dae8fc", generic.FillColor);
    }

    [Fact]
    public void Exclusion_rules_match_namespace_and_name()
    {
        var settings = DiagramSettings.CreateDefault();
        var resolver = new StyleResolver(settings);

        Assert.True(resolver.IsExcluded("InvoiceDesigner", "App.Features.InvoiceDesigner"));
        Assert.True(resolver.IsExcluded("GeneratedThing", "App.Generated.GeneratedThing"));
    }

    [Fact]
    public void Import_normalizes_empty_output_renderer_to_drawio()
    {
        var json = """
            {
              "version": 1,
              "outputRenderer": " "
            }
            """;

        var settings = SettingsSerializer.Import(json);

        Assert.Equal("drawio", settings.OutputRenderer);
    }

    [Fact]
    public void Import_normalizes_empty_external_dependency_tag()
    {
        var json = """
            {
              "version": 1,
              "externalDependencyTag": " "
            }
            """;

        var settings = SettingsSerializer.Import(json);

        Assert.Equal("[External]", settings.ExternalDependencyTag);
    }

    [Fact]
    public void Import_normalizes_empty_baseline_alignment_pattern()
    {
        var json = """
            {
              "version": 1,
              "layout": {
                "baselineAlignmentPattern": " "
              }
            }
            """;

        var settings = SettingsSerializer.Import(json);

        Assert.Equal(LayoutSettings.DefaultBaselineAlignmentPattern, settings.Layout.BaselineAlignmentPattern);
    }

    [Fact]
    public void Node_duplication_settings_round_trip()
    {
        var settings = DiagramSettings.CreateDefault();
        settings.NodeDuplication.AllowDuplicateNodes = false;
        settings.NodeDuplication.DuplicationExceptionPatterns.Add("Microsoft\\.Extensions\\.Logging\\.ILogger$");

        var imported = SettingsSerializer.Import(SettingsSerializer.Export(settings));

        Assert.False(imported.NodeDuplication.AllowDuplicateNodes);
        Assert.Equal(settings.NodeDuplication.DuplicationExceptionPatterns, imported.NodeDuplication.DuplicationExceptionPatterns);
    }

    [Fact]
    public void Import_rejects_invalid_node_duplication_regex()
    {
        var json = """
            {
              "version": 1,
              "nodeDuplication": {
                "allowDuplicateNodes": false,
                "duplicationExceptionPatterns": [ "[invalid" ]
              }
            }
            """;

        var exception = Assert.Throws<InvalidDataException>(() => SettingsSerializer.Import(json));

        Assert.Contains("index 0", exception.Message);
        Assert.Contains("[invalid", exception.Message);
    }
}
