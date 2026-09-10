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

namespace StandardIo.ArchitectureDiagram.Core2.Tests;

public sealed class ProjectPathExtractionTests
{
    [Theory]
    [InlineData("project")]
    [InlineData("directory")]
    [InlineData("source")]
    [InlineData("relative")]
    public async Task ShouldResolveInputsAndExcludeBuildOutputAsync(string inputKind)
    {
        using var fixture = new ProjectFolder();
        fixture.Write("Models/Entry.cs", "public class Entry { public List<string> Names { get; set; } }");
        fixture.Write("bin/Invalid.cs", "This is not valid C#");
        fixture.Write("obj/Invalid.cs", "This is not valid C#");
        fixture.Write("Nested/bin/Invalid.cs", "This is not valid C#");
        fixture.Write("Readme.txt", "source-folder input");
        string suppliedPath = inputKind switch
        {
            "directory" => fixture.DirectoryPath,
            "source" => Path.Combine(fixture.DirectoryPath, "Readme.txt"),
            "relative" => Path.GetRelativePath(Environment.CurrentDirectory, fixture.ProjectPath),
            _ => fixture.ProjectPath
        };

        ProjectModel model = await new ProjectModelBuilder().BuildAsync(suppliedPath);

        Assert.Equal("ProvingGround", model.Name);
        Assert.Equal(fixture.ProjectPath, model.Path);
        DefinedType type = Assert.Single(model.Types!);
        Assert.Equal("Entry", type.Name);
        Assert.Equal("System.Collections.Generic.List<System.String>", Assert.Single(type.Properties!).Type);
        Assert.Empty(model.Dependencies!);
        string snapshot = JsonSerializer.Serialize(model);
        Assert.Equal(snapshot, JsonSerializer.Serialize(JsonSerializer.Deserialize<ProjectModel>(snapshot)));
    }

    [Fact]
    public async Task ShouldUseDependencyAssembliesBesideTheLatestProjectBuildAsync()
    {
        using var fixture = new ProjectFolder();
        fixture.Write("Entry.cs", "public class Entry { public void Run() { External.Api.Run(); } }");
        string output = Path.Combine(fixture.DirectoryPath, "bin", "Debug", "net10.0");
        Directory.CreateDirectory(output);
        EmitAssembly(Path.Combine(output, "External.dll"), "namespace External; public class Api { public static void Run() {} }");
        EmitAssembly(Path.Combine(output, "ProvingGround.dll"), "public class OldBuild {}");

        ProjectModel model = await new ProjectModelBuilder().BuildAsync(fixture.ProjectPath);

        Assert.Equal("External.Api", Assert.Single(model.Dependencies!).ToType);
        DefinedType external = Assert.Single(model.Types!.Where(type => !type.IsInternal));
        Assert.Equal("External.Api", external.Name);
        Assert.Empty(external.Methods!);
        Assert.DoesNotContain(model.Types!, type => type.Name == "OldBuild");
    }

    [Fact]
    public async Task ShouldReportMissingReferencesInsteadOfReturningPartialFactsAsync()
    {
        using var fixture = new ProjectFolder();
        fixture.Write("Entry.cs", "class Entry { void Run() { Unavailable.Api.Run(); } }");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new ProjectModelBuilder().BuildAsync(fixture.ProjectPath));

        Assert.Contains("compilation errors", exception.Message);
        Assert.Contains("Unavailable", exception.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task ShouldRejectMissingPathsAsync(string? path)
    {
        await Assert.ThrowsAnyAsync<ArgumentException>(() =>
            new ProjectModelBuilder().BuildAsync(path!));
    }

    [Fact]
    public async Task ShouldCancelBeforeResolvingThePathAsync()
    {
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new ProjectModelBuilder().BuildAsync("does-not-exist.csproj", new CancellationToken(canceled: true)));
    }

    [Fact]
    public void ShouldRejectAnUnknownPath()
    {
        using var fixture = new ProjectFolder();
        Assert.Throws<DirectoryNotFoundException>(() => CreateResolver().ResolveProjectFilePath(Path.Combine(fixture.DirectoryPath, "missing.csproj")));
    }

    [Fact]
    public void ShouldRejectAFolderWithoutAProject()
    {
        using var fixture = new ProjectFolder();
        File.Delete(fixture.ProjectPath);
        Assert.Throws<FileNotFoundException>(() => CreateResolver().ResolveProjectFilePath(fixture.DirectoryPath));
    }

    [Fact]
    public void ShouldRequireAnExplicitProjectWhenTheFolderIsAmbiguous()
    {
        using var fixture = new ProjectFolder();
        fixture.Write("Second.csproj", "<Project />");
        fixture.Write("Entry.cs", "class Entry {}");
        ProjectProcessingService service = CreateResolver();
        Assert.Throws<InvalidOperationException>(() => service.ResolveProjectFilePath(fixture.DirectoryPath));
        Assert.Throws<InvalidOperationException>(() => service.ResolveProjectFilePath(Path.Combine(fixture.DirectoryPath, "Entry.cs")));
        Assert.Equal(fixture.ProjectPath, service.ResolveProjectFilePath(fixture.ProjectPath));
    }

    private static ProjectProcessingService CreateResolver() => new(new ProjectService(new FileBroker()));

    private static void EmitAssembly(string path, string code)
    {
        var compilation = CSharpCompilation.Create(
            assemblyName: Path.GetFileNameWithoutExtension(path),
            syntaxTrees: new[] { CSharpSyntaxTree.ParseText(code) },
            references: new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var result = compilation.Emit(path);
        Assert.True(result.Success, string.Join("\n", result.Diagnostics));
    }

    private sealed class ProjectFolder : IDisposable
    {
        public string DirectoryPath { get; } = Path.Combine(Path.GetTempPath(), "v8-project-model-tests", Guid.NewGuid().ToString("N"));
        public string ProjectPath => Path.Combine(DirectoryPath, "ProvingGround.csproj");

        public ProjectFolder() => Write("ProvingGround.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");

        public void Write(string relativePath, string text)
        {
            string path = Path.Combine(DirectoryPath, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
        }

        public void Dispose()
        {
            string root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "v8-project-model-tests")) + Path.DirectorySeparatorChar;
            string path = Path.GetFullPath(DirectoryPath);
            if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unexpected test folder.");
            Directory.Delete(path, recursive: true);
        }
    }
}
