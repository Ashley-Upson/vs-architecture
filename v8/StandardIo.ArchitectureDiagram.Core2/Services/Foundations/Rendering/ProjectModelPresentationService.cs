// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core2.Models;

namespace StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Rendering;
internal sealed class ProjectModelPresentationService : IProjectModelPresentationService
{
    /// <summary>Prepares visible types while keeping remaining contract calls connected to every available implementation.</summary>
    public ProjectModelPresentation Prepare(ProjectModel model, DiagramTypes diagramType = DiagramTypes.Architecture)
    {
        ArgumentNullException.ThrowIfNull(argument: model);
        DefinedType[] types = model.Types ?? Array.Empty<DefinedType>();
        TypeRelationship[] links = model.Dependencies ?? Array.Empty<TypeRelationship>();

        if (types.Any(predicate: type => string.IsNullOrWhiteSpace(value: type.Name)))
        {
            throw new ArgumentException(message: "Every rendered type needs a name.", paramName: nameof(model));
        }

        var byName = types.ToDictionary(keySelector: type => type.Name!, comparer: StringComparer.Ordinal);

        if (links.Any(predicate: link => link.FromType is null || link.ToType is null || !byName.ContainsKey(key: link.FromType) || !byName.ContainsKey(key: link.ToType)))
        {
            throw new ArgumentException(message: "Every rendered dependency endpoint must exist in its model.", paramName: nameof(model));
        }

        var inheritance = links.Where(predicate: link => link.DependencyType == DependencyType.Inheritance)
            .ToLookup(keySelector: link => link.FromType!);

        var contracts = new Dictionary<string, string[]>(comparer: StringComparer.Ordinal);

        foreach (DefinedType type in types.Where(predicate: type => type.FrameworkType == FrameworkType.Class))
        {
            var visited = new HashSet<string>(comparer: StringComparer.Ordinal);
            var pending = new Queue<string>();
            pending.Enqueue(item: type.Name!);

            while (pending.Count > 0)
            {
                string current = pending.Dequeue();

                if (!visited.Add(item: current))
                {
                    continue;
                }

                foreach (TypeRelationship link in inheritance[current])
                {
                    pending.Enqueue(item: link.ToType!);
                }
            }

            contracts.Add(key: type.Name!, value: visited.Where(predicate: name => byName[name].FrameworkType == FrameworkType.Interface)
                .OrderBy(keySelector: name => name, comparer: StringComparer.Ordinal)
                .ToArray());
        }

        var owners = contracts.SelectMany(selector: pair => pair.Value.Select(selector: contract => (Contract: contract, Owner: pair.Key)))
            .ToLookup(keySelector: pair => pair.Contract, elementSelector: pair => pair.Owner, comparer: StringComparer.Ordinal);

        var hidden = owners.Select(selector: group => group.Key)
            .ToHashSet(comparer: StringComparer.Ordinal);

        var dataTypes = types.Where(type => diagramType == DiagramTypes.Architecture && type.IsInternal && (type.Methods?.Length ?? 0) == 0)
            .Select(type => type.Name!).ToHashSet(StringComparer.Ordinal);

        DefinedType[] visible = types.Where(predicate: type => !hidden.Contains(item: type.Name!) && !dataTypes.Contains(type.Name!))
            .ToArray();

        var labels = visible.ToDictionary(keySelector: type => type.Name!, elementSelector: type => type.Name!.Split(separator: '.')
            .Last() + (contracts.TryGetValue(key: type.Name!, value: out string[]? implemented) && implemented.Length > 0 ? "\n" + string.Join(separator: ", ", values: implemented.Select(selector: name => name.Split(separator: '.')
            .Last())) : ""), comparer: StringComparer.Ordinal);

        var dependencies = new List<TypeRelationship>();

        foreach (TypeRelationship link in links)
        {
            if (dataTypes.Contains(link.FromType!) || dataTypes.Contains(link.ToType!) || hidden.Contains(item: link.FromType!))
            {
                continue;
            }

            if (!hidden.Contains(item: link.ToType!))
            {
                dependencies.Add(item: link);
                continue;
            }

            if (link.DependencyType == DependencyType.Inheritance)
            {
                continue;
            }

            foreach (string owner in owners[link.ToType!].Where(owner => !dataTypes.Contains(owner)))
            {
                dependencies.Add(item: new TypeRelationship { FromType = link.FromType, ToType = owner, DependencyType = link.DependencyType });
            }
        }

        return new ProjectModelPresentation(new ProjectModel { Name = model.Name, Path = model.Path, Types = visible, Dependencies = dependencies.DistinctBy(keySelector: link => (link.FromType, link.ToType, link.DependencyType))
            .Select(selector: link => new TypeRelationship { FromType = link.FromType, ToType = link.ToType, DependencyType = link.DependencyType })
            .ToArray() }, labels);
    }
}