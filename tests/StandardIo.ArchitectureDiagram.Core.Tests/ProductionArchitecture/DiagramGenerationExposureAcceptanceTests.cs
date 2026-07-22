using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.DependencyInjection;
using StandardIo.ArchitectureDiagram.Core;
using StandardIo.ArchitectureDiagram.Core.Exposures.Diagrams;
using StandardIo.ArchitectureDiagram.Core.Models;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core.Tests;

public sealed class DiagramGenerationExposureAcceptanceTests
{
    [Fact]
    public async Task GenerateAsync_ProducesDrawioTabsThroughRegisteredCallChain()
    {
        using var workspace = new AdhocWorkspace();
        var projectId = ProjectId.CreateNewId();
        var project = workspace.CurrentSolution
            .AddProject(ProjectInfo.Create(
                projectId,
                VersionStamp.Create(),
                "Api",
                "Api",
                LanguageNames.CSharp,
                parseOptions: CSharpParseOptions.Default,
                metadataReferences: BasicReferences()))
            .AddDocument(DocumentId.CreateNewId(projectId), "Types.cs", SourceText.From("""
                namespace Api
                {
                    public sealed class Page { public string Title { get; set; } = ""; }
                    public sealed class Request { public Page Page { get; set; } = new(); }
                    public sealed class PageController { public PageController(PageService service) {} }
                    public sealed class PageService { public PageService(PageBroker broker) {} }
                    public sealed class PageBroker {}
                }
                """))
            .GetProject(projectId)!;
        using var provider = new ServiceCollection()
            .AddArchitectureDiagram()
            .BuildServiceProvider();

        var xml = await provider
            .GetRequiredService<IDiagramGenerationExposure>()
            .GenerateAsync(new[] { project }, DiagramSettings.CreateDefault());
        var document = XDocument.Parse(xml);
        var diagrams = document.Descendants("diagram").ToArray();

        Assert.Contains(diagrams, diagram => (string?)diagram.Attribute("name") == "Architecture");
        Assert.Contains(diagrams, diagram => (string?)diagram.Attribute("name") == "Data Model");
        Assert.All(document.Descendants("mxGraphModel"), model =>
        {
            Assert.Equal("0", (string?)model.Attribute("grid"));
            Assert.Equal("0", (string?)model.Attribute("page"));
        });
        Assert.Contains(document.Descendants("mxCell"), cell => ((string?)cell.Attribute("value")) == "PageController");
        Assert.Contains(document.Descendants("mxCell"), cell => ((string?)cell.Attribute("value")) == "Request");
    }

    private static IEnumerable<MetadataReference> BasicReferences()
    {
        yield return MetadataReference.CreateFromFile(typeof(object).Assembly.Location);
        yield return MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location);
    }
}
