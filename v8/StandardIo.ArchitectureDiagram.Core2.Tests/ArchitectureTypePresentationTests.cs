// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System.Linq;
using StandardIo.ArchitectureDiagram.Core2.Exposures;
using StandardIo.ArchitectureDiagram.Core2.Models;
using StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Rendering;
using Xunit;
namespace StandardIo.ArchitectureDiagram.Core2.Tests;
public sealed class ArchitectureTypePresentationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ShouldStyleTheTypeContractAndBaseLabelsInBothFormats(bool html)
    {
        // Given
        var project = RenderConfigurationTests.Project("Example", "Example.Service<T>");
        project.Types![0].InterfaceNames = ["Example.IContract"];
        project.Types[0].BaseTypeName = "Example.Base";
        IDiagramRenderer renderer = html ? TestServices.Get<HtmlDiagramRenderer>() : TestServices.Get<DrawIODiagramRenderer>();
        // When
        var document = System.Xml.Linq.XDocument.Parse(System.Text.Encoding.UTF8.GetString(renderer.Render(new RenderModel([project]))));
        // Then
        if (html)
        {
            System.Xml.Linq.XNamespace svg = "http://www.w3.org/2000/svg";
            var lines = document.Descendants(svg + "tspan").ToArray();
            Assert.Equal(new[] { "Service<T>", "IContract", "Base" }, lines.Select(line => line.Value));
            Assert.Equal("bold", (string?)lines[0].Attribute("font-weight"));
            Assert.Equal("normal", (string?)lines[1].Attribute("font-weight"));
            Assert.Equal("10", (string?)lines[2].Attribute("font-size"));
        }
        else
        {
            var node = Assert.Single(document.Descendants("mxCell"), cell => cell.Attribute("typeName") is not null);
            Assert.Contains("html=1", (string)node.Attribute("style")!);
            var label = System.Xml.Linq.XElement.Parse("<label>" + ((string)node.Attribute("value")!).Replace("<br>", "<br/>") + "</label>");
            var lines = label.Elements("span").ToArray();
            Assert.Equal(new[] { "Service<T>", "IContract", "Base" }, lines.Select(line => line.Value));
            Assert.Contains("font-weight:bold", (string)lines[0].Attribute("style")!);
            Assert.Contains("font-weight:normal", (string)lines[1].Attribute("style")!);
            Assert.Contains("font-size:10px", (string)lines[2].Attribute("style")!);
        }
    }

    [Fact]
    public void ShouldHideDataAndInheritedOnlyTypesAndDescribeInheritanceInLabels()
    {
        // Given
        var project = RenderConfigurationTests.Project("Example", "Service", "Base", "IContract", "System.String", "Values", "InheritedOnly");
        project.Types![0].BaseTypeName = "Base";
        project.Types[2].FrameworkType = FrameworkType.Interface;
        project.Types[3].IsDataType = true;
        project.Types[4].IsDataType = true;
        project.Types[5].IsInternal = false;
        project.Types[5].Methods = [];
        project.Dependencies = [new() { FromType = "Service", ToType = "Base", DependencyType = DependencyType.Inheritance },
            new() { FromType = "Service", ToType = "IContract", DependencyType = DependencyType.Inheritance },
            RenderConfigurationTests.Link("Service", "System.String"), RenderConfigurationTests.Link("Service", "Values"),
            RenderConfigurationTests.Link("Service", "InheritedOnly")];
        // When
        var result = TestServices.Get<IProjectModelPresentationService>().Prepare(project);
        // Then
        Assert.Equal(new[] { "Service", "Base" }, result.Model.Types!.Select(type => type.Name));
        Assert.Empty(result.Model.Dependencies!);
        Assert.Equal("Service\nIContract\nBase", result.Labels["Service"]);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void ShouldInlineExternalNodesOnlyInTheSplitView(bool noDuplicates, bool internalChild)
    {
        // Given
        var project = RenderConfigurationTests.Project("Example", "Root", "Child", "External.Service");
        project.Types![2].IsInternal = false;
        project.Types[2].AssemblyName = "External";
        project.Dependencies = internalChild
            ? [RenderConfigurationTests.Link("Root", "Child"), RenderConfigurationTests.Link("Root", "External.Service")]
            : [RenderConfigurationTests.Link("Root", "External.Service")];
        var config = new RenderConfiguration { NoDuplicates = noDuplicates };
        config.Architecture.InlineExternals = true;
        // When
        var result = TestServices.Get<LayoutModelBuilder>().BuildRenderModel(new RenderModel([project], config));
        // Then
        Assert.Equal(noDuplicates ? 2 : 1, result.Projects.Length);
        var edge = result.Projects.SelectMany(box => box.Connections).Concat(result.CrossProjectConnections).Single(edge => edge.ToType == "External.Service");
        Assert.Equal(noDuplicates || !internalChild ? "#d1d5db" : "#ef4444", edge.Stroke);
        var root = result.Projects.SelectMany(box => box.Nodes).Single(node => node.TypeName == "Root");
        Assert.True(root.TextLines[0].Bold);
    }
}
