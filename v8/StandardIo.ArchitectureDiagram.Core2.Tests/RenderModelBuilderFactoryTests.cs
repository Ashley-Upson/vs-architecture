using System;
using Microsoft.Extensions.DependencyInjection;
using StandardIo.ArchitectureDiagram.Core2.Brokers.Rendering;
using StandardIo.ArchitectureDiagram.Core2.Exposures;
using StandardIo.ArchitectureDiagram.Core2.Models;
using Xunit;
namespace StandardIo.ArchitectureDiagram.Core2.Tests;
public sealed class RenderModelBuilderFactoryTests
{
    [Theory]
    [InlineData(DiagramTypes.Architecture)]
    [InlineData(DiagramTypes.CallChain)]
    [InlineData(DiagramTypes.DataModel)]
    public void ShouldResolveTheRegisteredBuilderForEachDiagramType(DiagramTypes diagramType)
    {
        using var provider = new ServiceCollection().AddArchitectureDiagram().BuildServiceProvider();
        var factory = provider.GetRequiredService<IRenderModelBuilderFactory>();
        Assert.IsType<LayoutModelBuilder>(factory.CreateRenderModelBuilder(diagramType.ToString()));
    }

    [Theory]
    [InlineData(true, DiagramTypes.Architecture)]
    [InlineData(false, DiagramTypes.Architecture)]
    [InlineData(true, DiagramTypes.CallChain)]
    [InlineData(false, DiagramTypes.CallChain)]
    public void ShouldUseDiagramSpecificBuilderThroughEitherFormatBroker(bool html, DiagramTypes diagramType)
    {
        var builder = new RecordingBuilder();
        var services = new ServiceCollection().AddArchitectureDiagram();
        services.AddKeyedSingleton<IRenderModelBuilder>(diagramType.ToString(), builder);
        using var provider = services.BuildServiceProvider();
        var input = new RenderModel([]) { DiagramType = diagramType };
        var output = html
            ? provider.GetRequiredService<IHtmlModelPreparationBroker>().PrepareRenderModel(input)
            : provider.GetRequiredService<IDrawIOModelPreparationBroker>().PrepareRenderModel(input);
        Assert.Same(input, builder.Input);
        Assert.Same(builder.Output, output);
    }

    [Fact]
    public void ShouldNotSilentlyFallBackWhenNoBuilderIsRegistered()
    {
        using var provider = new ServiceCollection().AddArchitectureDiagram().BuildServiceProvider();
        Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<IRenderModelBuilderFactory>().CreateRenderModelBuilder("Unregistered"));
    }

    private sealed class RecordingBuilder : IRenderModelBuilder
    {
        public RenderModel? Input { get; private set; }
        public RenderModel Output { get; } = new RenderModel([]);
        public RenderModel BuildRenderModel(RenderModel renderModel)
        {
            Input = renderModel;
            return Output;
        }
    }
}
