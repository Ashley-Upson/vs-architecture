using System;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using StandardIo.ArchitectureDiagram.Core2.Models;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core2.Tests;

public sealed class TreePresentationTests
{
    [Fact]
    public void ShouldPutImplementedContractsOnTheSecondLineAndRetainExternalBoundaries()
    {
        // Given
        ProjectModel model = Model("Example.Manager", "Example.Worker", "Example.IWorker", "Example.IBase", "External.IHub");
        foreach (DefinedType type in model.Types!.Skip(2)) type.FrameworkType = FrameworkType.Interface;
        model.Dependencies = new[] {
            Link("Example.Manager", "Example.Worker"), Link("Example.Worker", "Example.IWorker", true),
            Link("Example.IWorker", "Example.IBase", true), Link("Example.Worker", "External.IHub") };
        string before = JsonSerializer.Serialize(model);
        // When
        XElement[] cells = Render(model);
        // Then
        Assert.DoesNotContain(cells, cell => (string?)cell.Attribute("typeName") is "Example.IWorker" or "Example.IBase");
        Assert.Equal("Worker\nIBase, IWorker", (string?)Node(cells, "Example.Worker").Attribute("value"));
        Assert.NotNull(Node(cells, "External.IHub"));
        Assert.Equal(2, cells.Count(cell => (string?)cell.Attribute("edge") == "1"));
        Assert.Equal(before, JsonSerializer.Serialize(model));
    }

    [Fact]
    public void ShouldDisplayNoLabelsOnMethodConnections()
    {
        // Given
        ProjectModel model = Model("Parent", "Child");
        model.Dependencies = new[] { Link("Parent", "Child") };
        model.Dependencies[0].FromMethod = "Run";
        model.Dependencies[0].ToMethod = "Save";
        // When / Then
        Assert.All(Render(model).Where(cell => (string?)cell.Attribute("edge") == "1"),
            cell => Assert.Equal("", (string?)cell.Attribute("value")));
    }

    [Fact]
    public void ShouldRenderOnlyTypeRelationshipsRegardlessOfMethodData()
    {
        // Given
        ProjectModel model = Model("Parent", "Child");
        model.Dependencies = new[] { Link("Parent", "Child"), Link("Parent", "Child") };
        model.Dependencies[0].FromMethod = "Run";
        model.Dependencies[1].FromMethod = "Save";
        var renderer = new DrawIODiagramRenderer();
        // When
        byte[] original = renderer.Render(new[] { model });
        model.Dependencies[0].FromMethod = "EntirelyDifferentMethod";
        model.Dependencies[1].ToMethod = "AnotherMethod";
        byte[] changed = renderer.Render(new[] { model });
        // Then
        Assert.Equal(original, changed);
        XElement edge = Assert.Single(Render(model).Where(cell => (string?)cell.Attribute("edge") == "1"));
        Assert.Null(edge.Attribute("fromMethod"));
        Assert.Null(edge.Attribute("toMethod"));
        Assert.Equal("", (string?)edge.Attribute("value"));
        Assert.Equal(2, model.Dependencies.Length);
        Assert.Equal("Save", model.Dependencies[1].FromMethod);
    }

    [Fact]
    public void ShouldCentreAndEquallySpaceChildrenWithRoomForUnevenSubtrees()
    {
        // Given: A has three children, B only one, and C has none.
        ProjectModel model = Model("Root", "A", "B", "C", "A1", "A2", "A3", "B1");
        model.Dependencies = new[] { Link("Root", "A"), Link("Root", "B"), Link("Root", "C"),
            Link("A", "A1"), Link("A", "A2"), Link("A", "A3"), Link("B", "B1") };
        // When
        XElement[] cells = Render(model);
        // Then
        Assert.Equal(Centre(Node(cells, "Root")), (Centre(Node(cells, "A")) + Centre(Node(cells, "C"))) / 2);
        Assert.Equal(Centre(Node(cells, "B")) - Centre(Node(cells, "A")), Centre(Node(cells, "C")) - Centre(Node(cells, "B")));
        Assert.Equal(Centre(Node(cells, "A")), (Centre(Node(cells, "A1")) + Centre(Node(cells, "A3"))) / 2);
        Assert.Equal(Centre(Node(cells, "B")), Centre(Node(cells, "B1")));
        Assert.True(Centre(Node(cells, "A3")) + 240 <= Centre(Node(cells, "B1")));
        Assert.True(Centre(Node(cells, "B")) - Centre(Node(cells, "A")) > 240);
    }

    [Fact]
    public void ShouldKeepSharedAndCyclicRelationshipsWithoutDuplicatingNodes()
    {
        // Given
        ProjectModel model = Model("Root", "A", "B", "Shared");
        model.Dependencies = new[] { Link("Root", "A"), Link("Root", "B"), Link("A", "Shared"), Link("B", "Shared"), Link("Shared", "Root") };
        // When
        XElement[] cells = Render(model);
        // Then
        Assert.Equal(4, cells.Count(cell => cell.Attribute("typeName") is not null));
        Assert.Equal(5, cells.Count(cell => (string?)cell.Attribute("edge") == "1"));
        Assert.Equal(Centre(Node(cells, "A")), Centre(Node(cells, "Shared")));
    }

    [Fact]
    public void ShouldResolveRemainingInterfaceCallsToEveryDisplayedImplementation()
    {
        // Given
        ProjectModel model = Model("Manager", "First", "Second", "IContract");
        model.Types![3].FrameworkType = FrameworkType.Interface;
        model.Dependencies = new[] { Link("Manager", "IContract"), Link("First", "IContract", true), Link("Second", "IContract", true) };
        // When
        XElement[] cells = Render(model);
        // Then
        XElement[] edges = cells.Where(cell => (string?)cell.Attribute("edge") == "1").ToArray();
        Assert.Equal(2, edges.Length);
        Assert.Equal(new[] { "First\nIContract", "Second\nIContract" }, cells.Where(cell =>
            edges.Any(edge => (string?)edge.Attribute("target") == (string?)cell.Attribute("id"))).Select(cell => (string?)cell.Attribute("value")));
    }

    [Fact]
    public void ShouldRetainClassInheritanceAndInheritedContractsWithoutLooping()
    {
        // Given
        ProjectModel model = Model("Derived", "Base", "IChild", "IParent");
        model.Types![2].FrameworkType = model.Types[3].FrameworkType = FrameworkType.Interface;
        model.Dependencies = new[] { Link("Derived", "Base", true), Link("Base", "IChild", true),
            Link("IChild", "IParent", true), Link("IParent", "IChild", true) };
        // When
        XElement[] cells = Render(model);
        // Then
        Assert.Equal("Derived\nIChild, IParent", (string?)Node(cells, "Derived").Attribute("value"));
        Assert.Equal("Base\nIChild, IParent", (string?)Node(cells, "Base").Attribute("value"));
        Assert.Single(cells.Where(cell => (string?)cell.Attribute("edge") == "1"));
    }

    private static ProjectModel Model(params string[] names) => new() { Types = names.Select(name => new DefinedType { Name = name }).ToArray() };
    private static Dependency Link(string from, string to, bool inheritance = false) => new()
    {
        FromType = from, ToType = to, DependencyType = inheritance ? DependencyType.Inheritance : DependencyType.Consumed
    };
    private static XElement[] Render(ProjectModel model) => XDocument.Parse(Encoding.UTF8.GetString(new DrawIODiagramRenderer().Render(new[] { model }))).Descendants("mxCell").ToArray();
    private static XElement Node(XElement[] cells, string name) => cells.Single(cell => (string?)cell.Attribute("typeName") == name);
    private static double Centre(XElement node) => (double)node.Element("mxGeometry")!.Attribute("x")! + 90;
}
