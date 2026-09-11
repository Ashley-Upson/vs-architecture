// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using StandardIo.ArchitectureDiagram.Core2.Brokers.Rendering;
using StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Commands;
using StandardIo.ArchitectureDiagram.Core2.Models;
using StandardIo.ArchitectureDiagram.Core2.Services.Processings.Commands;
using Xunit;
using StandardIo.ArchitectureDiagram.Core2.Exposures;

namespace StandardIo.ArchitectureDiagram.Core2.Tests;
public sealed partial class RendererCommandTests
{
    private static readonly string[] helpCommand = new[]
    {
        "--help"
    };
    private static readonly string[] unknownFormatCommand = new[]
    {
        "Architecture",
        "missing.csproj",
        "-o",
        "x.unknown"
    };
    private static readonly string[] multipleProjectCommand = new[]
    {
        "Architecture",
        "one.csproj",
        "two.csproj",
        "--output",
        "out folder/test.html",
        "--format",
        "HTML"
    };
    private static readonly string[] invalidCommand = new[]
    {
        "invalid"
    };
    private static readonly string[] unsupportedProjectCommand = new[]
    {
        "Architecture",
        "one.txt",
        "-o",
        "x.html"
    };
    private static readonly string[] blankOutputCommand = new[]
    {
        "Architecture",
        "one.csproj",
        "-o",
        " "
    };
    private static readonly string[] missingOutputValueCommand = new[]
    {
        "Architecture",
        "one.csproj",
        "-o",
        "--format"
    };
    private static readonly string[] blankProjectCommand = new[]
    {
        "Architecture",
        " ",
        "-o",
        "x.html"
    };
    private static readonly string[] dataDiagramCommand = new[]
    {
        "Data",
        "one.csproj",
        "-o",
        "x.html"
    };
    [Fact]
    public async Task ShouldResolveTheEntireServiceGraphAndExecuteCustomRenderersAsync()
    {
        // Given
        var services = new ServiceCollection();
        services.AddArchitectureDiagram();
        var custom = new CustomRenderer();
        services.AddSingleton(implementationInstance: custom);
        services.AddKeyedSingleton<IDiagramRenderer>(serviceKey: "Html_Architecture", implementationInstance: custom);
        using var provider = services.BuildServiceProvider(options: new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        foreach (var service in services)
        {
            Assert.NotNull(@object: service.IsKeyedService ? provider.GetRequiredKeyedService(serviceType: service.ServiceType, serviceKey: service.ServiceKey) : provider.GetRequiredService(serviceType: service.ServiceType));
        }

        var command = provider.GetRequiredService<DiagramRenderCommand>();
        string folder = Path.Combine(path1: Path.GetTempPath(), path2: "renderer-di-" + Guid.NewGuid());
        Directory.CreateDirectory(path: folder);

        try
        {
            string project = Path.Combine(path1: folder, path2: "Example.csproj");
            await File.WriteAllTextAsync(path: project, contents: "<Project Sdk=\"Microsoft.NET.Sdk\" />");
            await File.WriteAllTextAsync(path: Path.Combine(path1: folder, path2: "Example.cs"), contents: "public class Example { public void Run() {} }");
            string output = Path.Combine(path1: folder, path2: "out.anything");
            // When
            DiagramRenderResult result = await command.ExecuteAsync(command: new[] { "Architecture", project, "-o", output, "-f", "html" });
            // Then
            Assert.Equal(expected: new byte[] { 1 }, actual: result.Content);
            Assert.Equal(expected: output, actual: result.OutputPath);
            Assert.False(condition: File.Exists(path: output)); // Only the console writes the returned result.
            Assert.Single(collection: custom.Models!);
            Assert.Equal(expected: "Example", actual: Assert.Single(collection: custom.Models![0].Types!).Name);
            DiagramRenderResult help = await command.ExecuteAsync(command: helpCommand);
            Assert.Null(@object: help.OutputPath);
            Assert.Contains(expectedSubstring: "Usage:", actualString: Encoding.UTF8.GetString(bytes: help.Content));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(testCode: () => command.ExecuteAsync(command: helpCommand, cancellationToken: new CancellationToken(true)));
            await Assert.ThrowsAsync<ArgumentException>(testCode: () => command.ExecuteAsync(command: new[] { "Architecture", project, "-o", "x.unknown", "-f", "unknown" }));
        }
        finally
        {
            File.Delete(path: Path.Combine(path1: folder, path2: "Example.cs"));
            File.Delete(path: Path.Combine(path1: folder, path2: "Example.csproj"));
            Directory.Delete(path: folder);
        }
    }

    [Fact]
    public void ShouldParseMultipleProjectsAndFormatWithoutLoadingSource()
    {
        // Given / When
        // When: exercise the operation under test.
        DiagramRenderRequest request = TestServices.Get<StandardIo.ArchitectureDiagram.Core2.Services.Processings.Commands.ICommandParserProcessingService>().Parse(command: multipleProjectCommand);
        // Then
        Assert.Equal(expected: new[] { Path.GetFullPath(path: "one.csproj"), Path.GetFullPath(path: "two.csproj") }, actual: request.ProjectPaths);
        Assert.Equal(expected: Path.GetFullPath(path: "out folder/test.html"), actual: request.OutputPath);
        Assert.Equal(expected: DiagramFormats.Html, actual: request.Format);
        Assert.Equal(expected: DiagramTypes.Architecture, actual: request.DiagramType);
    }

    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    public void ShouldRecogniseHelp(string option)
    {
        // Given
        var parser = TestServices.Get<StandardIo.ArchitectureDiagram.Core2.Services.Processings.Commands.ICommandParserProcessingService>();
        // When
        DiagramRenderRequest request = parser.Parse(command: new[] { option });
        // Then
        Assert.True(condition: request.ShowHelp);
    }

    [Fact]
    public void ShouldRejectNullEmptyAndInvalidOptionValues()
    {
        // Given: the fixture and inputs below.
        var parser = TestServices.Get<StandardIo.ArchitectureDiagram.Core2.Services.Processings.Commands.ICommandParserProcessingService>();
        // When: exercise the operation under test.
        Assert.Throws<ArgumentNullException>(testCode: () => parser.Parse(command: null !));
        // Then: verify the resulting contract.
        Assert.Throws<ArgumentException>(testCode: () => parser.Parse(command: Array.Empty<string>()));
        Assert.Throws<ArgumentException>(testCode: () => parser.Parse(command: invalidCommand));
        Assert.Throws<ArgumentException>(testCode: () => parser.Parse(command: unsupportedProjectCommand));
        Assert.Throws<ArgumentException>(testCode: () => parser.Parse(command: blankOutputCommand));
        Assert.Throws<ArgumentException>(testCode: () => parser.Parse(command: missingOutputValueCommand));
        Assert.Throws<ArgumentException>(testCode: () => parser.Parse(command: blankProjectCommand));
        Assert.Equal(expected: DiagramTypes.DataModel, actual: parser.Parse(command: dataDiagramCommand).DiagramType);
    }

    [Theory]
    [InlineData("html", "output.drawio", DiagramFormats.Html)]
    [InlineData(" HTML ", "output.anything", DiagramFormats.Html)]
    [InlineData(null, "output.html", DiagramFormats.DrawIO)]
    public void ShouldSelectFormatIndependentlyOfOutputPath(string? format, string outputPath, DiagramFormats expectedFormat)
    {
        // Given
        using var provider = new ServiceCollection()
            .AddArchitectureDiagram()
            .BuildServiceProvider();
        // When
        DiagramFormats key = ValidateFormat(provider: provider, format: format, diagramType: "Architecture", outputPath: outputPath);
        // Then
        Assert.Equal(expected: expectedFormat, actual: key);
    }

    [Fact]
    public void ShouldRenderEmptyHtmlAndPutInheritanceInNodeLabels()
    {
        // Given: the fixture and inputs below.
        var renderer = TestServices.Get<HtmlDiagramRenderer>();
        XNamespace svg = "http://www.w3.org/2000/svg";
        // When: exercise the operation under test.
        var empty = XDocument.Parse(text: Encoding.UTF8.GetString(bytes: renderer.Render(new RenderModel(Array.Empty<ProjectModel>()))));
        // Then: verify the resulting contract.
        Assert.Empty(collection: empty.Descendants(name: svg + "g"));

        var model = new ProjectModel
        {
            Types = new[]
            {
                new DefinedType
                {
                    Name = "Derived"
                },
                new DefinedType
                {
                    Name = "Base"
                }
            },
            Dependencies = new[]
            {
                new TypeRelationship
                {
                    FromType = "Derived",
                    ToType = "Base",
                    DependencyType = DependencyType.Inheritance
                }
            }
        };

        var document = XDocument.Parse(text: Encoding.UTF8.GetString(bytes: renderer.Render(new RenderModel(new[] { model }))));

        Assert.Empty(document.Descendants(svg + "polyline"));
        Assert.Contains(document.Descendants(svg + "g"), node => (string?)node.Attribute("data-type") == "Derived" && node.Descendants(svg + "tspan").Last().Value == "Base");

        Assert.Contains(collection: document.Descendants(name: svg + "text"), filter: text => text.Value == "Project");
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
    public void ShouldRejectMalformedCommands(string command)
    {
        // Given
        var parser = TestServices.Get<StandardIo.ArchitectureDiagram.Core2.Services.Processings.Commands.ICommandParserProcessingService>();
        // When
        Action parse = () => parser.Parse(command: command.Split(separator: ' '));
        // Then
        Assert.Throws<ArgumentException>(testCode: parse);
    }

    [Fact]
    public void ShouldProduceStandaloneHtmlWithTheSameLayoutAndEscapedLabels()
    {
        // Given
        var model = new ProjectModel
        {
            Name = "Project <&>",
            Types = new[]
            {
                new DefinedType
                {
                    Name = "Root<script>"
                },
                new DefinedType
                {
                    Name = "Child"
                }
            },
            Dependencies = new[]
            {
                new TypeRelationship
                {
                    FromType = "Root<script>",
                    ToType = "Child",
                    DependencyType = DependencyType.Consumed
                }
            }
        };
        // When

        string html = Encoding.UTF8.GetString(bytes: TestServices.Get<HtmlDiagramRenderer>().Render(new RenderModel(new[] { model })));
        XDocument document = XDocument.Parse(text: html);
        XNamespace svg = "http://www.w3.org/2000/svg";
        XDocument drawio = XDocument.Parse(text: Encoding.UTF8.GetString(bytes: TestServices.Get<DrawIODiagramRenderer>().Render(new RenderModel(new[] { model }))));
        // Then
        Assert.Equal(expected: "html", actual: document.Root!.Name.LocalName);
        Assert.Single(collection: document.Descendants(name: "style"));
        Assert.DoesNotContain("Root", Assert.Single(document.Descendants(name: "script")).Value);
        Assert.Contains(expectedSubstring: "Root&lt;script&gt;", actualString: html);

        var nodes = document.Descendants(name: svg + "g")
            .Where(predicate: node => node.Attribute(name: "data-type")is not null)
            .ToArray();

        Assert.Equal(expected: 2, actual: nodes.Length);
        Assert.Single(collection: document.Descendants(name: svg + "polyline"));

        foreach (var node in nodes)
        {
            string name = (string)node.Attribute(name: "data-type")!;

            var geometry = drawio.Descendants(name: "mxCell")
                .Single(predicate: cell => (string? )cell.Attribute(name: "typeName") == name)
                .Element(name: "mxGeometry")!;

            var rectangle = node.Element(name: svg + "rect")!;

            foreach (string coordinate in new[]
            {
                "x",
                "y",
                "width",
                "height"
            }

            )
            {
                Assert.Equal(expected: (string? )geometry.Attribute(name: coordinate), actual: (string? )rectangle.Attribute(name: coordinate));
            }
        }

        Assert.Equal(expected: html, actual: Encoding.UTF8.GetString(bytes: TestServices.Get<HtmlDiagramRenderer>().Render(new RenderModel(new[] { model }))));
    }

    [Fact]
    public async Task ShouldForwardTheTypedRequestWithoutChangingPathsAsync()
    {
        // Given
        var broker = new RecordingRequestBroker();
        var service = new DiagramRequestService(broker: broker);
        var request = new DiagramRenderRequest { Format = DiagramFormats.Html, OutputPath = "out/anything.bin", ProjectPaths = new[] { "project.csproj" } };
        using var cancellation = new CancellationTokenSource();
        // When
        await service.RenderDiagramRenderRequestAsync(diagramRenderRequest: request, cancellationToken: cancellation.Token);
        // Then
        Assert.Same(expected: request, actual: broker.Request);
        Assert.Equal(expected: DiagramFormats.Html, actual: request.Format);
        Assert.Equal(expected: "out/anything.bin", actual: request.OutputPath);
        Assert.Equal(expected: "project.csproj", actual: Assert.Single(collection: request.ProjectPaths));
        Assert.Equal(expected: cancellation.Token, actual: broker.Token);
    }

    [Fact]
    public async Task ShouldRejectMissingProjectPathsBeforeCallingTheBrokerAsync()
    {
        // Given
        var broker = new RecordingRequestBroker();
        var service = new DiagramRequestService(broker: broker);
        var request = new DiagramRenderRequest { Format = DiagramFormats.Html };
        // When
        Func<Task> render = () => service.RenderDiagramRenderRequestAsync(diagramRenderRequest: request, cancellationToken: CancellationToken.None);
        // Then
        await Assert.ThrowsAsync<ArgumentException>(testCode: render);
        Assert.Null(@object: broker.Request);
    }

    private static DiagramFormats ValidateFormat(ServiceProvider provider, string? format, string diagramType, string outputPath)
    {
        var command = new System.Collections.Generic.List<string> { diagramType, "fixture.csproj", "-o", outputPath };

        if (format is not null)
        {
            command.Add(item: "-f");
            command.Add(item: format);
        }

        var request = TestServices.Get<StandardIo.ArchitectureDiagram.Core2.Services.Processings.Commands.ICommandParserProcessingService>().Parse(command: command.ToArray());
        var broker = new RecordingRequestBroker();
        var service = new DiagramRequestService(broker: broker);

        service.RenderDiagramRenderRequestAsync(diagramRenderRequest: request, cancellationToken: CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        return broker.Request!.Format;
    }

    private sealed class RecordingRequestBroker : IDiagramRequestBroker
    {
        public DiagramRenderRequest? Request { get; private set; }
        public CancellationToken Token { get; private set; }

        public Task<byte[]> RenderAsync(DiagramRenderRequest request, CancellationToken cancellationToken)
        {
            this.Request = request;
            this.Token = cancellationToken;
            return Task.FromResult(result: Array.Empty<byte>());
        }
    }

    private sealed class CustomRenderer : IDiagramRenderer
    {
        public ProjectModel[]? Models { get; private set; }

        public byte[] Render(RenderModel renderModel)
        {
            this.Models = renderModel.ProjectModels;

            return new byte[]
            {
                1
            };
        }
    }

    private sealed class AlternativeHtmlRenderer : IDiagramRenderer
    {

        public byte[] Render(RenderModel renderModel) =>
            Array.Empty<byte>();
    }
}