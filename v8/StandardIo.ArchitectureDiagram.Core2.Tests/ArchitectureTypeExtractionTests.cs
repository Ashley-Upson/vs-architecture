// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.Core2.Models;
using StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Types;
using Xunit;
namespace StandardIo.ArchitectureDiagram.Core2.Tests;
public sealed partial class ProjectModelPopulationTests
{
    [Fact]
    public async Task ShouldRetainValueTypesAsDataEvenWhenTheyDeclareMethodsAsync()
    {
        // Given
        var broker = new CompilationBroker(() => CreateCompilation("Example", "public struct Value { public void Run() {} } public enum Choice { One }"));
        var project = new ProjectModel { Name = "Example", Path = "Example.csproj" };
        // When
        await new ProjectTypesService(broker).PopulateTypesAsync(project, CancellationToken.None);
        // Then
        var value = Assert.Single(project.Types!, type => type.Name == "Value");
        Assert.Equal(FrameworkType.Struct, value.FrameworkType);
        Assert.True(value.IsDataType);
        Assert.Equal("Run", Assert.Single(value.Methods!).Name);
        var choice = Assert.Single(project.Types!, type => type.Name == "Choice");
        Assert.Equal(FrameworkType.Enum, choice.FrameworkType);
        Assert.True(choice.IsDataType);
        var presentation = TestServices.Get<StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Rendering.IProjectModelPresentationService>().Prepare(project);
        Assert.Empty(presentation.Model.Types!);
        var drawing = TestServices.Get<StandardIo.ArchitectureDiagram.Core2.Exposures.LayoutModelBuilder>().BuildRenderModel(new RenderModel([project]));
        Assert.Empty(drawing.Projects);
    }

    [Fact]
    public async Task ShouldNotCountMethodsInheritedByAnExternalBoundaryAsync()
    {
        // Given
        var library = CreateCompilation("Library", "public class Base { public void Run() {} } public class Boundary : Base {}");
        var broker = new CompilationBroker(() => CreateCompilation("Example", "public class Entry : Boundary { public void Execute() {} }")
            .AddReferences(library.ToMetadataReference()));
        var project = new ProjectModel { Name = "Example", Path = "Example.csproj" };
        // When
        await new ProjectTypesService(broker).PopulateTypesAsync(project, CancellationToken.None);
        // Then
        var boundary = Assert.Single(project.Types!, type => type.Name == "Boundary");
        Assert.False(boundary.IsInternal);
        Assert.Empty(boundary.Methods!);
        Assert.Equal("Base", boundary.BaseTypeName);
        Assert.DoesNotContain(project.Types!, type => type.Name == "Base");
    }

    [Fact]
    public async Task ShouldClassifyStandardDataWithoutHidingCustomEnumerableBehaviourAsync()
    {
        // Given
        var broker = new CompilationBroker(() => CreateCompilation("Example", """
            using System.Collections.Generic;
            public class Behaviour : List<int> { public void Execute() { string.IsNullOrEmpty(null); System.Array.Empty<int>(); } }
            public class InheritedOnly : Behaviour { }
            """));
        var project = new ProjectModel { Name = "Example", Path = "Example.csproj" };
        // When
        await new ProjectTypesService(broker).PopulateTypesAsync(project, CancellationToken.None);
        // Then
        var behaviour = Assert.Single(project.Types!, type => type.Name == "Behaviour");
        Assert.False(behaviour.IsDataType);
        Assert.Equal("System.Collections.Generic.List<System.Int32>", behaviour.BaseTypeName);
        Assert.Equal("Execute", Assert.Single(behaviour.Methods!).Name);
        Assert.Empty(Assert.Single(project.Types!, type => type.Name == "InheritedOnly").Methods!);
        Assert.True(Assert.Single(project.Types!, type => type.Name == "System.String").IsDataType);
        Assert.True(Assert.Single(project.Types!, type => type.Name == "System.Array").IsDataType);
        Assert.True(Assert.Single(project.Types!, type => type.Name!.StartsWith("System.Collections.Generic.List")).IsDataType);
    }
}
