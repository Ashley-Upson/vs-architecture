// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core2.Models;
using StandardIo.ArchitectureDiagram.Core2.Services.Foundations.ProjectModels;

namespace StandardIo.ArchitectureDiagram.Core2.Services.Processings.ProjectModels;
internal sealed class ProjectModelSplitterProcessingService : IProjectModelSplitterProcessingService
{
    private readonly IProjectModelService projectModelService;
    public ProjectModelSplitterProcessingService(IProjectModelService projectModelService) => this.projectModelService = projectModelService;
    /// <summary>Splits roots and retains reused targets in the leftover model so every link remains self-contained.</summary>
    public ProjectModel[] Split(ProjectModel projectModel)
    {
        ArgumentNullException.ThrowIfNull(argument: projectModel);
        DefinedType[] types = projectModel.Types ?? Array.Empty<DefinedType>();
        TypeRelationship[] dependencies = projectModel.Dependencies ?? Array.Empty<TypeRelationship>();
        var typesByName = types.ToDictionary(keySelector: type => type.Name!, comparer: StringComparer.Ordinal);

        foreach (TypeRelationship dependency in dependencies)
        {
            if (dependency.FromType is null || dependency.ToType is null || !typesByName.ContainsKey(key: dependency.FromType) || !typesByName.ContainsKey(key: dependency.ToType))
            {
                throw new ArgumentException(message: "Every dependency endpoint must identify a type in the project model.", paramName: nameof(projectModel));
            }
        }

        var outgoing = dependencies.ToLookup(keySelector: link => link.FromType!, comparer: StringComparer.Ordinal);

        var referencedByOthers = dependencies.Where(predicate: link => link.FromType != link.ToType)
            .Select(selector: link => link.ToType!)
            .ToHashSet(comparer: StringComparer.Ordinal);

        DefinedType[] roots = types.Where(predicate: type => type.Methods is { Length: > 0 } && !referencedByOthers.Contains(item: type.Name!))
            .ToArray();

        var included = new HashSet<string>(comparer: StringComparer.Ordinal);
        var results = new List<ProjectModel>();

        foreach (DefinedType root in roots)
        {
            string[] names = CollectReachableTypes(startingNames: new[] { root.Name! }, outgoing: outgoing);
            var tree = CreateModel(source: projectModel, names: names, types: typesByName, dependencies: dependencies);
            tree.RootTypeName = root.Name;
            results.Add(tree);
            included.UnionWith(other: names);
        }

        string[] leftovers = types.Where(predicate: type => !included.Contains(item: type.Name!))
            .Select(selector: type => type.Name!)
            .ToArray();

        if (leftovers.Length > 0)
        {
            string[] names = CollectReachableTypes(startingNames: leftovers, outgoing: outgoing);
            var tree = CreateModel(source: projectModel, names: names, types: typesByName, dependencies: dependencies);
            results.Add(tree);
        }

        return results.ToArray();
    }

    private ProjectModel CreateModel(ProjectModel source, string[] names, Dictionary<string, DefinedType> types, TypeRelationship[] dependencies)
    {
        var selected = names.ToHashSet(comparer: StringComparer.Ordinal);

        var model = this.projectModelService.CreateProjectModel(source: source, types: names.Select(selector: name => types[name])
            .ToArray(), dependencies: dependencies.Where(predicate: link => selected.Contains(item: link.FromType!))
            .ToArray());
        model.IsSplitTree = true;
        return model;
    }

    private static string[] CollectReachableTypes(string[] startingNames, ILookup<string, TypeRelationship> outgoing)
    {
        var pending = new Queue<string>(collection: startingNames);
        var visited = new HashSet<string>(comparer: StringComparer.Ordinal);
        var names = new List<string>();

        while (pending.Count > 0)
        {
            string name = pending.Dequeue();

            if (!visited.Add(item: name))
            {
                continue;
            }

            names.Add(item: name);

            foreach (TypeRelationship dependency in outgoing[name])
            {
                pending.Enqueue(item: dependency.ToType!);
            }
        }

        return names.ToArray();
    }
}