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

public sealed class ProjectModelPopulationTests
{
    [Fact]
    public async Task ShouldLoadIndependentCompilationsAndPopulateOnlyOwnedPartsAsync()
    {
        var broker = new CompilationBroker(() => CreateCompilation("Example", "class Entry { public bool Run() => string.IsNullOrEmpty(null); }"));
        var types = new ProjectTypesService(broker);
        var dependencies = new ProjectDependenciesService(broker);
        var existingDependencies = new[] { new Dependency { FromType = "Existing" } };
        var existingTypes = new[] { new DefinedType { Name = "Existing" } };
        var typeModel = new ProjectModel { Name = "Example", Path = "Example.csproj", Dependencies = existingDependencies };
        var dependencyModel = new ProjectModel { Name = "Example", Path = "Example.csproj", Types = existingTypes };

        await types.PopulateTypesAsync(typeModel, CancellationToken.None);
        // No type extraction has been run on this model; the dependency foundation stands alone.
        await dependencies.PopulateDependenciesAsync(dependencyModel, CancellationToken.None);

        Assert.Equal(2, broker.Compilations.Count);
        Assert.NotSame(broker.Compilations[0], broker.Compilations[1]);
        Assert.Equal(new[] { "Example.csproj", "Example.csproj" }, broker.Paths);
        Assert.Same(existingDependencies, typeModel.Dependencies);
        Assert.Same(existingTypes, dependencyModel.Types);
        Assert.Contains(typeModel.Types!, type => type.Name == "Entry" && type.IsInternal);
        Assert.Contains(typeModel.Types!, type => type.Name == "System.String" && !type.IsInternal);
        Assert.Equal("IsNullOrEmpty", Assert.Single(dependencyModel.Dependencies!).ToMethod);
        Assert.Equal("Example", typeModel.Name);
        Assert.Equal("Example.csproj", typeModel.Path);
        Assert.Equal("Example", dependencyModel.Name);
        Assert.Equal("Example.csproj", dependencyModel.Path);
    }

    [Fact]
    public async Task ShouldValidateTheDependencyFoundationsOwnCompilationBeforeChangingTheModelAsync()
    {
        var broker = new CompilationBroker(() => CreateCompilation("Broken", "class Entry { void Run() { Missing.Call(); } }"));
        var existing = new[] { new Dependency { FromType = "Existing" } };
        var model = new ProjectModel { Name = "Broken", Path = "Broken.csproj", Dependencies = existing };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new ProjectDependenciesService(broker).PopulateDependenciesAsync(model, CancellationToken.None));

        Assert.Contains("compilation errors", exception.Message);
        Assert.Same(existing, model.Dependencies);
    }

    [Fact]
    public async Task ShouldRejectConflictingTypeNamesWithoutLeakingSymbolsOrPartialTypesAsync()
    {
        var library = CreateCompilation("ExternalLibrary", "namespace Same; public class Name { public static void Run() {} }");
        using var stream = new MemoryStream();
        Assert.True(library.Emit(stream).Success);
        var reference = MetadataReference.CreateFromImage(stream.ToArray())
            .WithAliases(new[] { "External" });
        var broker = new CompilationBroker(() => CreateCompilation("App",
            "extern alias External; namespace Same; public class Name { public void Go() { External::Same.Name.Run(); } }").AddReferences(reference));
        var existing = new[] { new DefinedType { Name = "Existing" } };
        var model = new ProjectModel { Name = "App", Path = "App.csproj", Types = existing };

        var exception = await Assert.ThrowsAsync<NotSupportedException>(() =>
            new ProjectTypesService(broker).PopulateTypesAsync(model, CancellationToken.None));

        Assert.Contains("Same.Name", exception.Message);
        Assert.Same(existing, model.Types);
    }

    [Fact]
    public void ShouldKeepRoslynTypesOutOfModelsAndServiceContracts()
    {
        Type[] types = typeof(ProjectModel).Assembly.GetTypes();
        var domainTypes = types.Where(type => type.Namespace == typeof(ProjectModel).Namespace
            || type.IsInterface && type.Namespace!.Contains(".Services."));

        foreach (Type type in domainTypes)
        {
            foreach (PropertyInfo property in type.GetProperties()) Assert.False(ContainsRoslynType(property.PropertyType), property.ToString());
            foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
                Assert.False(ContainsRoslynType(field.FieldType), field.ToString());
            foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                Assert.False(ContainsRoslynType(method.ReturnType), method.ToString());
                Assert.All(method.GetParameters(), parameter => Assert.False(ContainsRoslynType(parameter.ParameterType), parameter.ToString()));
            }
        }
    }

    private static bool ContainsRoslynType(Type type) =>
        type.Namespace?.StartsWith("Microsoft.CodeAnalysis", StringComparison.Ordinal) == true
        || type.HasElementType && ContainsRoslynType(type.GetElementType()!)
        || type.IsGenericType && type.GetGenericArguments().Any(ContainsRoslynType);

    private static CSharpCompilation CreateCompilation(string name, string source) => CSharpCompilation.Create(
        assemblyName: name,
        syntaxTrees: new[] { CSharpSyntaxTree.ParseText(source) },
        references: new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
        options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    private sealed class CompilationBroker(Func<Compilation> createCompilation) : RoslynBroker
    {
        public List<Compilation> Compilations { get; } = new();
        public List<string> Paths { get; } = new();

        public override Task<Compilation> LoadCompilationAsync(string projectFilePath, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Compilation compilation = createCompilation();
            Compilations.Add(compilation);
            Paths.Add(projectFilePath);
            return Task.FromResult(compilation);
        }
    }
}