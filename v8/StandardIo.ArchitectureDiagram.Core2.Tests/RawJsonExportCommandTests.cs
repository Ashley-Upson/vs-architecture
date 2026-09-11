// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using StandardIo.ArchitectureDiagram.Core2.Exposures;
using StandardIo.ArchitectureDiagram.Core2.Models;
using StandardIo.ArchitectureDiagram.Core2.Services.Orchestrations.Generation;
using StandardIo.ArchitectureDiagram.Core2.Services.Processings.Commands;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core2.Tests;
public sealed partial class RawJsonExportCommandTests
{
    [Theory]
    [InlineData("Architecture")]
    [InlineData("CallChain")]
    [InlineData("DataModel")]
    public void ShouldParseJsonAsANonRenderingFormatWithADefaultOutput(string diagramType)
    {
        // Given
        ICommandParserProcessingService parser =
            TestServices.Get<ICommandParserProcessingService>();

        // When
        DiagramRenderRequest request = parser.Parse(
            command: new[] { diagramType, "Json", "one.csproj" });

        // Then
        Assert.Equal(expected: DiagramFormats.Json, actual: request.Format);
        Assert.Equal(
            expected: Path.GetFullPath(path: diagramType + ".json"),
            actual: request.OutputPath);
    }

    [Fact]
    public void ShouldPreserveExplicitJsonOutputPath()
    {
        // Given
        ICommandParserProcessingService parser =
            TestServices.Get<ICommandParserProcessingService>();

        // When
        DiagramRenderRequest request = parser.Parse(
            command: new[]
            {
                "Architecture",
                "Json",
                "one.csproj",
                "--output",
                "out/raw-model.json",
            });

        // Then
        Assert.Equal(
            expected: Path.GetFullPath(path: "out/raw-model.json"),
            actual: request.OutputPath);
    }

    [Fact]
    public async Task ShouldExportValidDeterministicJsonWithoutRenderingAsync()
    {
        // Given
        string folder = Path.Combine(
            path1: Path.GetTempPath(),
            path2: "architecture-json-" + Guid.NewGuid());

        Directory.CreateDirectory(path: folder);

        try
        {
            string projectPath = Path.Combine(
                path1: folder,
                path2: "Example.csproj");

            string sourcePath = Path.Combine(
                path1: folder,
                path2: "Example.cs");

            await File.WriteAllTextAsync(
                path: projectPath,
                contents: "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");

            await File.WriteAllTextAsync(
                path: sourcePath,
                contents: "namespace Example; public class Dependency { public void Go() {} } public class Worker { public string Name { get; set; } = string.Empty; public void Run(Dependency dependency) { dependency.Go(); Continue(); } public void Continue() {} }");

            var rendererFactory = new FailingRendererFactory();

            using ServiceProvider serviceProvider = new ServiceCollection()
                .AddArchitectureDiagram()
                .AddSingleton<IDiagramRendererFactory>(
                    implementationInstance: rendererFactory)
                .BuildServiceProvider();

            DiagramRenderCommand command = serviceProvider
                .GetRequiredService<DiagramRenderCommand>();

            string[] arguments =
            {
                "Architecture",
                "Json",
                projectPath,
            };

            // When
            DiagramRenderResult firstResult = await command.ExecuteAsync(
                command: arguments);

            DiagramRenderResult secondResult = await command.ExecuteAsync(
                command: arguments);

            // Then
            Assert.False(condition: rendererFactory.WasCalled);
            Assert.Equal(expected: firstResult.Content, actual: secondResult.Content);

            using JsonDocument document = JsonDocument.Parse(
                utf8Json: firstResult.Content);

            JsonElement root = document.RootElement;
            Assert.Equal(
                expected: 1,
                actual: root.GetProperty(propertyName: "schemaVersion").GetInt32());

            Assert.Equal(
                expected: "Architecture",
                actual: root.GetProperty(propertyName: "diagramType").GetString());

            JsonElement project = Assert.Single(
                collection: root.GetProperty(propertyName: "projects")
                    .EnumerateArray());

            Assert.Equal(
                expected: new[] { "Example.Dependency", "Example.Worker" },
                actual: project.GetProperty(propertyName: "types")
                    .EnumerateArray()
                    .Select(selector: type => type.GetProperty(propertyName: "name").GetString()));

            JsonElement[] edges = project.GetProperty(propertyName: "edges")
                .EnumerateArray()
                .ToArray();

            Assert.Contains(
                collection: edges,
                filter: edge =>
                    edge.GetProperty(propertyName: "fromType").GetString() == "Example.Worker" &&
                    edge.GetProperty(propertyName: "toType").GetString() == "Example.Dependency" &&
                    edge.GetProperty(propertyName: "kind").GetString() == "Consumed" &&
                    edge.GetProperty(propertyName: "evidence").GetProperty(propertyName: "fromMethod").GetString() == "Run" &&
                    edge.GetProperty(propertyName: "evidence").GetProperty(propertyName: "toMethod").GetString() == "Go" &&
                    edge.GetProperty(propertyName: "target").GetProperty(propertyName: "targetScope").GetString() == "Source");

            Assert.Contains(
                collection: edges,
                filter: edge =>
                    edge.GetProperty(propertyName: "fromType").GetString() == "Example.Worker" &&
                    edge.GetProperty(propertyName: "toType").GetString() == "Example.Worker" &&
                    edge.GetProperty(propertyName: "evidence").GetProperty(propertyName: "toMethod").GetString() == "Continue");

            Assert.Equal(
                expected: Encoding.UTF8.GetString(bytes: firstResult.Content),
                actual: Encoding.UTF8.GetString(bytes: secondResult.Content));
        }
        finally
        {
            Directory.Delete(path: folder, recursive: true);
        }
    }

    private sealed class FailingRendererFactory : IDiagramRendererFactory
    {
        public bool WasCalled { get; private set; }

        public IDiagramGenerationOrchestrationService CreateDiagramGenerationOrchestrationService(
            string namedKey)
        {
            this.WasCalled = true;

            throw new InvalidOperationException(
                message: "JSON export must not resolve a renderer.");
        }
    }
}
