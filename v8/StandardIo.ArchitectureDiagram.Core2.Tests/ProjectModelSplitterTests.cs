// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System;
using System.Linq;
using System.Text.Json;
using StandardIo.ArchitectureDiagram.Core2.Models;
using Xunit;
using StandardIo.ArchitectureDiagram.Core2.Exposures;

namespace StandardIo.ArchitectureDiagram.Core2.Tests;
public sealed partial class ProjectModelSplitterTests
{
    private static readonly string[] firstSharedTreeNames = new[]
    {
        "A",
        "Shared",
        "Leaf"
    };
    private static readonly string[] secondSharedTreeNames = new[]
    {
        "B",
        "Shared",
        "Leaf"
    };
    private static readonly string[] rootAndChildNames = new[]
    {
        "Root",
        "Child"
    };
    private static readonly string[] isolatedNames = new[]
    {
        "Isolated"
    };
    private static readonly string[] dataNames = new[]
    {
        "Data"
    };
    private static readonly string[] managerAndContractNames = new[]
    {
        "Manager",
        "IService"
    };
    private static readonly string[] serviceAndContractNames = new[]
    {
        "Service",
        "IService"
    };
    private static readonly string[] rootOnlyNames = new[]
    {
        "Root"
    };
    private static readonly string[] rootedCycleNames = new[]
    {
        "Root",
        "B",
        "C"
    };
    private static readonly string[] leftoverCycleNames = new[]
    {
        "B",
        "C",
        "Data"
    };
    private static readonly string[] sharedRootNames = new[]
    {
        "Root",
        "Shared"
    };
    private static readonly string[] selfContainedLeftoverNames = new[]
    {
        "B",
        "C",
        "Shared"
    };
    private static readonly string[] distinctMethodNames = new[]
    {
        "Run",
        "Other"
    };
    [Fact]
    public void ShouldRepeatSharedDependenciesAndTheirDescendantsForEachRoot()
    {
        // Given: the fixture and inputs below.
        var model = Model(types: new[] { Type(name: "A"), Type(name: "B"), Type(name: "Shared"), Type(name: "Leaf", hasMethods: false) }, dependencies: [Link(from: "A", to: "Shared"), Link(from: "A", to: "Leaf"), Link(from: "B", to: "Shared"), Link(from: "Shared", to: "Leaf")]);
        string before = JsonSerializer.Serialize(value: model);
        // When: exercise the operation under test.
        ProjectModel[] result = TestServices.Get<ProjectModelSplitter>().Split(projectModel: model);
        // Then: verify the resulting contract.
        Assert.Equal(expected: 2, actual: result.Length);
        Assert.Equal(expected: firstSharedTreeNames, actual: Names(model: result[0]));
        Assert.Equal(expected: secondSharedTreeNames, actual: Names(model: result[1]));
        Assert.Equal(expected: 3, actual: result[0].Dependencies!.Length);
        Assert.Equal(expected: 2, actual: result[1].Dependencies!.Length);

        Assert.All(collection: result, action: tree =>
        {
            Assert.Equal(expected: model.Name, actual: tree.Name);
            Assert.Equal(expected: model.Path, actual: tree.Path);
        });

        Assert.Equal(expected: before, actual: JsonSerializer.Serialize(value: model));
        AssertSelfContained(models: result);
    }

    [Fact]
    public void ShouldRequireMethodsForRootsButKeepMethodlessDependenciesAndLeftovers()
    {
        // Given: the fixture and inputs below.
        var model = Model(types: new[] { Type(name: "Root"), Type(name: "Child", hasMethods: false), Type(name: "Data", hasMethods: false), Type(name: "Isolated") }, dependencies: [Link(from: "Root", to: "Child")]);
        // When: exercise the operation under test.
        ProjectModel[] result = TestServices.Get<ProjectModelSplitter>().Split(projectModel: model);
        // Then: verify the resulting contract.
        Assert.Equal(expected: 3, actual: result.Length);
        Assert.Equal(expected: rootAndChildNames, actual: Names(model: result[0]));
        Assert.Equal(expected: isolatedNames, actual: Names(model: result[1]));
        Assert.Equal(expected: dataNames, actual: Names(model: result[2]));
    }

    [Fact]
    public void ShouldTreatInterfaceImplementationAndCallsAsDependenciesWithoutResolvingImplementations()
    {
        // Given: the fixture and inputs below.
        var contract = Type(name: "IService");
        contract.FrameworkType = FrameworkType.Interface;
        TypeRelationship implementationLink = Link(from: "Service", to: "IService");
        implementationLink.DependencyType = DependencyType.Inheritance;
        implementationLink.FromMethod = null;
        implementationLink.ToMethod = null;
        var model = Model(types: new[] { Type(name: "Manager"), contract, Type(name: "Service") }, dependencies: [Link(from: "Manager", to: "IService"), implementationLink]);
        // When: exercise the operation under test.
        ProjectModel[] result = TestServices.Get<ProjectModelSplitter>().Split(projectModel: model);
        // Then: verify the resulting contract.
        Assert.Equal(expected: 2, actual: result.Length);
        Assert.Equal(expected: managerAndContractNames, actual: Names(model: result[0]));
        Assert.Equal(expected: serviceAndContractNames, actual: Names(model: result[1]));
        Assert.Equal(expected: DependencyType.Inheritance, actual: Assert.Single(collection: result[1].Dependencies!).DependencyType);
        Assert.Null(@object: result[1].Dependencies![0].FromMethod);
        Assert.Equal(expected: FrameworkType.Interface, actual: result[1].Types![1].FrameworkType);
    }

    [Fact]
    public void ShouldKeepSelfReferencesWithoutDisqualifyingARoot()
    {
        // Given: the fixture and inputs below.
        var model = Model(types: new[] { Type(name: "Root") }, dependencies: [Link(from: "Root", to: "Root")]);
        // When: exercise the operation under test.
        ProjectModel tree = Assert.Single(collection: TestServices.Get<ProjectModelSplitter>().Split(projectModel: model));
        // Then: verify the resulting contract.
        Assert.Equal(expected: rootOnlyNames, actual: Names(model: tree));
        Assert.Single(collection: tree.Dependencies!);
    }

    [Fact]
    public void ShouldStopCyclesWhilePreservingEveryReachableEdge()
    {
        // Given: the fixture and inputs below.
        var model = Model(types: new[] { Type(name: "Root"), Type(name: "B"), Type(name: "C") }, dependencies: [Link(from: "Root", to: "B"), Link(from: "B", to: "C"), Link(from: "C", to: "B")]);
        // When: exercise the operation under test.
        ProjectModel tree = Assert.Single(collection: TestServices.Get<ProjectModelSplitter>().Split(projectModel: model));
        // Then: verify the resulting contract.
        Assert.Equal(expected: rootedCycleNames, actual: Names(model: tree));
        Assert.Equal(expected: 3, actual: tree.Dependencies!.Length);
    }

    [Fact]
    public void ShouldGroupRootlessCyclesAndUnusedTypesIntoOneFinalModel()
    {
        // Given: the fixture and inputs below.
        var model = Model(types: new[] { Type(name: "B"), Type(name: "C"), Type(name: "Data", hasMethods: false) }, dependencies: [Link(from: "B", to: "C"), Link(from: "C", to: "B")]);
        // When: exercise the operation under test.
        ProjectModel remainder = Assert.Single(collection: TestServices.Get<ProjectModelSplitter>().Split(projectModel: model));
        // Then: verify the resulting contract.
        Assert.Equal(expected: leftoverCycleNames, actual: Names(model: remainder));
        Assert.Equal(expected: 2, actual: remainder.Dependencies!.Length);
    }

    [Fact]
    public void ShouldKeepLeftoverDependenciesSelfContainedWhenTheyPointIntoAnExistingTree()
    {
        // Given: the fixture and inputs below.
        var model = Model(types: new[] { Type(name: "Root"), Type(name: "Shared"), Type(name: "B"), Type(name: "C") }, dependencies: [Link(from: "Root", to: "Shared"), Link(from: "B", to: "C"), Link(from: "C", to: "B"), Link(from: "C", to: "Shared")]);
        // When: exercise the operation under test.
        ProjectModel[] result = TestServices.Get<ProjectModelSplitter>().Split(projectModel: model);
        // Then: verify the resulting contract.
        Assert.Equal(expected: 2, actual: result.Length);
        Assert.Equal(expected: sharedRootNames, actual: Names(model: result[0]));
        Assert.Equal(expected: selfContainedLeftoverNames, actual: Names(model: result[1]));
        Assert.Equal(expected: 3, actual: result[1].Dependencies!.Length);
        AssertSelfContained(models: result);
    }

    [Fact]
    public void ShouldCopyMemberAndDependencyDataWithoutSharingMutableObjects()
    {
        // Given: the fixture and inputs below.
        var shared = Type(name: "External", hasMethods: false);
        shared.IsInternal = false;

        shared.Fields = new[]
        {
            new Field
            {
                Name = "field",
                Type = "System.String"
            }
        };

        shared.Properties = new[]
        {
            new Property
            {
                Name = "property",
                Type = "System.Int32"
            }
        };

        shared.Methods = new[]
        {
            new Method
            {
                Name = "Work"
            }
        };

        var model = Model(types: new[] { Type(name: "A"), Type(name: "B"), shared }, dependencies: [Link(from: "A", to: "External"), Link(from: "B", to: "External")]);
        string before = JsonSerializer.Serialize(value: model);
        // When: exercise the operation under test.
        ProjectModel[] result = TestServices.Get<ProjectModelSplitter>().Split(projectModel: model);
        DefinedType first = result[0].Types![1];
        DefinedType second = result[1].Types![1];
        // Then: verify the resulting contract.
        Assert.Equal(expected: JsonSerializer.Serialize(value: shared), actual: JsonSerializer.Serialize(value: first));
        Assert.NotSame(expected: shared, actual: first);
        Assert.NotSame(expected: first, actual: second);
        Assert.NotSame(expected: model.Dependencies![0], actual: result[0].Dependencies![0]);
        first.Fields![0].Name = "changed";
        first.Properties![0].Type = "changed";
        first.Methods![0].Name = "changed";
        result[0].Dependencies![0].ToMethod = "changed";
        Assert.Equal(expected: before, actual: JsonSerializer.Serialize(value: model));
        Assert.Equal(expected: "field", actual: second.Fields![0].Name);
        Assert.Equal(expected: "System.Int32", actual: second.Properties![0].Type);
        Assert.Equal(expected: "Work", actual: second.Methods![0].Name);
    }

    [Fact]
    public void ShouldPreserveDifferentMethodLinksBetweenTheSameTypes()
    {
        // Given: the fixture and inputs below.
        TypeRelationship first = Link(from: "A", to: "B");
        TypeRelationship second = Link(from: "A", to: "B");
        second.FromMethod = "Other";
        var model = Model(types: new[] { Type(name: "A"), Type(name: "B") }, dependencies: [first, second]);
        // When: exercise the operation under test.
        ProjectModel tree = Assert.Single(collection: TestServices.Get<ProjectModelSplitter>().Split(projectModel: model));
        // Then: verify the resulting contract.
        Assert.Equal(expected: 2, actual: tree.Types!.Length);
        Assert.Equal(expected: distinctMethodNames, actual: tree.Dependencies!.Select(selector: link => link.FromMethod));
    }

    [Fact]
    public void ShouldReturnNoModelsForEmptyInputAndTreatMissingCollectionsAsEmpty()
    {
        // Given: the fixture and inputs below.
        // When: exercise the operation under test.
        Assert.Empty(collection: TestServices.Get<ProjectModelSplitter>().Split(projectModel: new ProjectModel()));
        var model = Model(types: new[] { new DefinedType { Name = "Data" } });
        // Then: verify the resulting contract.
        ProjectModel remainder = Assert.Single(collection: TestServices.Get<ProjectModelSplitter>().Split(projectModel: model));
        Assert.Empty(collection: remainder.Types![0].Methods!);
        Assert.Empty(collection: remainder.Types[0].Fields!);
        Assert.Empty(collection: remainder.Types[0].Properties!);
        Assert.Empty(collection: remainder.Dependencies!);
    }

    [Fact]
    public void ShouldRejectNullInput()
    {
        // Given
        var splitter = TestServices.Get<ProjectModelSplitter>();
        // When
        Action split = () => splitter.Split(projectModel: null !);
        // Then
        Assert.Throws<ArgumentNullException>(testCode: split);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ShouldRejectDependenciesWithUnknownEndpoints(bool missingSource)
    {
        // Given: the fixture and inputs below.
        var model = Model(types: new[] { Type(name: "Known") }, dependencies: [missingSource ? Link(from: "Missing", to: "Known") : Link(from: "Known", to: "Missing")]);
        // When: exercise the operation under test.
        // Then: verify the resulting contract.
        Assert.Throws<ArgumentException>(testCode: () => TestServices.Get<ProjectModelSplitter>().Split(projectModel: model));
    }

    [Fact]
    public void ShouldRejectAmbiguousTypeNames()
    {
        // Given: the fixture and inputs below.
        var model = Model(types: new[] { Type(name: "Duplicate"), Type(name: "Duplicate") });
        // When: exercise the operation under test.
        // Then: verify the resulting contract.
        Assert.Throws<ArgumentException>(testCode: () => TestServices.Get<ProjectModelSplitter>().Split(projectModel: model));
    }

    private static DefinedType Type(string name, bool hasMethods = true) =>
        new()
    {
        Name = name,
        IsInternal = true,
        FrameworkType = FrameworkType.Class,
        Methods = hasMethods ? new[]
        {
            new Method
            {
                Name = "Run"
            }
        }

        : Array.Empty<Method>()
    };

    private static TypeRelationship Link(string from, string to) =>
        new()
    {
        DependencyType = DependencyType.Consumed,
        FromType = from,
        ToType = to,
        FromMethod = "Run",
        ToMethod = "Work"
    };

    private static ProjectModel Model(DefinedType[] types, params TypeRelationship[] dependencies) =>
        new()
    {
        Name = "Example",
        Path = "Example.csproj",
        Types = types,
        Dependencies = dependencies
    };

    private static string[] Names(ProjectModel model) =>
        model.Types!.Select(selector: type => type.Name!)
        .ToArray();

    private static void AssertSelfContained(ProjectModel[] models)
    {
        foreach (ProjectModel model in models)
        {
            string[] names = Names(model: model);

            Assert.All(collection: model.Dependencies!, action: link =>
            {
                Assert.Contains(expected: link.FromType, collection: names);
                Assert.Contains(expected: link.ToType, collection: names);
            });
        }
    }
}