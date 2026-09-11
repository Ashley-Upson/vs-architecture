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
public sealed partial class TreePresentationTests
{
    private static readonly string[] expectedImplementationLabels = new[]
    {
        "First\nIContract",
        "Second\nIContract"
    };
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ShouldUseTheDeepestParentPathBeforePositioningDescendants(bool reverse)
    {
        var model = Model("DirectManager", "ImportManager", "Aggregation", "Orchestration", "Processing", "Broker");
        model.Dependencies = new[]
        {
            Link("DirectManager", "Orchestration"), Link("ImportManager", "Aggregation"),
            Link("Aggregation", "Orchestration"), Link("Orchestration", "Processing"), Link("Processing", "Broker")
        };
        if (reverse)
        {
            Array.Reverse(model.Types!);
            Array.Reverse(model.Dependencies);
        }
        var cells = Render(model);
        double Y(string name) => (double)Node(cells, name).Element("mxGeometry")!.Attribute("y")!;
        Assert.Equal(60, Y("DirectManager"));
        Assert.Equal(60, Y("ImportManager"));
        Assert.Equal(220, Y("Aggregation"));
        Assert.Equal(380, Y("Orchestration"));
        Assert.Equal(540, Y("Processing"));
        Assert.Equal(700, Y("Broker"));
        Assert.Equal(Centre(Node(cells, "Orchestration")), Centre(Node(cells, "Processing")));
        Assert.Equal(Centre(Node(cells, "Processing")), Centre(Node(cells, "Broker")));
    }

    [Fact]
    public void ShouldRetainDataTypesForDataOutputInBothFormats()
    {
        var model = Model("Data");
        model.Types![0].IsInternal = true;
        model.Types[0].Methods = Array.Empty<Method>();
        foreach (IDiagramRenderer renderer in new IDiagramRenderer[] { TestServices.Get<DrawIODiagramRenderer>(), TestServices.Get<HtmlDiagramRenderer>() })
        {
            string output = Encoding.UTF8.GetString(renderer.Render(new RenderModel(new[] { model }, diagramType: DiagramTypes.DataModel)));
            Assert.Contains("Data", output);
            var document = XDocument.Parse(output);
            Assert.Single(document.Descendants().Where(element => element.Attribute("typeName")?.Value == "Data" || element.Attribute("data-type")?.Value == "Data"));
        }
    }

    [Fact]
    public void ShouldOmitProjectDataTypesAndTheirLinksWithoutChangingTheModel()
    {
        var model = Model("Service", "Data", "External");
        model.Types![0].IsInternal = true;
        model.Types[0].Methods = new[] { new Method { Name = "Run" } };
        model.Types[1].IsInternal = true;
        model.Types[1].Methods = Array.Empty<Method>();
        model.Dependencies = new[] { Link("Service", "Data"), Link("Service", "External") };
        string before = JsonSerializer.Serialize(model);
        var cells = Render(model);
        Assert.DoesNotContain(cells, cell => (string?)cell.Attribute("typeName") == "Data");
        Assert.NotNull(Node(cells, "Service"));
        Assert.NotNull(Node(cells, "External"));
        Assert.Single(cells.Where(cell => (string?)cell.Attribute("edge") == "1"));
        Assert.Equal(before, JsonSerializer.Serialize(model));
    }

    [Fact]
    public void ShouldNotReserveSharedDescendantWidthInOnlyTheFirstParentTree()
    {
        var model = Model("First", "Second", "Hub", "Context");
        model.Dependencies = new[] { Link("First", "Hub"), Link("First", "Context"), Link("Second", "Hub"), Link("Second", "Context") };
        var cells = Render(model);
        Assert.Equal(270, Math.Abs(Centre(Node(cells, "Second")) - Centre(Node(cells, "First"))));
    }

    [Fact]
    public void ShouldCentreSharedChildUnderAllParentCentres()
    {
        var model = Model("First", "Second", "Third", "Hub", "Leaf");
        model.Dependencies = new[] { Link("First", "Hub"), Link("Second", "Hub"), Link("Third", "Hub"), Link("Hub", "Leaf") };
        var cells = Render(model);
        double expected = (Centre(Node(cells, "First")) + Centre(Node(cells, "Third"))) / 2;
        Assert.Equal(expected, Centre(Node(cells, "Hub")));
        Assert.Equal(expected, Centre(Node(cells, "Leaf")));
    }

    [Fact]
    public void ShouldSpaceSharedChildrenAroundTheirCommonParentGroup()
    {
        var model = Model("First", "Second", "Hub", "Context");
        model.Dependencies = new[] { Link("First", "Hub"), Link("First", "Context"), Link("Second", "Hub"), Link("Second", "Context") };
        var cells = Render(model);
        double parentCentre = (Centre(Node(cells, "First")) + Centre(Node(cells, "Second"))) / 2;
        double hub = Centre(Node(cells, "Hub")), context = Centre(Node(cells, "Context"));
        Assert.Equal(parentCentre, (hub + context) / 2);
        Assert.True(Math.Abs(hub - context) >= 270);
    }

    [Fact]
    public void ShouldPutImplementedContractsOnTheSecondLineAndRetainExternalBoundaries()
    {
        // Given
        ProjectModel model = Model(names: ["Example.Manager", "Example.Worker", "Example.IWorker", "Example.IBase", "External.IHub"]);

        foreach (DefinedType type in model.Types!.Skip(count: 2))
        {
            type.FrameworkType = FrameworkType.Interface;
        }

        model.Dependencies = new[]
        {
            Link(from: "Example.Manager", to: "Example.Worker"),
            Link(from: "Example.Worker", to: "Example.IWorker", inheritance: true),
            Link(from: "Example.IWorker", to: "Example.IBase", inheritance: true),
            Link(from: "Example.Worker", to: "External.IHub")
        };

        string before = JsonSerializer.Serialize(value: model);
        // When
        XElement[] cells = Render(model: model);
        // Then
        Assert.DoesNotContain(collection: cells, filter: cell => (string? )cell.Attribute(name: "typeName")is "Example.IWorker" or "Example.IBase");

        Assert.Equal(expected: "Worker\nIWorker", actual: (string? )Node(cells: cells, name: "Example.Worker")
            .Attribute(name: "value"));

        Assert.NotNull(@object: Node(cells: cells, name: "External.IHub"));
        Assert.Equal(expected: 2, actual: cells.Count(predicate: cell => (string? )cell.Attribute(name: "edge") == "1"));
        Assert.Equal(expected: before, actual: JsonSerializer.Serialize(value: model));
    }

    [Fact]
    public void ShouldDisplayNoLabelsOnMethodConnections()
    {
        // Given
        ProjectModel model = Model(names: ["Parent", "Child"]);

        model.Dependencies = new[]
        {
            Link(from: "Parent", to: "Child")
        };

        model.Dependencies[0].FromMethod = "Run";
        model.Dependencies[0].ToMethod = "Save";
        // When / Then
        // Then: verify the resulting contract.

        Assert.All(collection: Render(model: model)
            .Where(predicate: cell => (string? )cell.Attribute(name: "edge") == "1"), action: cell => Assert.Equal(expected: "", actual: (string? )cell.Attribute(name: "value")));
    }

    [Fact]
    public void ShouldRenderOnlyTypeRelationshipsRegardlessOfMethodData()
    {
        // Given
        ProjectModel model = Model(names: ["Parent", "Child"]);

        model.Dependencies = new[]
        {
            Link(from: "Parent", to: "Child"),
            Link(from: "Parent", to: "Child")
        };

        model.Dependencies[0].FromMethod = "Run";
        model.Dependencies[1].FromMethod = "Save";
        var renderer = TestServices.Get<DrawIODiagramRenderer>();
        // When
        byte[] original = renderer.Render(new RenderModel(new[] { model }));
        model.Dependencies[0].FromMethod = "EntirelyDifferentMethod";
        model.Dependencies[1].ToMethod = "AnotherMethod";
        byte[] changed = renderer.Render(new RenderModel(new[] { model }));
        // Then
        Assert.Equal(expected: original, actual: changed);
        XElement edge = Assert.Single(collection: Render(model: model), predicate: cell => (string? )cell.Attribute(name: "edge") == "1");
        Assert.Null(@object: edge.Attribute(name: "fromMethod"));
        Assert.Null(@object: edge.Attribute(name: "toMethod"));
        Assert.Equal(expected: "", actual: (string? )edge.Attribute(name: "value"));
        Assert.Equal(expected: 2, actual: model.Dependencies.Length);
        Assert.Equal(expected: "Save", actual: model.Dependencies[1].FromMethod);
    }

    [Fact]
    public void ShouldCentreChildrenWithSpaceForTheirActualUnevenSubtrees()
    {
        // Given: A has three children, B only one, and C has none.
        ProjectModel model = Model(names: ["Root", "A", "B", "C", "A1", "A2", "A3", "B1"]);

        model.Dependencies = new[]
        {
            Link(from: "Root", to: "A"),
            Link(from: "Root", to: "B"),
            Link(from: "Root", to: "C"),
            Link(from: "A", to: "A1"),
            Link(from: "A", to: "A2"),
            Link(from: "A", to: "A3"),
            Link(from: "B", to: "B1")
        };
        // When

        XElement[] cells = Render(model: model);
        // Then
        Assert.Equal(expected: Centre(node: Node(cells: cells, name: "Root")), actual: (Centre(node: Node(cells: cells, name: "A")) + Centre(node: Node(cells: cells, name: "C"))) / 2);
        Assert.Equal(expected: 270, actual: Centre(node: Node(cells: cells, name: "C")) - Centre(node: Node(cells: cells, name: "B")));
        Assert.Equal(expected: Centre(node: Node(cells: cells, name: "A")), actual: (Centre(node: Node(cells: cells, name: "A1")) + Centre(node: Node(cells: cells, name: "A3"))) / 2);
        Assert.Equal(expected: Centre(node: Node(cells: cells, name: "B")), actual: Centre(node: Node(cells: cells, name: "B1")));
        Assert.True(condition: Centre(node: Node(cells: cells, name: "A3")) + 270 <= Centre(node: Node(cells: cells, name: "B1")));
        Assert.True(condition: Centre(node: Node(cells: cells, name: "B")) - Centre(node: Node(cells: cells, name: "A")) > 270);
    }

    [Fact]
    public void ShouldKeepSharedAndCyclicRelationshipsWithoutDuplicatingNodes()
    {
        // Given
        ProjectModel model = Model(names: ["Root", "A", "B", "Shared"]);

        model.Dependencies = new[]
        {
            Link(from: "Root", to: "A"),
            Link(from: "Root", to: "B"),
            Link(from: "A", to: "Shared"),
            Link(from: "B", to: "Shared"),
            Link(from: "Shared", to: "Root")
        };
        // When

        XElement[] cells = Render(model: model);
        // Then
        Assert.Equal(expected: 4, actual: cells.Count(predicate: cell => cell.Attribute(name: "typeName")is not null));
        Assert.Equal(expected: 5, actual: cells.Count(predicate: cell => (string? )cell.Attribute(name: "edge") == "1"));
        Assert.Equal(expected: (Centre(node: Node(cells: cells, name: "A")) + Centre(node: Node(cells: cells, name: "B"))) / 2, actual: Centre(node: Node(cells: cells, name: "Shared")));
    }

    [Fact]
    public void ShouldResolveRemainingInterfaceCallsToEveryDisplayedImplementation()
    {
        // Given
        ProjectModel model = Model(names: ["Manager", "First", "Second", "IContract"]);
        model.Types![3].FrameworkType = FrameworkType.Interface;

        model.Dependencies = new[]
        {
            Link(from: "Manager", to: "IContract"),
            Link(from: "First", to: "IContract", inheritance: true),
            Link(from: "Second", to: "IContract", inheritance: true)
        };
        // When

        XElement[] cells = Render(model: model);
        // Then

        XElement[] edges = cells.Where(predicate: cell => (string? )cell.Attribute(name: "edge") == "1")
            .ToArray();

        Assert.Equal(expected: 2, actual: edges.Length);

        Assert.Equal(expected: expectedImplementationLabels, actual: cells.Where(predicate: cell => edges.Any(predicate: edge => (string? )edge.Attribute(name: "target") == (string? )cell.Attribute(name: "id")))
            .Select(selector: cell => (string? )cell.Attribute(name: "value")));
    }

    [Fact]
    public void ShouldDescribeClassInheritanceAndInheritedContractsWithoutLooping()
    {
        // Given
        ProjectModel model = Model(names: ["Derived", "Base", "IChild", "IParent"]);
        model.Types![2].FrameworkType = model.Types[3].FrameworkType = FrameworkType.Interface;

        model.Dependencies = new[]
        {
            Link(from: "Derived", to: "Base", inheritance: true),
            Link(from: "Base", to: "IChild", inheritance: true),
            Link(from: "IChild", to: "IParent", inheritance: true),
            Link(from: "IParent", to: "IChild", inheritance: true)
        };
        // When

        XElement[] cells = Render(model: model);
        // Then

        Assert.Equal(expected: "Derived\nBase", actual: (string? )Node(cells: cells, name: "Derived")
            .Attribute(name: "value"));

        Assert.Equal(expected: "Base\nIChild", actual: (string? )Node(cells: cells, name: "Base")
            .Attribute(name: "value"));

        Assert.Empty(cells.Where(cell => (string?)cell.Attribute("edge") == "1"));
    }

    private static ProjectModel Model(params string[] names) =>
        new()
    {
        Types = names.Select(selector: name => new DefinedType { Name = name })
            .ToArray()
    };

    private static TypeRelationship Link(string from, string to, bool inheritance = false) =>
        new()
    {
        FromType = from,
        ToType = to,
        DependencyType = inheritance ? DependencyType.Inheritance : DependencyType.Consumed
    };

    private static XElement[] Render(ProjectModel model) =>
        DiagramTestDocument.Parse(TestServices.Get<DrawIODiagramRenderer>().Render(new RenderModel(new[] { model })))
        .Descendants(name: "mxCell")
        .ToArray();

    private static XElement Node(XElement[] cells, string name) =>
        cells.Single(predicate: cell => (string? )cell.Attribute(name: "typeName") == name);

    private static double Centre(XElement node) =>
        (double)node.Element(name: "mxGeometry")!.Attribute(name: "x")! + 90;
}