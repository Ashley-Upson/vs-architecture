using System;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using StandardIo.ArchitectureDiagram.Core2.Models;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core2.Tests;

public sealed class DrawIORendererTests
{
    [Fact]
    public void ShouldKeepTextReadableOnEveryFixedFillInADarkTheme()
    {
        // Given: dark-theme defaults use white text; all our node/header fills are pale.
        ProjectModel model = Model();
        model.Types = model.Types!.Concat(new[] { new DefinedType
        {
            Name = "IContract", FrameworkType = FrameworkType.Interface
        } }).ToArray();
        // When
        XElement[] vertices = Parse(new DrawIODiagramRenderer().Render(new[] { model }))
            .Descendants("mxCell").Where(cell => (string?)cell.Attribute("vertex") == "1").ToArray();
        // Then: colour pairs must provide strong contrast without inherited text defaults.
        Assert.Equal(4, vertices.Length);
        foreach (XElement vertex in vertices)
        {
            var style = ((string)vertex.Attribute("style")!).Split(';')
                .Where(part => part.Contains('='))
                .Select(part => part.Split('=', 2)).ToDictionary(pair => pair[0], pair => pair[1]);
            string font = style.TryGetValue("fontColor", out string? colour) ? colour : "#ffffff";
            double foreground = Luminance(font);
            double background = Luminance(style["fillColor"]);
            double contrast = (Math.Max(foreground, background) + 0.05) / (Math.Min(foreground, background) + 0.05);
            Assert.True(contrast >= 7, $"{vertex.Attribute("value")}: text contrast is only {contrast:F2}:1.");
            Assert.True(style.ContainsKey("fontColor"));
        }
    }

    private static double Luminance(string hex)
    {
        double Channel(int offset)
        {
            double value = Convert.ToInt32(hex.Substring(offset, 2), 16) / 255d;
            return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Channel(1) + 0.7152 * Channel(3) + 0.0722 * Channel(5);
    }

    [Fact]
    public void ShouldRenderEveryNodeAndLinkWithUniqueIdsWithoutChangingTheInput()
    {
        // Given
        ProjectModel model = Model();
        var renderer = new DrawIODiagramRenderer();
        string before = JsonSerializer.Serialize(model);
        // When
        byte[] bytes = renderer.Render(new[] { model, model });
        XDocument doc = Parse(bytes);
        XElement[] cells = doc.Descendants("mxCell").ToArray();
        // Then
        Assert.Equal("mxfile", doc.Root!.Name.LocalName);
        Assert.Single(doc.Descendants("diagram"));
        Assert.Equal(cells.Length, cells.Select(c => (string?)c.Attribute("id")).Distinct().Count());
        Assert.Equal(2, cells.Count(c => (string?)c.Attribute("value") == "Project & Test"));
        Assert.Equal(2, cells.Count(c => (string?)c.Attribute("value") == "Parent<T>"));
        Assert.Equal(2, cells.Count(c => (string?)c.Attribute("edge") == "1"));
        foreach (XElement edge in cells.Where(c => (string?)c.Attribute("edge") == "1"))
        {
            XElement source = cells.Single(c => (string?)c.Attribute("id") == (string?)edge.Attribute("source"));
            XElement target = cells.Single(c => (string?)c.Attribute("id") == (string?)edge.Attribute("target"));
            Assert.Equal((string?)source.Attribute("parent"), (string?)target.Attribute("parent"));
            Assert.Equal((string?)source.Attribute("parent"), (string?)edge.Attribute("parent"));
            Assert.Contains("exitX=0.5", (string)edge.Attribute("style")!);
            Assert.Contains("entryX=0.5", (string)edge.Attribute("style")!);
            Assert.Contains("strokeColor=#d1d5db;", (string)edge.Attribute("style")!);
            Assert.Contains("fontColor=#ffffff;", (string)edge.Attribute("style")!);
        }
        Assert.All(cells.Where(c => (string?)c.Attribute("edge") == "1"), c => Assert.Equal("", (string?)c.Attribute("value")));
        Assert.Equal(before, JsonSerializer.Serialize(model));
        Assert.Equal(bytes, renderer.Render(new[] { model, model }));
    }

    [Fact]
    public void ShouldCentreASingleChildAndKeepTreeBoxesApart()
    {
        // Given / When
        XElement[] cells = Parse(new DrawIODiagramRenderer().Render(new[] { Model(), Model() })).Descendants("mxCell").ToArray();
        XElement[] boxes = cells.Where(c => (string?)c.Attribute("value") == "Project & Test").ToArray();
        XElement[] nodes = cells.Where(c => (string?)c.Attribute("parent") == (string?)boxes[0].Attribute("id") && (string?)c.Attribute("vertex") == "1").ToArray();
        // Then
        Assert.Equal(Number(boxes[0], "y"), Number(boxes[1], "y"));
        Assert.True(Number(boxes[1], "x") >= Number(boxes[0], "x") + Number(boxes[0], "width"));
        Assert.Equal(2, nodes.Length);
        Assert.Equal(Number(nodes[0], "x") + Number(nodes[0], "width") / 2, Number(nodes[1], "x") + Number(nodes[1], "width") / 2);
        Assert.True(Number(nodes[1], "y") >= Number(nodes[0], "y") + Number(nodes[0], "height"));
    }

    [Fact]
    public void ShouldKeepCyclesInheritanceAndDisconnectedNodes()
    {
        // Given
        ProjectModel model = Model();
        model.Types = model.Types!.Concat(new[] { new DefinedType { Name = "Unused" } }).ToArray();
        model.Dependencies![0].DependencyType = DependencyType.Inheritance;
        model.Dependencies[1] = new Dependency { FromType = "Child", ToType = "Parent<T>" };
        // When
        XElement[] cells = Parse(new DrawIODiagramRenderer().Render(new[] { model })).Descendants("mxCell").ToArray();
        // Then
        Assert.Equal(2, cells.Count(c => (string?)c.Attribute("edge") == "1"));
        Assert.Contains(cells, c => (string?)c.Attribute("value") == "Unused");
        Assert.Contains(cells, c => ((string?)c.Attribute("style"))?.Contains("endArrow=block") == true);
    }

    [Fact]
    public void ShouldSupportEmptyInputAndRejectDanglingEndpoints()
    {
        // Given
        var renderer = new DrawIODiagramRenderer();
        ProjectModel model = Model();
        model.Dependencies![0].ToType = "Missing";
        // When / Then
        Assert.Equal(2, Parse(renderer.Render(Array.Empty<ProjectModel>())).Descendants("mxCell").Count());
        Assert.Throws<ArgumentException>(() => renderer.Render(new[] { model }));
        Assert.Throws<ArgumentNullException>(() => renderer.Render(null!));
    }

    [Fact]
    public void ShouldRenderMissingCollectionsAndUnlabelledOrInitializerCalls()
    {
        // Given
        var renderer = new DrawIODiagramRenderer();
        ProjectModel model = Model();
        model.Dependencies![0].FromMethod = null;
        model.Dependencies[1].FromMethod = null;
        model.Dependencies[1].ToMethod = null;
        // When
        XElement[] cells = Parse(renderer.Render(new[] { new ProjectModel(), model })).Descendants("mxCell").ToArray();
        // Then
        Assert.Contains(cells, c => (string?)c.Attribute("value") == "Project");
        Assert.Single(cells.Where(c => (string?)c.Attribute("edge") == "1"));
        Assert.Contains(cells, c => (string?)c.Attribute("edge") == "1" && (string?)c.Attribute("value") == "");
    }

    [Theory]
    [InlineData(null, "Child")]
    [InlineData("Missing", "Child")]
    [InlineData("Parent<T>", null)]
    public void ShouldRejectMissingSourceOrTargetNames(string? source, string? target)
    {
        // Given
        ProjectModel model = Model();
        model.Dependencies![0].FromType = source;
        model.Dependencies[0].ToType = target;
        // When / Then
        Assert.Throws<ArgumentException>(() => new DrawIODiagramRenderer().Render(new[] { model }));
    }

    [Fact]
    public void ShouldRejectMissingAndDuplicateTypeNames()
    {
        // Given
        var renderer = new DrawIODiagramRenderer();
        ProjectModel model = Model();
        model.Types![0].Name = " ";
        // When / Then
        Assert.Throws<ArgumentException>(() => renderer.Render(new[] { model }));
        model.Types[0].Name = "Child";
        Assert.Throws<ArgumentException>(() => renderer.Render(new[] { model }));
        Assert.Throws<ArgumentNullException>(() => renderer.Render(new ProjectModel[] { null! }));
    }

    [Fact]
    public void ShouldKeepBranchingNodesApartAndInsideTheirProjectBox()
    {
        // Given
        ProjectModel model = Model();
        model.Types = model.Types!.Concat(new[] { new DefinedType { Name = "OtherChild" } }).ToArray();
        model.Dependencies![1].ToType = "OtherChild";
        // When
        XElement[] cells = Parse(new DrawIODiagramRenderer().Render(new[] { model })).Descendants("mxCell").ToArray();
        XElement box = cells.Single(c => (string?)c.Attribute("id") == "tree-0");
        XElement[] nodes = cells.Where(c => (string?)c.Attribute("parent") == "tree-0" && (string?)c.Attribute("vertex") == "1").ToArray();
        // Then
        foreach (XElement node in nodes)
        {
            Assert.True(Number(node, "x") >= 0 && Number(node, "y") >= 30);
            Assert.True(Number(node, "x") + Number(node, "width") <= Number(box, "width"));
            Assert.True(Number(node, "y") + Number(node, "height") <= Number(box, "height"));
        }
        for (int i = 0; i < nodes.Length; i++)
            for (int j = i + 1; j < nodes.Length; j++)
                Assert.True(Number(nodes[i], "x") + Number(nodes[i], "width") <= Number(nodes[j], "x")
                    || Number(nodes[j], "x") + Number(nodes[j], "width") <= Number(nodes[i], "x")
                    || Number(nodes[i], "y") + Number(nodes[i], "height") <= Number(nodes[j], "y")
                    || Number(nodes[j], "y") + Number(nodes[j], "height") <= Number(nodes[i], "y"));
    }

    [Fact]
    public void ShouldUseShortLabelsFixedWidthsRoleColoursAndFilledLeftLabelledContainers()
    {
        // Given
        string[] names = { "Example.Exposures.SchoolManager", "Example.Services.Orchestrations.SchoolOrchestrationService",
            "Example.Services.Processings.SchoolProcessingService", "Example.Services.Foundations.SchoolService",
            "Example.Brokers.Storages.SchoolBroker" };
        var model = new ProjectModel { Name = "School", Types = names.Select(name => new DefinedType { Name = name, IsInternal = true }).ToArray() };
        // When
        XElement[] cells = Parse(new DrawIODiagramRenderer().Render(new[] { model })).Descendants("mxCell").ToArray();
        XElement box = cells.Single(cell => (string?)cell.Attribute("id") == "tree-0");
        XElement[] nodes = cells.Where(cell => (string?)cell.Attribute("parent") == "tree-0").ToArray();
        // Then
        Assert.Equal(names.Select(name => name.Split('.').Last()), nodes.Select(node => (string?)node.Attribute("value")));
        Assert.Equal(names, nodes.Select(node => (string?)node.Attribute("typeName")));
        Assert.All(nodes, node => Assert.Equal(180, Number(node, "width")));
        Assert.Equal(5, nodes.Select(node => ((string)node.Attribute("style")!).Split(';').Single(part => part.StartsWith("fillColor="))).Distinct().Count());
        string style = (string)box.Attribute("style")!;
        Assert.Contains("fillColor=#374151;", style);
        Assert.Contains("align=left;", style);
        Assert.Contains("verticalAlign=top;", style);
        Assert.DoesNotContain("swimlane;", style);
    }

    private static XDocument Parse(byte[] bytes) => XDocument.Parse(Encoding.UTF8.GetString(bytes));
    private static double Number(XElement cell, string name) => (double)cell.Element("mxGeometry")!.Attribute(name)!;
    private static ProjectModel Model() => new()
    {
        Name = "Project & Test", Path = "Example.csproj",
        Types = new[] { new DefinedType { Name = "Parent<T>", IsInternal = true }, new DefinedType { Name = "Child" } },
        Dependencies = new[] {
            new Dependency { FromType = "Parent<T>", ToType = "Child", FromMethod = "Run", ToMethod = "Save", DependencyType = DependencyType.Consumed },
            new Dependency { FromType = "Parent<T>", ToType = "Child", FromMethod = "Update", ToMethod = "Save", DependencyType = DependencyType.Consumed }}
    };
}
