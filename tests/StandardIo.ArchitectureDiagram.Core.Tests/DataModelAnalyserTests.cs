using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using StandardIo.ArchitectureDiagram.Core.Models;
using StandardIo.ArchitectureDiagram.Core.Models.DataModels;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.DataModels;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core.Tests;

public sealed class DataModelAnalyserTests
{
    [Fact]
    public async Task Analyse_resolves_forward_property_references_in_two_passes()
    {
        using var workspace = Project("""
            namespace Fixture;
            public class A { public B Target { get; init; } = new(); }
            public class B { public string Name { get; set; } = ""; }
            """);

        var result = await new RoslynDataModelAnalyser().AnalyseAsync(
            workspace.CurrentSolution.Projects, new DataModelAnalysisSettings());

        var relationship = Assert.Single(result.Relationships);
        Assert.Equal(result.Entities.Single(entity => entity.Name == "A").Id, relationship.SourceEntityId);
        Assert.Equal(result.Entities.Single(entity => entity.Name == "B").Id, relationship.TargetEntityId);
    }

    [Fact]
    public async Task Analyse_retains_collection_nullability_and_property_accessors()
    {
        using var workspace = Project("""
            #nullable enable
            using System.Collections.Generic;
            namespace Fixture;
            public class Parent { public IReadOnlyList<Child?> Children { get; init; } = []; }
            public class Child { public string? Name { get; private set; } }
            """);

        var result = await new RoslynDataModelAnalyser().AnalyseAsync(
            workspace.CurrentSolution.Projects, new DataModelAnalysisSettings());

        var property = result.Entities.Single(entity => entity.Name == "Parent").Properties.Single();
        Assert.True(property.IsCollection);
        Assert.True(property.HasInit);
        Assert.NotNull(property.CollectionElementTypeId);
        Assert.Equal(DataModelRelationshipKind.CollectionPropertyReference, Assert.Single(result.Relationships).Kind);
    }

    [Fact]
    public async Task Analyse_is_deterministic_when_project_and_declaration_order_are_reversed()
    {
        using var forward = Project("""
            namespace Fixture;
            public class A { public B Target { get; set; } = new(); }
            public class B { public string Name { get; set; } = ""; }
            """);
        using var reversed = Project("""
            namespace Fixture;
            public class B { public string Name { get; set; } = ""; }
            public class A { public B Target { get; set; } = new(); }
            """);
        var analyser = new RoslynDataModelAnalyser();

        var first = await analyser.AnalyseAsync(forward.CurrentSolution.Projects, new DataModelAnalysisSettings());
        var second = await analyser.AnalyseAsync(reversed.CurrentSolution.Projects.Reverse(), new DataModelAnalysisSettings());

        Assert.Equal(Signature(first), Signature(second));
    }

    [Fact]
    public async Task Analyse_owns_the_property_only_selection_policy()
    {
        using var workspace = Project("""
            namespace Fixture;
            public interface IContract { string Name { get; } }
            public class Entity { public string Name { get; set; } = ""; }
            public class Behaviour { public string Name { get; set; } = ""; public void Run() {} }
            public class Empty { }
            """);

        var result = await new RoslynDataModelAnalyser().AnalyseAsync(
            workspace.CurrentSolution.Projects, new DataModelAnalysisSettings());

        Assert.Equal(new[] { "Entity" }, result.Entities.Select(entity => entity.Name));
        Assert.All(result.Entities, entity => Assert.NotEmpty(entity.SelectionReason));
    }

    private static string[] Signature(DataModelDiagram diagram) =>
        diagram.Entities.Select(entity => $"E|{entity.Id}|{entity.FullName}")
            .Concat(diagram.Entities.SelectMany(entity => entity.Properties.Select(property =>
                $"P|{property.Id}|{property.Name}|{property.TargetTypeId}|{property.IsCollection}|{property.IsNullable}")))
            .Concat(diagram.Relationships.Select(relationship =>
                $"R|{relationship.Id}|{relationship.SourceEntityId}|{relationship.TargetEntityId}|{relationship.Kind}"))
            .ToArray();

    private static AdhocWorkspace Project(string source)
    {
        var workspace = new AdhocWorkspace();
        var projectId = ProjectId.CreateNewId();
        var solution = workspace.CurrentSolution.AddProject(ProjectInfo.Create(
                projectId, VersionStamp.Create(), "Fixture", "Fixture", LanguageNames.CSharp,
                parseOptions: CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview),
                metadataReferences: References()))
            .AddDocument(DocumentId.CreateNewId(projectId), "Models.cs", SourceText.From(source));
        Assert.True(workspace.TryApplyChanges(solution));
        return workspace;
    }

    private static MetadataReference[] References() =>
        ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path)).ToArray();
}
