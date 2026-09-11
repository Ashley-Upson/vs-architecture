using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using StandardIo.ArchitectureDiagram.Core.Models;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.Analyses;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core.Tests;

public sealed class ArchitectureAnalyserTests
{
    [Fact]
    public async Task Analyse_returns_architecture_specific_nodes_without_property_tables()
    {
        using var workspace = Project("""
            namespace Fixture;
            public interface IService { }
            public class Service : IService { public Entity Model { get; } = new(); }
            public class Entity { public string Name { get; set; } = ""; }
            """);

        var diagram = await new RoslynDependencyAnalyzer().AnalyseAsync(
            workspace.CurrentSolution.Projects, new ArchitectureAnalysisSettings());

        Assert.Contains(diagram.Projects.SelectMany(project => project.Nodes), node => node.FullName == "Fixture.Entity");
        Assert.DoesNotContain(typeof(StandardIo.ArchitectureDiagram.Core.Models.Architectures.ArchitectureNode)
            .GetProperties(), property => property.Name == "Properties" || property.Name == "MethodCount");
    }

    [Fact]
    public async Task Analyse_owns_root_pattern_scope_independently()
    {
        using var workspace = Project("""
            namespace Fixture;
            public class Root { public Root(Child child) { } }
            public class Child { }
            public class Unrelated { }
            """);

        var diagram = await new RoslynDependencyAnalyzer().AnalyseAsync(
            workspace.CurrentSolution.Projects,
            new ArchitectureAnalysisSettings { RootDiscoveryPatternsText = "^Fixture\\.Root$" });

        Assert.Equal(new[] { "Fixture.Child", "Fixture.Root" }, diagram.Projects
            .SelectMany(project => project.Nodes).Select(node => node.FullName).OrderBy(name => name).ToArray());
        Assert.Single(diagram.Links);
        Assert.NotNull(diagram.Selection);
        Assert.Equal("ConfiguredRootReachability", diagram.Selection!.ScopePolicy);
    }

    private static AdhocWorkspace Project(string source)
    {
        var workspace = new AdhocWorkspace();
        var projectId = ProjectId.CreateNewId();
        var solution = workspace.CurrentSolution.AddProject(ProjectInfo.Create(
            projectId, VersionStamp.Create(), "Fixture", "Fixture", LanguageNames.CSharp,
            metadataReferences:
            [
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location)
            ],
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
            parseOptions: CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview)));
        solution = solution.AddDocument(DocumentId.CreateNewId(projectId), "Fixture.cs", SourceText.From(source));
        Assert.True(workspace.TryApplyChanges(solution));
        return workspace;
    }
}
