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

    public ProjectModelSplitterProcessingService(IProjectModelService projectModelService) =>
        this.projectModelService = projectModelService;

    public ProjectModel[] Split(ProjectModel projectModel)
    {
        ArgumentNullException.ThrowIfNull(argument: projectModel);
        DefinedType[] types = projectModel.Types ?? Array.Empty<DefinedType>();
        Dependency[] dependencies = projectModel.Dependencies ?? Array.Empty<Dependency>();
        var typesByName = types.ToDictionary(keySelector: type => type.Name!, comparer: StringComparer.Ordinal);

        foreach (Dependency dependency in dependencies)
        {
            if (dependency.FromType is null || dependency.ToType is null
                || !typesByName.ContainsKey(key: dependency.FromType) || !typesByName.ContainsKey(key: dependency.ToType))
            {
                throw new ArgumentException(message: "Every dependency endpoint must identify a type in the project model.", paramName: nameof(projectModel));
            }
        }

        var outgoing = dependencies.ToLookup(keySelector: link => link.FromType!, comparer: StringComparer.Ordinal);
        var referencedByOthers = dependencies.Where(link => link.FromType != link.ToType)
            .Select(link => link.ToType!).ToHashSet(comparer: StringComparer.Ordinal);
        DefinedType[] roots = types.Where(type => type.Methods is { Length: > 0 }
            && !referencedByOthers.Contains(item: type.Name!)).ToArray();
        var included = new HashSet<string>(comparer: StringComparer.Ordinal);
        var results = new List<ProjectModel>();

        foreach (DefinedType root in roots)
        {
            string[] names = CollectReachableTypes(startingNames: new[] { root.Name! }, outgoing: outgoing);
            results.Add(item: CreateModel(source: projectModel, names: names, types: typesByName, dependencies: dependencies));
            included.UnionWith(other: names);
        }

        string[] leftovers = types.Where(type => !included.Contains(item: type.Name!)).Select(type => type.Name!).ToArray();

        if (leftovers.Length > 0)
        {
            // Preserve targets already used by a root so leftover links remain self-contained.
            string[] names = CollectReachableTypes(startingNames: leftovers, outgoing: outgoing);
            results.Add(item: CreateModel(source: projectModel, names: names, types: typesByName, dependencies: dependencies));
        }

        return results.ToArray();
    }

    private ProjectModel CreateModel(ProjectModel source, string[] names, IReadOnlyDictionary<string, DefinedType> types, Dependency[] dependencies)
    {
        var selected = names.ToHashSet(comparer: StringComparer.Ordinal);
        return this.projectModelService.CreateProjectModel(
            source: source,
            types: names.Select(name => types[name]).ToArray(),
            dependencies: dependencies.Where(link => selected.Contains(item: link.FromType!)).ToArray());
    }

    private static string[] CollectReachableTypes(string[] startingNames, ILookup<string, Dependency> outgoing)
    {
        var pending = new Queue<string>(collection: startingNames);
        var visited = new HashSet<string>(comparer: StringComparer.Ordinal);
        var names = new List<string>();

        while (pending.Count > 0)
        {
            string name = pending.Dequeue();
            if (!visited.Add(item: name)) continue;
            names.Add(item: name);

            foreach (Dependency dependency in outgoing[name])
            {
                pending.Enqueue(item: dependency.ToType!);
            }
        }

        return names.ToArray();
    }
}
