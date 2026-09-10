// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using StandardIo.ArchitectureDiagram.Core2.Models;
using StandardIo.ArchitectureDiagram.Core2.Exposures;
using StandardIo.ArchitectureDiagram.Core2.Brokers.Files;
using StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Commands;
using StandardIo.ArchitectureDiagram.Core2.Services.Processings.Commands;
using Xunit;
namespace StandardIo.ArchitectureDiagram.Core2.Tests;
public sealed class RenderConfigurationTests
{
    [Fact]
    public void ShouldKeepExistingDefaultsAndIndependentConfigurationInstances()
    {
        var first = new DiagramRenderRequest().RenderConfiguration;
        var second = new DiagramRenderRequest().RenderConfiguration;
        Assert.Equal(10, first.HorizontalOffset);
        Assert.Equal(1000, first.MaxLayoutIterations);
        Assert.Equal(160, first.Architecture.RowDepth);
        Assert.Equal(180, first.Architecture.NodeWidth);
        Assert.Equal(60, first.Architecture.NodeSpacing);
        Assert.Equal(100, first.Architecture.ProjectSpacing);
        Assert.False(first.ColourLines);
        Assert.False(first.NoDuplicates);
        Assert.NotNull(first.CallChain);
        Assert.NotNull(first.DataModel);
        first.Architecture.LayerColours[0] = "#123456";
        Assert.Equal("#046079", second.Architecture.LayerColours[0]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ShouldLoadPartialJsonAndApplyExplicitFlagsRegardlessOfOrder(bool configFirst)
    {
        var broker = new ConfigurationBroker("""{"horizontalOffset": 17, "colourLines": true, "Architecture": {"NodeWidth": 240}}""");
        var parser = new CommandParserProcessingService(new RenderConfigurationService(broker));
        string[] config = ["--config", "settings.json"];
        string[] flags = ["--horizontal-offset", "25", "--noduplicates"];
        var command = new[] { "Architecture", "one.csproj", "two.csproj", "-o", "out.html", "-f", "Html" }
            .Concat(configFirst ? config.Concat(flags) : flags.Concat(config)).ToArray();
        var request = parser.Parse(command);
        Assert.Equal("settings.json", broker.Path);
        Assert.Equal(2, request.ProjectPaths.Length);
        Assert.Equal(25, request.RenderConfiguration.HorizontalOffset);
        Assert.True(request.RenderConfiguration.NoDuplicates);
        Assert.True(request.RenderConfiguration.ColourLines);
        Assert.Equal(240, request.RenderConfiguration.Architecture.NodeWidth);
        Assert.Equal(160, request.RenderConfiguration.Architecture.RowDepth);
    }

    [Theory]
    [InlineData("{bad")]
    [InlineData("{\"Architecture\":{\"NodeWitdh\":200}}")]
    public void ShouldRejectMalformedOrMisspelledConfiguration(string json)
    {
        var service = new RenderConfigurationService(new ConfigurationBroker(json));
        Assert.Throws<JsonException>(() => service.Load("settings.json"));
    }

    [Theory]
    [InlineData("{\"Architecture\":null}")]
    [InlineData("{\"CallChain\":null}")]
    [InlineData("{\"DataModel\":null}")]
    [InlineData("{\"HorizontalOffset\":0}")]
    [InlineData("{\"MaxLayoutIterations\":0}")]
    [InlineData("{\"Architecture\":{\"RowDepth\":60}}")]
    [InlineData("{\"Architecture\":{\"NodeWidth\":0}}")]
    [InlineData("{\"Architecture\":{\"NodeSpacing\":-1}}")]
    [InlineData("{\"Architecture\":{\"ProjectSpacing\":0}}")]
    [InlineData("{\"Architecture\":{\"LayerColours\":[]}}")]
    public void ShouldRejectInvalidConfigurationBeforeLayout(string json)
    {
        var service = new RenderConfigurationService(new ConfigurationBroker(json));
        Assert.ThrowsAny<ArgumentException>(() => service.Validate(service.Load("settings.json")));
    }

    [Theory]
    [InlineData("CallChain", DiagramTypes.CallChain)]
    [InlineData("DataModel", DiagramTypes.DataModel)]
    [InlineData("Data", DiagramTypes.DataModel)]
    public void ShouldAcceptAllDiagramNamesAndLegacyDataAlias(string name, DiagramTypes type)
    {
        var request = TestServices.Get<ICommandParserProcessingService>().Parse([name, "a.csproj", "-o", "out"]);
        Assert.Equal(type, request.DiagramType);
    }

    [Fact]
    public void ShouldApplyDimensionsAndColoursToSameModelInBothFormats()
    {
        var configuration = new RenderConfiguration
        {
            ColourLines = true,
            Architecture = new ArchitectureRenderConfiguration
            {
                RowDepth = 210.5, NodeWidth = 220, NodeSpacing = 85, ProjectSpacing = 130,
                LayerColours = ["#123456", "#234567", "#345678", "#456789", "#567890", "#678901", "#789012"]
            }
        };
        var project = Project("First", "RootOrchestrationService", "LeftProcessingService", "RightProcessingService");
        project.Dependencies = [Link("RootOrchestrationService", "LeftProcessingService"), Link("RootOrchestrationService", "RightProcessingService")];
        var input = new RenderModel([project, Project("Second", "Other")], configuration);
        var model = TestServices.Get<LayoutModelBuilder>().BuildRenderModel(input);
        Assert.Same(input, model);
        var rendererForRepeat = TestServices.Get<HtmlDiagramRenderer>();
        Assert.Equal(rendererForRepeat.Render(input), rendererForRepeat.Render(input));
        Assert.Same(configuration, model.Configuration);
        var box = model.Projects[0];
        var root = box.Nodes.Single(node => node.TypeName == "RootOrchestrationService");
        var children = box.Nodes.Where(node => node.TypeName != root.TypeName).OrderBy(node => node.X).ToArray();
        Assert.All(box.Nodes, node => Assert.Equal(220, node.Width));
        Assert.All(children, node => Assert.Equal(210.5, node.Y - root.Y));
        Assert.Equal(85, children[1].X - children[0].X - children[0].Width, 2);
        Assert.Equal((children[0].X + children[1].X + children[1].Width) / 2, root.X + root.Width / 2, 2);
        Assert.Equal(130, model.Projects[1].X - box.X - box.Width, 2);
        Assert.Equal("#123456", root.Fill);
        Assert.All(children, node => Assert.Equal("#234567", node.Fill));
        foreach (IDiagramRenderer renderer in new IDiagramRenderer[] { TestServices.Get<HtmlDiagramRenderer>(), TestServices.Get<DrawIODiagramRenderer>() })
        {
            var document = XDocument.Parse(Encoding.UTF8.GetString(renderer.Render(new RenderModel([project], configuration))));
            string output = document.ToString();
            Assert.Contains("#123456", output);
            Assert.Contains("#234567", output);
            if (renderer is DrawIODiagramRenderer)
            {
                var cells = document.Descendants("mxCell").Where(cell => (string?)cell.Attribute("edge") == "1");
                Assert.All(cells, cell => Assert.Contains("strokeColor=#234567", (string)cell.Attribute("style")!));
                Assert.All(document.Descendants("mxGeometry").Where(geometry => (string?)geometry.Attribute("width") == "220"), geometry => Assert.Equal("60", (string?)geometry.Attribute("height")));
            }
            else
            {
                XNamespace svg = "http://www.w3.org/2000/svg";
                Assert.All(document.Descendants(svg + "polyline"), line => Assert.Equal("#234567", (string?)line.Attribute("stroke")));
            }
        }
    }

    internal static ProjectModel Project(string name, params string[] types) => new()
    {
        Name = name, Path = name + ".csproj",
        Types = types.Select(type => new DefinedType { Name = type, IsInternal = true, Methods = [new Method { Name = "Run" }] }).ToArray(),
        Dependencies = []
    };
    internal static TypeRelationship Link(string from, string to) => new() { FromType = from, ToType = to };
    private sealed class ConfigurationBroker(string json) : IRenderConfigurationBroker
    {
        public string? Path { get; private set; }
        public string ReadConfiguration(string path) { Path = path; return json; }
    }
}
