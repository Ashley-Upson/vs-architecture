using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.Extensions.DependencyInjection;
using StandardIo.ArchitectureDiagram.Core2.Exposures;
using StandardIo.ArchitectureDiagram.Core2.Models;
using StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Rendering;
using Xunit;
namespace StandardIo.ArchitectureDiagram.Core2.Tests;
public class DiagramTabsTests
{
    [Fact]
    public void ShouldEmitBrowserCompatibleFrameAndScriptMarkup()
    {
        var tabs = new[] { DiagramTypes.Architecture, DiagramTypes.Composition, DiagramTypes.CallChain, DiagramTypes.DataModel }
            .Select(t => new RenderedDiagramTab(t, Encoding.UTF8.GetBytes("<html><body>diagram</body></html>"))).ToArray();
        string html = Encoding.UTF8.GetString(TestServices.Get<IDocumentCompilationService>().Compile(tabs, DiagramFormats.Html));
        Assert.Equal(4, System.Text.RegularExpressions.Regex.Matches(html,"</iframe>").Count);
        string script = System.Text.RegularExpressions.Regex.Match(html,"<script>(.*?)</script>",System.Text.RegularExpressions.RegexOptions.Singleline).Groups[1].Value;
        Assert.DoesNotContain("&gt;", script);
        Assert.DoesNotContain("&amp;", script);
    }
    private static Task<ProjectModel> Model() => ConcreteCallChainTests.ExtractAsync("""
        public class Item { public string Name { get; set; } }
        public class Order { public Item[] Items { get; set; } }
        public class Broker { public void Use() {} }
        public class Service { public Service(Broker broker) {} public void Run() {} }
        public class Wiring { public void Register() { _ = typeof(Service); } }
        public class Mixed { public void Run() { _ = typeof(Service); new Broker().Use(); } }
        """);
    [Fact]
    public async Task ShouldSeparateTypeReferencesFromOperationalConsumptionWithoutChangingRawModel()
    {
        var model = await Model();
        Assert.Contains(model.Dependencies!, d => d.FromType == "Mixed" && d.ToType == "Service" && d.IsComposition);
        Assert.Contains(model.Dependencies!, d => d.FromType == "Mixed" && d.ToType == "Broker" && !d.IsComposition);
        var architecture = TestServices.Get<IProjectModelPresentationService>().Prepare(model).Model;
        Assert.DoesNotContain(architecture.Types!, t => t.Name == "Wiring");
        Assert.Contains(architecture.Types!, t => t.Name == "Mixed");
        Assert.DoesNotContain(architecture.Dependencies!, d => d.IsComposition);
        var data = TestServices.Get<IContextualModelService>().Prepare(new RenderModel([model], diagramType: DiagramTypes.DataModel));
        Assert.Contains(data.Links, l => l.From == "Order" && l.To == "Item" && l.Label == "Items [many]");
    }
    [Theory]
    [InlineData(DiagramFormats.Html)]
    [InlineData(DiagramFormats.DrawIO)]
    public async Task ShouldProduceFourIsolatedTabsOnlyForAll(DiagramFormats format)
    {
        var model = await Model();
        using var provider = new ServiceCollection().AddArchitectureDiagram().BuildServiceProvider();
        var factory = provider.GetRequiredService<IDiagramTabRendererFactory>();
        foreach (var type in new[] { DiagramTypes.Architecture, DiagramTypes.Composition, DiagramTypes.CallChain, DiagramTypes.DataModel, DiagramTypes.All })
        {
            var bytes = factory.Create($"{format}_{type}").Render(new RenderModel([model], new RenderConfiguration { NoDuplicates = true }, type));
            var doc = XDocument.Parse(Encoding.UTF8.GetString(bytes));
            if (format == DiagramFormats.DrawIO)
                Assert.Equal(type == DiagramTypes.All ? 4 : 1, doc.Descendants("diagram").Count());
            else if (type == DiagramTypes.All)
            {
                var frames = doc.Descendants("iframe").ToArray();
                Assert.Equal(4, frames.Length);
                foreach (var frame in frames) Assert.Single(XDocument.Parse(frame.Attribute("srcdoc")!.Value).Descendants().Where(e => e.Name.LocalName == "svg"));
                Assert.DoesNotContain("data-type=\"Wiring\"", frames[0].Attribute("srcdoc")!.Value);
                Assert.Contains("Wiring", frames[1].Attribute("srcdoc")!.Value);
                Assert.Contains("Items [many]", frames[3].Attribute("srcdoc")!.Value);
            }
            else Assert.Single(doc.Descendants().Where(e => e.Name.LocalName == "svg"));
        }
    }
}
