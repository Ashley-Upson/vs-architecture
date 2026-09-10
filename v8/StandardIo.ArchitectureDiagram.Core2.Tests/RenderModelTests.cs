// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using Microsoft.Extensions.DependencyInjection;
using StandardIo.ArchitectureDiagram.Core2.Exposures;
using StandardIo.ArchitectureDiagram.Core2.Models;
using StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Rendering;
using StandardIo.ArchitectureDiagram.Core2.Services.Orchestrations.Rendering;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core2.Tests;
public sealed partial class RenderModelTests
{
    [Theory]
    [InlineData("Html_Architecture")]
    [InlineData("DrawIO_Architecture")]
    public void ShouldUseInjectedLayoutBuilderAndPassItsModelToTheWriter(string key)
    {
        // Given
        var services = new ServiceCollection();
        services.AddArchitectureDiagram();
        var spy = new LayoutAndWriterSpy();
        services.AddSingleton<ILayoutOrchestrationService>(implementationInstance: spy);
        services.AddSingleton<IHtmlDocumentService>(implementationInstance: spy);
        services.AddSingleton<IDrawIODocumentService>(implementationInstance: spy);
        using var provider = services.BuildServiceProvider();
        var renderer = provider.GetRequiredKeyedService<IDiagramRenderer>(serviceKey: key);
        var projects = new[] { new ProjectModel() };
        // When
        var input = new RenderModel(projects, new RenderConfiguration { HorizontalOffset = 23 });
        byte[] bytes = renderer.Render(input);
        // Then
        Assert.Equal(expected: 1, actual: spy.Preparations);
        Assert.Same(expected: projects, actual: spy.Input);
        Assert.Same(expected: input, actual: spy.Written);
        Assert.Same(input.Configuration, spy.Written!.Configuration);
        Assert.Same(expected: spy.Bytes, actual: bytes);
    }

    [Fact]
    public void ShouldSerializePreparedCoordinatesAndColoursWithoutRecomputing()
    {
        // Given
        var first = new RenderNode("a", "Example.Exposures.First", "First", "#123456", 17, 31, 123, 47, new[] { new RenderText("First", 55, 65) });
        var second = first with { Id = "b", TypeName = "Second", X = 250, Y = 400 };
        var points = new[] { new DrawingPoint(78.5, 78), new DrawingPoint(78.5, 211), new DrawingPoint(311.5, 211), new DrawingPoint(311.5, 400) };
        var connection = new RenderConnection("edge", "a", "b", first.TypeName, second.TypeName, false, points);
        var model = new RenderModel(987, 654, new[] { new RenderProject("scope", "Prepared", 12, 34, 800, 500, new[] { first, second }, new[] { connection }) });
        // When
        var html = XDocument.Parse(text: Encoding.UTF8.GetString(bytes: new HtmlDocumentService().Render(renderModel: model)));
        var drawio = XDocument.Parse(text: Encoding.UTF8.GetString(bytes: new DrawIODocumentService().Render(renderModel: model)));
        XNamespace svg = "http://www.w3.org/2000/svg";
        // Then
        Assert.Equal(expected: "987", actual: html.Descendants(name: svg + "svg").Single().Attribute(name: "width")!.Value);
        Assert.Equal(expected: "78.5,78 78.5,211 311.5,211 311.5,400", actual: html.Descendants(name: svg + "polyline").Single().Attribute(name: "points")!.Value);
        var node = drawio.Descendants(name: "mxCell").Single(cell => (string?)cell.Attribute(name: "id") == "a");
        Assert.Contains(expectedSubstring: "fillColor=#123456", actualString: node.Attribute(name: "style")!.Value);
        Assert.Equal(expected: "17", actual: node.Element(name: "mxGeometry")!.Attribute(name: "x")!.Value);
        Assert.Equal(expected: "123", actual: node.Element(name: "mxGeometry")!.Attribute(name: "width")!.Value);
        Assert.All(collection: drawio.Descendants(name: "mxPoint"), action: point => Assert.Equal(expected: "211", actual: point.Attribute(name: "y")!.Value));
    }

    private sealed class LayoutAndWriterSpy : ILayoutOrchestrationService, IHtmlDocumentService, IDrawIODocumentService
    {
        public int Preparations { get; private set; }
        public ProjectModel[]? Input { get; private set; }
        public RenderModel? Written { get; private set; }
        public RenderModel Model { get; } = new(300, 200, Array.Empty<RenderProject>());
        public byte[] Bytes { get; } = new byte[] { 42 };
        public RenderModel BuildRenderModel(RenderModel renderModel)
        {
            this.Preparations++;
            this.Input = renderModel.ProjectModels;
            return renderModel;
        }
        public byte[] Render(RenderModel renderModel)
        {
            this.Written = renderModel;
            return this.Bytes;
        }
    }
}