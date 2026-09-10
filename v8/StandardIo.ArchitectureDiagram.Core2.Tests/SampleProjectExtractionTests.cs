// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------

using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using StandardIo.ArchitectureDiagram.Core2.Models;
using StandardIo.ArchitectureDiagram.Core2.Services.Orchestrations.Projects;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core2.Tests;

public sealed partial class SampleProjectExtractionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ShouldMatchTheReviewedSampleModelAsync(bool useDirectory)
    {
        // Given
        string projectPath = FindSampleProject();
        string projectDirectory = Path.GetDirectoryName(path: projectPath)!;
        var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
        jsonOptions.Converters.Add(item: new JsonStringEnumConverter());
        string expectedJson = await File.ReadAllTextAsync(path: Path.Combine(path1: projectDirectory, path2: "ExpectedModel.json"));
        var expected = JsonSerializer.Deserialize<ProjectModel>(json: expectedJson, options: jsonOptions)!;
        expected.Path = projectPath;
        var builder = new ProjectModelBuilder();

        // When: use the same folder-based loading approach as cCoder.CodeAnalysis.
        ProjectModel actual = await builder.BuildAsync(projectFilePath: useDirectory ? projectDirectory : projectPath);

        // Then
        Assert.Equal(expected: projectPath, actual: actual.Path);
        Assert.Equal(
            expected: JsonSerializer.Serialize(value: expected, options: jsonOptions),
            actual: JsonSerializer.Serialize(value: actual, options: jsonOptions));
    }

    [Fact]
    public async Task ShouldSplitTheSampleIntoItsSpecifiedRootsAndOneLeftoverModelAsync()
    {
        ProjectModel model = await new ProjectModelBuilder().BuildAsync(projectFilePath: FindSampleProject());
        string before = JsonSerializer.Serialize(value: model);
        const string prefix = "StandardIo.ArchitectureDiagram.SampleProject.";

        ProjectModel[] trees = new ProjectModelSplitter().Split(projectModel: model);

        Assert.Equal(expected: 7, actual: trees.Length);
        Assert.Equal(
            expected: new[] { "Exposures.ClassManager", "Exposures.ClassStudentManager", "Exposures.SchoolManager",
                "Exposures.StudentManager", "Exposures.TeacherManager", "SchoolServiceCollectionExtensions" }.Select(name => prefix + name),
            actual: trees.Take(6).Select(tree => tree.Types![0].Name));
        ProjectModel schoolManager = Assert.Single(collection: trees.Where(tree => tree.Types![0].Name == prefix + "Exposures.SchoolManager"));
        string[] schoolStack =
        {
            "Exposures.SchoolManager", "Exposures.ISchoolManager", "Exposures.SchoolFactory", "Exposures.ISchoolFactory",
            "Services.Orchestrations.SchoolOrchestrationService", "Services.Orchestrations.ISchoolOrchestrationService",
            "Services.Processings.SchoolProcessingService", "Services.Processings.ISchoolProcessingService",
            "Services.Processings.SchoolEventProcessingService", "Services.Processings.ISchoolEventProcessingService",
            "Services.Foundations.SchoolService", "Services.Foundations.ISchoolService",
            "Services.Foundations.SchoolEventService", "Services.Foundations.ISchoolEventService",
            "Brokers.Storages.SchoolBroker", "Brokers.Storages.ISchoolBroker",
            "Brokers.Eventings.SchoolEventBroker", "Brokers.Eventings.ISchoolEventBroker"
        };
        Assert.Equal(
            expected: schoolStack.Select(name => prefix + name).Concat(new[] { "System.Collections.Generic.List<T>", "cCoder.Eventing.IEventHub" }).OrderBy(name => name, StringComparer.Ordinal),
            actual: schoolManager.Types!.Select(type => type.Name).OrderBy(name => name, StringComparer.Ordinal));
        Assert.Equal(expected: 47, actual: schoolManager.Dependencies!.Length);
        Assert.Equal(expected: 5, actual: trees.Count(tree => tree.Types!.Any(type => type.Name == prefix + "Exposures.SchoolFactory")));
        Assert.Equal(
            expected: new[] { "Class", "ClassStudent", "School", "SchoolDataContext", "Student", "Teacher" }.Select(name => prefix + "Models." + name),
            actual: trees[^1].Types!.Select(type => type.Name));
        Assert.Empty(collection: trees[^1].Dependencies!);
        Assert.Equal(expected: before, actual: JsonSerializer.Serialize(value: model));
    }

    [Fact]
    public async Task ShouldGenerateTheReviewedSampleTreesAsDrawIOBytesAsync()
    {
        // Given: the reviewed source-model oracle supplies the expected graph.
        string projectPath = FindSampleProject();
        var options = new JsonSerializerOptions();
        options.Converters.Add(new JsonStringEnumConverter());
        ProjectModel expected = JsonSerializer.Deserialize<ProjectModel>(
            await File.ReadAllTextAsync(Path.Combine(Path.GetDirectoryName(projectPath)!, "ExpectedModel.json")), options)!;
        ProjectModel[] expectedTrees = new ProjectModelSplitter().Split(expected);
        // When: use the public generator and every production service and broker.
        byte[] bytes = await new DiagramGenerator().GenerateAsync(new DiagramGenerationRequest
        {
            ProjectPaths = new[] { projectPath }, DiagramType = DiagramTypes.Architecture
        });
        var document = System.Xml.Linq.XDocument.Parse(System.Text.Encoding.UTF8.GetString(bytes));
        var cells = document.Descendants("mxCell").ToArray();
        // Then: interface definitions become class labels; unique concrete type relationships remain exact.
        Assert.Equal(7, expectedTrees.Length);
        Assert.Equal(7, cells.Count(cell => (string?)cell.Attribute("parent") == "1"));
        for (int index = 0; index < expectedTrees.Length; index++)
        {
            var children = cells.Where(cell => (string?)cell.Attribute("parent") == "tree-" + index).ToArray();
            var vertices = children.Where(cell => (string?)cell.Attribute("vertex") == "1").ToArray();
            var names = vertices.ToDictionary(cell => (string)cell.Attribute("id")!, cell => (string)cell.Attribute("typeName")!);
            Assert.Equal(expectedTrees[index].Types!.Where(type => type.FrameworkType == FrameworkType.Class || !type.IsInternal).Select(type => type.Name).OrderBy(name => name),
                vertices.Select(cell => (string?)cell.Attribute("typeName")).OrderBy(name => name));
            string[] expectedLinks = expectedTrees[index].Dependencies!.Where(link => link.DependencyType == DependencyType.Consumed).Select(link =>
                link.FromType + "|" + link.ToType).Distinct().OrderBy(value => value).ToArray();
            string[] actualLinks = children.Where(cell => (string?)cell.Attribute("edge") == "1").Select(cell =>
                names[(string)cell.Attribute("source")!] + "|" + names[(string)cell.Attribute("target")!]).OrderBy(value => value).ToArray();
            Assert.Equal(expectedLinks, actualLinks);
            Assert.All(children.Where(cell => (string?)cell.Attribute("edge") == "1"), cell => Assert.Equal("", (string?)cell.Attribute("value")));
            foreach (var vertex in vertices)
            {
                string typeName = (string)vertex.Attribute("typeName")!;
                string[] interfaces = expectedTrees[index].Dependencies!.Where(link => link.FromType == typeName && link.DependencyType == DependencyType.Inheritance)
                    .Select(link => link.ToType!).OrderBy(name => name, StringComparer.Ordinal).ToArray();
                string expectedLabel = typeName.Split('.').Last() + (interfaces.Length == 0 ? "" : "\n" + string.Join(", ", interfaces.Select(name => name.Split('.').Last())));
                Assert.Equal(expectedLabel, (string?)vertex.Attribute("value"));
            }
        }
    }

    [Theory]
    [InlineData("drawio", false)]
    [InlineData("html", false)]
    [InlineData("html", true)]
    public async Task ShouldGenerateTheSampleFromTheStandaloneCliAsync(string format, bool explicitFormat)
    {
        // Given: launch the CLI in its own runtime, without the test host's assemblies.
        string sample = FindSampleProject();
        string v8 = Path.GetDirectoryName(Path.GetDirectoryName(sample))!;
        string configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        string cli = Path.Combine(v8, "DiagramCLI", "bin", configuration, "net10.0", "DiagramCLI.dll");
        var start = new System.Diagnostics.ProcessStartInfo("dotnet")
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
            CreateNoWindow = true
        };
        start.ArgumentList.Add(cli);
        start.ArgumentList.Add("Architecture");
        start.ArgumentList.Add(sample);
        string outputPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + " output." + format);
        start.ArgumentList.Add("--output");
        start.ArgumentList.Add(outputPath);
        if (explicitFormat) { start.ArgumentList.Add("--format"); start.ArgumentList.Add(format); }
        // When
        using var process = System.Diagnostics.Process.Start(start)!;
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> errors = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        // Then
        Assert.True(process.ExitCode == 0, await errors);
        Assert.Empty(await output);
        Assert.True(File.Exists(outputPath));
        string generated;
        try { generated = await File.ReadAllTextAsync(outputPath); }
        finally { File.Delete(outputPath); }
        var document = System.Xml.Linq.XDocument.Parse(generated);
        if (format == "drawio")
        {
            Assert.Equal(7, document.Descendants("mxCell").Count(cell => (string?)cell.Attribute("parent") == "1"));
            Assert.Equal(53, document.Descendants("mxCell").Count(cell => (string?)cell.Attribute("edge") == "1"));
        }
        else
        {
            System.Xml.Linq.XNamespace svg = "http://www.w3.org/2000/svg";
            Assert.Equal(7, document.Descendants(svg + "g").Count(group => group.Attribute("id") is not null));
            Assert.Equal(65, document.Descendants(svg + "g").Count(group => group.Attribute("data-type") is not null));
            Assert.Equal(53, document.Descendants(svg + "polyline").Count());
            Assert.Single(document.Descendants("style"));
        }
    }

    private static string FindSampleProject()
    {
        const string projectName = "StandardIo.ArchitectureDiagram.SampleProject";

        for (DirectoryInfo? directory = new DirectoryInfo(path: AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            string path = Path.Combine(directory.FullName, "v8", projectName, projectName + ".csproj");

            if (File.Exists(path: path))
            {
                return path;
            }
        }

        throw new FileNotFoundException(message: "Run the sample extraction tests from a checkout containing the v8 sample project.");
    }
}
