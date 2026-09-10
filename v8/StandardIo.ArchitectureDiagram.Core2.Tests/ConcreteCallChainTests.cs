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

public sealed class ConcreteCallChainTests
{
    [Fact]
    public async Task ShouldLinkAnInjectedInterfaceCallToEveryConcreteImplementationAsync()
    {
        ProjectModel model = await ExtractAsync("""
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

        Dependency[] calls = model.Dependencies!.Where(link => link.DependencyType == DependencyType.Consumed).ToArray();
        Assert.Equal(new[] { "First", "Second" }, calls.Select(link => link.ToType));
        Assert.All(calls, link => { Assert.Equal("Manager", link.FromType); Assert.Equal("Execute", link.FromMethod); Assert.Equal("Run", link.ToMethod); });
        Assert.DoesNotContain(calls, link => link.ToType == "IWorker" || link.ToType == "AbstractWorker");
        Assert.Contains(model.Dependencies!, link => link.FromType == "First" && link.ToType == "IWorker" && link.DependencyType == DependencyType.Inheritance);
    }

    [Fact]
    public async Task ShouldCollapsePrivateHelperChainsForEachPublicCallerAndStopHelperCyclesAsync()
    {
        ProjectModel model = await ExtractAsync("""
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

        Dependency[] calls = model.Dependencies!.Where(link => link.DependencyType == DependencyType.Consumed).ToArray();
        Assert.Equal(new[] { "Create", "Update" }, calls.Select(link => link.FromMethod));
        Assert.All(calls, link => { Assert.Equal("Worker", link.ToType); Assert.Equal("Run", link.ToMethod); });
        Assert.Equal(new[] { "Create", "Update" }, model.Types!.Single(type => type.Name == "Manager").Methods!.Select(method => method.Name));
    }

    [Fact]
    public async Task ShouldOnlyListPubliclyDeclaredConcreteMethodsAsync()
    {
        ProjectModel model = await ExtractAsync("""
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

        Assert.Equal(new[] { "Run", "Run", "Static" }, model.Types!.Single(type => type.Name == "Example").Methods!.Select(method => method.Name));
    }

    [Fact]
    public async Task ShouldResolveGenericAndInheritedContractsUsingRoslynMethodIdentityAsync()
    {
        ProjectModel model = await ExtractAsync("""
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

        var calls = model.Dependencies!.Where(link => link.DependencyType == DependencyType.Consumed).ToArray();
        Assert.Equal(new[] { "GenericWorker<T>", "IntegerWorker" }, calls.Select(link => link.ToType));
        Assert.All(calls, link => Assert.Equal("Run", link.ToMethod));
    }

    [Fact]
    public async Task ShouldRetainTheInterfaceBoundaryWhenNoConcreteImplementationIsAvailableAsync()
    {
        ProjectModel model = await ExtractAsync("""
            interface IRemote { void Send(); }
            class Manager { public void Execute(IRemote remote) { remote.Send(); } }
            """);

        Dependency call = Assert.Single(model.Dependencies!.Where(link => link.DependencyType == DependencyType.Consumed));
        Assert.Equal("IRemote", call.ToType);
        Assert.Equal("Execute", call.FromMethod);
    }

    [Fact]
    public async Task ShouldIncludeExplicitContractMethodsAndCollapseTheirPrivateHelpersAsync()
    {
        ProjectModel model = await ExtractAsync("""
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

        var worker = model.Types!.Single(type => type.Name == "Worker");
        string contractMethod = Assert.Single(worker.Methods!).Name!;
        Assert.Equal("IWorker.Run", contractMethod);
        Assert.Contains(model.Dependencies!, link => link.FromType == "Manager" && link.FromMethod == "Execute"
            && link.ToType == "Worker" && link.ToMethod == contractMethod);
        Assert.Contains(model.Dependencies!, link => link.FromType == "Worker" && link.FromMethod == contractMethod
            && link.ToType == "Storage" && link.ToMethod == "Save");
        Assert.DoesNotContain(model.Dependencies!, link => link.FromMethod == "Private" || link.ToMethod == "Private");
    }

    [Fact]
    public async Task ShouldFollowOnlyTheCalledPrivateOverloadAndRetainPublicLocalCallsAsync()
    {
        ProjectModel model = await ExtractAsync("""
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

        var calls = model.Dependencies!.Where(link => link.DependencyType == DependencyType.Consumed).ToArray();
        Assert.Equal(2, calls.Length);
        Assert.Contains(calls, link => link.ToType == "Used" && link.FromMethod == "Execute");
        Assert.Contains(calls, link => link.ToType == "Manager" && link.ToMethod == "Public");
        Assert.DoesNotContain(calls, link => link.ToType == "Unused");
    }

    internal static async Task<ProjectModel> ExtractAsync(string source)
    {
        using var workspace = new AdhocWorkspace();
        var info = ProjectInfo.Create(
            id: ProjectId.CreateNewId(), version: VersionStamp.Create(), name: "ConcreteCalls", assemblyName: "ConcreteCalls",
            language: LanguageNames.CSharp, filePath: Path.GetFullPath("ConcreteCalls.csproj"),
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
            metadataReferences: new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) });
        Project project = workspace.AddProject(info);
        workspace.AddDocument(project.Id, "Source.cs", SourceText.From(source));
        return await ProjectModelTestFactory.ExtractAsync(workspace.CurrentSolution.GetProject(project.Id)!);
    }
}