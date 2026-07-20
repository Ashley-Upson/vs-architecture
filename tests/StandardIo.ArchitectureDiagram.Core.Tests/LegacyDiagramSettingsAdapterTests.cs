using StandardIo.ArchitectureDiagram.Core.Models;
using StandardIo.ArchitectureDiagram.Core.Models.Generation;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.Settings;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core.Tests;

public sealed class LegacyDiagramSettingsAdapterTests
{
    [Fact]
    public void Independent_and_combined_requests_select_only_the_requested_typed_jobs()
    {
        var settings = DiagramSettings.CreateDefault();

        var architecture = LegacyDiagramSettingsAdapter.ArchitectureRequest(settings);
        var dataModel = LegacyDiagramSettingsAdapter.DataModelRequest(settings);
        var combined = LegacyDiagramSettingsAdapter.CombinedRequest(settings);

        Assert.IsType<ArchitectureGenerationJob>(Assert.Single(architecture.Jobs));
        Assert.IsType<DataModelGenerationJob>(Assert.Single(dataModel.Jobs));
        Assert.Collection(combined.Jobs,
            job => Assert.IsType<ArchitectureGenerationJob>(job),
            job => Assert.IsType<DataModelGenerationJob>(job));
        Assert.Equal(DiagramGenerationFailurePolicy.FailAll, combined.FailurePolicy);
    }

    [Fact]
    public void Legacy_root_discovery_maps_only_to_architecture_analysis()
    {
        var settings = DiagramSettings.CreateDefault();
        settings.RootDiscoveryPatternsText = "^Example.Root$";

        var architecture = Assert.IsType<ArchitectureGenerationJob>(
            Assert.Single(LegacyDiagramSettingsAdapter.ArchitectureRequest(settings).Jobs));
        var dataModel = Assert.IsType<DataModelGenerationJob>(
            Assert.Single(LegacyDiagramSettingsAdapter.DataModelRequest(settings).Jobs));

        Assert.Equal("^Example.Root$", architecture.Analysis.RootDiscoveryPatternsText);
        Assert.DoesNotContain(dataModel.Analysis.GetType().GetProperties(),
            property => property.Name.Contains("Root", System.StringComparison.Ordinal));
    }
}
