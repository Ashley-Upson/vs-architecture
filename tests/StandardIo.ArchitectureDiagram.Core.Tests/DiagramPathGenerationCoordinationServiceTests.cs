using System.Xml.Linq;
using StandardIo.ArchitectureDiagram.Core.Brokers.Workspaces;
using StandardIo.ArchitectureDiagram.Core.Models;
using StandardIo.ArchitectureDiagram.Core.Services.Processings.Diagrams;
using StandardIo.ArchitectureDiagram.Core.Services.Orchestrations.Diagrams;
using StandardIo.ArchitectureDiagram.Core.Services.Coordinations.Diagrams;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core.Tests;

public sealed class DiagramPathGenerationCoordinationServiceTests
{
    [Fact]
    public async Task Generate_accepts_csproj_path()
    {
        using var fixture = PathFixture.Create();
        var projectPath = fixture.AddProject("Api", ("Controller.cs", """
            namespace Api
            {
                public class Service {}
                public class Controller
                {
                    public Controller(Service service) {}
                }
            }
            """));

        var result = await new DiagramPathGenerationCoordinationService()
            .GenerateAsync(projectPath, DiagramSettings.CreateDefault());
        var document = XDocument.Parse(result.Content);

        Assert.Equal("drawio", result.RendererId);
        Assert.Contains(document.Descendants("mxCell"), cell => (string?)cell.Attribute("value") == "Controller");
        Assert.Contains(document.Descendants("mxCell"), cell => (string?)cell.Attribute("value") == "Service");
    }

    [Fact]
    public async Task Generate_accepts_solution_path_and_keeps_selected_order()
    {
        using var fixture = PathFixture.Create();
        var api = fixture.AddProject("Api", ("Controller.cs", "namespace Api { public class Controller {} }"));
        var worker = fixture.AddProject("Worker", ("Job.cs", "namespace Worker { public class Job {} }"));
        var solution = fixture.AddSolution("Sample", api, worker);

        var target = await new WorkspacePathBroker().LoadAsync(solution, new WorkspacePathLoadOptions());

        Assert.Equal(new[] { "Api", "Worker" }, target.Projects.Select(project => project.Name).ToArray());
    }

    [Fact]
    public async Task Generate_accepts_folder_path_and_json_renderer_override()
    {
        using var fixture = PathFixture.Create();
        fixture.AddProject("Api", ("Controller.cs", "namespace Api { public class Controller {} }"));
        var settings = DiagramSettings.CreateDefault();
        settings.OutputRenderer = "json";

        var result = await new DiagramPathGenerationCoordinationService()
            .GenerateAsync(fixture.RootPath, settings);
        var diagram = DiagramModelSerializer.Import(result.Content);

        Assert.Equal("json", result.RendererId);
        Assert.Contains(diagram.Projects, project => project.Name == "Api");
    }

    [Fact]
    public async Task Generate_applies_project_filter()
    {
        using var fixture = PathFixture.Create();
        fixture.AddProject("Api", ("Controller.cs", "namespace Api { public class Controller {} }"));
        fixture.AddProject("Worker", ("Job.cs", "namespace Worker { public class Job {} }"));

        var target = await new WorkspacePathBroker()
            .LoadAsync(fixture.RootPath, new WorkspacePathLoadOptions { ProjectFilter = "Worker" });

        var project = Assert.Single(target.Projects);
        Assert.Equal("Worker", project.Name);
    }

    [Fact]
    public async Task Generate_rejects_invalid_path()
    {
        await Assert.ThrowsAsync<DirectoryNotFoundException>(() =>
            new DiagramPathGenerationCoordinationService()
                .GenerateAsync(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")), DiagramSettings.CreateDefault()));
    }

    private sealed class PathFixture : IDisposable
    {
        private PathFixture(string rootPath)
        {
            RootPath = rootPath;
        }

        public string RootPath { get; }

        public static PathFixture Create()
        {
            var root = Path.Combine(Path.GetTempPath(), "standard-io-diagram-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return new PathFixture(root);
        }

        public string AddProject(string name, params (string FileName, string Source)[] documents)
        {
            var projectDirectory = Path.Combine(RootPath, name);
            Directory.CreateDirectory(projectDirectory);
            var projectPath = Path.Combine(projectDirectory, $"{name}.csproj");
            File.WriteAllText(projectPath, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                    <ImplicitUsings>disable</ImplicitUsings>
                    <Nullable>enable</Nullable>
                  </PropertyGroup>
                </Project>
                """);

            foreach (var document in documents)
            {
                File.WriteAllText(Path.Combine(projectDirectory, document.FileName), document.Source);
            }

            return projectPath;
        }

        public string AddSolution(string name, params string[] projectPaths)
        {
            var solutionPath = Path.Combine(RootPath, $"{name}.sln");
            var lines = new List<string>
            {
                "Microsoft Visual Studio Solution File, Format Version 12.00",
                "# Visual Studio Version 17"
            };

            foreach (var projectPath in projectPaths)
            {
                var projectName = Path.GetFileNameWithoutExtension(projectPath);
                var relativePath = Path.GetRelativePath(RootPath, projectPath);
                lines.Add($"Project(\"{{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}}\") = \"{projectName}\", \"{relativePath}\", \"{{{Guid.NewGuid():D}}}\"");
                lines.Add("EndProject");
            }

            lines.Add("Global");
            lines.Add("EndGlobal");
            File.WriteAllLines(solutionPath, lines);
            return solutionPath;
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(RootPath, recursive: true);
            }
            catch
            {
            }
        }
    }
}
