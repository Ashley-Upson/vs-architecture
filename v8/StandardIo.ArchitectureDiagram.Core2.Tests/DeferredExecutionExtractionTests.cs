// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.Core2.Models;
using StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Dependencies;
using StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Types;
using Xunit;
namespace StandardIo.ArchitectureDiagram.Core2.Tests;
public sealed partial class ProjectModelPopulationTests
{
    [Fact]
    public async Task ShouldFindExecutionOfACallbackStoredInTheReceivingComponentAsync()
    {
        // Given
        var broker = new CompilationBroker(() => CreateCompilation("Example", """
            public class Handler { public void Handle() {} }
            public class Hub {
                private System.Action callback;
                public void Register(System.Action handler) { var saved = handler; callback = saved; }
                public void Raise() { callback(); }
            }
            public class Entry(Hub hub) { public void Wire() { hub.Register(() => new Handler().Handle()); } }
            """));
        var project = new ProjectModel { Name = "Example", Path = "Example.csproj" };
        // When
        await new ProjectDependenciesService(broker).PopulateDependenciesAsync(project, CancellationToken.None);
        // Then
        Assert.Equal("Hub", Assert.Single(project.Dependencies!, link => link.FromType == "Entry" && link.ToMethod == "Handle").ExecutorType);
    }

    [Fact]
    public async Task ShouldFollowGenericInterfaceRegistrationToTheExternalHubAsync()
    {
        // Given: the same generic interface forwarding shape as EventHandlerService.
        var library = CreateCompilation("Library", "public interface IHub { void Register<T, TService>(string name, System.Action<TService, T> handler); }");
        var broker = new CompilationBroker(() => CreateCompilation("Example", """
            public interface IForwarder { void Register<T, TService>(string name, System.Action<TService, T> handler); }
            public class Forwarder(IHub hub) : IForwarder, IHub {
                public void Register<T, TService>(string name, System.Action<TService, T> handler) => hub.Register(name, handler);
            }
            public interface IHandler { void Handle(int value); }
            public class Handler : IHandler { public void Handle(int value) {} }
            public class Entry(IForwarder forwarder) {
                public void Wire() { forwarder.Register(name: "event", handler: (IHandler service, int value) => service.Handle(value)); }
            }
            """).AddReferences(library.ToMetadataReference()));
        var project = new ProjectModel { Name = "Example", Path = "Example.csproj" };
        // When
        await new ProjectDependenciesService(broker).PopulateDependenciesAsync(project, CancellationToken.None);
        // Then
        Assert.Equal("IHub", Assert.Single(project.Dependencies!, link => link.FromType == "Entry" && link.ToMethod == "Handle").ExecutorType);
        Assert.Equal("Forwarder", Assert.Single(project.Dependencies!, link => link.FromType == "Entry" && link.ToMethod == "Handle").RegistrationType);
    }

    [Theory]
    [InlineData(true, "Hub")]
    [InlineData(false, "Forwarder")]
    public async Task ShouldFollowCallbackForwardingToExecutionOrExternalBoundaryAsync(bool external, string expected)
    {
        // Given
        var library = CreateCompilation("Library", "public class Hub { public void Register(System.Action<Handler> callback) {} } public class Handler { public void Handle() {} }");
        string forwarding = external ? "hub.Register(callback);" : "callback(new Handler());";
        var broker = new CompilationBroker(() => CreateCompilation("Example", """
            public class Entry {
                private readonly Forwarder forwarder;
                public Entry(Forwarder forwarder) { this.forwarder = forwarder; }
                public void Wire() { TryCatch(() => forwarder.Register(handler => handler.Handle())); }
                private void TryCatch(System.Action callback) { callback(); }
            }
            public class Forwarder {
                private readonly Hub hub;
                public Forwarder(Hub hub) { this.hub = hub; }
                public void Register(System.Action<Handler> callback) {
            """ + forwarding + "} }").AddReferences(library.ToMetadataReference()));
        var project = new ProjectModel { Name = "Example", Path = "Example.csproj" };
        // When
        await new ProjectTypesService(broker).PopulateTypesAsync(project, CancellationToken.None);
        await new ProjectDependenciesService(broker).PopulateDependenciesAsync(project, CancellationToken.None);
        // Then
        var call = Assert.Single(project.Dependencies!, link => link.FromType == "Entry" && link.ToMethod == "Handle");
        Assert.Equal(expected, call.ExecutorType);
        Assert.False(call.IsInjected);
        Assert.Null(Assert.Single(project.Dependencies!, link => link.FromType == "Entry" && link.ToMethod == "Register").ExecutorType);
        Assert.Contains(project.Types!, type => type.Name == expected);
    }

    [Fact]
    public async Task ShouldRetainCapturedConstructorDependenciesAndStopForwardingCyclesAsync()
    {
        // Given
        var library = CreateCompilation("Library", "public class Hub { public void Register(System.Action callback) {} } public class Handler { public void Handle() {} }");
        var broker = new CompilationBroker(() => CreateCompilation("Example", """
            public class Entry(Hub hub, Handler handler) {
                public void Wire() { hub.Register(() => handler.Handle()); }
                public void Unresolved() { Loop(() => handler.Handle()); }
                private void Loop(System.Action callback) { Loop(callback); }
            }
            """).AddReferences(library.ToMetadataReference()));
        var project = new ProjectModel { Name = "Example", Path = "Example.csproj" };
        // When
        await new ProjectDependenciesService(broker).PopulateDependenciesAsync(project, CancellationToken.None);
        // Then
        var wired = Assert.Single(project.Dependencies!, link => link.FromMethod == "Wire" && link.ToMethod == "Handle");
        Assert.Equal("Hub", wired.ExecutorType);
        Assert.True(wired.IsInjected);
        Assert.Null(Assert.Single(project.Dependencies!, link => link.FromMethod == "Unresolved" && link.ToMethod == "Handle").ExecutorType);
    }
}
