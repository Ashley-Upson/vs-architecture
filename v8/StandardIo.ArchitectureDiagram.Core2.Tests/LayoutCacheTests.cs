// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------

using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StandardIo.ArchitectureDiagram.Core2.Exposures;
using StandardIo.ArchitectureDiagram.Core2.Models;
using StandardIo.ArchitectureDiagram.Core2.Services.Processings.Layout;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core2.Tests;

public sealed partial class LayoutCacheTests
{
    [Fact]
    public void Render_WhenFormatsShareAView_AppliesLayoutOnce()
    {
        // Given
        var layoutRule = new CountingLayoutRule();
        var services = new ServiceCollection();
        services.AddArchitectureDiagram();
        services.RemoveAll<ILayoutRuleProcessingService>();
        services.AddSingleton<ILayoutRuleProcessingService>(
            implementationInstance: layoutRule);

        using ServiceProvider provider = services.BuildServiceProvider();

        var projectModel = new ProjectModel
        {
            Name = "Cache Test",
            Types =
            [
                new DefinedType { Name = "CacheTestService" }
            ],
            Dependencies = []
        };

        var renderConfiguration = new RenderConfiguration
        {
            MaxLayoutIterations = 2
        };

        // When
        provider.GetRequiredService<HtmlDiagramRenderer>()
            .Render(renderModel: new RenderModel(
                projectModels: [projectModel],
                configuration: renderConfiguration));

        provider.GetRequiredService<DrawIODiagramRenderer>()
            .Render(renderModel: new RenderModel(
                projectModels: [projectModel],
                configuration: renderConfiguration));

        // Then
        Assert.Equal(expected: 1, actual: layoutRule.ApplyCount);
    }

    private sealed class CountingLayoutRule : ILayoutRuleProcessingService
    {
        public int ApplyCount { get; private set; }

        public void ApplyRule(RenderModel renderModel) =>
            ApplyCount++;

        public IEnumerable<string> GetViolations(RenderModel renderModel) =>
            [];
    }
}
