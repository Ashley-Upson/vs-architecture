using System.IO;
using StandardIo.ArchitectureDiagram.Core.Models;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.Analyses;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core.Tests;

public sealed class SemanticScopeSelectorTests
{
    [Fact]
    public void Empty_patterns_do_not_activate_exposure_or_controller_conventions()
    {
        var result = SemanticScopeSelector.Select(Diagram(), DiagramSettings.CreateDefault());

        Assert.Equal(4, result.Projects.Single().Types.Count);
        Assert.Empty(result.Metadata!.SemanticSelection!.Roots);
        Assert.Equal("FullSelectedInput", result.Metadata.SemanticSelection.ScopePolicy);
    }

    [Fact]
    public void Ordered_multiline_patterns_are_trimmed_and_blank_lines_are_ignored()
    {
        var parsed = RootDiscoveryPatternParser.Parse("  \\.Exposures\\.  \r\n\r\n Controller$ ");

        Assert.Equal(new[] { "\\.Exposures\\.", "Controller$" }, parsed.Select(item => item.PatternText));
        Assert.Equal(new[] { 1, 3 }, parsed.Select(item => item.SourceLine));
    }

    [Fact]
    public void Invalid_pattern_reports_source_line_and_text()
    {
        var exception = Assert.Throws<InvalidDataException>(() =>
            RootDiscoveryPatternParser.Parse("valid\n  [invalid  "));

        Assert.Contains("line 2", exception.Message);
        Assert.Contains("[invalid", exception.Message);
    }

    [Fact]
    public void Patterns_match_case_sensitive_fully_qualified_semantic_names()
    {
        var settings = DiagramSettings.CreateDefault();
        settings.RootDiscoveryPatternsText = "^Fixture\\.RootController$";

        var result = SemanticScopeSelector.Select(Diagram(), settings);

        var root = Assert.Single(result.Metadata!.SemanticSelection!.Roots);
        Assert.Equal("Fixture.RootController", root.MatchedCanonicalValue);
        Assert.Equal(new[] { "root", "service" }, result.Projects.Single().Types.Select(type => type.Id));
    }

    [Fact]
    public void Configured_scope_does_not_force_property_table_candidates_into_architecture()
    {
        var settings = DiagramSettings.CreateDefault();
        settings.RootDiscoveryPatternsText = "^Fixture\\.RootController$";
        var source = Diagram();
        var types = source.Projects.Single().Types.Select(type => type.Id == "unrelated"
            ? type with { Properties = new[] { new TypeProperty("Name", "string") }, MethodCount = 0 }
            : type).ToArray();
        source = source with { Projects = new[] { source.Projects.Single() with { Types = types } } };

        var result = SemanticScopeSelector.Select(source, settings);

        Assert.DoesNotContain(result.Projects.SelectMany(project => project.Types), type => type.Id == "unrelated");
    }

    [Fact]
    public void Duplicate_matches_preserve_first_pattern_provenance()
    {
        var settings = DiagramSettings.CreateDefault();
        settings.RootDiscoveryPatternsText = "RootController$\n^Fixture\\.RootController$";

        var result = SemanticScopeSelector.Select(Diagram(), settings);

        var root = Assert.Single(result.Metadata!.SemanticSelection!.Roots);
        Assert.Equal(0, root.PatternIndex);
        Assert.Equal("RootController$", root.PatternText);
    }

    [Fact]
    public void Unmatched_patterns_are_diagnostic_and_do_not_crash()
    {
        var settings = DiagramSettings.CreateDefault();
        settings.RootDiscoveryPatternsText = "DoesNotExist$";

        var result = SemanticScopeSelector.Select(Diagram(), settings);

        Assert.Empty(result.Metadata!.SemanticSelection!.Roots);
        Assert.Equal(new[] { 0 }, result.Metadata.SemanticSelection.UnmatchedPatternIndexes);
    }

    [Fact]
    public void Graph_size_threshold_does_not_change_analyser_selection()
    {
        var low = DiagramSettings.CreateDefault();
        low.RootDiscoveryPatternsText = "RootController$";
        low.Layout.ExposureTreeLayoutThreshold = 0;
        var high = DiagramSettings.CreateDefault();
        high.RootDiscoveryPatternsText = low.RootDiscoveryPatternsText;
        high.Layout.ExposureTreeLayoutThreshold = int.MaxValue;

        var first = SemanticScopeSelector.Select(Diagram(), low);
        var second = SemanticScopeSelector.Select(Diagram(), high);

        Assert.Equal(first.Metadata!.SemanticSelection!.Roots, second.Metadata!.SemanticSelection!.Roots);
        Assert.Equal(first.Metadata.SemanticSelection.SelectedNodeIds, second.Metadata.SemanticSelection.SelectedNodeIds);
        Assert.Equal(first.Metadata.SemanticSelection.SelectedLinkIds, second.Metadata.SemanticSelection.SelectedLinkIds);
        Assert.Equal(first.Projects.SelectMany(project => project.Types).Select(type => type.Id),
            second.Projects.SelectMany(project => project.Types).Select(type => type.Id));
        Assert.Equal(first.Edges.Select(edge => edge.Id), second.Edges.Select(edge => edge.Id));
    }

    [Fact]
    public void Settings_round_trip_preserves_raw_line_order()
    {
        var settings = DiagramSettings.CreateDefault();
        settings.RootDiscoveryPatternsText = "First$\nSecond$";

        var imported = SettingsSerializer.Import(SettingsSerializer.Export(settings));

        Assert.Equal(settings.RootDiscoveryPatternsText, imported.RootDiscoveryPatternsText);
    }

    [Fact]
    public void Invalid_patterns_are_rejected_before_settings_are_exported()
    {
        var settings = DiagramSettings.CreateDefault();
        settings.RootDiscoveryPatternsText = "[invalid";

        var exception = Assert.Throws<InvalidDataException>(() => SettingsSerializer.Export(settings));

        Assert.Contains("line 1", exception.Message);
        Assert.Contains("[invalid", exception.Message);
    }

    [Fact]
    public void Drawio_semantic_preparation_contains_no_root_name_or_regex_discovery()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "StandardIo.ArchitectureDiagram.sln")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        var source = File.ReadAllText(Path.Combine(directory!.FullName, "src",
            "StandardIo.ArchitectureDiagram.Core", "Services", "Processings", "Drawios",
            "DeterministicDrawioExporter.RenderGraph.cs"));

        Assert.DoesNotContain(".Exposures.", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Controller\"", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RootDiscoveryPatternParser", source, StringComparison.Ordinal);
    }

    private static DiagramModel Diagram() => new(
        new[]
        {
            new ProjectContainer("project", "Project", new[]
            {
                Node("root", "RootController"), Node("service", "Service"),
                Node("exposure", "OtherExposure", "Fixture.Exposures.OtherExposure"), Node("unrelated", "Unrelated")
            })
        },
        Array.Empty<ExternalDependencyNode>(),
        new[] { new DependencyEdge("edge", "root", "service", "internal") });

    private static TypeNode Node(string id, string name, string? fullName = null) =>
        new(id, "project", name, fullName ?? $"Fixture.{name}", "Class");
}
