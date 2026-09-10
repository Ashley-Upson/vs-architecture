// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------

using System;
using System.Linq;
using System.Text.Json;
using StandardIo.ArchitectureDiagram.Core2.Models;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core2.Tests;

public sealed class ProjectModelSplitterTests
{
    [Fact]
    public void ShouldRepeatSharedDependenciesAndTheirDescendantsForEachRoot()
    {
        var model = Model(new[] { Type("A"), Type("B"), Type("Shared"), Type("Leaf", hasMethods: false) },
            Link("A", "Shared"), Link("A", "Leaf"), Link("B", "Shared"), Link("Shared", "Leaf"));
        string before = JsonSerializer.Serialize(model);

        ProjectModel[] result = new ProjectModelSplitter().Split(model);

        Assert.Equal(2, result.Length);
        Assert.Equal(new[] { "A", "Shared", "Leaf" }, Names(result[0]));
        Assert.Equal(new[] { "B", "Shared", "Leaf" }, Names(result[1]));
        Assert.Equal(3, result[0].Dependencies!.Length);
        Assert.Equal(2, result[1].Dependencies!.Length);
        Assert.All(result, tree => { Assert.Equal(model.Name, tree.Name); Assert.Equal(model.Path, tree.Path); });
        Assert.Equal(before, JsonSerializer.Serialize(model));
        AssertSelfContained(result);
    }

    [Fact]
    public void ShouldRequireMethodsForRootsButKeepMethodlessDependenciesAndLeftovers()
    {
        var model = Model(new[] { Type("Root"), Type("Child", false), Type("Data", false), Type("Isolated") }, Link("Root", "Child"));

        ProjectModel[] result = new ProjectModelSplitter().Split(model);

        Assert.Equal(3, result.Length);
        Assert.Equal(new[] { "Root", "Child" }, Names(result[0]));
        Assert.Equal(new[] { "Isolated" }, Names(result[1]));
        Assert.Equal(new[] { "Data" }, Names(result[2]));
    }

    [Fact]
    public void ShouldTreatInterfaceImplementationAndCallsAsDependenciesWithoutResolvingImplementations()
    {
        var contract = Type("IService");
        contract.FrameworkType = FrameworkType.Interface;
        var implementationLink = Link("Service", "IService");
        implementationLink.DependencyType = DependencyType.Inheritance;
        implementationLink.FromMethod = null;
        implementationLink.ToMethod = null;
        var model = Model(new[] { Type("Manager"), contract, Type("Service") }, Link("Manager", "IService"), implementationLink);

        ProjectModel[] result = new ProjectModelSplitter().Split(model);

        Assert.Equal(2, result.Length);
        Assert.Equal(new[] { "Manager", "IService" }, Names(result[0]));
        Assert.Equal(new[] { "Service", "IService" }, Names(result[1]));
        Assert.Equal(DependencyType.Inheritance, Assert.Single(result[1].Dependencies!).DependencyType);
        Assert.Null(result[1].Dependencies![0].FromMethod);
        Assert.Equal(FrameworkType.Interface, result[1].Types![1].FrameworkType);
    }

    [Fact]
    public void ShouldKeepSelfReferencesWithoutDisqualifyingARoot()
    {
        var model = Model(new[] { Type("Root") }, Link("Root", "Root"));

        ProjectModel tree = Assert.Single(new ProjectModelSplitter().Split(model));

        Assert.Equal(new[] { "Root" }, Names(tree));
        Assert.Single(tree.Dependencies!);
    }

    [Fact]
    public void ShouldStopCyclesWhilePreservingEveryReachableEdge()
    {
        var model = Model(new[] { Type("Root"), Type("B"), Type("C") }, Link("Root", "B"), Link("B", "C"), Link("C", "B"));

        ProjectModel tree = Assert.Single(new ProjectModelSplitter().Split(model));

        Assert.Equal(new[] { "Root", "B", "C" }, Names(tree));
        Assert.Equal(3, tree.Dependencies!.Length);
    }

    [Fact]
    public void ShouldGroupRootlessCyclesAndUnusedTypesIntoOneFinalModel()
    {
        var model = Model(new[] { Type("B"), Type("C"), Type("Data", false) }, Link("B", "C"), Link("C", "B"));

        ProjectModel remainder = Assert.Single(new ProjectModelSplitter().Split(model));

        Assert.Equal(new[] { "B", "C", "Data" }, Names(remainder));
        Assert.Equal(2, remainder.Dependencies!.Length);
    }

    [Fact]
    public void ShouldKeepLeftoverDependenciesSelfContainedWhenTheyPointIntoAnExistingTree()
    {
        var model = Model(new[] { Type("Root"), Type("Shared"), Type("B"), Type("C") },
            Link("Root", "Shared"), Link("B", "C"), Link("C", "B"), Link("C", "Shared"));

        ProjectModel[] result = new ProjectModelSplitter().Split(model);

        Assert.Equal(2, result.Length);
        Assert.Equal(new[] { "Root", "Shared" }, Names(result[0]));
        Assert.Equal(new[] { "B", "C", "Shared" }, Names(result[1]));
        Assert.Equal(3, result[1].Dependencies!.Length);
        AssertSelfContained(result);
    }

    [Fact]
    public void ShouldCopyMemberAndDependencyDataWithoutSharingMutableObjects()
    {
        var shared = Type("External", false);
        shared.IsInternal = false;
        shared.Fields = new[] { new Field { Name = "field", Type = "System.String" } };
        shared.Properties = new[] { new Property { Name = "property", Type = "System.Int32" } };
        shared.Methods = new[] { new Method { Name = "Work" } };
        var model = Model(new[] { Type("A"), Type("B"), shared }, Link("A", "External"), Link("B", "External"));
        string before = JsonSerializer.Serialize(model);

        ProjectModel[] result = new ProjectModelSplitter().Split(model);

        DefinedType first = result[0].Types![1];
        DefinedType second = result[1].Types![1];
        Assert.Equal(JsonSerializer.Serialize(shared), JsonSerializer.Serialize(first));
        Assert.NotSame(shared, first);
        Assert.NotSame(first, second);
        Assert.NotSame(model.Dependencies![0], result[0].Dependencies![0]);
        first.Fields![0].Name = "changed";
        first.Properties![0].Type = "changed";
        first.Methods![0].Name = "changed";
        result[0].Dependencies![0].ToMethod = "changed";
        Assert.Equal(before, JsonSerializer.Serialize(model));
        Assert.Equal("field", second.Fields![0].Name);
        Assert.Equal("System.Int32", second.Properties![0].Type);
        Assert.Equal("Work", second.Methods![0].Name);
    }

    [Fact]
    public void ShouldPreserveDifferentMethodLinksBetweenTheSameTypes()
    {
        var first = Link("A", "B");
        var second = Link("A", "B");
        second.FromMethod = "Other";
        var model = Model(new[] { Type("A"), Type("B") }, first, second);

        ProjectModel tree = Assert.Single(new ProjectModelSplitter().Split(model));

        Assert.Equal(2, tree.Types!.Length);
        Assert.Equal(new[] { "Run", "Other" }, tree.Dependencies!.Select(link => link.FromMethod));
    }

    [Fact]
    public void ShouldReturnNoModelsForEmptyInputAndTreatMissingCollectionsAsEmpty()
    {
        Assert.Empty(new ProjectModelSplitter().Split(new ProjectModel()));
        var model = Model(new[] { new DefinedType { Name = "Data" } });
        ProjectModel remainder = Assert.Single(new ProjectModelSplitter().Split(model));
        Assert.Empty(remainder.Types![0].Methods!);
        Assert.Empty(remainder.Types[0].Fields!);
        Assert.Empty(remainder.Types[0].Properties!);
        Assert.Empty(remainder.Dependencies!);
    }

    [Fact]
    public void ShouldRejectNullInput() => Assert.Throws<ArgumentNullException>(() => new ProjectModelSplitter().Split(null!));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ShouldRejectDependenciesWithUnknownEndpoints(bool missingSource)
    {
        var model = Model(new[] { Type("Known") }, missingSource ? Link("Missing", "Known") : Link("Known", "Missing"));
        Assert.Throws<ArgumentException>(() => new ProjectModelSplitter().Split(model));
    }

    [Fact]
    public void ShouldRejectAmbiguousTypeNames()
    {
        var model = Model(new[] { Type("Duplicate"), Type("Duplicate") });
        Assert.Throws<ArgumentException>(() => new ProjectModelSplitter().Split(model));
    }

    private static DefinedType Type(string name, bool hasMethods = true) => new()
    {
        Name = name,
        IsInternal = true,
        FrameworkType = FrameworkType.Class,
        Methods = hasMethods ? new[] { new Method { Name = "Run" } } : Array.Empty<Method>()
    };

    private static Dependency Link(string from, string to) => new()
    {
        DependencyType = DependencyType.Consumed,
        FromType = from,
        ToType = to,
        FromMethod = "Run",
        ToMethod = "Work"
    };

    private static ProjectModel Model(DefinedType[] types, params Dependency[] dependencies) => new()
    {
        Name = "Example", Path = "Example.csproj", Types = types, Dependencies = dependencies
    };

    private static string[] Names(ProjectModel model) => model.Types!.Select(type => type.Name!).ToArray();

    private static void AssertSelfContained(ProjectModel[] models)
    {
        foreach (ProjectModel model in models)
        {
            string[] names = Names(model);
            Assert.All(model.Dependencies!, link => { Assert.Contains(link.FromType, names); Assert.Contains(link.ToType, names); });
        }
    }
}
