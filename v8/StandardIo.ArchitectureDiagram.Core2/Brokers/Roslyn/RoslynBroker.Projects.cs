// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace StandardIo.ArchitectureDiagram.Core2.Brokers.Roslyn;
internal partial class RoslynBroker
{
    /// <summary>Matches cCoder.CodeAnalysis's folder-based compilation approach.</summary>
    public virtual async Task<Compilation> LoadCompilationAsync(string projectFilePath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string path = projectFilePath;
        string directory = Path.GetDirectoryName(path: path)!;
        string name = Path.GetFileNameWithoutExtension(path: path);

        string[] sourcePaths = Directory.GetFiles(path: directory, searchPattern: "*.cs", searchOption: SearchOption.AllDirectories)
            .Where(predicate: source => !IsBuildOutput(path: source, directory: directory))
            .OrderBy(keySelector: source => source, comparer: StringComparer.Ordinal)
            .ToArray();

        var trees = new SyntaxTree[sourcePaths.Length + 1];

        for (int index = 0; index < sourcePaths.Length; index++)
        {
            string sourcePath = sourcePaths[index];
            string text = await File.ReadAllTextAsync(path: sourcePath, cancellationToken: cancellationToken);
            trees[index] = CSharpSyntaxTree.ParseText(text: text, path: sourcePath, cancellationToken: cancellationToken);
        }

        string outputDirectory = Path.Combine(directory, "obj");
        string? generatedUsings = Directory.Exists(outputDirectory)
            ? Directory.GetFiles(outputDirectory, name + ".GlobalUsings.g.cs", SearchOption.AllDirectories)
                .OrderByDescending(File.GetLastWriteTimeUtc).ThenBy(path => path, StringComparer.Ordinal).FirstOrDefault()
            : null;
        string globalUsings = generatedUsings is null
            ? "global using System; global using System.Collections.Generic; global using System.IO; global using System.Linq; global using System.Net.Http; global using System.Threading; global using System.Threading.Tasks;"
            : await File.ReadAllTextAsync(generatedUsings, cancellationToken);
        trees[sourcePaths.Length] = CSharpSyntaxTree.ParseText(globalUsings, cancellationToken: cancellationToken);
        var compilation = CSharpCompilation.Create(assemblyName: name, syntaxTrees: trees, references: GetMetadataReferences(directory: directory, projectName: name), options: new CSharpCompilationOptions(outputKind: OutputKind.DynamicallyLinkedLibrary));
        return compilation;
    }

    private static bool IsBuildOutput(string path, string directory) =>
        Path.GetRelativePath(relativeTo: directory, path: path)
        .Split(separator: new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar })
        .Any(predicate: segment => string.Equals(a: segment, b: "bin", comparisonType: StringComparison.OrdinalIgnoreCase) || string.Equals(a: segment, b: "obj", comparisonType: StringComparison.OrdinalIgnoreCase));
    /// <summary>Excludes the analysed assembly and duplicate copies of dependency assemblies.</summary>
    private static MetadataReference[] GetMetadataReferences(string directory, string projectName)
    {
        string trustedAssemblies = AppContext.GetData(name: "TRUSTED_PLATFORM_ASSEMBLIES") as string ?? throw new InvalidOperationException(message: "Platform assemblies could not be resolved.");
        string buildDirectory = Path.Combine(path1: directory, path2: "bin");

        string? projectAssembly = Directory.Exists(path: buildDirectory) ? Directory.GetFiles(path: buildDirectory, searchPattern: projectName + ".dll", searchOption: SearchOption.AllDirectories)
            .Where(predicate: path => !Path.GetRelativePath(relativeTo: buildDirectory, path: path)
            .Split(separator: Path.DirectorySeparatorChar)
            .Any(predicate: segment => segment is "ref" or "refint"))
            .OrderByDescending(keySelector: path => File.GetLastWriteTimeUtc(path: path))
            .ThenBy(keySelector: path => path, comparer: StringComparer.Ordinal)
            .FirstOrDefault() : null;

        string[] buildAssemblies = projectAssembly is null ? Array.Empty<string>() : Directory.GetFiles(path: Path.GetDirectoryName(path: projectAssembly)!, searchPattern: "*.dll");

        return trustedAssemblies.Split(separator: Path.PathSeparator)
            .Concat(second: buildAssemblies)
            .Concat(second: GetRestoredPackageAssemblies(directory: directory))
            .Where(predicate: path => !string.Equals(a: Path.GetFileNameWithoutExtension(path: path), b: projectName, comparisonType: StringComparison.OrdinalIgnoreCase))
            .DistinctBy(keySelector: path => Path.GetFileName(path: path), comparer: StringComparer.OrdinalIgnoreCase)
            .Select(selector: path => MetadataReference.CreateFromFile(path: path))
            .ToArray();
    }

    private static IEnumerable<string> GetRestoredPackageAssemblies(
        string directory)
    {
        string assetsPath = Path.Combine(
            path1: directory,
            path2: "obj",
            path3: "project.assets.json");

        if (!File.Exists(path: assetsPath))
        {
            return [];
        }

        using JsonDocument document = JsonDocument.Parse(
            json: File.ReadAllText(path: assetsPath));

        JsonElement root = document.RootElement;

        if (!root.TryGetProperty(propertyName: "targets", value: out JsonElement targets)
            || !root.TryGetProperty(propertyName: "libraries", value: out JsonElement libraries)
            || !root.TryGetProperty(propertyName: "packageFolders", value: out JsonElement packageFolders))
        {
            return [];
        }

        string[] roots = packageFolders
            .EnumerateObject()
            .Select(selector: packageFolder => packageFolder.Name)
            .ToArray();

        var assemblies = new List<string>();

        foreach (JsonProperty target in targets.EnumerateObject())
        {
            foreach (JsonProperty libraryTarget in target.Value.EnumerateObject())
            {
                if (!libraryTarget.Value.TryGetProperty(
                    propertyName: "type",
                    value: out JsonElement targetType)
                    || targetType.GetString() != "package"
                    || !libraryTarget.Value.TryGetProperty(
                        propertyName: "compile",
                        value: out JsonElement compileAssets)
                    || !libraries.TryGetProperty(
                        propertyName: libraryTarget.Name,
                        value: out JsonElement library)
                    || !library.TryGetProperty(
                        propertyName: "path",
                        value: out JsonElement libraryPathElement))
                {
                    continue;
                }

                string? libraryPath = libraryPathElement.GetString();

                if (string.IsNullOrWhiteSpace(value: libraryPath))
                {
                    continue;
                }

                foreach (JsonProperty compileAsset in compileAssets.EnumerateObject())
                {
                    if (!compileAsset.Name.EndsWith(
                        value: ".dll",
                        comparisonType: StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string? assembly = roots
                        .Select(rootPath => Path.Combine(
                            path1: rootPath,
                            path2: libraryPath.Replace(
                                oldChar: '/',
                                newChar: Path.DirectorySeparatorChar),
                            path3: compileAsset.Name.Replace(
                                oldChar: '/',
                                newChar: Path.DirectorySeparatorChar)))
                        .FirstOrDefault(predicate: File.Exists);

                    if (assembly is not null)
                    {
                        assemblies.Add(item: assembly);
                    }
                }
            }
        }

        return assemblies;
    }
}
