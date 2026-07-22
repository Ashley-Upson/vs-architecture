using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using StandardIo.ArchitectureDiagram.Core.Models;
using StandardIo.ArchitectureDiagram.Core.Models.Generation;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.Analyses;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.Renderers;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.Settings;
using StandardIo.ArchitectureDiagram.Core.Services.Orchestrations.Diagrams;
using StandardIo.ArchitectureDiagram.Core.Services.Processings.Architectures;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core.Tests;

public sealed class FullPipelineRepeatTests
{
    [Fact]
    public async Task Independent_full_pipeline_runs_are_stage_deterministic()
    {
        var first = await Run();
        var second = await Run();

        Assert.Equal(first.SemanticGraph, second.SemanticGraph);
        Assert.Equal(first.ProjectedTopology, second.ProjectedTopology);
        Assert.Equal(first.PlacedNodeGeometry, second.PlacedNodeGeometry);
        Assert.Equal(first.LogicalRoutes, second.LogicalRoutes);
        Assert.Equal(first.PhysicalOwnershipGeometry, second.PhysicalOwnershipGeometry);
        Assert.Equal(first.DrawioBytes, second.DrawioBytes);
    }

    private static async Task<StageHashes> Run()
    {
        using var workspace = new AdhocWorkspace();
        var projectId = ProjectId.CreateNewId();
        var solution = workspace.CurrentSolution.AddProject(ProjectInfo.Create(
                projectId, VersionStamp.Create(), "RepeatFixture", "RepeatFixture", LanguageNames.CSharp,
                metadataReferences:
                [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
                compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)))
            .AddDocument(DocumentId.CreateNewId(projectId), "Fixture.cs", SourceText.From("""
                namespace RepeatFixture;
                public interface IService { }
                public class Service : IService { }
                public class Root { public Root(IService service) { } }
                public class Services { public void AddScoped<TService,TImplementation>() { } }
                public class Startup { public void Configure(Services services) { services.AddScoped<IService,Service>(); } }
                """));
        var analyser = new RoslynDependencyAnalyzer();
        var settings = DiagramSettings.CreateDefault();
        settings.NodeDuplication.AllowDuplicateNodes = false;
        var job = new ArchitectureGenerationJob(
            new ArchitectureAnalysisSettings(),
            LegacyDiagramSettingsAdapter.ToArchitectureRendering(settings));
        var generation = await new ArchitectureGenerationService(
            analyser, new ArchitectureTopologyProjector(), new DrawioArchitectureRenderer(), new DrawioDocumentComposer())
            .GenerateAsync(solution.Projects, job);
        var semantic = generation.Diagram.Projects.OrderBy(project => project.Id).Select(project => new
        {
            project.Id,
            project.Name,
            Nodes = project.Nodes.OrderBy(node => node.Id).Select(node => new
            {
                node.Id, node.ProjectId, node.Name, node.FullName, node.Kind,
                node.SemanticTypeIdentity, node.InterfaceIdentity, node.ImplementationIdentity,
                node.ImplementationCount, node.InterfaceResolution
            })
        });
        var root = generation.Page.GraphModel.Element("root")!;
        var nodeGeometry = root.Elements("mxCell").Where(cell => (string?)cell.Attribute("vertex") == "1")
            .OrderBy(cell => (string?)cell.Attribute("id")).Select(cell => new
            {
                Id = (string?)cell.Attribute("id"), Parent = (string?)cell.Attribute("parent"),
                Geometry = cell.Element("mxGeometry")?.ToString(SaveOptions.DisableFormatting)
            });
        var physical = root.Elements("mxCell").Where(cell => cell.Attribute("logicalEdgeId") is not null)
            .OrderBy(cell => (string?)cell.Attribute("id")).Select(cell => new
            {
                Id = (string?)cell.Attribute("id"), Logical = (string?)cell.Attribute("logicalEdgeId"),
                Parent = (string?)cell.Attribute("parent"), Segment = (string?)cell.Attribute("segmentIndex"),
                Geometry = cell.Element("mxGeometry")?.ToString(SaveOptions.DisableFormatting)
            });
        var document = new DrawioDocumentComposer().Compose([generation.Page], new()).Content;
        return new StageHashes(
            Hash(JsonSerializer.Serialize(new { Projects = semantic, generation.Diagram.Links })),
            Hash(JsonSerializer.Serialize(generation.ProjectedGraph)),
            Hash(JsonSerializer.Serialize(nodeGeometry)),
            Hash(JsonSerializer.Serialize(generation.Routes)),
            Hash(JsonSerializer.Serialize(physical)),
            Hash(document));
    }

    private static string Hash(string value)
    {
        using var sha = SHA256.Create();
        return string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(value)).Select(item => item.ToString("x2")));
    }

    private sealed record StageHashes(
        string SemanticGraph,
        string ProjectedTopology,
        string PlacedNodeGeometry,
        string LogicalRoutes,
        string PhysicalOwnershipGeometry,
        string DrawioBytes);
}
