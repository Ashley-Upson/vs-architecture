using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using StandardIo.ArchitectureDiagram.Core2.Brokers.Roslyn;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core2.Tests;

public sealed class RestoredReferenceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ShouldResolveRestoredCompileAssetsWithoutDependingOnCopiedOutputAssemblies(bool staleOutput)
    {
        // Given: a library output folder need not contain its package dependencies.
        string root = Path.Combine(Path.GetTempPath(), "diagram-references-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "obj"));
        string packageRoot = Path.Combine(root, "packages");
        string packagePath = Path.Combine(packageRoot, "example", "1.0.0", "lib", "net10.0");
        Directory.CreateDirectory(packagePath);
        void Emit(string path, string code, string name)
        {
            var result = CSharpCompilation.Create(name, [CSharpSyntaxTree.ParseText(code)],
                [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)).Emit(path);
            Assert.True(result.Success, string.Join("\n", result.Diagnostics));
        }
        Emit(Path.Combine(packagePath, "ExamplePackage.dll"), "namespace Package; public class Widget { public int Read() => 1; }", "ExamplePackage");
        string output = Path.Combine(root, "bin", "Release", "net10.0");
        Directory.CreateDirectory(output);
        Emit(Path.Combine(output, "Example.dll"), "public class OldOutput {}", "Example");
        if (staleOutput) Emit(Path.Combine(output, "ExamplePackage.dll"), "namespace Package; public class Widget {}", "ExamplePackage");
        string assets = """
            {"targets":{"net10.0":{"Example/1.0.0":{"type":"package","compile":{"lib/net10.0/ExamplePackage.dll":{}}}}},
             "libraries":{"Example/1.0.0":{"type":"package","path":"example/1.0.0"}},"packageFolders":{PACKAGE_ROOT:{}}}
            """.Replace("PACKAGE_ROOT", JsonSerializer.Serialize(packageRoot));
        await File.WriteAllTextAsync(Path.Combine(root, "obj", "project.assets.json"), assets);
        await File.WriteAllTextAsync(Path.Combine(root, "Example.cs"), "public class Example { public int Run() => new Package.Widget().Read(); }");
        // When
        var compilation = await new RoslynBroker().LoadCompilationAsync(Path.Combine(root, "Example.csproj"), CancellationToken.None);
        // Then
        Assert.Empty(compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
    }
}
