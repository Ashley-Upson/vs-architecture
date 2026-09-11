using System.Linq;
using System.Text;
using System.Xml.Linq;
using StandardIo.ArchitectureDiagram.Core2.Exposures;
using StandardIo.ArchitectureDiagram.Core2.Models;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core2.Tests;

public sealed class HtmlNavigationTests
{
    [Fact]
    public void ShouldProvideNavigationControlsAndIdentifyConnectionEndpoints()
    {
        // Given
        var project = RenderConfigurationTests.Project("Example", "Source", "Target");
        project.Dependencies = [new() { FromType = "Source", ToType = "Target", DependencyType = DependencyType.Consumed }];
        // When
        var document = XDocument.Parse(Encoding.UTF8.GetString(TestServices.Get<HtmlDiagramRenderer>().Render(new RenderModel([project]))));
        // Then
        foreach (string id in new[] { "zoom-in", "zoom-out", "zoom-fit", "zoom-reset" })
            Assert.Single(document.Descendants("button").Where(button => (string?)button.Attribute("id") == id));
        Assert.Single(document.Descendants("script"));
        XNamespace svg = "http://www.w3.org/2000/svg";
        Assert.Equal("Source → Target", document.Descendants(svg + "polyline").Single().Element(svg + "title")!.Value);
    }
}
