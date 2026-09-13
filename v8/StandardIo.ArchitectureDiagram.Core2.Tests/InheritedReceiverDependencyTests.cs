// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.Core2.Models;
using StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Types;
using StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Dependencies;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core2.Tests;
public sealed partial class ProjectModelPopulationTests
{
    [Theory]
    [InlineData("Content(value, \"text/plain\");")]
    [InlineData("this.Content(value, \"text/plain\");")]
    public async Task ShouldNotExposeAnExternalAncestorForInheritedCallsWithDynamicArgumentsAsync(string call)
    {
        // Given: a local controller inherits through an external boundary, and supplies a dynamic argument.
        var library = CreateCompilation("Library", """
            public class ControllerBase { public void Content(object value, string contentType) {} }
            public class ODataController : ControllerBase { public void Configure() {} }
            """);
        var broker = new CompilationBroker(() => CreateCompilation("Example",
            "public class AppController : ODataController { public void Render(dynamic value) { " + call + " } }")
            .AddReferences(library.ToMetadataReference(), Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(
                typeof(System.Runtime.CompilerServices.DynamicAttribute).Assembly.Location)));
        var project = new ProjectModel { Name = "Example", Path = "Example.csproj" };
        // When
        await new ProjectTypesService(broker).PopulateTypesAsync(project, CancellationToken.None);
        await new ProjectDependenciesService(broker).PopulateDependenciesAsync(project, CancellationToken.None);
        // Then
        Assert.DoesNotContain(project.Types!, type => type.Name == "ControllerBase");
        Assert.DoesNotContain(project.Dependencies!, link => link.ToType == "ControllerBase");
        Assert.Equal("ODataController", Assert.Single(project.Types!, type => type.Name == "AppController").BaseTypeName);
        // The inherited call is local to the consumed controller, so it adds no type dependency.
        Assert.DoesNotContain(project.Dependencies!, link => link.DependencyType == DependencyType.Consumed);
    }

    [Theory]
    [InlineData("context.Save();", "CoreContext")]
    [InlineData("context?.Save();", "CoreContext")]
    [InlineData("((BaseContext)context).Save();", "BaseContext")]
    [InlineData("CoreContext.StaticSave();", "BaseContext")]
    public async Task ShouldAttributeInheritedCallsToTheConsumedReceiverAsync(string call, string expectedType)
    {
        // Given: a referenced context provides its own behaviour and inherits persistence methods.
        var library = CreateCompilation("Library", """
            public class BaseContext { public void Save() {} public static void StaticSave() {} }
            public class CoreContext : BaseContext { public void Configure() {} }
            """);
        var broker = new CompilationBroker(() => CreateCompilation("Example",
            "public class Broker { public void Store(CoreContext context) { " + call + " } }")
            .AddReferences(library.ToMetadataReference()));
        var project = new ProjectModel { Name = "Example", Path = "Example.csproj" };
        // When
        await new ProjectTypesService(broker).PopulateTypesAsync(project, CancellationToken.None);
        await new ProjectDependenciesService(broker).PopulateDependenciesAsync(project, CancellationToken.None);
        // Then
        var dependency = Assert.Single(project.Dependencies!, link => link.DependencyType == DependencyType.Consumed);
        Assert.Equal(expectedType, dependency.ToType);
        Assert.Equal("Store", dependency.FromMethod);
        Assert.Equal(call.Contains("StaticSave") ? "StaticSave" : "Save", dependency.ToMethod);
        Assert.Contains(project.Types!, type => type.Name == expectedType);
        if (expectedType == "CoreContext")
        {
            var context = Assert.Single(project.Types!, type => type.Name == "CoreContext");
            Assert.Equal("BaseContext", context.BaseTypeName);
            Assert.Equal("Configure", Assert.Single(context.Methods!).Name);
            Assert.DoesNotContain(project.Types!, type => type.Name == "BaseContext");
        }
    }
}
