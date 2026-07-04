using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace StandardIo.ArchitectureDiagram.Core.Brokers.Workspaces;

public sealed class WorkspacePathBroker : IWorkspacePathBroker
{
    public Task<WorkspacePathLoadResult> LoadAsync(
        string inputPath,
        WorkspacePathLoadOptions options,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(inputPath))
        {
            throw new ArgumentException("Input path is required.", nameof(inputPath));
        }

        options ??= new WorkspacePathLoadOptions();
        var fullPath = Path.GetFullPath(inputPath);
        var projectPaths = ResolveProjectPaths(fullPath);
        var selectedProjectPaths = ApplyProjectFilter(projectPaths, options.ProjectFilter).ToArray();
        if (selectedProjectPaths.Length == 0)
        {
            throw new InvalidOperationException("No matching C# projects were found.");
        }

        var projects = LoadProjects(selectedProjectPaths, cancellationToken);
        var name = Directory.Exists(fullPath)
            ? new DirectoryInfo(fullPath).Name
            : Path.GetFileNameWithoutExtension(fullPath);

        return Task.FromResult(new WorkspacePathLoadResult(name, projects));
    }

    private static IReadOnlyList<string> ResolveProjectPaths(string fullPath)
    {
        if (File.Exists(fullPath))
        {
            var extension = Path.GetExtension(fullPath);
            if (string.Equals(extension, ".csproj", StringComparison.OrdinalIgnoreCase))
            {
                return new[] { fullPath };
            }

            if (string.Equals(extension, ".sln", StringComparison.OrdinalIgnoreCase))
            {
                return ReadSolutionProjectPaths(fullPath);
            }

            throw new InvalidOperationException("Input file must be a .sln or .csproj file.");
        }

        if (!Directory.Exists(fullPath))
        {
            throw new DirectoryNotFoundException($"Input path was not found: {fullPath}");
        }

        var solutionPaths = Directory.GetFiles(fullPath, "*.sln", SearchOption.TopDirectoryOnly);
        if (solutionPaths.Length == 1)
        {
            return ReadSolutionProjectPaths(solutionPaths[0]);
        }

        if (solutionPaths.Length > 1)
        {
            throw new InvalidOperationException("Folder contains multiple solution files. Pass a .sln path or --project filter.");
        }

        var projectPaths = Directory.GetFiles(fullPath, "*.csproj", SearchOption.AllDirectories)
            .Where(path => !IsUnderBuildOutput(path))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (projectPaths.Length == 0)
        {
            throw new InvalidOperationException("Folder does not contain any C# project files.");
        }

        return projectPaths;
    }

    private static IReadOnlyList<string> ReadSolutionProjectPaths(string solutionPath)
    {
        var basePath = Path.GetDirectoryName(solutionPath)!;
        return File.ReadLines(solutionPath)
            .Select(TryReadProjectPath)
            .OfType<string>()
            .Where(path => string.Equals(Path.GetExtension(path), ".csproj", StringComparison.OrdinalIgnoreCase))
            .Select(path => Path.GetFullPath(Path.Combine(basePath, path)))
            .Where(File.Exists)
            .ToArray();
    }

    private static string? TryReadProjectPath(string line)
    {
        if (!line.StartsWith("Project(", StringComparison.Ordinal))
        {
            return null;
        }

        var parts = line.Split(',');
        return parts.Length < 2
            ? null
            : parts[1].Trim().Trim('"');
    }

    private static IEnumerable<string> ApplyProjectFilter(
        IReadOnlyList<string> projectPaths,
        string? projectFilter)
    {
        if (string.IsNullOrWhiteSpace(projectFilter))
        {
            return projectPaths;
        }

        var normalizedFilter = projectFilter!.Trim();
        return projectPaths.Where(path =>
            string.Equals(Path.GetFileNameWithoutExtension(path), normalizedFilter, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Path.GetFileName(path), normalizedFilter, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Path.GetFullPath(path), Path.GetFullPath(normalizedFilter), StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<Project> LoadProjects(
        IReadOnlyList<string> selectedProjectPaths,
        CancellationToken cancellationToken)
    {
        using var workspace = new AdhocWorkspace();
        var allProjectPaths = ExpandProjectClosure(selectedProjectPaths);
        var projectIds = allProjectPaths.ToDictionary(
            path => path,
            _ => ProjectId.CreateNewId(),
            StringComparer.OrdinalIgnoreCase);
        var solution = workspace.CurrentSolution;

        foreach (var projectPath in allProjectPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var projectInfo = ReadProjectInfo(projectPath);
            var references = projectInfo.ProjectReferences
                .Where(projectIds.ContainsKey)
                .Select(path => new ProjectReference(projectIds[path]))
                .ToImmutableArray();

            solution = solution.AddProject(ProjectInfo.Create(
                projectIds[projectPath],
                VersionStamp.Create(),
                projectInfo.Name,
                projectInfo.AssemblyName,
                LanguageNames.CSharp,
                filePath: projectPath,
                compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
                parseOptions: CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview),
                projectReferences: references,
                metadataReferences: BasicReferences()));
        }

        foreach (var projectPath in allProjectPaths)
        {
            foreach (var sourcePath in Directory.GetFiles(Path.GetDirectoryName(projectPath)!, "*.cs", SearchOption.AllDirectories)
                .Where(path => !IsUnderBuildOutput(path))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                solution = solution.AddDocument(
                    DocumentId.CreateNewId(projectIds[projectPath]),
                    Path.GetFileName(sourcePath),
                    SourceText.From(File.ReadAllText(sourcePath)),
                    filePath: sourcePath);
            }
        }

        if (!workspace.TryApplyChanges(solution))
        {
            throw new InvalidOperationException("Could not load projects into the Roslyn workspace.");
        }

        return selectedProjectPaths
            .Select(path => workspace.CurrentSolution.GetProject(projectIds[path])!)
            .ToArray();
    }

    private static IReadOnlyList<string> ExpandProjectClosure(IReadOnlyList<string> selectedProjectPaths)
    {
        var ordered = new List<string>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Visit(string projectPath)
        {
            projectPath = Path.GetFullPath(projectPath);
            if (!visited.Add(projectPath))
            {
                return;
            }

            ordered.Add(projectPath);
            foreach (var reference in ReadProjectInfo(projectPath).ProjectReferences)
            {
                if (File.Exists(reference))
                {
                    Visit(reference);
                }
            }
        }

        foreach (var projectPath in selectedProjectPaths)
        {
            Visit(projectPath);
        }

        return ordered;
    }

    private static ProjectFileInfo ReadProjectInfo(string projectPath)
    {
        var document = XDocument.Load(projectPath);
        var projectDirectory = Path.GetDirectoryName(projectPath)!;
        var name = Path.GetFileNameWithoutExtension(projectPath);
        var assemblyName = document.Descendants()
            .FirstOrDefault(e => e.Name.LocalName == "AssemblyName")
            ?.Value;
        var references = document.Descendants()
            .Where(e => e.Name.LocalName == "ProjectReference")
            .Select(e => e.Attribute("Include")?.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => Path.GetFullPath(Path.Combine(projectDirectory, value!)))
            .ToArray();

        return new ProjectFileInfo(name, string.IsNullOrWhiteSpace(assemblyName) ? name : assemblyName!, references);
    }

    private static IReadOnlyList<MetadataReference> BasicReferences()
    {
        var trustedAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        if (!string.IsNullOrWhiteSpace(trustedAssemblies))
        {
            return trustedAssemblies!
                .Split(Path.PathSeparator)
                .Where(File.Exists)
                .Select(path => MetadataReference.CreateFromFile(path))
                .ToArray();
        }

        return new[]
        {
            MetadataReference.CreateFromFile(typeof(object).GetTypeInfo().Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Enumerable).GetTypeInfo().Assembly.Location)
        };
    }

    private static bool IsUnderBuildOutput(string path)
    {
        var parts = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return parts.Any(part =>
            string.Equals(part, "bin", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(part, "obj", StringComparison.OrdinalIgnoreCase));
    }

    private sealed record ProjectFileInfo(
        string Name,
        string AssemblyName,
        IReadOnlyList<string> ProjectReferences);
}
