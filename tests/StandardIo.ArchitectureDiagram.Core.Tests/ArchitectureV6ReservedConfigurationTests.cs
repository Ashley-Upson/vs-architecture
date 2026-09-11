using System.Linq;
using StandardIo.ArchitectureDiagram.Core.Models;
using StandardIo.ArchitectureDiagram.Core.Models.ArchitectureV6;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.ArchitectureV6;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core.Tests;

public sealed class ArchitectureV6ReservedConfigurationTests
{
    [Fact]
    public void Settings_import_preserves_ordered_reserved_layer_patterns()
    {
        var settings = SettingsSerializer.Import("""
        {
          "version": 1,
          "layout": {
            "reservedLayerTypePatterns": ["*ProcessingService", "*Service", "*Broker"]
          }
        }
        """);

        Assert.Equal(new[] { "*ProcessingService", "*Service", "*Broker" },
            settings.Layout.ReservedLayerTypePatterns);
    }

    [Fact]
    public void Empty_reserved_pattern_configuration_leaves_external_as_the_only_reserved_group()
    {
        var request = new ArchitectureV6SemanticFixtureBuilder()
            .Project("p", "Project")
            .Node("root", "Root", "p")
            .BuildRequest() with
        {
            NodePlacement = new NodePlacementPolicy("", 120, 60, 20, 40,
                ReservedLayerTypePatterns: System.Array.Empty<ArchitectureV6RoleRule>())
        };

        var plan = new ArchitectureDiagramV6Planner().Plan(request);

        Assert.Equal(new[] { "External" }, plan.ReservedDepthTable!.Reservations.Select(item => item.Name));
    }

    [Fact]
    public void Reserved_role_resolution_is_first_match_case_insensitive()
    {
        var rules = new[]
        {
            new ArchitectureV6RoleRule("Specific", "*ProcessingService", 0),
            new ArchitectureV6RoleRule("General", "*Service", 1)
        };

        Assert.Equal("Specific", ArchitectureV6RoleResolver.Resolve("SomePROCESSINGSERVICE", rules));
        Assert.Equal("General", ArchitectureV6RoleResolver.Resolve("SomeService", rules));
    }

    [Fact]
    public void Zero_match_reserved_groups_are_removed_and_external_is_appended()
    {
        var request = new ArchitectureV6SemanticFixtureBuilder()
            .Project("p", "Project")
            .Node("root", "RootProcessingService", "p")
            .BuildRequest() with
        {
            NodePlacement = new NodePlacementPolicy("", 120, 60, 20, 40,
                ReservedLayerTypePatterns: new[]
                {
                    new ArchitectureV6RoleRule("Unused", "*CoordinationService", 0),
                    new ArchitectureV6RoleRule("Processing", "*ProcessingService", 1)
                })
        };

        var plan = new ArchitectureDiagramV6Planner().Plan(request);

        Assert.DoesNotContain(plan.ReservedDepthTable!.Reservations, item => item.Name == "Unused");
        Assert.Equal("Processing", plan.ReservedDepthTable.Reservations[0].Name);
        Assert.True(plan.ReservedDepthTable.Reservations[^1].IsExternal);
        Assert.Contains("*CoordinationService", plan.Diagnostics.Metrics.ZeroMatchReservedLayerTypePatterns!);
    }
}
