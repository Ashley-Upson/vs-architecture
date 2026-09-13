using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.Core2.Exposures;
using StandardIo.ArchitectureDiagram.Core2.Models;
using StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Dependencies;
using StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Rendering;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core2.Tests;

public sealed partial class ProjectModelPopulationTests
{
    [Fact]
    public async Task ShouldOmitSelfDependenciesButKeepCallsToOtherTypesAsync()
    {
        var broker = new CompilationBroker(() => CreateCompilation("Example", """
            public class Entry {
                public void Run() { RunAgain(); new Worker().Run(); }
                public void RunAgain() { RunAgain(); }
            }
            public class Worker { public void Run() {} }
            """));
        var project = new ProjectModel { Name = "Example", Path = "Example.csproj" };
        await new ProjectDependenciesService(broker).PopulateDependenciesAsync(project, CancellationToken.None);
        Assert.DoesNotContain(project.Dependencies!, e => e.FromType == e.ToType);
        Assert.Contains(project.Dependencies!, e => e.FromType == "Entry" && e.ToType == "Worker");
    }
}

public sealed class CompositionExposureTests
{
    [Fact]
    public void ShouldPlaceCompositionExtensionsAlongsideExposures()
    {
        var project = RenderConfigurationTests.Project("Example", "Example.IServiceCollectionExtensions", "Example.EntryManager");
        var model = TestServices.Get<LayoutModelBuilder>().BuildRenderModel(new RenderModel([project]));
        var nodes = model.Projects.Single().Nodes;
        Assert.All(nodes, n => Assert.Equal(RenderNodeCategory.Exposure, n.Category));
        Assert.Single(nodes.Select(n => n.Y).Distinct());
        Assert.Single(nodes.Select(n => n.Fill).Distinct());
    }

    [Fact]
    public void ShouldOmitSelfLinksFromPreviouslyExtractedModels()
    {
        var project = RenderConfigurationTests.Project("Example", "Example.EntryManager");
        project.Dependencies = [RenderConfigurationTests.Link("Example.EntryManager", "Example.EntryManager")];
        Assert.Empty(TestServices.Get<IProjectModelPresentationService>().Prepare(project).Model.Dependencies!);
    }
}
