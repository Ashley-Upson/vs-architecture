using System.Linq;
using System.Text;
using System.Xml.Linq;
using StandardIo.ArchitectureDiagram.Core2.Exposures;
using StandardIo.ArchitectureDiagram.Core2.Models;
using StandardIo.ArchitectureDiagram.Core2.Services.Processings.Commands;
using Xunit;
namespace StandardIo.ArchitectureDiagram.Core2.Tests;
public class ColourLinesTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ShouldUseDestinationColoursOnlyWhenEnabled(bool enabled)
    {
        string[] command = { "Architecture", "sample.csproj", "--output", "sample.html" };
        if (enabled) command = command.Append("--colour-lines").ToArray();
        var request = TestServices.Get<StandardIo.ArchitectureDiagram.Core2.Services.Processings.Commands.ICommandParserProcessingService>().Parse(command);
        Assert.Equal(enabled, request.RenderConfiguration.ColourLines);
        Assert.False(new DiagramGenerationRequest().RenderConfiguration.ColourLines);
        var model = new ProjectModel
        {
            Types = new[] { "Parent", "Other", "Example.Services.Foundations.WorkerService" }.Select(name => new DefinedType { Name = name }).ToArray(),
            Dependencies = new[] { "Parent", "Other" }.Select(name => new TypeRelationship { FromType = name, ToType = "Example.Services.Foundations.WorkerService" }).ToArray()
        };
        var html = XDocument.Parse(Encoding.UTF8.GetString(TestServices.Get<HtmlDiagramRenderer>().Render(new RenderModel(new[] { model }, new RenderConfiguration { ColourLines = request.RenderConfiguration.ColourLines }))));
        var xml = XDocument.Parse(Encoding.UTF8.GetString(TestServices.Get<DrawIODiagramRenderer>().Render(new RenderModel(new[] { model }, new RenderConfiguration { ColourLines = request.RenderConfiguration.ColourLines }))));
        XNamespace svg = "http://www.w3.org/2000/svg";
        string fill = html.Descendants(svg + "g").Single(node => node.Attribute("data-type")?.Value == model.Types[2].Name).Element(svg + "rect")!.Attribute("fill")!.Value;
        string expected = enabled ? fill : "#d1d5db";
        Assert.All(html.Descendants(svg + "polyline"), line => Assert.Equal(expected, line.Attribute("stroke")!.Value));
        Assert.All(xml.Descendants("mxCell").Where(cell => cell.Attribute("edge")?.Value == "1"), edge => Assert.Contains("strokeColor=" + expected + ";", edge.Attribute("style")!.Value));
        Assert.Equal("context-stroke", html.Descendants(svg + "marker").Single(marker => marker.Attribute("id")!.Value == "arrow").Element(svg + "path")!.Attribute("fill")!.Value);
    }
}
