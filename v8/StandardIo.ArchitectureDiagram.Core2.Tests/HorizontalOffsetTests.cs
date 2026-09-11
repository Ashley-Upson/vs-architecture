using System;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using StandardIo.ArchitectureDiagram.Core2.Exposures;
using StandardIo.ArchitectureDiagram.Core2.Models;
using StandardIo.ArchitectureDiagram.Core2.Services.Processings.Commands;
using Xunit;
namespace StandardIo.ArchitectureDiagram.Core2.Tests;
public class HorizontalOffsetTests
{
    [Theory]
    [InlineData("10", false)]
    [InlineData("17", true)]
    public void ShouldParseAndUseSpacingInBothRenderers(string value, bool explicitValue)
    {
        string[] command = { "Architecture", "sample.csproj", "--output", "sample.html" };
        if (explicitValue) command = command.Concat(new[] { "--horizontal-offset", value }).ToArray();
        var request = TestServices.Get<StandardIo.ArchitectureDiagram.Core2.Services.Processings.Commands.ICommandParserProcessingService>().Parse(command);
        Assert.Equal(double.Parse(value), request.RenderConfiguration.HorizontalOffset);
        Assert.Equal(10, new DiagramGenerationRequest().RenderConfiguration.HorizontalOffset);
        var model = new ProjectModel
        {
            Types = new[] { "Root", "A", "B", "C", "D" }.Select(name => new DefinedType { Name = name }).ToArray(),
            Dependencies = new[] { "A", "B", "C", "D" }.Select(name => new TypeRelationship { DependencyType = DependencyType.Consumed, FromType = "Root", ToType = name }).ToArray()
        };
        var html = XDocument.Parse(Encoding.UTF8.GetString(TestServices.Get<HtmlDiagramRenderer>().Render(new RenderModel(new[] { model }, new RenderConfiguration { HorizontalOffset = request.RenderConfiguration.HorizontalOffset }))));
        var xml = XDocument.Parse(Encoding.UTF8.GetString(TestServices.Get<DrawIODiagramRenderer>().Render(new RenderModel(new[] { model }, new RenderConfiguration { HorizontalOffset = request.RenderConfiguration.HorizontalOffset }))));
        XNamespace svg = "http://www.w3.org/2000/svg";
        var ys = html.Descendants(svg + "polyline").Select(line => double.Parse(line.Attribute("points")!.Value.Split(' ')[1].Split(',')[1])).Distinct().Order().ToArray();
        Assert.Equal(2, ys.Length);
        Assert.Equal(request.RenderConfiguration.HorizontalOffset, ys[1] - ys[0]);
        var xmlYs = xml.Descendants("mxPoint").Select(point => (double)point.Attribute("y")!).Distinct().Order().ToArray();
        Assert.Equal(ys, xmlYs);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    public void ShouldRejectInvalidSpacing(string value)
    {
        Assert.Throws<ArgumentException>(() => TestServices.Get<StandardIo.ArchitectureDiagram.Core2.Services.Processings.Commands.ICommandParserProcessingService>().Parse(new[] { "Architecture", "sample.csproj", "--output", "sample.html", "--horizontal-offset", value }));
    }
}
