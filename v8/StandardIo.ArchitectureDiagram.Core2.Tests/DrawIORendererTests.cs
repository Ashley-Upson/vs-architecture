// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using StandardIo.ArchitectureDiagram.Core2.Models;
using Xunit;
using StandardIo.ArchitectureDiagram.Core2.Exposures;

namespace StandardIo.ArchitectureDiagram.Core2.Tests;
public sealed partial class DrawIORendererTests
{
    [Fact]
    public void ShouldKeepTextReadableOnEveryFixedFillInADarkTheme()
    {
        // Given: dark-theme defaults use white text; all our node/header fills are pale.
        ProjectModel model = Model();

        model.Types = model.Types!.Concat(second: new[] { new DefinedType { Name = "IContract", FrameworkType = FrameworkType.Interface } })
            .ToArray();
        // When

        XElement[] vertices = Parse(bytes: TestServices.Get<DrawIODiagramRenderer>().Render(new RenderModel(new[] { model })))
            .Descendants(name: "mxCell")
            .Where(predicate: cell => (string? )cell.Attribute(name: "vertex") == "1")
            .ToArray();
        // Then: colour pairs must provide strong contrast without inherited text defaults.

        Assert.Equal(expected: 4, actual: vertices.Length);

        foreach (XElement vertex in vertices)
        {
            var style = ((string)vertex.Attribute(name: "style")!).Split(separator: ';')
                .Where(predicate: part => part.Contains(value: '='))
                .Select(selector: part => part.Split(separator: '=', count: 2))
                .ToDictionary(keySelector: pair => pair[0], elementSelector: pair => pair[1]);

            if (vertex.Attribute(name: "typeName") is not null)
            {
                Assert.NotEqual(expected: "#374151", actual: style["fillColor"]);
            }

            string font = style.TryGetValue(key: "fontColor", value: out string? colour) ? colour : "#ffffff";
            double foreground = Luminance(hex: font);
            double background = Luminance(hex: style["fillColor"]);
            double contrast = (Math.Max(val1: foreground, val2: background) + 0.05) / (Math.Min(val1: foreground, val2: background) + 0.05);
            Assert.True(condition: contrast >= 7, userMessage: $"{vertex.Attribute(name: "value")}: text contrast is only {contrast:F2}:1.");
            Assert.True(condition: style.ContainsKey(key: "fontColor"));
        }
    }

    private static double Luminance(string hex)
    {
        double Channel(int offset)
        {
            double value = Convert.ToInt32(value: hex.Substring(startIndex: offset, length: 2), fromBase: 16) / 255d;
            return value <= 0.04045 ? value / 12.92 : Math.Pow(x: (value + 0.055) / 1.055, y: 2.4);
        }

        return 0.2126 * Channel(offset: 1) + 0.7152 * Channel(offset: 3) + 0.0722 * Channel(offset: 5);
    }

    [Fact]
    public void ShouldRenderEveryNodeAndLinkWithUniqueIdsWithoutChangingTheInput()
    {
        // Given
        ProjectModel model = Model();
        var renderer = TestServices.Get<DrawIODiagramRenderer>();
        string before = JsonSerializer.Serialize(value: model);
        // When
        byte[] bytes = renderer.Render(new RenderModel(new[] { model, model }));
        XDocument doc = Parse(bytes: bytes);

        XElement[] cells = doc.Descendants(name: "mxCell")
            .ToArray();
        // Then

        Assert.Equal(expected: "mxfile", actual: doc.Root!.Name.LocalName);
        Assert.Single(collection: doc.Descendants(name: "diagram"));

        Assert.Equal(expected: cells.Length, actual: cells.Select(selector: c => (string? )c.Attribute(name: "id"))
            .Distinct()
            .Count());

        Assert.Equal(expected: 2, actual: cells.Count(predicate: c => (string? )c.Attribute(name: "value") == "Project & Test"));
        Assert.Equal(expected: 2, actual: cells.Count(predicate: c => (string? )c.Attribute(name: "value") == "Parent<T>"));
        Assert.Equal(expected: 2, actual: cells.Count(predicate: c => (string? )c.Attribute(name: "edge") == "1"));

        foreach (XElement edge in cells.Where(predicate: c => (string? )c.Attribute(name: "edge") == "1"))
        {
            XElement source = cells.Single(predicate: c => (string? )c.Attribute(name: "id") == (string? )edge.Attribute(name: "source"));
            XElement target = cells.Single(predicate: c => (string? )c.Attribute(name: "id") == (string? )edge.Attribute(name: "target"));
            Assert.Equal(expected: (string? )source.Attribute(name: "parent"), actual: (string? )target.Attribute(name: "parent"));
            Assert.Equal(expected: (string? )source.Attribute(name: "parent"), actual: (string? )edge.Attribute(name: "parent"));
            Assert.Contains(expectedSubstring: "exitX=0.5", actualString: (string)edge.Attribute(name: "style")!);
            Assert.Contains(expectedSubstring: "entryX=0.5", actualString: (string)edge.Attribute(name: "style")!);
            Assert.Contains(expectedSubstring: "strokeColor=#d1d5db;", actualString: (string)edge.Attribute(name: "style")!);
            Assert.Contains(expectedSubstring: "fontColor=#ffffff;", actualString: (string)edge.Attribute(name: "style")!);
        }

        Assert.All(collection: cells.Where(predicate: c => (string? )c.Attribute(name: "edge") == "1"), action: c => Assert.Equal(expected: "", actual: (string? )c.Attribute(name: "value")));
        Assert.Equal(expected: before, actual: JsonSerializer.Serialize(value: model));
        Assert.Equal(expected: bytes, actual: renderer.Render(new RenderModel(new[] { model, model })));
    }

    [Fact]
    public void ShouldCentreASingleChildAndKeepTreeBoxesApart()
    {
        // Given / When
        // When: exercise the operation under test.
        XElement[] cells = Parse(bytes: TestServices.Get<DrawIODiagramRenderer>().Render(new RenderModel(new[] { Model(), Model() })))
            .Descendants(name: "mxCell")
            .ToArray();

        XElement[] boxes = cells.Where(predicate: c => (string? )c.Attribute(name: "value") == "Project & Test")
            .ToArray();

        XElement[] nodes = cells.Where(predicate: c => (string? )c.Attribute(name: "parent") == (string? )boxes[0].Attribute(name: "id") && (string? )c.Attribute(name: "vertex") == "1")
            .ToArray();
        // Then

        Assert.Equal(expected: Number(cell: boxes[0], name: "y"), actual: Number(cell: boxes[1], name: "y"));
        Assert.True(condition: Number(cell: boxes[1], name: "x") >= Number(cell: boxes[0], name: "x") + Number(cell: boxes[0], name: "width"));
        Assert.Equal(expected: 2, actual: nodes.Length);
        Assert.Equal(expected: Number(cell: nodes[0], name: "x") + Number(cell: nodes[0], name: "width") / 2, actual: Number(cell: nodes[1], name: "x") + Number(cell: nodes[1], name: "width") / 2);
        Assert.True(condition: Number(cell: nodes[1], name: "y") >= Number(cell: nodes[0], name: "y") + Number(cell: nodes[0], name: "height"));
    }

    [Fact]
    public void ShouldKeepConsumedCyclesAndDescribeInheritanceInLabels()
    {
        // Given
        ProjectModel model = Model();

        model.Types = model.Types!.Concat(second: new[] { new DefinedType { Name = "Unused" } })
            .ToArray();

        model.Dependencies![0].DependencyType = DependencyType.Inheritance;

        model.Dependencies[1] = new TypeRelationship
        {
            FromType = "Child",
            ToType = "Parent<T>", DependencyType = DependencyType.Consumed
        };
        model.Dependencies = model.Dependencies.Append(new TypeRelationship { FromType = "Parent<T>", ToType = "Child", DependencyType = DependencyType.Consumed }).ToArray();
        // When

        XElement[] cells = Parse(bytes: TestServices.Get<DrawIODiagramRenderer>().Render(new RenderModel(new[] { model })))
            .Descendants(name: "mxCell")
            .ToArray();
        // Then

        Assert.Equal(expected: 2, actual: cells.Count(predicate: c => (string? )c.Attribute(name: "edge") == "1"));
        Assert.Contains(collection: cells, filter: c => (string? )c.Attribute(name: "value") == "Unused");
        Assert.DoesNotContain(cells, c => ((string?)c.Attribute("style"))?.Contains("endArrow=block") == true);
        Assert.Contains(cells, c => (string?)c.Attribute("value") == "Parent<T>\nChild");
    }

    [Fact]
    public void ShouldSupportEmptyInputAndRejectDanglingEndpoints()
    {
        // Given
        var renderer = TestServices.Get<DrawIODiagramRenderer>();
        ProjectModel model = Model();
        model.Dependencies![0].ToType = "Missing";
        // When / Then

        Assert.Equal(expected: 2, actual: Parse(bytes: renderer.Render(new RenderModel(Array.Empty<ProjectModel>())))
            .Descendants(name: "mxCell")
            .Count());
        // Then: verify the resulting contract.

        Assert.Throws<ArgumentException>(testCode: () => renderer.Render(new RenderModel(new[] { model })));
        Assert.Throws<ArgumentNullException>(testCode: () => renderer.Render(new RenderModel(null !)));
    }

    [Fact]
    public void ShouldRenderMissingCollectionsAndUnlabelledOrInitializerCalls()
    {
        // Given
        var renderer = TestServices.Get<DrawIODiagramRenderer>();
        ProjectModel model = Model();
        model.Dependencies![0].FromMethod = null;
        model.Dependencies[1].FromMethod = null;
        model.Dependencies[1].ToMethod = null;
        // When

        XElement[] cells = Parse(bytes: renderer.Render(new RenderModel(new[] { new ProjectModel(), model })))
            .Descendants(name: "mxCell")
            .ToArray();
        // Then

        Assert.Contains(collection: cells, filter: c => (string? )c.Attribute(name: "value") == "Project");
        Assert.Single(collection: cells, predicate: c => (string? )c.Attribute(name: "edge") == "1");
        Assert.Contains(collection: cells, filter: c => (string? )c.Attribute(name: "edge") == "1" && (string? )c.Attribute(name: "value") == "");
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
        // Then: verify the resulting contract.
        Assert.Throws<ArgumentException>(testCode: () => TestServices.Get<DrawIODiagramRenderer>().Render(new RenderModel(new[] { model })));
    }

    [Fact]
    public void ShouldRejectMissingAndDuplicateTypeNames()
    {
        // Given
        var renderer = TestServices.Get<DrawIODiagramRenderer>();
        ProjectModel model = Model();
        model.Types![0].Name = " ";
        // When / Then
        Assert.Throws<ArgumentException>(testCode: () => renderer.Render(new RenderModel(new[] { model })));
        model.Types[0].Name = "Child";
        // Then: verify the resulting contract.
        Assert.Throws<ArgumentException>(testCode: () => renderer.Render(new RenderModel(new[] { model })));
        Assert.Throws<ArgumentNullException>(testCode: () => renderer.Render(new RenderModel(new ProjectModel[] { null ! })));
    }

    [Fact]
    public void ShouldKeepBranchingNodesApartAndInsideTheirProjectBox()
    {
        // Given
        ProjectModel model = Model();

        model.Types = model.Types!.Concat(second: new[] { new DefinedType { Name = "OtherChild" } })
            .ToArray();

        model.Dependencies![1].ToType = "OtherChild";
        // When

        XElement[] cells = Parse(bytes: TestServices.Get<DrawIODiagramRenderer>().Render(new RenderModel(new[] { model })))
            .Descendants(name: "mxCell")
            .ToArray();

        XElement box = cells.Single(predicate: c => (string? )c.Attribute(name: "id") == "tree-0");

        XElement[] nodes = cells.Where(predicate: c => (string? )c.Attribute(name: "parent") == "tree-0" && (string? )c.Attribute(name: "vertex") == "1")
            .ToArray();
        // Then

        foreach (XElement node in nodes)
        {
            Assert.True(condition: Number(cell: node, name: "x") >= 0 && Number(cell: node, name: "y") >= 30);
            Assert.True(condition: Number(cell: node, name: "x") + Number(cell: node, name: "width") <= Number(cell: box, name: "width"));
            Assert.True(condition: Number(cell: node, name: "y") + Number(cell: node, name: "height") <= Number(cell: box, name: "height"));
        }

        for (int i = 0; i < nodes.Length; i++)
        {
            for (int j = i + 1; j < nodes.Length; j++)
            {
                Assert.True(condition: Number(cell: nodes[i], name: "x") + Number(cell: nodes[i], name: "width") <= Number(cell: nodes[j], name: "x") || Number(cell: nodes[j], name: "x") + Number(cell: nodes[j], name: "width") <= Number(cell: nodes[i], name: "x") || Number(cell: nodes[i], name: "y") + Number(cell: nodes[i], name: "height") <= Number(cell: nodes[j], name: "y") || Number(cell: nodes[j], name: "y") + Number(cell: nodes[j], name: "height") <= Number(cell: nodes[i], name: "y"));
            }
        }
    }

    [Fact]
    public void ShouldUseShortLabelsFixedWidthsRoleColoursAndFilledLeftLabelledContainers()
    {
        // Given
        string[] names =
        {
            "Example.Exposures.SchoolManager",
            "Example.Services.Orchestrations.SchoolOrchestrationService",
            "Example.Services.Processings.SchoolProcessingService",
            "Example.Services.Foundations.SchoolService",
            "Example.Brokers.Storages.SchoolBroker"
        };

        var model = new ProjectModel
        {
            Name = "School",
            Types = names.Select(selector: name => new DefinedType { Name = name, Methods = new[] { new Method { Name = "Run" } }, IsInternal = true })
                .ToArray()
        };
        // When

        XElement[] cells = Parse(bytes: TestServices.Get<DrawIODiagramRenderer>().Render(new RenderModel(new[] { model })))
            .Descendants(name: "mxCell")
            .ToArray();

        XElement box = cells.Single(predicate: cell => (string? )cell.Attribute(name: "id") == "tree-0");

        XElement[] nodes = cells.Where(predicate: cell => (string? )cell.Attribute(name: "parent") == "tree-0")
            .ToArray();
        // Then

        Assert.Equal(expected: names.Select(selector: name => name.Split(separator: '.')
            .Last()), actual: nodes.Select(selector: node => (string? )node.Attribute(name: "value")));

        Assert.Equal(expected: names, actual: nodes.Select(selector: node => (string? )node.Attribute(name: "typeName")));
        Assert.All(collection: nodes, action: node => Assert.Equal(expected: 180, actual: Number(cell: node, name: "width")));

        Assert.Equal(expected: 5, actual: nodes.Select(selector: node => ((string)node.Attribute(name: "style")!).Split(separator: ';')
            .Single(predicate: part => part.StartsWith(value: "fillColor=")))
            .Distinct()
            .Count());

        string style = (string)box.Attribute(name: "style")!;
        Assert.Contains(expectedSubstring: "fillColor=#263242;", actualString: style);
        Assert.Contains(expectedSubstring: "align=left;", actualString: style);
        Assert.Contains(expectedSubstring: "verticalAlign=top;", actualString: style);
        Assert.DoesNotContain(expectedSubstring: "swimlane;", actualString: style);
    }

    private static XDocument Parse(byte[] bytes) =>
        DiagramTestDocument.Parse(bytes);

    private static double Number(XElement cell, string name) =>
        (double)cell.Element(name: "mxGeometry")!.Attribute(name: name)!;

    private static ProjectModel Model() =>
        new()
    {
        Name = "Project & Test",
        Path = "Example.csproj",
        Types = new[]
        {
            new DefinedType
            {
                Name = "Parent<T>",
                Methods = new[] { new Method { Name = "Run" } }, IsInternal = true
            },
            new DefinedType
            {
                Name = "Child"
            }
        },
        Dependencies = new[]
        {
            new TypeRelationship
            {
                FromType = "Parent<T>",
                ToType = "Child",
                FromMethod = "Run",
                ToMethod = "Save",
                DependencyType = DependencyType.Consumed
            },
            new TypeRelationship
            {
                FromType = "Parent<T>",
                ToType = "Child",
                FromMethod = "Update",
                ToMethod = "Save",
                DependencyType = DependencyType.Consumed
            }
        }
    };
}