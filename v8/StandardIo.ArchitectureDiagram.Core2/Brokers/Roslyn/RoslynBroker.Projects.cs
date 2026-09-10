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

namespace StandardIo.ArchitectureDiagram.Core2.Brokers.Roslyn;

internal partial class RoslynBroker
{
    // Matches cCoder.CodeAnalysis's folder-based compilation approach.
    public virtual async Task<Compilation> LoadCompilationAsync(string projectFilePath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string path = projectFilePath;
        string directory = Path.GetDirectoryName(path: path)!;
        string name = Path.GetFileNameWithoutExtension(path: path);
        string[] sourcePaths = Directory.GetFiles(path: directory, searchPattern: "*.cs", searchOption: SearchOption.AllDirectories)
            .Where(source => !IsBuildOutput(path: source, directory: directory))
            .OrderBy(source => source, StringComparer.Ordinal).ToArray();
        var trees = new SyntaxTree[sourcePaths.Length + 1];

        for (int index = 0; index < sourcePaths.Length; index++)
        {
            string sourcePath = sourcePaths[index];
            string text = await File.ReadAllTextAsync(path: sourcePath, cancellationToken: cancellationToken);
            trees[index] = CSharpSyntaxTree.ParseText(text: text, path: sourcePath, cancellationToken: cancellationToken);
        }

        trees[sourcePaths.Length] = CSharpSyntaxTree.ParseText(text:
            "global using System; global using System.Collections.Generic; global using System.IO; " +
            "global using System.Linq; global using System.Net.Http; global using System.Threading; global using System.Threading.Tasks;");
        var compilation = CSharpCompilation.Create(
            assemblyName: name,
            syntaxTrees: trees,
            references: GetMetadataReferences(directory: directory, projectName: name),
            options: new CSharpCompilationOptions(outputKind: OutputKind.DynamicallyLinkedLibrary));

        return compilation;
    }

    private static bool IsBuildOutput(string path, string directory) =>
        Path.GetRelativePath(relativeTo: directory, path: path)
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(segment => string.Equals(segment, "bin", StringComparison.OrdinalIgnoreCase)
                || string.Equals(segment, "obj", StringComparison.OrdinalIgnoreCase));

    private static MetadataReference[] GetMetadataReferences(string directory, string projectName)
    {
        string trustedAssemblies = AppContext.GetData(name: "TRUSTED_PLATFORM_ASSEMBLIES") as string
            ?? throw new InvalidOperationException(message: "Platform assemblies could not be resolved.");
        string buildDirectory = Path.Combine(path1: directory, path2: "bin");
        string? projectAssembly = Directory.Exists(path: buildDirectory)
            ? Directory.GetFiles(path: buildDirectory, searchPattern: projectName + ".dll", searchOption: SearchOption.AllDirectories)
                .Where(path => !Path.GetRelativePath(buildDirectory, path).Split(Path.DirectorySeparatorChar).Any(segment => segment is "ref" or "refint"))
                .OrderByDescending(path => File.GetLastWriteTimeUtc(path: path)).ThenBy(path => path, StringComparer.Ordinal).FirstOrDefault()
            : null;
        string[] buildAssemblies = projectAssembly is null ? Array.Empty<string>()
            : Directory.GetFiles(path: Path.GetDirectoryName(path: projectAssembly)!, searchPattern: "*.dll");

        // Avoid referencing the project being analysed, or two copies of a dependency.
        return trustedAssemblies.Split(separator: Path.PathSeparator).Concat(buildAssemblies)
            .Where(path => !string.Equals(Path.GetFileNameWithoutExtension(path: path), projectName, StringComparison.OrdinalIgnoreCase))
            .DistinctBy(path => Path.GetFileName(path: path), StringComparer.OrdinalIgnoreCase)
            .Select(path => MetadataReference.CreateFromFile(path: path)).ToArray();
    }
}
