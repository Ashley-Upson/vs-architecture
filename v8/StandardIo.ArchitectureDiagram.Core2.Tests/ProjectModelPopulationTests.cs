// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using StandardIo.ArchitectureDiagram.Core2.Brokers.Roslyn;
using StandardIo.ArchitectureDiagram.Core2.Models;
using StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Types;
using StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Dependencies;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core2.Tests;
public sealed partial class ProjectModelPopulationTests
{
    private static readonly string[] expectedCompilationPaths = new[]
    {
        "Example.csproj",
        "Example.csproj"
    };
    private static readonly string[] externalAliases = new[]
    {
        "External"
    };
    [Fact]
    public async Task ShouldLoadIndependentCompilationsAndPopulateOnlyOwnedPartsAsync()
    {
        // Given: the fixture and inputs below.
        var broker = new CompilationBroker(() => CreateCompilation(name: "Example", source: "class Entry { public bool Run() => string.IsNullOrEmpty(null); }"));
        var types = new ProjectTypesService(broker);
        var dependencies = new ProjectDependenciesService(broker);

        var existingDependencies = new[]
        {
            new TypeRelationship
            {
                FromType = "Existing"
            }
        };

        var existingTypes = new[]
        {
            new DefinedType
            {
                Name = "Existing"
            }
        };

        var typeModel = new ProjectModel
        {
            Name = "Example",
            Path = "Example.csproj",
            Dependencies = existingDependencies
        };

        var dependencyModel = new ProjectModel
        {
            Name = "Example",
            Path = "Example.csproj",
            Types = existingTypes
        };
        // When: exercise the operation under test.

        await types.PopulateTypesAsync(project: typeModel, cancellationToken: CancellationToken.None);
        // No type extraction has been run on this model; the dependency foundation stands alone.
        await dependencies.PopulateDependenciesAsync(project: dependencyModel, cancellationToken: CancellationToken.None);
        // Then: verify the resulting contract.
        Assert.Equal(expected: 2, actual: broker.Compilations.Count);
        Assert.NotSame(expected: broker.Compilations[0], actual: broker.Compilations[1]);
        Assert.Equal(expected: expectedCompilationPaths, actual: broker.Paths);
        Assert.Same(expected: existingDependencies, actual: typeModel.Dependencies);
        Assert.Same(expected: existingTypes, actual: dependencyModel.Types);
        Assert.Contains(collection: typeModel.Types!, filter: type => type.Name == "Entry" && type.IsInternal);
        Assert.Contains(collection: typeModel.Types!, filter: type => type.Name == "System.String" && !type.IsInternal);
        Assert.Equal(expected: "IsNullOrEmpty", actual: Assert.Single(collection: dependencyModel.Dependencies!).ToMethod);
        Assert.Equal(expected: "Example", actual: typeModel.Name);
        Assert.Equal(expected: "Example.csproj", actual: typeModel.Path);
        Assert.Equal(expected: "Example", actual: dependencyModel.Name);
        Assert.Equal(expected: "Example.csproj", actual: dependencyModel.Path);
    }

    [Fact]
    public async Task ShouldValidateTheDependencyFoundationsOwnCompilationBeforeChangingTheModelAsync()
    {
        // Given: the fixture and inputs below.
        var broker = new CompilationBroker(() => CreateCompilation(name: "Broken", source: "class Entry { void Run() { Missing.Call(); } }"));

        var existing = new[]
        {
            new TypeRelationship
            {
                FromType = "Existing"
            }
        };

        var model = new ProjectModel
        {
            Name = "Broken",
            Path = "Broken.csproj",
            Dependencies = existing
        };
        // When: exercise the operation under test.

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(testCode: () => new ProjectDependenciesService(broker).PopulateDependenciesAsync(project: model, cancellationToken: CancellationToken.None));
        // Then: verify the resulting contract.
        Assert.Contains(expectedSubstring: "compilation errors", actualString: exception.Message);
        Assert.Same(expected: existing, actual: model.Dependencies);
    }

    [Fact]
    public async Task ShouldRejectConflictingTypeNamesWithoutLeakingSymbolsOrPartialTypesAsync()
    {
        // Given: the fixture and inputs below.
        var library = CreateCompilation(name: "ExternalLibrary", source: "namespace Same; public class Name { public static void Run() {} }");
        using var stream = new MemoryStream();
        Assert.True(condition: library.Emit(peStream: stream).Success);

        var reference = MetadataReference.CreateFromImage(peImage: stream.ToArray())
            .WithAliases(aliases: externalAliases);

        var broker = new CompilationBroker(() => CreateCompilation(name: "App", source: "extern alias External; namespace Same; public class Name { public void Go() { External::Same.Name.Run(); } }")
            .AddReferences(references: [reference]));

        var existing = new[]
        {
            new DefinedType
            {
                Name = "Existing"
            }
        };

        var model = new ProjectModel
        {
            Name = "App",
            Path = "App.csproj",
            Types = existing
        };
        // When: exercise the operation under test.

        var exception = await Assert.ThrowsAsync<NotSupportedException>(testCode: () => new ProjectTypesService(broker).PopulateTypesAsync(project: model, cancellationToken: CancellationToken.None));
        // Then: verify the resulting contract.
        Assert.Contains(expectedSubstring: "Same.Name", actualString: exception.Message);
        Assert.Same(expected: existing, actual: model.Types);
    }

    [Fact]
    public void ShouldKeepRoslynTypesOutOfModelsAndServiceContracts()
    {
        // Given: the fixture and inputs below.
        Type[] types = typeof(ProjectModel).Assembly.GetTypes();
        var domainTypes = types.Where(predicate: type => type.Namespace == typeof(ProjectModel).Namespace || type.IsInterface && type.Namespace!.Contains(value: ".Services."));
        // When: exercise the operation under test.
        // Then: verify the resulting contract.

        foreach (Type type in domainTypes)
        {
            foreach (PropertyInfo property in type.GetProperties())
            {
                Assert.False(condition: ContainsRoslynType(type: property.PropertyType), userMessage: property.ToString());
            }

            foreach (FieldInfo field in type.GetFields(bindingAttr: BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
            {
                Assert.False(condition: ContainsRoslynType(type: field.FieldType), userMessage: field.ToString());
            }

            foreach (MethodInfo method in type.GetMethods(bindingAttr: BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                Assert.False(condition: ContainsRoslynType(type: method.ReturnType), userMessage: method.ToString());
                Assert.All(collection: method.GetParameters(), action: parameter => Assert.False(condition: ContainsRoslynType(type: parameter.ParameterType), userMessage: parameter.ToString()));
            }
        }
    }

    private static bool ContainsRoslynType(Type type) =>
        type.Namespace?.StartsWith(value: "Microsoft.CodeAnalysis", comparisonType: StringComparison.Ordinal) == true || type.HasElementType && ContainsRoslynType(type: type.GetElementType()!) || type.IsGenericType && type.GetGenericArguments()
        .Any(predicate: ContainsRoslynType);

    private static CSharpCompilation CreateCompilation(string name, string source) =>
        CSharpCompilation.Create(assemblyName: name, syntaxTrees: new[] { CSharpSyntaxTree.ParseText(text: source) }, references: new[] { MetadataReference.CreateFromFile(path: typeof(object).Assembly.Location) }, options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    private sealed class CompilationBroker(Func<Compilation> createCompilation) : RoslynBroker
    {
        public List<Compilation> Compilations { get; } = new();
        public List<string> Paths { get; } = new();

        public override Task<Compilation> LoadCompilationAsync(string projectFilePath, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Compilation compilation = createCompilation();
            Compilations.Add(item: compilation);
            Paths.Add(item: projectFilePath);
            return Task.FromResult(result: compilation);
        }
    }
}