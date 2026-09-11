using StandardIo.ArchitectureDiagram.Cli;
using StandardIo.ArchitectureDiagram.Core.Models;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Cli.Tests;

public sealed class ArchitectureGenerationOutputTests
{
    [Fact]
    public async Task Strict_failure_does_not_write_the_output_file()
    {
        using var fixture = new CliFixture();
        var result = await Program.Main(fixture.Arguments("--strict-validation"));

        Assert.Equal(1, result);
        Assert.False(File.Exists(fixture.OutputPath));
    }

    [Fact]
    public async Task Normal_mode_remains_eligible_and_writes_best_effort_output()
    {
        using var fixture = new CliFixture();
        var result = await Program.Main(fixture.Arguments());

        Assert.Equal(0, result);
        Assert.True(File.Exists(fixture.OutputPath));
    }

    private sealed class CliFixture : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "standard-io-v6-output-" + Guid.NewGuid().ToString("N"));

        public CliFixture()
        {
            Directory.CreateDirectory(directory);
            ManifestPath = Path.Combine(directory, "invalid-manifest.json");
            OutputPath = Path.Combine(directory, "architecture.drawio");
            File.WriteAllText(ManifestPath, DiagramModelSerializer.Export(new DiagramModel(
                new[]
                {
                    new ProjectContainer("project", "Project", new[]
                    {
                        new TypeNode("source", "project", "SourceService", "Project.SourceService", "Class", "source-guid")
                    }, "project-guid")
                },
                Array.Empty<ExternalDependencyNode>(),
                new[] { new DependencyEdge("missing-target", "source", "missing", "dependency") },
                new DiagramMetadata())));
        }

        public string ManifestPath { get; }
        public string OutputPath { get; }

        public string[] Arguments(string? validation = null) =>
            new[] { ManifestPath, "--diagram-types", "architecture", "--diagram-manifest", ManifestPath, "--output", OutputPath }
                .Concat(validation is null ? Array.Empty<string>() : new[] { validation })
                .ToArray();

        public void Dispose()
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}
