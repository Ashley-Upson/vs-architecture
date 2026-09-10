// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using StandardIo.ArchitectureDiagram.Core2.Models;
using StandardIo.ArchitectureDiagram.Core2.Services.Orchestrations.Projects;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core2.Tests;

public sealed partial class ProjectExtractionTests
{
    private static Project AddProject(AdhocWorkspace workspace, string name, string source, params ProjectId[] references)
    {
        var info = ProjectInfo.Create(id: ProjectId.CreateNewId(), version: VersionStamp.Create(), name: name, assemblyName: name, language: LanguageNames.CSharp, filePath: Path.GetFullPath(path: name + ".csproj"), compilationOptions: new CSharpCompilationOptions(outputKind: OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true), metadataReferences: new[] { typeof(object).Assembly.Location, typeof(System.Runtime.CompilerServices.DynamicAttribute).Assembly.Location, Path.Combine(path1: Path.GetDirectoryName(path: typeof(object).Assembly.Location)!, path2: "System.Runtime.dll") }.Distinct()
            .Select(selector: path => MetadataReference.CreateFromFile(path: path)), projectReferences: references.Select(selector: id => new ProjectReference(projectId: id)));
        var project = workspace.AddProject(projectInfo: info);
        workspace.AddDocument(projectId: project.Id, name: name + ".cs", text: SourceText.From(text: source));
        return workspace.CurrentSolution.GetProject(projectId: project.Id)!;
    }

    [Fact]

    public async Task ShouldExtractDeclaredMembersAndMergePartialTypesAsync()
    {
        // Given
        using var workspace = new AdhocWorkspace();
        var project = AddProject(workspace: workspace, name: "Members", source: """
            namespace Example;

            public partial class Parent { public void Inherited() {} }
            public partial class Sample : Parent
            {
                public string Text;
                public int Count { get; set; }
                public void Run() {}
                public void Run(int count) {}
                public class Nested {}
            }
            public partial class Sample { public void Extra() {} }
            public interface IRun { void Run(); }
            """);
        // When
        var model = await ProjectModelTestFactory.ExtractAsync(project: project);
        var sample = Assert.Single(collection: model.Types!.Where(predicate: type => type.Name == "Example.Sample"));
        // Then
        Assert.Equal(expected: "Members", actual: model.Name);
        Assert.Equal(expected: project.FilePath, actual: model.Path);
        Assert.True(condition: sample.IsInternal);
        Assert.Equal(expected: "System.String", actual: Assert.Single(collection: sample.Fields!).Type);
        Assert.Equal(expected: "Text", actual: Assert.Single(collection: sample.Fields!).Name);
        Assert.Equal(expected: "System.Int32", actual: Assert.Single(collection: sample.Properties!).Type);
        Assert.Equal(expected: new[] { "Extra", "Run", "Run" }, actual: sample.Methods!.Select(selector: method => method.Name));
        Assert.Contains(collection: model.Types!, filter: type => type.Name == "Example.Sample.Nested");
        Assert.Equal(expected: FrameworkType.Interface, actual: model.Types!.Single(predicate: type => type.Name == "Example.IRun").FrameworkType);
    }

    [Fact]

    public async Task ShouldUseFullNamesForGenericArrayAndNullableMembersAsync()
    {
        // Given
        using var workspace = new AdhocWorkspace();
        var project = AddProject(workspace: workspace, name: "Names", source: """
            namespace Example;

            public class Box<T> {}
            public class Sample
            {
                public Box<string[]> Values;
                public int? Count { get; set; }
            }
            """);
        // When
        var model = await ProjectModelTestFactory.ExtractAsync(project: project);
        var sample = model.Types!.Single(predicate: type => type.Name == "Example.Sample");
        // Then
        Assert.Equal(expected: "Example.Box<System.String[]>", actual: Assert.Single(collection: sample.Fields!).Type);
        Assert.Equal(expected: "System.Nullable<System.Int32>", actual: Assert.Single(collection: sample.Properties!).Type);
    }

    [Fact]

    public async Task ShouldExtractDirectInheritanceAndDeduplicateCallsAsync()
    {
        // Given
        using var workspace = new AdhocWorkspace();
        var project = AddProject(workspace: workspace, name: "Links", source: """
            namespace Example;

            public interface IBase {}
            public interface IChild : IBase {}
            public class Base {}
            public class Target { public static void Work() {} }
            public class Caller : Base, IChild
            {
                public Target Field;
                public Target Property { get; set; }
                public void Run() { Target.Work(); Target.Work(); }
            }
            """);
        // When
        var model = await ProjectModelTestFactory.ExtractAsync(project: project);
        // Then
        var call = Assert.Single(collection: model.Dependencies!.Where(predicate: link => link.DependencyType == DependencyType.Consumed));
        Assert.Equal(expected: "Example.Caller", actual: call.FromType);
        Assert.Equal(expected: "Example.Target", actual: call.ToType);
        Assert.Equal(expected: "Run", actual: call.FromMethod);
        Assert.Equal(expected: "Work", actual: call.ToMethod);
        var inheritance = model.Dependencies!.Where(predicate: link => link.DependencyType == DependencyType.Inheritance)
            .ToArray();
        Assert.Equal(expected: 3, actual: inheritance.Length);
        Assert.Contains(collection: inheritance, filter: link => link.FromType == "Example.Caller" && link.ToType == "Example.Base");
        Assert.Contains(collection: inheritance, filter: link => link.FromType == "Example.Caller" && link.ToType == "Example.IChild");
        Assert.Contains(collection: inheritance, filter: link => link.FromType == "Example.IChild" && link.ToType == "Example.IBase");
        Assert.All(collection: inheritance, action: link =>
        {
            Assert.Null(@object: link.FromMethod);
            Assert.Null(@object: link.ToMethod);
        });
    }

    [Fact]

    public async Task ShouldStopAtAnUnselectedSourceProjectAsync()
    {
        // Given
        using var workspace = new AdhocWorkspace();
        var library = AddProject(workspace: workspace, name: "Library", source: """
            namespace Library;

            public class Further { public static void Run() {} }
            public class Boundary { public static void Run() { Further.Run(); } }
            """);
        var app = AddProject(workspace: workspace, name: "App", source: "class Entry { public void Go() { Library.Boundary.Run(); } }", library.Id);
        // When
        var model = await ProjectModelTestFactory.ExtractAsync(project: app);
        // Then
        var boundary = model.Types!.Single(predicate: type => type.Name == "Library.Boundary");
        Assert.False(condition: boundary.IsInternal);
        Assert.Empty(collection: boundary.Fields!);
        Assert.Empty(collection: boundary.Properties!);
        Assert.Empty(collection: boundary.Methods!);
        Assert.DoesNotContain(collection: model.Types!, filter: type => type.Name == "Library.Further");
        Assert.Single(collection: model.Dependencies!);
    }

    [Fact]

    public async Task ShouldRetainMetadataBoundariesWithoutExpandingThemAsync()
    {
        // Given
        using var workspace = new AdhocWorkspace();
        var project = AddProject(workspace: workspace, name: "External", source: "class Entry { public bool Go() => System.String.IsNullOrEmpty(null); }");
        // When
        var model = await ProjectModelTestFactory.ExtractAsync(project: project);
        // Then
        var boundary = model.Types!.Single(predicate: type => type.Name == "System.String");
        Assert.False(condition: boundary.IsInternal);
        Assert.Empty(collection: boundary.Methods!);
        Assert.Equal(expected: 2, actual: model.Types!.Length);
        Assert.Equal(expected: "IsNullOrEmpty", actual: Assert.Single(collection: model.Dependencies!).ToMethod);
    }

    [Fact]

    public async Task ShouldHandleCyclesAndIsolatedTypesWithoutRepeatingExpansionAsync()
    {
        // Given
        using var workspace = new AdhocWorkspace();
        var project = AddProject(workspace: workspace, name: "Cycles", source: """
            class A { public static void Go() { B.Go(); } }
            class B { public static void Go() { A.Go(); } }
            class Isolated {}
            """);
        // When
        var model = await ProjectModelTestFactory.ExtractAsync(project: project);
        // Then
        Assert.Equal(expected: 3, actual: model.Types!.Length);
        Assert.Equal(expected: 2, actual: model.Dependencies!.Length);
        Assert.Contains(collection: model.Types!, filter: type => type.Name == "Isolated");
    }

    [Fact]

    public async Task ShouldAssociateNestedCallsWithTheCorrectTypeAsync()
    {
        // Given
        using var workspace = new AdhocWorkspace();
        var project = AddProject(workspace: workspace, name: "Nested", source: """
            class Target { public static void Go() {} }
            class Outer { public class Inner { public void Run() { Target.Go(); } } }
            """);
        // When
        var model = await ProjectModelTestFactory.ExtractAsync(project: project);
        // Then
        Assert.Equal(expected: "Outer.Inner", actual: Assert.Single(collection: model.Dependencies!).FromType);
    }

    [Fact]

    public async Task ShouldRejectCompilationErrorsInsteadOfReturningIncompleteFactsAsync()
    {
        // Given
        using var workspace = new AdhocWorkspace();
        var project = AddProject(workspace: workspace, name: "Broken", source: "class Entry { public void Go() { Missing.Run(); } }");
        // When
        Func<Task> extract = () => ProjectModelTestFactory.ExtractAsync(project: project);
        // Then
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(testCode: extract);
        Assert.Contains(expectedSubstring: "Broken", actualString: exception.Message);
        Assert.Contains(expectedSubstring: "CS0103", actualString: exception.Message);
    }

    [Fact]

    public async Task ShouldNormalizeDynamicAndTupleMemberTypesAsync()
    {
        // Given
        using var workspace = new AdhocWorkspace();
        var project = AddProject(workspace: workspace, name: "SpecialNames", source: """
            class Box<T> {}
            class Entry { public Box<dynamic> Value; public (int Count, string Name) Pair { get; set; } }
            """);
        // When
        var model = await ProjectModelTestFactory.ExtractAsync(project: project);
        var entry = model.Types!.Single(predicate: type => type.Name == "Entry");
        // Then
        Assert.Equal(expected: "Box<System.Object>", actual: Assert.Single(collection: entry.Fields!).Type);
        Assert.Equal(expected: "System.ValueTuple<System.Int32, System.String>", actual: Assert.Single(collection: entry.Properties!).Type);
    }

    [Fact]

    public async Task ShouldNormalizeGenericCallTargetsAndResolveExtensionMethodsAsync()
    {
        // Given
        using var workspace = new AdhocWorkspace();
        var project = AddProject(workspace: workspace, name: "GenericCalls", source: """
            namespace Example;

            public class Box<T> { public static void Run() {} }
            public static class Extensions { public static void Go(this string value) {} }
            public class Entry { public void Run() { Box<string>.Run(); "value".Go(); } }
            """);
        // When
        var model = await ProjectModelTestFactory.ExtractAsync(project: project);
        // Then
        Assert.Equal(expected: 2, actual: model.Dependencies!.Length);
        Assert.Contains(collection: model.Dependencies!, filter: link => link.ToType == "Example.Box<T>" && link.ToMethod == "Run");
        Assert.Contains(collection: model.Dependencies!, filter: link => link.ToType == "Example.Extensions" && link.ToMethod == "Go");
        Assert.All(collection: model.Types!, action: type => Assert.True(condition: type.IsInternal));
    }

    [Fact]

    public async Task ShouldResolveInterfaceTargetsAndAttributeLambdaCallsToTheirMethodAsync()
    {
        // Given
        using var workspace = new AdhocWorkspace();
        var project = AddProject(workspace: workspace, name: "InterfaceCalls", source: """
            interface IRun { void Go(); }
            class Target : IRun { public void Go() {} }
            class Entry
            {
                public void Run(IRun target)
                {
                    System.Action action = () => target.Go();
                    void Local() { target.Go(); }
                    Local();
                }
            }
            """);
        // When
        var model = await ProjectModelTestFactory.ExtractAsync(project: project);
        // Then
        var call = Assert.Single(collection: model.Dependencies!.Where(predicate: link => link.ToType == "Target" && link.DependencyType == DependencyType.Consumed));
        Assert.Equal(expected: "Run", actual: call.FromMethod);
        Assert.Equal(expected: "Go", actual: call.ToMethod);
        Assert.DoesNotContain(collection: model.Dependencies!, filter: link => link.DependencyType == DependencyType.Consumed && link.ToType == "IRun");
    }

    [Fact]

    public async Task ShouldRejectValueTypeCallTargetsThatTheModelCannotRepresentAsync()
    {
        // Given
        using var workspace = new AdhocWorkspace();
        var project = AddProject(workspace: workspace, name: "ValueCalls", source: "class Entry { public string Run() => 1.ToString(); }");
        // When
        Func<Task> extract = () => ProjectModelTestFactory.ExtractAsync(project: project);
        // Then
        var exception = await Assert.ThrowsAsync<NotSupportedException>(testCode: extract);
        Assert.Contains(expectedSubstring: "System.Int32", actualString: exception.Message);
    }

    [Fact]

    public async Task ShouldRequireSavedProjectIdentityAsync()
    {
        // Given
        using var workspace = new AdhocWorkspace();
        var saved = AddProject(workspace: workspace, name: "Unsaved", source: "class Entry {}");
        var project = saved.Solution.WithProjectFilePath(projectId: saved.Id, filePath: null)
            .GetProject(projectId: saved.Id)!;
        // When
        Func<Task> extract = () => ProjectModelTestFactory.ExtractAsync(project: project);
        // Then
        await Assert.ThrowsAnyAsync<ArgumentException>(testCode: extract);
    }

    [Fact]

    public async Task ShouldNameTypeParametersNestedGenericsAndPointersAsync()
    {
        // Given
        using var workspace = new AdhocWorkspace();
        var project = AddProject(workspace: workspace, name: "ConstructedNames", source: """
            public unsafe class Generic<T>
            {
                public T Value;
                public int* Pointer;
                public Generic<T>.Nested<string>[,] Items;
                public class Nested<U> {}
            }
            public struct Container { public class Nested {} }
            public enum Values { A }
            """);

        // When
        var model = await ProjectModelTestFactory.ExtractAsync(project: project);
        var type = model.Types!.Single(predicate: type => type.Name == "Generic<T>");

        // Then
        Assert.Contains(collection: type.Fields!, filter: field => field.Name == "Value" && field.Type == "T");
        Assert.Contains(collection: type.Fields!, filter: field => field.Name == "Pointer" && field.Type == "System.Int32*");
        Assert.Contains(collection: type.Fields!, filter: field => field.Name == "Items" && field.Type == "Generic<T>.Nested<System.String>[,]");
        Assert.Contains(collection: model.Types!, filter: type => type.Name == "Container.Nested");
        Assert.DoesNotContain(collection: model.Types!, filter: type => type.Name == "Values" || type.Name == "Container");
    }

    [Fact]

    public async Task ShouldRejectUnresolvedDynamicDispatchAsync()
    {
        // Given
        using var workspace = new AdhocWorkspace();
        var project = AddProject(workspace: workspace, name: "DynamicCalls", source: "class Entry { void Run(dynamic value) { value.Go(); } }");
        var runtimeReferences = ((string)AppContext.GetData(name: "TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(separator: Path.PathSeparator)
            .Select(selector: path => MetadataReference.CreateFromFile(path: path));
        project = project.WithMetadataReferences(metadataReferences: runtimeReferences);

        // When
        Func<Task> extract = () => ProjectModelTestFactory.ExtractAsync(project: project);

        // Then
        var exception = await Assert.ThrowsAsync<NotSupportedException>(testCode: extract);
        Assert.Contains(expectedSubstring: "Cannot statically resolve invocation", actualString: exception.Message);
    }

    [Fact]

    public async Task ShouldRetainCallsInConstructorsAccessorsAndInitializersAsync()
    {
        // Given
        using var workspace = new AdhocWorkspace();
        var project = AddProject(workspace: workspace, name: "CallSites", source: """
            class Target { public static int Run() => 1; }
            class Entry
            {
                int field = Target.Run();
                public Entry() { Target.Run(); }
                int Property { get { return Target.Run(); } }
            }
            """);

        // When
        var model = await ProjectModelTestFactory.ExtractAsync(project: project);

        // Then
        Assert.Equal(expected: 3, actual: model.Dependencies!.Length);
        Assert.Contains(collection: model.Dependencies!, filter: link => link.FromMethod == ".ctor");
        Assert.Contains(collection: model.Dependencies!, filter: link => link.FromMethod == "get_Property");
        Assert.Contains(collection: model.Dependencies!, filter: link => link.FromMethod == null);
    }
}