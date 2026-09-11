using System.Xml.Linq;
using StandardIo.ArchitectureDiagram.Core.Models.Drawios;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core.Tests;

public sealed class DrawioDocumentComposerTests
{
    [Fact]
    public void Compose_rejects_empty_page_set_by_default()
    {
        Assert.Throws<InvalidOperationException>(() =>
            new DrawioDocumentComposer().Compose([], new DrawioDocumentSettings()));
    }

    [Fact]
    public void Compose_supports_explicit_empty_document_policy()
    {
        var document = new DrawioDocumentComposer().Compose([], new DrawioDocumentSettings { AllowEmptyDocument = true });

        Assert.Empty(document.PageNames);
        Assert.Empty(XDocument.Parse(document.Content).Root!.Elements("diagram"));
    }

    [Fact]
    public void Compose_preserves_requested_order_and_disambiguates_duplicate_names()
    {
        var pages = new[]
        {
            Page("Architecture", "architecture-a", "shared-cell"),
            Page("Data Model", "data-model", "shared-cell"),
            Page("Architecture", "architecture-b", "shared-cell")
        };

        var document = new DrawioDocumentComposer().Compose(pages, new DrawioDocumentSettings());
        var diagrams = XDocument.Parse(document.Content).Root!.Elements("diagram").ToArray();

        Assert.Equal(new[] { "Architecture", "Data Model", "Architecture (2)" }, document.PageNames);
        Assert.Equal(document.PageNames, diagrams.Select(item => (string)item.Attribute("name")!).ToArray());
        Assert.Equal(3, document.PageIds.Distinct(StringComparer.Ordinal).Count());
        Assert.All(diagrams, diagram => Assert.NotNull(diagram.Descendants("mxCell").Single(cell => (string?)cell.Attribute("id") == "shared-cell")));
    }

    [Fact]
    public void Compose_is_byte_deterministic()
    {
        var pages = new[] { Page("Architecture", "architecture", "2"), Page("Data Model", "data-model", "2") };
        var composer = new DrawioDocumentComposer();

        var first = composer.Compose(pages, new DrawioDocumentSettings());
        var second = composer.Compose(pages, new DrawioDocumentSettings());

        Assert.Equal(first.Content, second.Content);
        Assert.Equal(first.PageIds, second.PageIds);
    }

    private static DrawioPage Page(string name, string key, string cellId) =>
        new(name, key,
            new XElement("mxGraphModel", new XElement("root", new XElement("mxCell", new XAttribute("id", cellId)))),
            []);
}
