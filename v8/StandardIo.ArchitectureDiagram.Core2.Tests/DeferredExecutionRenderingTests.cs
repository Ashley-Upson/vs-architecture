// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System.Linq;
using StandardIo.ArchitectureDiagram.Core2.Exposures;
using StandardIo.ArchitectureDiagram.Core2.Models;
using Xunit;
namespace StandardIo.ArchitectureDiagram.Core2.Tests;
public sealed class DeferredExecutionRenderingTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ShouldWriteCommunicationCopiesAndDeferredLinksInBothFormats(bool html)
    {
        // Given
        var project = RenderConfigurationTests.Project("Example", "Entry", "Hub", "Handler");
        project.Dependencies = [RenderConfigurationTests.Link("Entry", "Hub"), Callback("Entry", "Handler")];
        var configuration = new RenderConfiguration { NoDuplicates = true };
        configuration.Architecture.DeferredExecutionSupport = true;
        IDiagramRenderer renderer = html ? TestServices.Get<HtmlDiagramRenderer>() : TestServices.Get<DrawIODiagramRenderer>();
        // When
        var document = System.Xml.Linq.XDocument.Parse(System.Text.Encoding.UTF8.GetString(renderer.Render(new RenderModel([project], configuration))));
        // Then
        if (html)
        {
            System.Xml.Linq.XNamespace svg = "http://www.w3.org/2000/svg";
            Assert.Equal(2, document.Descendants(svg + "g").Count(node => (string?)node.Attribute("data-type") == "Hub"));
            Assert.Single(document.Descendants(svg + "polyline"), edge => (string?)edge.Attribute("class") == "link deferred");
        }
        else
        {
            Assert.Equal(2, document.Descendants("mxCell").Count(node => (string?)node.Attribute("typeName") == "Hub"));
            Assert.Single(document.Descendants("mxCell"), edge => (string?)edge.Attribute("edge") == "1" && ((string?)edge.Attribute("style"))!.Contains("dashed=1"));
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ShouldShowSeparateCommunicationOccurrencesAtRegistrationAndEveryHandler(bool noDuplicates)
    {
        // Given
        var project = RenderConfigurationTests.Project("Example", "Entry", "Broker", "Hub", "FirstHandler", "SecondHandler");
        project.Types![2].IsInternal = false;
        project.Types[2].AssemblyName = "Eventing";
        project.Dependencies = [RenderConfigurationTests.Link("Entry", "Broker"), RenderConfigurationTests.Link("Broker", "Hub"),
            Callback("Entry", "FirstHandler"), Callback("Entry", "SecondHandler")];
        var configuration = new RenderConfiguration { NoDuplicates = noDuplicates };
        configuration.Architecture.DeferredExecutionSupport = true;
        // When
        var model = TestServices.Get<LayoutModelBuilder>().BuildRenderModel(new RenderModel([project], configuration));
        // Then
        var nodes = model.Projects.SelectMany(project => project.Nodes).ToArray();
        var hubs = nodes.Where(node => node.TypeName == "Hub").ToArray();
        Assert.Equal(3, hubs.Length);
        Assert.Equal(3, hubs.Select(node => node.Id).Distinct().Count());
        var edges = model.Projects.SelectMany(project => project.Connections).Concat(model.CrossProjectConnections).ToArray();
        Assert.DoesNotContain(edges, edge => edge.FromType == "Entry" && edge.ToType.EndsWith("Handler"));
        Assert.Single(edges, edge => edge.FromType == "Broker" && edge.ToType == "Hub");
        foreach (string handler in new[] { "FirstHandler", "SecondHandler" })
        {
            var edge = Assert.Single(edges, edge => edge.FromType == "Hub" && edge.ToType == handler);
            Assert.True(nodes.Single(node => node.Id == edge.SourceId).Y < nodes.Single(node => node.Id == edge.TargetId).Y);
        }
        Assert.Equal(4, project.Dependencies.Length);
    }

    [Fact]
    public void ShouldPreserveTheOriginalViewWhenDisabledAndRealInjectedUsageWhenEnabled()
    {
        // Given
        var project = RenderConfigurationTests.Project("Example", "Entry", "Hub", "Handler");
        var callback = Callback("Entry", "Handler");
        callback.IsInjected = true;
        project.Dependencies = [RenderConfigurationTests.Link("Entry", "Hub"), callback];
        // When
        foreach (bool enabled in new[] { false, true })
        {
            var configuration = new RenderConfiguration { NoDuplicates = true };
            configuration.Architecture.DeferredExecutionSupport = enabled;
            var model = TestServices.Get<LayoutModelBuilder>().BuildRenderModel(new RenderModel([project], configuration));
            // Then
            var edges = model.Projects.SelectMany(project => project.Connections).Concat(model.CrossProjectConnections).ToArray();
            Assert.Contains(edges, edge => edge.FromType == "Entry" && edge.ToType == "Handler");
            Assert.Equal(enabled, edges.Any(edge => edge.FromType == "Hub" && edge.ToType == "Handler"));
        }
    }

    private static TypeRelationship Callback(string source, string target) => new()
    {
        FromType = source, ToType = target, FromMethod = "Wire", ToMethod = "Handle",
        DependencyType = DependencyType.Consumed, ExecutorType = "Hub"
    };
}
