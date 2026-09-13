using System.Linq;
using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.Core2.Exposures;
using StandardIo.ArchitectureDiagram.Core2.Models;
using Xunit;
namespace StandardIo.ArchitectureDiagram.Core2.Tests;
public sealed class ArchitecturalLinkReviewTests
{
    [Fact]
    public void ShouldNotTreatUnrelatedPhysicalRowsAsAnArchitecturalSkip()
    {
        var p = RenderConfigurationTests.Project("App", "EntryManager", "WorkOrchestrationService", "WorkProcessingService", "Helper", "WorkBroker");
        p.Dependencies = [RenderConfigurationTests.Link("EntryManager", "WorkOrchestrationService"), RenderConfigurationTests.Link("WorkOrchestrationService", "WorkProcessingService"), RenderConfigurationTests.Link("EntryManager", "Helper"), RenderConfigurationTests.Link("WorkProcessingService", "WorkBroker"), RenderConfigurationTests.Link("EntryManager", "WorkBroker")];
        var drawing = TestServices.Get<LayoutModelBuilder>().BuildRenderModel(new RenderModel([p]));
        var edges = drawing.Projects.Single().Connections;
        Assert.NotEqual("#ef4444", edges.Single(e => e.FromType == "WorkOrchestrationService" && e.ToType == "WorkProcessingService").Stroke);
        Assert.Equal("#ef4444", edges.Single(e => e.FromType == "EntryManager" && e.ToType == "WorkBroker").Stroke);
    }
    [Fact]
    public void ShouldAllowBrokersToUseExternalExposuresBeforeTheLastPhysicalRow()
    {
        var p = RenderConfigurationTests.Project("App", "FirstBroker", "NestedManager", "LastBroker", "External.Client");
        p.Types![3].IsInternal = false; p.Types[3].AssemblyName = "Library";
        p.Dependencies = [RenderConfigurationTests.Link("FirstBroker", "NestedManager"), RenderConfigurationTests.Link("NestedManager", "LastBroker"), RenderConfigurationTests.Link("FirstBroker", "External.Client")];
        var drawing = TestServices.Get<LayoutModelBuilder>().BuildRenderModel(new RenderModel([p]));
        Assert.NotEqual("#ef4444", Assert.Single(drawing.CrossProjectConnections).Stroke);
    }
    [Fact]
    public void ShouldInferCategoryOrderFromRuntimeConnectionsRatherThanEnumOrder()
    {
        var p = RenderConfigurationTests.Project("App", "FirstProcessingService", "SecondOrchestrationService");
        p.Dependencies = [RenderConfigurationTests.Link("FirstProcessingService", "SecondOrchestrationService")];
        var drawing = TestServices.Get<LayoutModelBuilder>().BuildRenderModel(new RenderModel([p]));
        Assert.Equal(new[] { RenderNodeCategory.Processing, RenderNodeCategory.Orchestration }, drawing.Projects.Single().ArchitecturalLayers);
        Assert.NotEqual("#ef4444", Assert.Single(drawing.Projects.Single().Connections).Stroke);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ShouldNotJudgeRegistrationAsRuntimeLayerSkipping(bool split)
    {
        var p = await ConcreteCallChainTests.ExtractAsync("""
            namespace Microsoft.Extensions.DependencyInjection { public interface IServiceCollection {} }
            public static class Bootstrap {
              public static void Configure(Microsoft.Extensions.DependencyInjection.IServiceCollection services) { Register<EntryManager>(); Register<WorkProcessingService>(); Register<WorkBroker>(); }
              private static void Register<T>() {}
            }
            public class EntryManager { public void Run() { new WorkProcessingService().Run(); } }
            public class WorkProcessingService { public void Run() { new WorkBroker().Run(); } }
            public class WorkBroker { public void Run() {} }
            """);
        var projects = split ? TestServices.Get<ProjectModelSplitter>().Split(p) : new[] { p };
        Assert.All(projects.SelectMany(project => project.Types!), type => Assert.Equal(p.Types!.Single(original => original.Name == type.Name).HasDeclaredBehaviour, type.HasDeclaredBehaviour));
        var drawing = TestServices.Get<LayoutModelBuilder>().BuildRenderModel(new RenderModel(projects));
        var registrations = drawing.Projects.SelectMany(p => p.Connections).Where(e => e.FromType == "Bootstrap").ToArray();
        Assert.NotEmpty(registrations);
        Assert.All(registrations, e => Assert.True(e.IsComposition));
        Assert.All(registrations, e => Assert.NotEqual("#ef4444", e.Stroke));
        foreach (IDiagramRenderer renderer in new IDiagramRenderer[] { TestServices.Get<HtmlDiagramRenderer>(), TestServices.Get<DrawIODiagramRenderer>() })
        {
            var document = System.Xml.Linq.XDocument.Parse(System.Text.Encoding.UTF8.GetString(renderer.Render(new RenderModel(projects))));
            Assert.Contains(document.Descendants(), e => (string?)e.Attribute("class") == "link composition" || ((string?)e.Attribute("style"))?.Contains("dashPattern=2 4") == true);
        }
    }
}
