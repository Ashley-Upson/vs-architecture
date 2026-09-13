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
using StandardIo.ArchitectureDiagram.Core2.Exposures;

namespace StandardIo.ArchitectureDiagram.Core2.Tests;
public sealed partial class SampleProjectExtractionTests
{
    private static readonly string[] expectedRootTypeNames =
    {
        "Exposures.ClassManager",
        "Exposures.ClassStudentManager",
        "Exposures.SchoolImportManager",
        "Exposures.SchoolManager",
        "Exposures.SchoolReportManager",
        "Exposures.StudentManager",
        "Exposures.TeacherManager",
        "IServiceCollectionExtensions"
    };
    private static readonly string[] expectedExternalTypeNames = new[]
    {
        "cCoder.Eventing.IEventHub"
    };
    private static readonly JsonSerializerOptions modelJsonOptions = new()
    {
        Converters =
        {
            new JsonStringEnumConverter()
        }
    };
    [Fact]
    public async Task ShouldCentreEverySampleOrchestrationOverItsChildrenAfterSharedParentsMove()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddArchitectureDiagram();
        using var provider = Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions.BuildServiceProvider(services);
        var builder = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<ProjectModelBuilder>(provider);
        var layout = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<LayoutModelBuilder>(provider);
        var project = await builder.BuildAsync(FindSampleProject());
        var rendered = layout.BuildRenderModel(new RenderModel(new[] { project }));
        var drawing = Assert.Single(rendered.Projects, drawing => drawing.Name == project.Name);
        foreach (var parent in drawing.Nodes.Where(node => node.TypeName.EndsWith("OrchestrationService")))
        {
            var children = drawing.Connections.Where(edge => edge.SourceId == parent.Id)
                .Select(edge => drawing.Nodes.Single(node => node.Id == edge.TargetId)).ToArray();
            double expected = (children.Min(node => node.X + node.Width / 2) + children.Max(node => node.X + node.Width / 2)) / 2;
            Assert.InRange(Math.Abs(parent.X + parent.Width / 2 - expected), 0, 0.01);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task ShouldMatchTheReviewedSampleModelAsync(bool useDirectory, bool dataProject)
    {
        // Given
        string projectPath = dataProject ? FindDataProject() : FindSampleProject();
        string projectDirectory = Path.GetDirectoryName(path: projectPath)!;

        var jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        jsonOptions.Converters.Add(item: new JsonStringEnumConverter());
        string expectedJson = await File.ReadAllTextAsync(path: Path.Combine(path1: projectDirectory, path2: "ExpectedModel.json"));
        var expected = JsonSerializer.Deserialize<ProjectModel>(json: expectedJson, options: jsonOptions)!;
        expected.Path = projectPath;
        var builder = TestServices.Get<ProjectModelBuilder>();
        // When: use the same folder-based loading approach as cCoder.CodeAnalysis.
        ProjectModel actual = await builder.BuildAsync(projectFilePath: useDirectory ? projectDirectory : projectPath);
        // Then
        Assert.Equal(expected: projectPath, actual: actual.Path);
        Assert.Equal(expected: JsonSerializer.Serialize(value: expected, options: jsonOptions), actual: JsonSerializer.Serialize(value: actual, options: jsonOptions));
    }

    [Fact]
    public async Task ShouldSplitTheSampleIntoItsSpecifiedRootsAsync()
    {
        // Given: the fixture and inputs below.
        ProjectModel model = await TestServices.Get<ProjectModelBuilder>().BuildAsync(projectFilePath: FindSampleProject());
        string before = JsonSerializer.Serialize(value: model);
        const string prefix = "StandardIo.ArchitectureDiagram.SampleProject.";
        // When: exercise the operation under test.
        ProjectModel[] trees = TestServices.Get<ProjectModelSplitter>().Split(projectModel: model);
        // Then: verify the resulting contract.
        Assert.Equal(2, trees.Length);
        Assert.Equal(new[] { prefix + "Exposures.SchoolReportManager", prefix + "IServiceCollectionExtensions" }, trees.Select(tree => tree.Types![0].Name));
        var composition = Assert.Single(trees, tree => tree.Types![0].Name == prefix + "IServiceCollectionExtensions");
        Assert.Equal(101, composition.Types!.Length);
        Assert.Equal(318, composition.Dependencies!.Length);
        Assert.Contains(composition.Dependencies, link => link.FromType == prefix + "IServiceCollectionExtensions" && link.ToType == prefix + "Exposures.SchoolManager");
        Assert.Contains(composition.Types, type => type.Name == prefix + "Brokers.Storages.SchoolBroker");
        Assert.DoesNotContain(composition.Types, type => type.Name == prefix + "Exposures.SchoolReportManager");
        Assert.Equal(expected: before, actual: JsonSerializer.Serialize(value: model));
    }

    [Fact]
    public async Task ShouldGenerateTheReviewedSampleTreesAsDrawIOBytesAsync()
    {
        // Given: the reviewed source-model oracle supplies the expected graph.
        string projectPath = FindSampleProject();
        ProjectModel expected = JsonSerializer.Deserialize<ProjectModel>(json: await File.ReadAllTextAsync(path: Path.Combine(path1: Path.GetDirectoryName(path: projectPath)!, path2: "ExpectedModel.json")), options: modelJsonOptions)!;
        ProjectModel[] expectedTrees = TestServices.Get<ProjectModelSplitter>().Split(projectModel: expected);
        // When: use the public generator and every production service and broker.
        byte[] bytes = await TestServices.Get<DiagramGenerator>().GenerateAsync(request: new DiagramGenerationRequest { ProjectPaths = new[] { projectPath }, DiagramType = DiagramTypes.Architecture });
        var document = DiagramTestDocument.Parse(bytes);

        var cells = document.Descendants(name: "mxCell")
            .ToArray();
        // Then: interface definitions become class labels; unique concrete type relationships remain exact.

        Assert.Equal(expected: 2, actual: expectedTrees.Length);
        Assert.Equal(expected: 4, actual: cells.Count(predicate: cell => (string? )cell.Attribute(name: "parent") == "1" && (string?)cell.Attribute("vertex") == "1"));

        var allNames = cells.Where(cell => cell.Attribute("typeName") != null)
            .ToDictionary(cell => (string)cell.Attribute("id")!, cell => (string)cell.Attribute("typeName")!);
        foreach (var boundary in expected.Types!.Where(type => !type.IsInternal && !type.IsDataType && (type.HasDeclaredBehaviour ?? type.Methods!.Length > 0)))
        {
            var copies = cells.Where(cell => (string?)cell.Attribute("typeName") == boundary.Name).ToArray();
            int consumers = expectedTrees.Count(tree => tree.Dependencies!.Any(link =>
                link.DependencyType == DependencyType.Consumed && !link.IsComposition && !link.IsResultExtension && link.ToType == boundary.Name));
            Assert.Equal(consumers, copies.Length);
            Assert.Equal(copies.Length, copies.Select(node => (string?)node.Attribute("parent")).Distinct().Count());
            foreach (var node in copies)
            {
                var container = Assert.Single(cells, cell => (string?)cell.Attribute("id") == (string?)node.Attribute("parent"));
                Assert.Equal(boundary.AssemblyName + " (external)", (string?)container.Attribute("value"));
            }
        }

        for (int index = 0; index < expectedTrees.Length; index++)
        {
            var children = cells.Where(predicate: cell => (string? )cell.Attribute(name: "parent") == "tree-" + index)
                .ToArray();

            var vertices = children.Where(predicate: cell => (string? )cell.Attribute(name: "vertex") == "1")
                .ToArray();

            var names = vertices.ToDictionary(keySelector: cell => (string)cell.Attribute(name: "id")!, elementSelector: cell => (string)cell.Attribute(name: "typeName")!);

            Assert.Equal(expected: expectedTrees[index].Types!.Where(predicate: type => type.IsInternal && type.FrameworkType == FrameworkType.Class && type.Methods!.Length > 0)
                .Select(selector: type => type.Name)
                .OrderBy(keySelector: name => name), actual: vertices.Select(selector: cell => (string? )cell.Attribute(name: "typeName"))
                .OrderBy(keySelector: name => name));

            string[] expectedLinks = expectedTrees[index].Dependencies!.Where(predicate: link => link.DependencyType == DependencyType.Consumed && !link.IsComposition && !link.IsResultExtension && expectedTrees[index].Types!.Any(type => type.Name == link.ToType && type.IsInternal) && expectedTrees[index].Types!.Any(type => type.Name == link.FromType && (!type.IsInternal || type.Methods!.Length > 0)) && expectedTrees[index].Types!.Any(type => type.Name == link.ToType && (!type.IsInternal || type.Methods!.Length > 0)))
                .Select(selector: link => link.FromType + "|" + link.ToType)
                .Distinct()
                .OrderBy(keySelector: value => value)
                .ToArray();

            string[] actualLinks = children.Where(predicate: cell => (string? )cell.Attribute(name: "edge") == "1")
                .Select(selector: cell => names[(string)cell.Attribute(name: "source")!] + "|" + names[(string)cell.Attribute(name: "target")!])
                .OrderBy(keySelector: value => value)
                .ToArray();

            Assert.Equal(expected: expectedLinks, actual: actualLinks);
            var expectedExternalLinks = expectedTrees[index].Dependencies!
                .Where(link => link.DependencyType == DependencyType.Consumed && !link.IsComposition && !link.IsResultExtension && expectedTrees[index].Types!.Any(type => type.Name == link.ToType && !type.IsInternal && !type.IsDataType && (type.HasDeclaredBehaviour ?? type.Methods!.Length > 0)))
                .Select(link => link.FromType + "|" + link.ToType).Distinct().OrderBy(value => value).ToArray();
            var actualExternalLinks = cells.Where(cell => (string?)cell.Attribute("edge") == "1" && (string?)cell.Attribute("parent") == "1" && names.ContainsKey((string)cell.Attribute("source")!))
                .Select(cell => allNames[(string)cell.Attribute("source")!] + "|" + allNames[(string)cell.Attribute("target")!]).OrderBy(value => value).ToArray();
            Assert.Equal(expectedExternalLinks, actualExternalLinks);
            Assert.All(collection: children.Where(predicate: cell => (string? )cell.Attribute(name: "edge") == "1"), action: cell => Assert.Equal(expected: "", actual: (string? )cell.Attribute(name: "value")));

            foreach (var vertex in vertices)
            {
                string typeName = (string)vertex.Attribute(name: "typeName")!;

                string[] interfaces = expectedTrees[index].Dependencies!.Where(predicate: link => link.FromType == typeName && link.DependencyType == DependencyType.Inheritance)
                    .Select(selector: link => link.ToType!)
                    .OrderBy(keySelector: name => name, comparer: StringComparer.Ordinal)
                    .ToArray();

                string expectedLabel = typeName.Split(separator: '.')
                    .Last() + (interfaces.Length == 0 ? "" : "\n" + string.Join(separator: ", ", values: interfaces.Select(selector: name => name.Split(separator: '.')
                    .Last())));

                Assert.Equal(expected: expectedLabel, actual: (string? )vertex.Attribute(name: "value"));
            }
        }
    }

    [Theory]
    [InlineData("drawio", false, false)]
    [InlineData("drawio", false, true)]
    [InlineData("html", true, false)]
    [InlineData("html", true, true)]
    public async Task ShouldGenerateTheSampleFromTheStandaloneCliAsync(string format, bool explicitFormat, bool noDuplicates)
    {
        // Given: launch the CLI in its own runtime, without the test host's assemblies.
        string sample = FindSampleProject();
        string v8 = Path.GetDirectoryName(path: Path.GetDirectoryName(path: sample))!;
        string configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        string cli = Path.Combine(paths: new[] { v8, "DiagramCLI", "bin", configuration, "net10.0", "DiagramCLI.dll" });

        var start = new System.Diagnostics.ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        start.ArgumentList.Add(item: cli);
        start.ArgumentList.Add(item: "Architecture");
        start.ArgumentList.Add(item: sample);
        string outputPath = Path.Combine(path1: Path.GetTempPath(), path2: Guid.NewGuid() + " output." + format);
        start.ArgumentList.Add(item: "--output");
        start.ArgumentList.Add(item: outputPath);

        if (explicitFormat)
        {
            start.ArgumentList.Add(item: "--format");
            start.ArgumentList.Add(item: format);
        }

        if (noDuplicates)
        {
            start.ArgumentList.Add(item: "--noduplicates");
        }

        // When

        using var process = System.Diagnostics.Process.Start(startInfo: start)!;
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> errors = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        // Then
        Assert.True(condition: process.ExitCode == 0, userMessage: await errors);
        Assert.Empty(value: await output);
        Assert.True(condition: File.Exists(path: outputPath));
        string generated;

        try
        {
            generated = await File.ReadAllTextAsync(path: outputPath);
        }
        finally
        {
            File.Delete(path: outputPath);
        }

        var document = System.Xml.Linq.XDocument.Parse(text: generated);

        if (noDuplicates)
        {
            string[] typeNames = document.Descendants()
                .Select(selector: element => (string?)(element.Attribute(name: "typeName") ?? element.Attribute(name: "data-type")))
                .OfType<string>()
                .ToArray();

            Assert.Equal(expected: 52, actual: typeNames.Length);
            Assert.Equal(expected: typeNames.Length, actual: typeNames.Distinct()
                .Count());
        }

        if (format == "drawio")
        {
            Assert.Equal(expected: noDuplicates ? 3 : 4, actual: document.Descendants(name: "mxCell")
                .Count(predicate: cell => (string? )cell.Attribute(name: "parent") == "1" && (string?)cell.Attribute("vertex") == "1"));

            Assert.Equal(expected: 68, actual: document.Descendants(name: "mxCell")
                .Count(predicate: cell => (string? )cell.Attribute(name: "edge") == "1"));
        }
        else
        {
            System.Xml.Linq.XNamespace svg = "http://www.w3.org/2000/svg";

            Assert.Equal(expected: noDuplicates ? 3 : 4, actual: document.Descendants(name: svg + "g")
                .Count(predicate: group => group.Attribute(name: "id")is not null));

            Assert.Equal(expected: 52, actual: document.Descendants(name: svg + "g")
                .Count(predicate: group => group.Attribute(name: "data-type")is not null));

            Assert.Equal(expected: 68, actual: document.Descendants(name: svg + "polyline")
                .Count());

            Assert.Single(collection: document.Descendants(name: "style"));
        }
    }

    [Theory]
    [InlineData(DiagramFormats.DrawIO)]
    [InlineData(DiagramFormats.Html)]
    public async Task ShouldRenderTheSampleProjectPairAsync(DiagramFormats format)
    {
        // Given: the sample references the separately compiled Data project.
        var builder = TestServices.Get<ProjectModelBuilder>();
        ProjectModel sample = await builder.BuildAsync(FindSampleProject());
        ProjectModel data = await builder.BuildAsync(FindDataProject());
        const string dataPrefix = "StandardIo.ArchitectureDiagram.SampleProject.Data.";
        Assert.Equal(8, data.Types!.Length);
        Assert.All(data.Types, type => Assert.True(type.IsInternal));
        Assert.DoesNotContain(sample.Types!, type => type.IsInternal && type.Name!.StartsWith(dataPrefix));
        Assert.Contains(sample.Dependencies!, link => link.FromType!.EndsWith(".SchoolBroker") && link.ToType == dataPrefix + "Models.SchoolDataContext");
        Assert.Contains(sample.Dependencies!, link => link.FromType!.EndsWith(".SchoolBroker") && link.ToType == dataPrefix + "Exposures.ISchoolFactory");

        // When: both projects enter the same production rendering pipeline.
        IDiagramRenderer renderer = format == DiagramFormats.Html
            ? TestServices.Get<HtmlDiagramRenderer>() : TestServices.Get<DrawIODiagramRenderer>();
        byte[] bytes = renderer.Render(new RenderModel(new[] { sample, data }));
        var document = System.Xml.Linq.XDocument.Parse(System.Text.Encoding.UTF8.GetString(bytes));

        // Then: both project containers and the owned Data context/factory are present.
        var containers = document.Descendants().Where(element =>
            format == DiagramFormats.DrawIO ? (string?)element.Attribute("parent") == "1" && (string?)element.Attribute("vertex") == "1"
                : element.Name.LocalName == "g" && element.Attribute("id") != null).ToArray();
        Assert.Equal(3, containers.Length);
        string[] names = document.Descendants().Select(element =>
            (string?)(element.Attribute("typeName") ?? element.Attribute("data-type"))).OfType<string>().ToArray();
        Assert.Contains(dataPrefix + "Exposures.SchoolFactory", names);
        Assert.Contains(dataPrefix + "Models.SchoolDataContext", names);
        Assert.DoesNotContain(names, name => name.StartsWith("System.Collections.Generic.List"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ShouldJoinReferencesToTheirOwningProjectNodesAsync(bool reverseOrder)
    {
        // Given: the same two projects used by the CLI.
        var builder = TestServices.Get<ProjectModelBuilder>();
        var models = new[] { await builder.BuildAsync(FindSampleProject()), await builder.BuildAsync(FindDataProject()) };
        if (reverseOrder) Array.Reverse(models);
        string before = JsonSerializer.Serialize(models);

        // When: prepare the complete drawing through its public DI exposure.
        RenderModel drawing = TestServices.Get<LayoutModelBuilder>().BuildRenderModel(new RenderModel(models, new RenderConfiguration { ColourLines = true }));

        // Then: each selected concrete type has one node, in its defining project.
        var nodes = drawing.Projects.SelectMany(project => project.Nodes).ToArray();
        var context = Assert.Single(nodes, node => node.TypeName.EndsWith(".SchoolDataContext"));
        var factory = Assert.Single(nodes, node => node.TypeName.EndsWith(".SchoolFactory"));
        Assert.DoesNotContain(nodes, node => node.TypeName.EndsWith(".ISchoolFactory"));
        Assert.Contains("ISchoolFactory", factory.Label);
        var data = Assert.Single(drawing.Projects, project => project.Name.EndsWith(".Data"));
        Assert.Contains(context, data.Nodes);
        Assert.Contains(factory, data.Nodes);
        Assert.Equal(before, JsonSerializer.Serialize(models));
        var consumingProject = drawing.Projects.Single(project => project.Name == "StandardIo.ArchitectureDiagram.SampleProject");
        var childProjects = drawing.Projects.Where(project => project.Id != consumingProject.Id).ToArray();
        Assert.InRange(Math.Abs(consumingProject.X + consumingProject.Width / 2 - (childProjects.Min(project => project.X) + childProjects.Max(project => project.X + project.Width)) / 2), 0, 0.01);
        var dataLinks = drawing.CrossProjectConnections.Where(edge => data.Nodes.Any(node => node.Id == edge.TargetId)).ToArray();
        foreach (var edge in dataLinks)
        {
            var target = data.Nodes.Single(node => node.Id == edge.TargetId);
            double gutterTop = data.Nodes.Where(node => node.Y < target.Y).Select(node => data.Y + node.Y + node.Height)
                .DefaultIfEmpty(consumingProject.Y + consumingProject.Height).Max();
            Assert.Equal(edge.Points[^3].Y, edge.Points[^2].Y);
            Assert.InRange(edge.Points[^2].Y, gutterTop, data.Y + target.Y);
            Assert.All(edge.Points.Zip(edge.Points.Skip(1)), segment => Assert.True(segment.First.X == segment.Second.X || segment.First.Y == segment.Second.Y));
        }
        Assert.Equal(10, dataLinks.Length);
        Assert.Equal(5, drawing.CrossProjectConnections.Count(edge => edge.TargetId == context.Id));
        Assert.Equal(5, drawing.CrossProjectConnections.Count(edge => edge.TargetId == factory.Id));
        foreach (var edge in dataLinks)
        {
            var sourceProject = drawing.Projects.Single(project => project.Nodes.Any(node => node.Id == edge.SourceId));
            var source = sourceProject.Nodes.Single(node => node.Id == edge.SourceId);
            var target = data.Nodes.Single(node => node.Id == edge.TargetId);
            Assert.Equal(target.Fill, edge.Stroke);
            Assert.Equal(sourceProject.Y + source.Y + source.Height, edge.Points[0].Y);
            Assert.Equal(data.X + target.X + target.Width / 2, edge.Points[^1].X);
            Assert.Equal(data.Y + target.Y, edge.Points[^1].Y);
            Assert.True(edge.Points[0].Y < edge.Points[^1].Y);
            foreach (var project in drawing.Projects)
            foreach (var node in project.Nodes.Where(node => node.Id != edge.SourceId && node.Id != edge.TargetId))
            for (int index = 1; index < edge.Points.Length; index++)
            {
                var a = edge.Points[index - 1];
                var b = edge.Points[index];
                double left = project.X + node.X, top = project.Y + node.Y;
                bool intersects = a.X == b.X
                    ? a.X > left && a.X < left + node.Width && Math.Max(a.Y, b.Y) > top && Math.Min(a.Y, b.Y) < top + node.Height
                    : a.Y > top && a.Y < top + node.Height && Math.Max(a.X, b.X) > left && Math.Min(a.X, b.X) < left + node.Width;
                Assert.False(intersects, edge.FromType + " -> " + edge.ToType + " crosses " + node.TypeName);
            }
        }
        foreach (IDiagramRenderer renderer in new IDiagramRenderer[] { TestServices.Get<DrawIODiagramRenderer>(), TestServices.Get<HtmlDiagramRenderer>() })
        {
            var xml = System.Xml.Linq.XDocument.Parse(System.Text.Encoding.UTF8.GetString(renderer.Render(new RenderModel(models, new RenderConfiguration { ColourLines = true }))));
            var crossLinks = xml.Descendants().Where(element =>
                (element.Name.LocalName == "mxCell" && (string?)element.Attribute("parent") == "1" && (string?)element.Attribute("edge") == "1") ||
                (element.Name.LocalName == "polyline" && element.Parent!.Name.LocalName == "svg")).ToArray();
            Assert.Equal(15, crossLinks.Length);
            Assert.DoesNotContain(crossLinks, link => ((string?)link.Attribute("class"))?.Contains("composition") == true || ((string?)link.Attribute("style"))?.Contains("dashPattern=2 4") == true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ShouldKeepSampleOrchestrationBranchesInSeparateHorizontalSpaces(bool includeData)
    {
        // Given: the real project pair with shared and cross-project dependencies.
        var builder = TestServices.Get<ProjectModelBuilder>();
        var projects = new[] { await builder.BuildAsync(FindSampleProject()), await builder.BuildAsync(FindDataProject()) };
        if (!includeData) projects = projects.Take(1).ToArray();
        // When: use the complete layout pipeline.
        var drawing = TestServices.Get<LayoutModelBuilder>().BuildRenderModel(new RenderModel(projects));
        var project = drawing.Projects.Single(project => project.Name == "StandardIo.ArchitectureDiagram.SampleProject");
        var roots = project.Nodes.Where(node => node.TypeName.EndsWith("OrchestrationService")).OrderBy(node => node.X).ToArray();
        var bounds = roots.Select(root =>
        {
            var ids = new System.Collections.Generic.HashSet<string> { root.Id };
            var pending = new System.Collections.Generic.Queue<string>();
            pending.Enqueue(root.Id);
            while (pending.Count > 0)
            {
                string id = pending.Dequeue();
                foreach (var edge in project.Connections.Where(edge => edge.SourceId == id))
                {
                    if (project.Connections.Where(link => link.TargetId == edge.TargetId).Select(link => link.SourceId).Distinct().Count() != 1) continue;
                    if (ids.Add(edge.TargetId)) pending.Enqueue(edge.TargetId);
                }
            }
            var branch = project.Nodes.Where(node => ids.Contains(node.Id)).ToArray();
            return (Root: root, Left: branch.Min(node => node.X), Right: branch.Max(node => node.X + node.Width));
        }).ToArray();
        // Then: an entire branch, not just its individual nodes, owns a separate interval.
        for (int index = 1; index < bounds.Length; index++)
            Assert.True(bounds[index].Left >= bounds[index - 1].Right + 60 - 0.01,
                bounds[index - 1].Root.TypeName + " overlaps branch " + bounds[index].Root.TypeName);
        // A successful layout must remain stable under another complete rule pass.
        var positions = drawing.Projects.SelectMany(project => project.Nodes).ToDictionary(node => node.Id, node => node.X);
        TestServices.Get<StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Rendering.IProjectModelLayoutService>().Layout(drawing);
        foreach (var node in drawing.Projects.SelectMany(project => project.Nodes))
            Assert.InRange(Math.Abs(node.X - positions[node.Id]), 0, 0.1);

    }

    [Fact]
    public async Task ShouldRenderEventHubInItsDeclaringAssemblyContainer()
    {
        // Given: EventHub is a referenced package boundary, not a sample definition.
        var builder = TestServices.Get<ProjectModelBuilder>();
        var projects = new[] { await builder.BuildAsync(FindSampleProject()), await builder.BuildAsync(FindDataProject()) };
        // When
        var model = TestServices.Get<LayoutModelBuilder>().BuildRenderModel(new RenderModel(projects));
        // Then
        var owner = Assert.Single(model.Projects, project => project.Nodes.Any(node => node.TypeName == "cCoder.Eventing.IEventHub"));
        Assert.Equal("cCoder.Eventing (external)", owner.Name);
        var hub = Assert.Single(owner.Nodes, node => node.TypeName == "cCoder.Eventing.IEventHub");
        Assert.Equal(5, model.CrossProjectConnections.Count(edge => edge.TargetId == hub.Id));
    }

    private static string FindDataProject()
    {
        string directory = Path.GetDirectoryName(Path.GetDirectoryName(FindSampleProject()))!;
        const string name = "StandardIo.ArchitectureDiagram.SampleProject.Data";
        return Path.Combine(directory, name, name + ".csproj");
    }

    private static string FindSampleProject()
    {
        const string projectName = "StandardIo.ArchitectureDiagram.SampleProject";

        for (DirectoryInfo? directory = new DirectoryInfo(path: AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            string path = Path.Combine(path1: directory.FullName, path2: "v8", path3: projectName, path4: projectName + ".csproj");

            if (File.Exists(path: path))
            {
                return path;
            }
        }

        throw new FileNotFoundException(message: "Run the sample extraction tests from a checkout containing the v8 sample project.");
    }
}