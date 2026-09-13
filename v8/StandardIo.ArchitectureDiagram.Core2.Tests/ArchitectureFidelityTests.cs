using System.Linq;
using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.Core2.Models;
using StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Rendering;
using Xunit;
namespace StandardIo.ArchitectureDiagram.Core2.Tests;
public sealed class ArchitectureFidelityTests
{
    [Fact]
    public async Task ShouldKeepInternalBehaviourConstructionAndInjectionWhileOmittingDataCarriers()
    {
        var model = await ConcreteCallChainTests.ExtractAsync("""
            public class Entry { public void Run() { new Worker().Run(); } }
            internal class Worker { internal void Run() { new Sink(); } }
            internal class Sink { public void Save() {} }
            internal class Injected { public Injected(Sink sink) {} public void Run() {} }
            internal class Data { public string Name { get; set; } }
            """);
        var visible = TestServices.Get<IProjectModelPresentationService>().Prepare(model).Model;
        Assert.Contains(visible.Types!, t => t.Name == "Worker");
        Assert.DoesNotContain(visible.Types!, t => t.Name == "Data");
        Assert.Contains(visible.Dependencies!, d => d.FromType == "Worker" && d.ToType == "Sink");
        Assert.Contains(visible.Dependencies!, d => d.FromType == "Injected" && d.ToType == "Sink");
    }
    [Fact]
    public void ShouldKeepFrameworkBehaviourAndInternalBaseConnections()
    {
        var model = RenderConfigurationTests.Project("App", "App.Entry", "App.Base", "System.Text.RegularExpressions.Regex");
        model.Dependencies = [new() { FromType = "App.Entry", ToType = "App.Base", DependencyType = DependencyType.Inheritance }, RenderConfigurationTests.Link("App.Entry", "System.Text.RegularExpressions.Regex")];
        var visible = TestServices.Get<IProjectModelPresentationService>().Prepare(model).Model;
        Assert.Equal(2, visible.Dependencies!.Length);
        Assert.Equal(3, visible.Types!.Length);
    }
    [Fact]
    public void ShouldHighlightSkippedLayersAndAvoidDelayingReadyOtherNodes()
    {
        var project = RenderConfigurationTests.Project("App", "EntryManager", "PlainHelper", "WorkOrchestrationService", "WorkProcessingService", "WorkBroker");
        project.Dependencies = [RenderConfigurationTests.Link("EntryManager", "PlainHelper"), RenderConfigurationTests.Link("EntryManager", "WorkOrchestrationService"), RenderConfigurationTests.Link("WorkOrchestrationService", "WorkProcessingService"), RenderConfigurationTests.Link("WorkProcessingService", "WorkBroker"), RenderConfigurationTests.Link("EntryManager", "WorkBroker")];
        var model = TestServices.Get<StandardIo.ArchitectureDiagram.Core2.Exposures.LayoutModelBuilder>().BuildRenderModel(new RenderModel([project]));
        var result = model.Projects.Single();
        Assert.True(result.Nodes.Single(n => n.TypeName == "PlainHelper").Y < result.Nodes.Single(n => n.TypeName == "WorkProcessingService").Y);
        Assert.Equal("#ef4444", result.Connections.Single(e => e.FromType == "EntryManager" && e.ToType == "WorkBroker").Stroke);
        Assert.NotEqual("#ef4444", result.Connections.Single(e => e.FromType == "WorkProcessingService" && e.ToType == "WorkBroker").Stroke);
    }
    [Fact]
    public void ShouldKeepExternalBoundaryBoxesOnTheirOwnSingleOccupiedRow()
    {
        var project = RenderConfigurationTests.Project("App", "EntryManager", "WorkerProcessingService", "External.One", "External.Two");
        foreach (var type in project.Types!.Where(t => t.Name!.StartsWith("External."))) { type.IsInternal = false; type.AssemblyName = "External"; }
        project.Dependencies = [RenderConfigurationTests.Link("EntryManager", "WorkerProcessingService"), RenderConfigurationTests.Link("EntryManager", "External.One"), RenderConfigurationTests.Link("WorkerProcessingService", "External.Two")];
        var model = TestServices.Get<StandardIo.ArchitectureDiagram.Core2.Exposures.LayoutModelBuilder>().BuildRenderModel(new RenderModel([project]));
        var boundary = Assert.Single(model.Projects, p => p.Name == "External (external)");
        Assert.All(boundary.Nodes, n => Assert.Equal(60, n.Y));
        Assert.All(model.Projects, p => Assert.Equal(Enumerable.Range(0, p.Nodes.Select(n => n.Y).Distinct().Count()).Select(i => 60d + 160 * i), p.Nodes.Select(n => n.Y).Distinct().OrderBy(y => y)));
    }
    [Theory]
    [InlineData(DiagramFormats.Html)]
    [InlineData(DiagramFormats.DrawIO)]
    public void ShouldRenderSkippedLayerLinksInRedInBothFormats(DiagramFormats format)
    {
        var project = RenderConfigurationTests.Project("App", "EntryManager", "WorkerProcessingService", "StorageBroker");
        project.Dependencies = [RenderConfigurationTests.Link("EntryManager", "WorkerProcessingService"), RenderConfigurationTests.Link("WorkerProcessingService", "StorageBroker"), RenderConfigurationTests.Link("EntryManager", "StorageBroker")];
        StandardIo.ArchitectureDiagram.Core2.IDiagramRenderer renderer = format == DiagramFormats.Html
            ? TestServices.Get<StandardIo.ArchitectureDiagram.Core2.Exposures.HtmlDiagramRenderer>() : TestServices.Get<StandardIo.ArchitectureDiagram.Core2.Exposures.DrawIODiagramRenderer>();
        var xml = System.Xml.Linq.XDocument.Parse(System.Text.Encoding.UTF8.GetString(renderer.Render(new RenderModel([project]))));
        Assert.Single(xml.Descendants().Where(e => (string?)e.Attribute("stroke") == "#ef4444" || ((string?)e.Attribute("style"))?.Contains("strokeColor=#ef4444") == true));
    }
    [Fact]
    public void ShouldChooseAContinuousNearbyCorridorInsteadOfZigzagging()
    {
        DrawingNode Node(string name, double x, double y, double width) => new(name, new DefinedType { Name = name }, name, x, y, width, 60);
        var nodes = new[] { Node("Source", 250, 0, 100), Node("First", 200, 160, 300), Node("Second", 100, 320, 150), Node("Target", 450, 480, 100) };
        var link = RenderConfigurationTests.Link("Source", "Target");
        var route = Assert.Single(DiagramRouting.CreateRoutes(new ProjectModelDrawing("p", new ProjectModel { Dependencies = [link] }, 0, 600, 600, nodes)));
        Assert.Equal(6, route.Points.Length);
        Assert.Contains(route.Points, p => p.X == 510);
        Assert.DoesNotContain(route.Points, p => p.X < 300);
    }
}
