// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using StandardIo.ArchitectureDiagram.Core2.Models;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core2.Tests;
public sealed partial class ConcreteCallChainTests
{
    private static readonly string[] workerImplementations = new[]
    {
        "First",
        "Second"
    };
    private static readonly string[] publicEntryMethods = new[]
    {
        "Create",
        "Update"
    };
    private static readonly string[] declaredPublicMethods = new[]
    {
        "Run",
        "Run",
        "Static"
    };
    private static readonly string[] genericImplementations = new[]
    {
        "GenericWorker<T>",
        "IntegerWorker"
    };
    [Theory]
    [InlineData("context.Items.Add(1)")]
    [InlineData("context.Set<int>().Add(1)")]
    [InlineData("context.Items.Clear()")]
    public async Task ShouldAttributeCollectionCallsToTheirContextAsync(string expression)
    {
        ProjectModel model = await ExtractAsync(source: """
            using System.Collections.Generic;
            class Context
            {
                public List<int> Items { get; } = new();
                public List<T> Set<T>() => new();
            }
            class Broker
            {
                readonly Context context = new();
                public void Execute() { EXPRESSION; }
            }
            """.Replace("EXPRESSION", expression));

        var calls = model.Dependencies!.Where(link => link.FromType == "Broker").ToArray();
        Assert.NotEmpty(calls);
        Assert.All(calls, link => Assert.Equal("Context", link.ToType));
        Assert.Contains(calls, link => link.ToMethod == (expression.Contains("Clear") ? "Clear" : "Add"));
        Assert.DoesNotContain(model.Types!, type => type.Name!.StartsWith("System.Collections.Generic.List"));
    }

    [Fact]
    public async Task ShouldLinkAnInjectedInterfaceCallToEveryConcreteImplementationAsync()
    {
        // Given: the fixture and inputs below.
        // When: exercise the operation under test.
        ProjectModel model = await ExtractAsync(source: """
            interface IWorker { void Run(); }
            abstract class AbstractWorker : IWorker { public abstract void Run(); }
            class First : IWorker { public void Run() {} }
            class Second : AbstractWorker { public override void Run() {} }
            class Manager
            {
                readonly IWorker worker;
                public Manager(IWorker worker) { this.worker = worker; }
                public void Execute() { worker.Run(); }
            }
            """);

        TypeRelationship[] calls = model.Dependencies!.Where(predicate: link => link.DependencyType == DependencyType.Consumed)
            .ToArray();
        // Then: verify the resulting contract.

        Assert.Equal(expected: workerImplementations, actual: calls.Select(selector: link => link.ToType));

        Assert.All(collection: calls, action: link =>
        {
            Assert.Equal(expected: "Manager", actual: link.FromType);
            Assert.Equal(expected: "Execute", actual: link.FromMethod);
            Assert.Equal(expected: "Run", actual: link.ToMethod);
        });

        Assert.DoesNotContain(collection: calls, filter: link => link.ToType == "IWorker" || link.ToType == "AbstractWorker");
        Assert.Contains(collection: model.Dependencies!, filter: link => link.FromType == "First" && link.ToType == "IWorker" && link.DependencyType == DependencyType.Inheritance);
    }

    [Fact]
    public async Task ShouldCollapsePrivateHelperChainsForEachPublicCallerAndStopHelperCyclesAsync()
    {
        // Given: the fixture and inputs below.
        // When: exercise the operation under test.
        ProjectModel model = await ExtractAsync(source: """
            interface IWorker { void Run(); }
            class Worker : IWorker { public void Run() {} }
            class Manager
            {
                readonly IWorker worker;
                public Manager(IWorker worker) { this.worker = worker; }
                public void Create() { Start(); }
                public void Update() { Start(); }
                private void Start() { Finish(); }
                private void Finish() { Start(); worker.Run(); }
                private void NeverCalled() { worker.Run(); }
            }
            """);

        TypeRelationship[] calls = model.Dependencies!.Where(predicate: link => link.DependencyType == DependencyType.Consumed)
            .ToArray();
        // Then: verify the resulting contract.

        Assert.Equal(expected: new[] { "Create", "NeverCalled", "Update" }, actual: calls.Select(selector: link => link.FromMethod));

        Assert.All(collection: calls, action: link =>
        {
            Assert.Equal(expected: "Worker", actual: link.ToType);
            Assert.Equal(expected: "Run", actual: link.ToMethod);
        });

        Assert.Equal(expected: publicEntryMethods, actual: model.Types!.Single(predicate: type => type.Name == "Manager").Methods!.Select(selector: method => method.Name));
    }

    [Fact]
    public async Task ShouldOnlyListPubliclyDeclaredConcreteMethodsAsync()
    {
        // Given: the fixture and inputs below.
        // When: exercise the operation under test.
        ProjectModel model = await ExtractAsync(source: """
            class Parent { public void Inherited() {} }
            class Example : Parent
            {
                public void Run() {}
                public static void Static() {}
                public void Run(int count) {}
                private void Private() {}
                protected void Protected() {}
                internal void Internal() {}
                public int Property { get; set; }
            }
            """);
        // Then: verify the resulting contract.

        Assert.Equal(expected: declaredPublicMethods, actual: model.Types!.Single(predicate: type => type.Name == "Example").Methods!.Select(selector: method => method.Name));
    }

    [Fact]
    public async Task ShouldResolveGenericAndInheritedContractsUsingRoslynMethodIdentityAsync()
    {
        // Given: the fixture and inputs below.
        // When: exercise the operation under test.
        ProjectModel model = await ExtractAsync(source: """
            interface IWorker<T> { void Run(T item); }
            interface IChild : IWorker<int> {}
            class IntegerWorker : IChild { public void Run(int item) {} }
            class StringWorker : IWorker<string> { public void Run(string item) {} }
            class GenericWorker<T> : IWorker<T> { public void Run(T item) {} }
            class Manager
            {
                public void Execute(IChild worker) { worker.Run(1); }
            }
            """);

        var calls = model.Dependencies!.Where(predicate: link => link.DependencyType == DependencyType.Consumed)
            .ToArray();
        // Then: verify the resulting contract.

        Assert.Equal(expected: genericImplementations, actual: calls.Select(selector: link => link.ToType));
        Assert.All(collection: calls, action: link => Assert.Equal(expected: "Run", actual: link.ToMethod));
    }

    [Fact]
    public async Task ShouldRetainTheInterfaceBoundaryWhenNoConcreteImplementationIsAvailableAsync()
    {
        // Given: the fixture and inputs below.
        // When: exercise the operation under test.
        ProjectModel model = await ExtractAsync(source: """
            interface IRemote { void Send(); }
            class Manager { public void Execute(IRemote remote) { remote.Send(); } }
            """);
        // Then: verify the resulting contract.

        TypeRelationship call = Assert.Single(collection: model.Dependencies!, predicate: link => link.DependencyType == DependencyType.Consumed);
        Assert.Equal(expected: "IRemote", actual: call.ToType);
        Assert.Equal(expected: "Execute", actual: call.FromMethod);
    }

    [Fact]
    public async Task ShouldIncludeExplicitContractMethodsAndCollapseTheirPrivateHelpersAsync()
    {
        // Given: the fixture and inputs below.
        // When: exercise the operation under test.
        ProjectModel model = await ExtractAsync(source: """
            interface IWorker { void Run(); }
            interface IStorage { void Save(); }
            class Storage : IStorage { public void Save() {} }
            class Worker : IWorker
            {
                readonly IStorage storage;
                public Worker(IStorage storage) { this.storage = storage; }
                void IWorker.Run() { Private(); }
                private void Private() { storage.Save(); }
            }
            class Manager { public void Execute(IWorker worker) { worker.Run(); } }
            """);

        var worker = model.Types!.Single(predicate: type => type.Name == "Worker");
        // Then: verify the resulting contract.
        string contractMethod = Assert.Single(collection: worker.Methods!).Name!;
        Assert.Equal(expected: "IWorker.Run", actual: contractMethod);
        Assert.Contains(collection: model.Dependencies!, filter: link => link.FromType == "Manager" && link.FromMethod == "Execute" && link.ToType == "Worker" && link.ToMethod == contractMethod);
        Assert.Contains(collection: model.Dependencies!, filter: link => link.FromType == "Worker" && link.FromMethod == contractMethod && link.ToType == "Storage" && link.ToMethod == "Save");
        Assert.DoesNotContain(collection: model.Dependencies!, filter: link => link.FromMethod == "Private" || link.ToMethod == "Private");
    }

    [Fact]
    public async Task ShouldRetainUncalledPrivateBehaviourWithoutAttributingItToPublicCallersAsync()
    {
        // Given: the fixture and inputs below.
        // When: exercise the operation under test.
        ProjectModel model = await ExtractAsync(source: """
            class Used { public static void Run() {} }
            class Unused { public static void Run() {} }
            class Manager
            {
                public void Execute() { Helper(1); Public(); }
                public void Public() {}
                private void Helper(int value) { Used.Run(); }
                private void Helper(string value) { Unused.Run(); }
            }
            """);

        var calls = model.Dependencies!.Where(predicate: link => link.DependencyType == DependencyType.Consumed)
            .ToArray();
        // Then: verify the resulting contract.

        Assert.Equal(2, calls.Length);
        Assert.Contains(collection: calls, filter: link => link.ToType == "Used" && link.FromMethod == "Execute");
        Assert.DoesNotContain(collection: calls, filter: link => link.FromType == link.ToType);
        Assert.Contains(collection: calls, filter: link => link.ToType == "Unused" && link.FromMethod == "Helper");
        Assert.DoesNotContain(calls, link => link.ToType == "Unused" && link.FromMethod == "Execute");
    }

    internal static async Task<ProjectModel> ExtractAsync(string source)
    {
        using var workspace = new AdhocWorkspace();
        var info = ProjectInfo.Create(id: ProjectId.CreateNewId(), version: VersionStamp.Create(), name: "ConcreteCalls", assemblyName: "ConcreteCalls", language: LanguageNames.CSharp, filePath: Path.GetFullPath(path: "ConcreteCalls.csproj"), compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary), metadataReferences: new[] { MetadataReference.CreateFromFile(path: typeof(object).Assembly.Location) });
        Project project = workspace.AddProject(projectInfo: info);
        workspace.AddDocument(projectId: project.Id, name: "Source.cs", text: SourceText.From(text: source));
        return await ProjectModelTestFactory.ExtractAsync(project: workspace.CurrentSolution.GetProject(projectId: project.Id)!);
    }
}