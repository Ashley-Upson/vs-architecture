using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using StandardIo.ArchitectureDiagram.Core.Models;
using StandardIo.ArchitectureDiagram.Core.Models.Generation;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.Analyses;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.Renderers;
using StandardIo.ArchitectureDiagram.Core.Services.Orchestrations.Diagrams;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core.Tests;

public sealed class InterfaceResolutionTests
{
    [Theory]
    [InlineData("AddScoped")]
    [InlineData("AddTransient")]
    [InlineData("AddSingleton")]
    public async Task Two_type_registration_is_unique(string method)
    {
        var diagram = await Analyse(Source($"services.{method}<IOrderService, OrderService>();"));

        AssertUnique(diagram, "OrderService : IOrderService", "Fixture.IOrderService", "Fixture.OrderService");
    }

    [Fact]
    public async Task Factory_registration_with_static_new_type_is_unique()
    {
        var diagram = await Analyse(Source("services.AddScoped<IOrderService>(_ => new OrderService());"));

        AssertUnique(diagram, "OrderService : IOrderService", "Fixture.IOrderService", "Fixture.OrderService");
    }

    [Fact]
    public async Task Instance_registration_with_static_type_is_unique()
    {
        var diagram = await Analyse(Source("var instance = new OrderService(); services.AddSingleton<IOrderService>(instance);"));

        AssertUnique(diagram, "OrderService : IOrderService", "Fixture.IOrderService", "Fixture.OrderService");
    }

    [Fact]
    public async Task No_registration_keeps_unresolved_interface()
    {
        var diagram = await Analyse(Source(string.Empty));
        var node = Node(diagram, "IOrderService");

        Assert.Equal(InterfaceResolutionStatus.Unresolved, node.InterfaceResolution);
        Assert.Equal("Fixture.IOrderService", node.InterfaceIdentity);
        Assert.Equal(0, node.ImplementationCount);
        AssertLinkTargets(diagram, node.Id);
    }

    [Fact]
    public async Task Implementation_without_registration_does_not_imply_resolution()
    {
        var diagram = await Analyse(Source(string.Empty));

        Assert.Equal(InterfaceResolutionStatus.Unresolved, Node(diagram, "IOrderService").InterfaceResolution);
        Assert.Contains(diagram.Projects.SelectMany(project => project.Nodes), node => node.Name == "OrderService");
    }

    [Fact]
    public async Task Multiple_distinct_registrations_keep_counted_interface()
    {
        var diagram = await Analyse(Source("services.AddScoped<IOrderService, OrderService>(); services.AddTransient<IOrderService, OtherOrderService>();"));
        var node = Node(diagram, "IOrderService\n(2 implementations)");

        Assert.Equal(InterfaceResolutionStatus.Multiple, node.InterfaceResolution);
        Assert.Equal(2, node.ImplementationCount);
        Assert.Null(node.ImplementationIdentity);
        AssertLinkTargets(diagram, node.Id);
    }

    [Fact]
    public async Task Duplicate_registration_of_same_implementation_counts_once()
    {
        var diagram = await Analyse(Source("services.AddScoped<IOrderService, OrderService>(); services.AddSingleton<IOrderService, OrderService>();"));

        AssertUnique(diagram, "OrderService : IOrderService", "Fixture.IOrderService", "Fixture.OrderService");
    }

    [Fact]
    public async Task Generic_service_and_implementation_are_resolved()
    {
        var source = Source("services.AddScoped<IRepository<Order>, Repository<Order>>();")
            .Replace("public interface IOrderService { }", "public interface IOrderService { } public class Order { } public interface IRepository<T> { } public class Repository<T> : IRepository<T> { }")
            .Replace("public Consumer(IOrderService value)", "public Consumer(IRepository<Order> value)");
        var diagram = await Analyse(source);
        var node = diagram.Projects.SelectMany(project => project.Nodes)
            .Single(item => item.Name.StartsWith("Repository", StringComparison.Ordinal) && item.InterfaceResolution == InterfaceResolutionStatus.Unique);

        Assert.Contains("IRepository", node.Name);
        Assert.Contains("IRepository", node.InterfaceIdentity);
        AssertLinkTargets(diagram, node.Id);
    }

    [Fact]
    public async Task Same_short_interface_name_in_different_namespaces_stays_distinct()
    {
        var diagram = await Analyse("""
            namespace A { public interface IService { } public class ServiceA : IService { } }
            namespace B { public interface IService { } public class ServiceB : IService { } }
            namespace Fixture
            {
                public class Services { public void AddScoped<TService,TImplementation>() { } }
                public class Consumer { public Consumer(A.IService a, B.IService b) { } }
                public class Startup { public void Configure(Services services) { services.AddScoped<A.IService,A.ServiceA>(); services.AddScoped<B.IService,B.ServiceB>(); } }
            }
            """);

        var nodes = diagram.Projects.SelectMany(project => project.Nodes)
            .Where(node => node.InterfaceResolution == InterfaceResolutionStatus.Unique).ToArray();
        Assert.Contains(nodes, node => node.InterfaceIdentity == "A.IService" && node.Name == "ServiceA : IService");
        Assert.Contains(nodes, node => node.InterfaceIdentity == "B.IService" && node.Name == "ServiceB : IService");
    }

    [Fact]
    public async Task Reversed_registration_enumeration_is_stable()
    {
        var first = await Analyse(Source("services.AddScoped<IOrderService, OrderService>(); services.AddTransient<IOrderService, OtherOrderService>();"));
        var second = await Analyse(Source("services.AddTransient<IOrderService, OtherOrderService>(); services.AddScoped<IOrderService, OrderService>();"));

        Assert.Equal(Signature(first), Signature(second));
    }

    [Fact]
    public async Task Unique_node_is_owned_by_implementation_project()
    {
        using var workspace = new AdhocWorkspace();
        var implementationId = ProjectId.CreateNewId();
        var consumerId = ProjectId.CreateNewId();
        var solution = workspace.CurrentSolution
            .AddProject(ProjectInfo.Create(implementationId, VersionStamp.Create(), "Implementation", "Implementation", LanguageNames.CSharp,
                metadataReferences: References(), compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)))
            .AddDocument(DocumentId.CreateNewId(implementationId), "Implementation.cs", SourceText.From("namespace Contracts { public interface IService { } public class Service : IService { } }"))
            .AddProject(ProjectInfo.Create(consumerId, VersionStamp.Create(), "Consumer", "Consumer", LanguageNames.CSharp,
                projectReferences: [new ProjectReference(implementationId)], metadataReferences: References(),
                compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)))
            .AddDocument(DocumentId.CreateNewId(consumerId), "Consumer.cs", SourceText.From("namespace Consumer { public class Services { public void AddScoped<TS,TI>(){} } public class Root { public Root(Contracts.IService service){} } public class Startup { public void Configure(Services services){ services.AddScoped<Contracts.IService,Contracts.Service>(); } } }"));

        var diagram = await new RoslynDependencyAnalyzer().AnalyseAsync(solution.Projects, new ArchitectureAnalysisSettings());
        var implementationProject = diagram.Projects.Single(project => project.Name == "Implementation");
        var node = implementationProject.Nodes.Single(item => item.Name == "Service : IService");

        Assert.Equal(implementationProject.Id, node.ProjectId);
        Assert.Contains(diagram.Links, link => link.TargetId == node.Id);
    }

    [Theory]
    [InlineData("services.AddScoped<IOrderService, OrderService>();", "OrderService : IOrderService")]
    [InlineData("", "IOrderService")]
    [InlineData("services.AddScoped<IOrderService, OrderService>(); services.AddTransient<IOrderService, OtherOrderService>();", "IOrderService\n(2 implementations)")]
    public async Task Typed_production_serializes_interface_text_and_reconciles_link(string registration, string expectedText)
    {
        using var workspace = Workspace(Source(registration));
        var analyser = new RoslynDependencyAnalyzer();
        var semantic = await analyser.AnalyseAsync(workspace.CurrentSolution.Projects, new ArchitectureAnalysisSettings());
        var defaults = DiagramSettings.CreateDefault();
        var result = await new ArchitectureGenerationService(analyser, new DrawioArchitectureRenderer(), new DrawioDocumentComposer())
            .GenerateAsync(semantic, new ArchitectureGenerationJob(new ArchitectureAnalysisSettings(),
                new ArchitectureRenderSettings { Layout = defaults.Layout, Canvas = defaults.Canvas, StyleRules = defaults.StyleRules,
                    ProjectContainerStyle = defaults.ProjectContainerStyle, ExternalDependencyStyle = defaults.ExternalDependencyStyle,
                    Connector = defaults.Connector, NodeDuplication = defaults.NodeDuplication }));
        var values = result.Page.GraphModel.Descendants("mxCell").Select(cell => (string?)cell.Attribute("value")).ToArray();
        var analysis = new StandardIo.ArchitectureDiagram.Core.Services.Processings.Drawios.ArchitectureGeometryAnalyser().Analyse(result);

        Assert.Contains(values, value => value is not null && value.StartsWith(expectedText, StringComparison.Ordinal));
        Assert.All(analysis.Links, link => Assert.Equal(StandardIo.ArchitectureDiagram.Core.Models.Generation.ArchitectureLinkDisposition.Rendered, link.Disposition));
    }

    private static void AssertUnique(StandardIo.ArchitectureDiagram.Core.Models.Architectures.ArchitectureDiagram diagram,
        string name, string interfaceIdentity, string implementationIdentity)
    {
        var node = Node(diagram, name);
        Assert.Equal(InterfaceResolutionStatus.Unique, node.InterfaceResolution);
        Assert.Equal(interfaceIdentity, node.InterfaceIdentity);
        Assert.Equal(implementationIdentity, node.ImplementationIdentity);
        Assert.Equal(1, node.ImplementationCount);
        Assert.DoesNotContain(diagram.Projects.SelectMany(project => project.Nodes), item => item.FullName == interfaceIdentity);
        AssertLinkTargets(diagram, node.Id);
    }

    private static StandardIo.ArchitectureDiagram.Core.Models.Architectures.ArchitectureNode Node(
        StandardIo.ArchitectureDiagram.Core.Models.Architectures.ArchitectureDiagram diagram, string name) =>
        diagram.Projects.SelectMany(project => project.Nodes).Single(node => node.Name == name);

    private static void AssertLinkTargets(StandardIo.ArchitectureDiagram.Core.Models.Architectures.ArchitectureDiagram diagram, string targetId) =>
        Assert.Contains(diagram.Links, link => link.TargetId == targetId);

    private static string[] Signature(StandardIo.ArchitectureDiagram.Core.Models.Architectures.ArchitectureDiagram diagram) =>
        diagram.Projects.SelectMany(project => project.Nodes).OrderBy(node => node.Id)
            .Select(node => $"{node.Id}|{node.Name}|{node.InterfaceIdentity}|{node.ImplementationIdentity}|{node.ImplementationCount}|{node.InterfaceResolution}")
            .Concat(diagram.Links.OrderBy(link => link.Id).Select(link => $"{link.Id}|{link.SourceId}|{link.TargetId}"))
            .ToArray();

    private static async Task<StandardIo.ArchitectureDiagram.Core.Models.Architectures.ArchitectureDiagram> Analyse(string source)
    {
        using var workspace = Workspace(source);
        return await new RoslynDependencyAnalyzer().AnalyseAsync(workspace.CurrentSolution.Projects, new ArchitectureAnalysisSettings());
    }

    private static AdhocWorkspace Workspace(string source)
    {
        var workspace = new AdhocWorkspace();
        var id = ProjectId.CreateNewId();
        var solution = workspace.CurrentSolution.AddProject(ProjectInfo.Create(id, VersionStamp.Create(), "Fixture", "Fixture",
            LanguageNames.CSharp, metadataReferences: References(),
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)))
            .AddDocument(DocumentId.CreateNewId(id), "Fixture.cs", SourceText.From(source));
        Assert.True(workspace.TryApplyChanges(solution));
        return workspace;
    }

    private static MetadataReference[] References() =>
    [
        MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location)
    ];

    private static string Source(string registration) => $$"""
        using System;
        namespace Fixture
        {
            public interface IOrderService { }
            public class OrderService : IOrderService { }
            public class OtherOrderService : IOrderService { }
            public class Consumer { public Consumer(IOrderService value) { } }
            public class Services
            {
                public void AddScoped<TService,TImplementation>() { }
                public void AddTransient<TService,TImplementation>() { }
                public void AddSingleton<TService,TImplementation>() { }
                public void AddScoped<TService>(Func<object,TService> factory) { }
                public void AddSingleton<TService>(TService instance) { }
            }
            public class Startup { public void Configure(Services services) { {{registration}} } }
        }
        """;
}
