using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using StandardIo.ArchitectureDiagram.Core.Analysis;
using StandardIo.ArchitectureDiagram.Core.Settings;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core.Tests;

public sealed class RoslynDependencyAnalyzerTests
{
    [Fact]
    public async Task Analyze_detects_internal_project_reference_dependencies()
    {
        using var workspace = new AdhocWorkspace();
        var referencedId = ProjectId.CreateNewId();
        var selectedId = ProjectId.CreateNewId();
        var solution = workspace.CurrentSolution
            .AddProject(ProjectInfo.Create(
                referencedId,
                VersionStamp.Create(),
                "Domain",
                "Domain",
                LanguageNames.CSharp,
                metadataReferences: BasicReferences()))
            .AddDocument(DocumentId.CreateNewId(referencedId), "Service.cs", SourceText.From("namespace Domain { public class DomainService {} }"))
            .AddProject(ProjectInfo.Create(
                selectedId,
                VersionStamp.Create(),
                "Api",
                "Api",
                LanguageNames.CSharp,
                projectReferences: new[] { new ProjectReference(referencedId) },
                metadataReferences: BasicReferences()))
            .AddDocument(DocumentId.CreateNewId(selectedId), "Controller.cs", SourceText.From("namespace Api { public class HomeController { private readonly Domain.DomainService service; public HomeController(Domain.DomainService service) { this.service = service; } } }"));

        var selectedProject = solution.GetProject(selectedId)!;

        var graph = await new RoslynDependencyAnalyzer().AnalyzeAsync(selectedProject, DiagramSettings.CreateDefault());

        Assert.Contains(graph.Projects, p => p.Name == "Api");
        Assert.Contains(graph.Projects, p => p.Name == "Domain");
        Assert.Equal("Api", graph.Projects[0].Name);
        Assert.Contains(graph.Edges, e => e.Kind == "internal");
    }

    [Fact]
    public async Task Analyze_collapses_external_dependencies_by_assembly()
    {
        using var workspace = new AdhocWorkspace();
        var projectId = ProjectId.CreateNewId();
        var solution = workspace.CurrentSolution
            .AddProject(ProjectInfo.Create(
                projectId,
                VersionStamp.Create(),
                "Api",
                "Api",
                LanguageNames.CSharp,
                metadataReferences: BasicReferences()))
            .AddDocument(DocumentId.CreateNewId(projectId), "Controller.cs", SourceText.From("using System.Text.Json; namespace Api { public class HomeController { public HomeController(JsonDocument document) {} } }"));

        var graph = await new RoslynDependencyAnalyzer().AnalyzeAsync(solution.GetProject(projectId)!, DiagramSettings.CreateDefault());

        Assert.Contains(graph.ExternalDependencies, e => e.AssemblyName.Contains("System.Text.Json"));
    }

    [Fact]
    public async Task Analyze_ignores_implicit_framework_member_dependencies()
    {
        using var workspace = new AdhocWorkspace();
        var projectId = ProjectId.CreateNewId();
        var solution = workspace.CurrentSolution
            .AddProject(ProjectInfo.Create(
                projectId,
                VersionStamp.Create(),
                "Api",
                "Api",
                LanguageNames.CSharp,
                metadataReferences: BasicReferences()))
            .AddDocument(DocumentId.CreateNewId(projectId), "Controller.cs", SourceText.From("""
                namespace Api
                {
                    public class HomeController
                    {
                        public void Run()
                        {
                            var values = new[] { "a", "b" };
                            var count = values.Length;
                            var name = typeof(HomeController).FullName;
                        }
                    }
                }
                """));

        var graph = await new RoslynDependencyAnalyzer().AnalyzeAsync(solution.GetProject(projectId)!, DiagramSettings.CreateDefault());

        Assert.DoesNotContain(graph.ExternalDependencies, e => e.AssemblyName.StartsWith("System"));
        Assert.DoesNotContain(graph.ExternalDependencies, e => e.AssemblyName.StartsWith("Microsoft"));
    }

    [Fact]
    public async Task Analyze_ignores_base_types_and_method_body_types()
    {
        using var workspace = new AdhocWorkspace();
        var projectId = ProjectId.CreateNewId();
        var solution = workspace.CurrentSolution
            .AddProject(ProjectInfo.Create(
                projectId,
                VersionStamp.Create(),
                "Api",
                "Api",
                LanguageNames.CSharp,
                metadataReferences: BasicReferences()))
            .AddDocument(DocumentId.CreateNewId(projectId), "Services.cs", SourceText.From("""
                namespace Api
                {
                    public class EntityBase {}
                    public class MethodOnlyDependency {}
                    public class ConstructorDependency {}

                    public class HomeController : EntityBase
                    {
                        public HomeController(ConstructorDependency dependency) {}

                        public MethodOnlyDependency Build()
                        {
                            return new MethodOnlyDependency();
                        }
                    }
                }
                """));

        var graph = await new RoslynDependencyAnalyzer().AnalyzeAsync(solution.GetProject(projectId)!, DiagramSettings.CreateDefault());
        var project = Assert.Single(graph.Projects);
        var controller = project.Types.Single(t => t.Name == "HomeController");
        var constructorDependency = project.Types.Single(t => t.Name == "ConstructorDependency");
        var entityBase = project.Types.Single(t => t.Name == "EntityBase");
        var methodOnlyDependency = project.Types.Single(t => t.Name == "MethodOnlyDependency");

        Assert.Contains(graph.Edges, e => e.SourceId == controller.Id && e.TargetId == constructorDependency.Id);
        Assert.DoesNotContain(graph.Edges, e => e.SourceId == controller.Id && e.TargetId == entityBase.Id);
        Assert.DoesNotContain(graph.Edges, e => e.SourceId == controller.Id && e.TargetId == methodOnlyDependency.Id);
    }

    [Fact]
    public async Task Analyze_never_emits_core_library_dependencies()
    {
        using var workspace = new AdhocWorkspace();
        var projectId = ProjectId.CreateNewId();
        var solution = workspace.CurrentSolution
            .AddProject(ProjectInfo.Create(
                projectId,
                VersionStamp.Create(),
                "Api",
                "Api",
                LanguageNames.CSharp,
                metadataReferences: BasicReferences()))
            .AddDocument(DocumentId.CreateNewId(projectId), "Controller.cs", SourceText.From("""
                using System;

                namespace Api
                {
                    public class HomeController
                    {
                        public string Name { get; set; } = String.Empty;
                    }
                }
                """));

        var graph = await new RoslynDependencyAnalyzer().AnalyzeAsync(solution.GetProject(projectId)!, DiagramSettings.CreateDefault());

        Assert.DoesNotContain(graph.ExternalDependencies, e => e.AssemblyName is "System.Private.CoreLib" or "mscorlib" or "netstandard");
    }

    [Fact]
    public async Task Analyze_accepts_multiple_selected_projects()
    {
        using var workspace = new AdhocWorkspace();
        var apiId = ProjectId.CreateNewId();
        var workerId = ProjectId.CreateNewId();
        var solution = workspace.CurrentSolution
            .AddProject(ProjectInfo.Create(
                apiId,
                VersionStamp.Create(),
                "Api",
                "Api",
                LanguageNames.CSharp,
                metadataReferences: BasicReferences()))
            .AddDocument(DocumentId.CreateNewId(apiId), "Controller.cs", SourceText.From("namespace Api { public class HomeController {} }"))
            .AddProject(ProjectInfo.Create(
                workerId,
                VersionStamp.Create(),
                "Worker",
                "Worker",
                LanguageNames.CSharp,
                metadataReferences: BasicReferences()))
            .AddDocument(DocumentId.CreateNewId(workerId), "Job.cs", SourceText.From("namespace Worker { public class NightlyJob {} }"));

        var projects = new[] { solution.GetProject(apiId)!, solution.GetProject(workerId)! };

        var graph = await new RoslynDependencyAnalyzer().AnalyzeAsync(projects, DiagramSettings.CreateDefault());

        Assert.Contains(graph.Projects, p => p.Name == "Api");
        Assert.Contains(graph.Projects, p => p.Name == "Worker");
    }

    [Fact]
    public async Task Analyze_uses_implementation_when_interface_has_solution_implementation()
    {
        using var workspace = new AdhocWorkspace();
        var projectId = ProjectId.CreateNewId();
        var solution = workspace.CurrentSolution
            .AddProject(ProjectInfo.Create(
                projectId,
                VersionStamp.Create(),
                "Api",
                "Api",
                LanguageNames.CSharp,
                metadataReferences: BasicReferences()))
            .AddDocument(DocumentId.CreateNewId(projectId), "Services.cs", SourceText.From("""
                namespace Api
                {
                    public interface IPaymentService {}
                    public class PaymentService : IPaymentService {}
                    public class HomeController
                    {
                        public HomeController(IPaymentService service) {}
                    }
                }
                """));

        var graph = await new RoslynDependencyAnalyzer().AnalyzeAsync(solution.GetProject(projectId)!, DiagramSettings.CreateDefault());
        var project = Assert.Single(graph.Projects);
        var controller = project.Types.Single(t => t.Name == "HomeController");
        var implementation = project.Types.Single(t => t.Name == "PaymentService");

        Assert.DoesNotContain(project.Types, t => t.Name == "IPaymentService");
        Assert.Contains(graph.Edges, e => e.SourceId == controller.Id && e.TargetId == implementation.Id);
    }

    [Fact]
    public async Task Analyze_keeps_interface_when_no_solution_implementation_exists()
    {
        using var workspace = new AdhocWorkspace();
        var projectId = ProjectId.CreateNewId();
        var solution = workspace.CurrentSolution
            .AddProject(ProjectInfo.Create(
                projectId,
                VersionStamp.Create(),
                "Api",
                "Api",
                LanguageNames.CSharp,
                metadataReferences: BasicReferences()))
            .AddDocument(DocumentId.CreateNewId(projectId), "Services.cs", SourceText.From("""
                namespace Api
                {
                    public interface IPaymentService {}
                    public class HomeController
                    {
                        public HomeController(IPaymentService service) {}
                    }
                }
                """));

        var graph = await new RoslynDependencyAnalyzer().AnalyzeAsync(solution.GetProject(projectId)!, DiagramSettings.CreateDefault());
        var project = Assert.Single(graph.Projects);
        var controller = project.Types.Single(t => t.Name == "HomeController");
        var serviceInterface = project.Types.Single(t => t.Name == "IPaymentService");

        Assert.Contains(graph.Edges, e => e.SourceId == controller.Id && e.TargetId == serviceInterface.Id);
    }

    private static IEnumerable<MetadataReference> BasicReferences()
    {
        yield return MetadataReference.CreateFromFile(typeof(object).Assembly.Location);
        yield return MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location);
        yield return MetadataReference.CreateFromFile(typeof(System.Text.Json.JsonDocument).Assembly.Location);
    }
}
