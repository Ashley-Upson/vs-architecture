using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using StandardIo.ArchitectureDiagram.Core2.Factories;
using StandardIo.ArchitectureDiagram.Core2.Models;
using StandardIo.ArchitectureDiagram.Core2.Services.Processings.Commands;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core2.Tests;

public sealed class RendererCommandTests
{
    [Fact]
    public async Task ShouldResolveTheEntireServiceGraphAndExecuteCustomRenderersAsync()
    {
        // Given
        var services = new ServiceCollection();
        services.AddArchitectureDiagram();
        var custom = new CustomRenderer();
        services.AddSingleton<IDiagramRenderer>(custom);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        foreach (var service in services) Assert.NotNull(provider.GetRequiredService(service.ServiceType));
        var command = provider.GetRequiredService<DiagramRenderCommand>();
        string folder = Path.Combine(Path.GetTempPath(), "renderer-di-" + Guid.NewGuid());
        Directory.CreateDirectory(folder);
        try
        {
            string project = Path.Combine(folder, "Example.csproj");
            await File.WriteAllTextAsync(project, "<Project Sdk=\"Microsoft.NET.Sdk\" />");
            await File.WriteAllTextAsync(Path.Combine(folder, "Example.cs"), "public class Example { public void Run() {} }");
            string output = Path.Combine(folder, "out.custom");
            // When
            DiagramRenderResult result = await command.ExecuteAsync(new[] { "Architecture", project, "-o", output, "-f", "custom" });
            // Then
            Assert.Equal(new byte[] { 1 }, result.Content);
            Assert.Equal(output, result.OutputPath);
            Assert.False(File.Exists(output)); // Only the console writes the returned result.
            Assert.Single(custom.Models!);
            Assert.Equal("Example", Assert.Single(custom.Models![0].Types!).Name);
            DiagramRenderResult help = await command.ExecuteAsync(new[] { "--help" });
            Assert.Null(help.OutputPath);
            Assert.Contains("Usage:", Encoding.UTF8.GetString(help.Content));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => command.ExecuteAsync(new[] { "--help" }, new CancellationToken(true)));
            await Assert.ThrowsAsync<ArgumentException>(() => command.ExecuteAsync(new[] { "Architecture", "missing.csproj", "-o", "x.unknown" }));
        }
        finally
        {
            File.Delete(Path.Combine(folder, "Example.cs"));
            File.Delete(Path.Combine(folder, "Example.csproj"));
            Directory.Delete(folder);
        }
    }
    [Fact]
    public void ShouldParseMultipleProjectsAndFormatWithoutLoadingSource()
    {
        // Given / When
        DiagramRenderRequest request = new CommandParserProcessingService().Parse(new[]
            { "Architecture", "one.csproj", "two.csproj", "--output", "out folder/test.html", "--format", "HTML" });
        // Then
        Assert.Equal(new[] { Path.GetFullPath("one.csproj"), Path.GetFullPath("two.csproj") }, request.ProjectPaths);
        Assert.Equal(Path.GetFullPath("out folder/test.html"), request.OutputPath);
        Assert.Equal("HTML", request.Format);
        Assert.Equal(DiagramTypes.Architecture, request.DiagramType);
    }

    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    public void ShouldRecogniseHelp(string option) => Assert.True(new CommandParserProcessingService().Parse(new[] { option }).ShowHelp);

    [Fact]
    public void ShouldRejectNullEmptyAndInvalidOptionValues()
    {
        var parser = new CommandParserProcessingService();
        Assert.Throws<ArgumentNullException>(() => parser.Parse(null!));
        Assert.Throws<ArgumentException>(() => parser.Parse(Array.Empty<string>()));
        Assert.Throws<ArgumentException>(() => parser.Parse(new[] { "invalid" }));
        Assert.Throws<ArgumentException>(() => parser.Parse(new[] { "Architecture", "one.txt", "-o", "x.html" }));
        Assert.Throws<ArgumentException>(() => parser.Parse(new[] { "Architecture", "one.csproj", "-o", " " }));
        Assert.Throws<ArgumentException>(() => parser.Parse(new[] { "Architecture", "one.csproj", "-o", "--format" }));
        Assert.Throws<ArgumentException>(() => parser.Parse(new[] { "Architecture", " ", "-o", "x.html" }));
        Assert.Equal(DiagramTypes.Data, parser.Parse(new[] { "Data", "one.csproj", "-o", "x.html" }).DiagramType);
    }

    [Fact]
    public void ShouldRequireANamedFormatWhenExtensionRulesOverlap()
    {
        var html = new HtmlDiagramRenderer();
        var factory = new DiagramRendererFactory(new IDiagramRenderer[] { html, new AlternativeHtmlRenderer() });
        Assert.Throws<InvalidOperationException>(() => factory.Create(null, "x.html"));
        Assert.Same(html, factory.Create("html", "x.html"));
    }

    [Fact]
    public void ShouldRenderEmptyHtmlAndHollowInheritanceArrows()
    {
        var renderer = new HtmlDiagramRenderer();
        XNamespace svg = "http://www.w3.org/2000/svg";
        var empty = XDocument.Parse(Encoding.UTF8.GetString(renderer.Render(Array.Empty<ProjectModel>())));
        Assert.Empty(empty.Descendants(svg + "g"));
        var model = new ProjectModel { Types = new[] { new DefinedType { Name = "Derived" }, new DefinedType { Name = "Base" } },
            Dependencies = new[] { new Dependency { FromType = "Derived", ToType = "Base", DependencyType = DependencyType.Inheritance } } };
        var document = XDocument.Parse(Encoding.UTF8.GetString(renderer.Render(new[] { model })));
        Assert.Equal("url(#inheritance)", (string?)Assert.Single(document.Descendants(svg + "polyline")).Attribute("marker-end"));
        Assert.Contains(document.Descendants(svg + "text"), text => text.Value == "Project");
    }

    [Theory]
    [InlineData("Architecture one.csproj")]
    [InlineData("Architecture one.csproj --output")]
    [InlineData("Architecture one.csproj -o a.html --format")]
    [InlineData("Architecture one.csproj -o a.html -o b.html")]
    [InlineData("Architecture one.csproj -o a.html -f html --format drawio")]
    [InlineData("Architecture one.csproj --unknown x -o a.html")]
    [InlineData("Unknown one.csproj -o a.html")]
    [InlineData("Architecture -o a.html")]
    public void ShouldRejectMalformedCommands(string command) =>
        Assert.Throws<ArgumentException>(() => new CommandParserProcessingService().Parse(command.Split(' ')));

    [Fact]
    public void ShouldChooseRenderersByNamedRulesAndPermitAdditionalFormats()
    {
        // Given
        var drawio = new DrawIODiagramRenderer();
        var html = new HtmlDiagramRenderer();
        var custom = new CustomRenderer();
        var factory = new DiagramRendererFactory(new IDiagramRenderer[] { drawio, html, custom });
        // When / Then
        Assert.Same(drawio, factory.Create(null, "test.drawio"));
        Assert.Same(html, factory.Create("HTML", "test.htm"));
        Assert.Same(html, factory.Create(null, "test.HTML"));
        Assert.Same(custom, factory.Create("custom", "test.custom"));
        Assert.Throws<ArgumentException>(() => factory.Create("html", "test.drawio"));
        Assert.Throws<ArgumentException>(() => factory.Create("unknown", "test.unknown"));
        Assert.Throws<ArgumentException>(() => factory.Create(null, "test.unknown"));
        Assert.Throws<InvalidOperationException>(() => new DiagramRendererFactory(new IDiagramRenderer[] { html, html }).Create("html", "test.html"));
    }

    [Fact]
    public void ShouldProduceStandaloneHtmlWithTheSameLayoutAndEscapedLabels()
    {
        // Given
        var model = new ProjectModel { Name = "Project <&>", Types = new[] {
            new DefinedType { Name = "Root<script>" }, new DefinedType { Name = "Child" } },
            Dependencies = new[] { new Dependency { FromType = "Root<script>", ToType = "Child", DependencyType = DependencyType.Consumed } } };
        // When
        string html = Encoding.UTF8.GetString(new HtmlDiagramRenderer().Render(new[] { model }));
        XDocument document = XDocument.Parse(html);
        XNamespace svg = "http://www.w3.org/2000/svg";
        XDocument drawio = XDocument.Parse(Encoding.UTF8.GetString(new DrawIODiagramRenderer().Render(new[] { model })));
        // Then
        Assert.Equal("html", document.Root!.Name.LocalName);
        Assert.Single(document.Descendants("style"));
        Assert.Empty(document.Descendants("script"));
        Assert.Contains("Root&lt;script&gt;", html);
        var nodes = document.Descendants(svg + "g").Where(node => node.Attribute("data-type") is not null).ToArray();
        Assert.Equal(2, nodes.Length);
        Assert.Single(document.Descendants(svg + "polyline"));
        foreach (var node in nodes)
        {
            string name = (string)node.Attribute("data-type")!;
            var geometry = drawio.Descendants("mxCell").Single(cell => (string?)cell.Attribute("typeName") == name).Element("mxGeometry")!;
            var rectangle = node.Element(svg + "rect")!;
            foreach (string coordinate in new[] { "x", "y", "width", "height" })
                Assert.Equal((string?)geometry.Attribute(coordinate), (string?)rectangle.Attribute(coordinate));
        }
        Assert.Equal(html, Encoding.UTF8.GetString(new HtmlDiagramRenderer().Render(new[] { model })));
    }

    private sealed class CustomRenderer : IDiagramRenderer
    {
        public DiagramRendererRule Rule { get; } = new("custom", ".custom");
        public ProjectModel[]? Models { get; private set; }
        public byte[] Render(ProjectModel[] projectModels)
        {
            this.Models = projectModels;
            return new byte[] { 1 };
        }
    }

    private sealed class AlternativeHtmlRenderer : IDiagramRenderer
    {
        public DiagramRendererRule Rule { get; } = new("alternative", ".html");
        public byte[] Render(ProjectModel[] projectModels) => Array.Empty<byte>();
    }
}
