using StandardIo.ArchitectureDiagram.Core.Graph;
using StandardIo.ArchitectureDiagram.Core.Settings;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core.Tests;

public sealed class SettingsTests
{
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
        Assert.Equal(settings.Layout.EdgePortSpacing, imported.Layout.EdgePortSpacing);
        Assert.Equal(settings.ExternalDependencyStyle.Shape, imported.ExternalDependencyStyle.Shape);
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
}
