// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using StandardIo.ArchitectureDiagram.Core2.Brokers.Files;
using StandardIo.ArchitectureDiagram.Core2.Models;
using StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Projects;
using StandardIo.ArchitectureDiagram.Core2.Services.Processings.Projects;
using Xunit;
using StandardIo.ArchitectureDiagram.Core2.Exposures;

namespace StandardIo.ArchitectureDiagram.Core2.Tests;
public sealed partial class ProjectPathExtractionTests
{
    [Fact]
    public async Task ShouldUseTheProjectsGeneratedGlobalUsings()
    {
        using var fixture = new ProjectFolder();
        fixture.Write("Entry.cs", "public class Entry { public StringBuilder Builder { get; set; } }");
        fixture.Write("obj/Debug/net10.0/ProvingGround.GlobalUsings.g.cs", "global using System.Text;");
        var model = await TestServices.Get<ProjectModelBuilder>().BuildAsync(fixture.ProjectPath);
        Assert.Equal("System.Text.StringBuilder", Assert.Single(Assert.Single(model.Types!).Properties!).Type);
    }

    [Theory]
    [InlineData("project")]
    [InlineData("directory")]
    [InlineData("source")]
    [InlineData("relative")]
    public async Task ShouldResolveInputsAndExcludeBuildOutputAsync(string inputKind)
    {
        // Given: the fixture and inputs below.
        using var fixture = new ProjectFolder();
        fixture.Write(relativePath: "Models/Entry.cs", text: "public class Entry { public List<string> Names { get; set; } }");
        fixture.Write(relativePath: "bin/Invalid.cs", text: "This is not valid C#");
        fixture.Write(relativePath: "obj/Invalid.cs", text: "This is not valid C#");
        fixture.Write(relativePath: "Nested/bin/Invalid.cs", text: "This is not valid C#");
        fixture.Write(relativePath: "Readme.txt", text: "source-folder input");

        string suppliedPath = inputKind switch
        {
            "directory" => fixture.DirectoryPath,
            "source" => Path.Combine(path1: fixture.DirectoryPath, path2: "Readme.txt"),
            "relative" => Path.GetRelativePath(relativeTo: Environment.CurrentDirectory, path: fixture.ProjectPath),
            _ => fixture.ProjectPath
        };
        // When: exercise the operation under test.

        ProjectModel model = await TestServices.Get<ProjectModelBuilder>().BuildAsync(projectFilePath: suppliedPath);
        // Then: verify the resulting contract.
        Assert.Equal(expected: "ProvingGround", actual: model.Name);
        Assert.Equal(expected: fixture.ProjectPath, actual: model.Path);
        DefinedType type = Assert.Single(collection: model.Types!);
        Assert.Equal(expected: "Entry", actual: type.Name);
        Assert.Equal(expected: "System.Collections.Generic.List<System.String>", actual: Assert.Single(collection: type.Properties!).Type);
        Assert.Empty(collection: model.Dependencies!);
        string snapshot = JsonSerializer.Serialize(value: model);
        Assert.Equal(expected: snapshot, actual: JsonSerializer.Serialize(value: JsonSerializer.Deserialize<ProjectModel>(json: snapshot)));
    }

    [Fact]
    public async Task ShouldUseDependencyAssembliesBesideTheLatestProjectBuildAsync()
    {
        // Given: the fixture and inputs below.
        using var fixture = new ProjectFolder();
        fixture.Write(relativePath: "Entry.cs", text: "public class Entry { public void Run() { External.Api.Run(); } }");
        string output = Path.Combine(path1: fixture.DirectoryPath, path2: "bin", path3: "Debug", path4: "net10.0");
        Directory.CreateDirectory(path: output);
        EmitAssembly(path: Path.Combine(path1: output, path2: "External.dll"), code: "namespace External; public class Api { public static void Run() {} }");
        EmitAssembly(path: Path.Combine(path1: output, path2: "ProvingGround.dll"), code: "public class OldBuild {}");
        // When: exercise the operation under test.
        ProjectModel model = await TestServices.Get<ProjectModelBuilder>().BuildAsync(projectFilePath: fixture.ProjectPath);
        // Then: verify the resulting contract.
        Assert.Equal(expected: "External.Api", actual: Assert.Single(collection: model.Dependencies!).ToType);
        DefinedType external = Assert.Single(collection: model.Types!, predicate: type => !type.IsInternal);
        Assert.Equal(expected: "External.Api", actual: external.Name);
        Assert.Equal("Run", Assert.Single(external.Methods!).Name);
        Assert.DoesNotContain(collection: model.Types!, filter: type => type.Name == "OldBuild");
    }

    [Fact]
    public async Task ShouldReportMissingReferencesInsteadOfReturningPartialFactsAsync()
    {
        // Given: the fixture and inputs below.
        using var fixture = new ProjectFolder();
        fixture.Write(relativePath: "Entry.cs", text: "class Entry { void Run() { Unavailable.Api.Run(); } }");
        // When: exercise the operation under test.
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(testCode: () => TestServices.Get<ProjectModelBuilder>().BuildAsync(projectFilePath: fixture.ProjectPath));
        // Then: verify the resulting contract.
        Assert.Contains(expectedSubstring: "compilation errors", actualString: exception.Message);
        Assert.Contains(expectedSubstring: "Unavailable", actualString: exception.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task ShouldRejectMissingPathsAsync(string? path)
    {
        // Given
        var builder = TestServices.Get<ProjectModelBuilder>();
        // When
        Func<Task> build = () => builder.BuildAsync(projectFilePath: path!);
        // Then
        await Assert.ThrowsAnyAsync<ArgumentException>(testCode: build);
    }

    [Fact]
    public async Task ShouldCancelBeforeResolvingThePathAsync()
    {
        // Given
        var builder = TestServices.Get<ProjectModelBuilder>();
        var token = new CancellationToken(canceled: true);
        // When
        Func<Task> build = () => builder.BuildAsync(projectFilePath: "does-not-exist.csproj", cancellationToken: token);
        // Then
        await Assert.ThrowsAnyAsync<OperationCanceledException>(testCode: build);
    }

    [Fact]
    public void ShouldRejectAnUnknownPath()
    {
        // Given: the fixture and inputs below.
        using var fixture = new ProjectFolder();
        // When: exercise the operation under test.
        // Then: verify the resulting contract.

        Assert.Throws<DirectoryNotFoundException>(testCode: () => CreateResolver()
            .ResolveProjectFilePath(suppliedPath: Path.Combine(path1: fixture.DirectoryPath, path2: "missing.csproj")));
    }

    [Fact]
    public void ShouldRejectAFolderWithoutAProject()
    {
        // Given: the fixture and inputs below.
        using var fixture = new ProjectFolder();
        File.Delete(path: fixture.ProjectPath);
        // When: exercise the operation under test.
        // Then: verify the resulting contract.

        Assert.Throws<FileNotFoundException>(testCode: () => CreateResolver()
            .ResolveProjectFilePath(suppliedPath: fixture.DirectoryPath));
    }

    [Fact]
    public void ShouldRequireAnExplicitProjectWhenTheFolderIsAmbiguous()
    {
        // Given: the fixture and inputs below.
        using var fixture = new ProjectFolder();
        fixture.Write(relativePath: "Second.csproj", text: "<Project />");
        fixture.Write(relativePath: "Entry.cs", text: "class Entry {}");
        ProjectProcessingService service = CreateResolver();
        // When: exercise the operation under test.
        Assert.Throws<InvalidOperationException>(testCode: () => service.ResolveProjectFilePath(suppliedPath: fixture.DirectoryPath));
        // Then: verify the resulting contract.
        Assert.Throws<InvalidOperationException>(testCode: () => service.ResolveProjectFilePath(suppliedPath: Path.Combine(path1: fixture.DirectoryPath, path2: "Entry.cs")));
        Assert.Equal(expected: fixture.ProjectPath, actual: service.ResolveProjectFilePath(suppliedPath: fixture.ProjectPath));
    }

    private static ProjectProcessingService CreateResolver() =>
        new(new ProjectService(new FileBroker()));

    private static void EmitAssembly(string path, string code)
    {
        var compilation = CSharpCompilation.Create(assemblyName: Path.GetFileNameWithoutExtension(path: path), syntaxTrees: new[] { CSharpSyntaxTree.ParseText(text: code) }, references: new[] { MetadataReference.CreateFromFile(path: typeof(object).Assembly.Location) }, options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var result = compilation.Emit(outputPath: path);
        Assert.True(condition: result.Success, userMessage: string.Join(separator: "\n", values: result.Diagnostics));
    }

    private sealed class ProjectFolder : IDisposable
    {
        public string DirectoryPath { get; } = Path.Combine(path1: Path.GetTempPath(), path2: "v8-project-model-tests", path3: Guid.NewGuid()
            .ToString(format: "N"));
        public string ProjectPath => Path.Combine(path1: DirectoryPath, path2: "ProvingGround.csproj");

        public ProjectFolder() => Write(relativePath: "ProvingGround.csproj", text: "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        public void Write(string relativePath, string text)
        {
            string path = Path.Combine(path1: DirectoryPath, path2: relativePath);
            Directory.CreateDirectory(path: Path.GetDirectoryName(path: path)!);
            File.WriteAllText(path: path, contents: text);
        }

        public void Dispose()
        {
            string root = Path.GetFullPath(path: Path.Combine(path1: Path.GetTempPath(), path2: "v8-project-model-tests")) + Path.DirectorySeparatorChar;
            string path = Path.GetFullPath(path: DirectoryPath);

            if (!path.StartsWith(value: root, comparisonType: StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Unexpected test folder.");
            }

            Directory.Delete(path: path, recursive: true);
        }
    }
}