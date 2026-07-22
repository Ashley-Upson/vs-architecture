using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.Analyses;
using StandardIo.ArchitectureDiagram.Core.Models;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core.Tests;

public sealed class RoslynDependencyAnalyzerTests
{
    [Fact]
    public async Task Analyze_requests_one_compilation_per_project()
    {
        using var workspace = new AdhocWorkspace();
        var solution = workspace.CurrentSolution;
        var firstId = ProjectId.CreateNewId();
        var secondId = ProjectId.CreateNewId();
        solution = solution
            .AddProject(firstId, "First", "First", LanguageNames.CSharp)
            .AddMetadataReference(firstId, MetadataReference.CreateFromFile(typeof(object).Assembly.Location))
            .AddDocument(DocumentId.CreateNewId(firstId), "First.cs", "public class First { }")
            .AddProject(secondId, "Second", "Second", LanguageNames.CSharp)
            .AddMetadataReference(secondId, MetadataReference.CreateFromFile(typeof(object).Assembly.Location))
            .AddDocument(DocumentId.CreateNewId(secondId), "Second.cs", "public class Second { }");
        using var session = GenerationPerformanceSession.Start();

        await new RoslynDependencyAnalyzer().AnalyzeAsync(
            new[] { solution.GetProject(secondId)!, solution.GetProject(firstId)! },
            DiagramSettings.CreateDefault());
        var report = session.Snapshot();

        Assert.Equal(2, report.Counters.Single(item => item.Name == "Roslyn compilation requests").Value);
        Assert.Equal(2, report.Phases.Single(item => item.Phase == "Roslyn compilation acquisition").InvocationCount);
    }

    [Fact]
    public async Task Analyze_emits_external_boundary_node_for_unselected_project_reference()
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

        var project = Assert.Single(graph.Projects);
        Assert.Equal("Api", project.Name);
        Assert.Equal("Api", graph.Projects[0].Name);
        var external = Assert.Single(graph.ExternalDependencies);
        Assert.Equal("DomainService", external.Name);
        Assert.Equal("Domain.DomainService", external.FullName);
        Assert.Equal("[External]", external.Tag);
        Assert.Contains(graph.Edges, e => e.Kind == "external" && e.TargetId == external.Id);
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
    public async Task Analyze_detects_primary_constructor_dependencies()
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
                    public class Dependency {}
                    public class HomeController(Dependency dependency) {}
                }
                """));

        var graph = await new RoslynDependencyAnalyzer().AnalyzeAsync(solution.GetProject(projectId)!, DiagramSettings.CreateDefault());
        var project = Assert.Single(graph.Projects);
        var controller = project.Types.Single(t => t.Name == "HomeController");
        var dependency = project.Types.Single(t => t.Name == "Dependency");

        Assert.Contains(graph.Edges, e => e.SourceId == controller.Id && e.TargetId == dependency.Id);
    }

    [Fact]
    public async Task Analyze_detects_record_primary_constructor_dependencies()
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
                    public class Dependency {}
                    public record HomeController(Dependency Dependency);
                }
                """));

        var graph = await new RoslynDependencyAnalyzer().AnalyzeAsync(solution.GetProject(projectId)!, DiagramSettings.CreateDefault());
        var project = Assert.Single(graph.Projects);
        var controller = project.Types.Single(t => t.Name == "HomeController");
        var dependency = project.Types.Single(t => t.Name == "Dependency");

        Assert.Contains(graph.Edges, e => e.SourceId == controller.Id && e.TargetId == dependency.Id);
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
    public async Task Analyze_is_stable_when_selected_project_enumeration_is_reversed()
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
        var forwardProjects = new[] { solution.GetProject(apiId)!, solution.GetProject(workerId)! };
        var reverseProjects = new[] { forwardProjects[1], forwardProjects[0] };
        var analyzer = new RoslynDependencyAnalyzer();

        var forward = await analyzer.AnalyzeAsync(forwardProjects, DiagramSettings.CreateDefault());
        var reverse = await analyzer.AnalyzeAsync(reverseProjects, DiagramSettings.CreateDefault());

        Assert.Equal(
            forward.Projects.Select(project => (project.Id, project.Name)),
            reverse.Projects.Select(project => (project.Id, project.Name)));
        Assert.Equal(
            forward.Projects.SelectMany(project => project.Types).Select(type => (type.Id, type.ProjectId, type.FullName)),
            reverse.Projects.SelectMany(project => project.Types).Select(type => (type.Id, type.ProjectId, type.FullName)));
        Assert.Equal(
            forward.Edges.Select(edge => (edge.Id, edge.SourceId, edge.TargetId, edge.Kind)),
            reverse.Edges.Select(edge => (edge.Id, edge.SourceId, edge.TargetId, edge.Kind)));
    }

    [Fact]
    public async Task Analyze_keeps_constructor_interface_when_implementation_is_not_registered()
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
        var serviceInterface = project.Types.Single(t => t.Name == "IPaymentService");

        Assert.Contains(project.Types, t => t.Name == "PaymentService");
        Assert.Contains(graph.Edges, e => e.SourceId == controller.Id && e.TargetId == serviceInterface.Id);
    }

    [Fact]
    public async Task Analyze_resolves_registered_constructor_interface_to_implementation()
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
                    public interface ICultureService {}
                    public class CultureService : ICultureService {}
                    public class CultureProcessingService
                    {
                        public CultureProcessingService(ICultureService service) {}
                    }

                    public class Startup
                    {
                        public void ConfigureServices(dynamic services)
                        {
                            services.AddScoped<ICultureService, CultureService>();
                        }
                    }
                }
                """));

        var graph = await new RoslynDependencyAnalyzer().AnalyzeAsync(solution.GetProject(projectId)!, DiagramSettings.CreateDefault());
        var project = Assert.Single(graph.Projects);
        var processing = project.Types.Single(t => t.Name == "CultureProcessingService");
        var serviceImplementation = project.Types.Single(t => t.Name == "CultureService : ICultureService");

        Assert.Contains(graph.Edges, e => e.SourceId == processing.Id && e.TargetId == serviceImplementation.Id);
        Assert.DoesNotContain(project.Types, t => t.FullName == "Api.ICultureService");
        Assert.Equal(InterfaceResolutionStatus.Unique, serviceImplementation.InterfaceResolution);
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

    [Fact]
    public async Task Analyze_generates_unique_node_ids()
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
                    public class Controller {}
                    public class Service {}
                }
                """));

        var graph = await new RoslynDependencyAnalyzer().AnalyzeAsync(solution.GetProject(projectId)!, DiagramSettings.CreateDefault());
        var ids = graph.Projects
            .Select(project => project.UniqueId)
            .Concat(graph.Projects.SelectMany(project => project.Types.Select(type => type.UniqueId)))
            .ToArray();

        Assert.All(ids, id => Assert.True(Guid.TryParse(id, out _)));
        Assert.Equal(ids.Length, ids.Distinct().Count());
    }

    [Fact]
    public async Task Analyze_preserves_constructor_dependency_order()
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
                    public class FirstDependency {}
                    public class SecondDependency {}
                    public class Controller
                    {
                        public Controller(FirstDependency first, SecondDependency second) {}
                    }
                }
                """));

        var graph = await new RoslynDependencyAnalyzer().AnalyzeAsync(solution.GetProject(projectId)!, DiagramSettings.CreateDefault());
        var project = Assert.Single(graph.Projects);
        var controller = project.Types.Single(t => t.Name == "Controller");
        var orderedTargets = graph.Edges
            .Where(edge => edge.SourceId == controller.Id)
            .Select(edge => project.Types.Single(type => type.Id == edge.TargetId).Name)
            .ToArray();

        Assert.Equal(new[] { "FirstDependency", "SecondDependency" }, orderedTargets);
    }

    [Fact]
    public async Task Analyze_keeps_cycles_finite_with_reused_nodes()
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
                    public class A { public A(B b) {} }
                    public class B { public B(A a) {} }
                }
                """));

        var graph = await new RoslynDependencyAnalyzer().AnalyzeAsync(solution.GetProject(projectId)!, DiagramSettings.CreateDefault());
        var project = Assert.Single(graph.Projects);
        var a = project.Types.Single(t => t.Name == "A");
        var b = project.Types.Single(t => t.Name == "B");

        Assert.Equal(2, project.Types.Count);
        Assert.Contains(graph.Edges, edge => edge.SourceId == a.Id && edge.TargetId == b.Id);
        Assert.Contains(graph.Edges, edge => edge.SourceId == b.Id && edge.TargetId == a.Id);
    }

    private static IEnumerable<MetadataReference> BasicReferences()
    {
        yield return MetadataReference.CreateFromFile(typeof(object).Assembly.Location);
        yield return MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location);
        yield return MetadataReference.CreateFromFile(typeof(System.Text.Json.JsonDocument).Assembly.Location);
    }
}
