using System;
using System.Collections.Generic;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core2.Models;
namespace StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Rendering;
internal sealed class ProjectModelPresentationService : IProjectModelPresentationService
{
    public ProjectModelPresentation Prepare(ProjectModel model)
    {
        ArgumentNullException.ThrowIfNull(argument: model);
        DefinedType[] types = model.Types ?? Array.Empty<DefinedType>();
        Dependency[] links = model.Dependencies ?? Array.Empty<Dependency>();
        if (types.Any(type => string.IsNullOrWhiteSpace(value: type.Name)))
            throw new ArgumentException(message: "Every rendered type needs a name.", paramName: nameof(model));
        var byName = types.ToDictionary(keySelector: type => type.Name!, comparer: StringComparer.Ordinal);
        if (links.Any(link => link.FromType is null || link.ToType is null || !byName.ContainsKey(link.FromType) || !byName.ContainsKey(link.ToType)))
            throw new ArgumentException(message: "Every rendered dependency endpoint must exist in its model.", paramName: nameof(model));
        var inheritance = links.Where(link => link.DependencyType == DependencyType.Inheritance).ToLookup(link => link.FromType!);
        var contracts = new Dictionary<string, string[]>(comparer: StringComparer.Ordinal);
        foreach (DefinedType type in types.Where(type => type.FrameworkType == FrameworkType.Class))
        {
            var visited = new HashSet<string>(comparer: StringComparer.Ordinal);
            var pending = new Queue<string>();
            pending.Enqueue(item: type.Name!);
            while (pending.Count > 0)
            {
                string current = pending.Dequeue();
                if (!visited.Add(item: current)) continue;
                foreach (Dependency link in inheritance[current]) pending.Enqueue(item: link.ToType!);
            }
            contracts.Add(key: type.Name!, value: visited.Where(name => byName[name].FrameworkType == FrameworkType.Interface)
                .OrderBy(name => name, StringComparer.Ordinal).ToArray());
        }
        var owners = contracts.SelectMany(pair => pair.Value.Select(contract => (Contract: contract, Owner: pair.Key)))
            .ToLookup(pair => pair.Contract, pair => pair.Owner, StringComparer.Ordinal);
        var hidden = owners.Select(group => group.Key).ToHashSet(comparer: StringComparer.Ordinal);
        DefinedType[] visible = types.Where(type => !hidden.Contains(type.Name!)).ToArray();
        var labels = visible.ToDictionary(type => type.Name!, type => type.Name!.Split('.').Last()
            + (contracts.TryGetValue(type.Name!, out string[]? implemented) && implemented.Length > 0
                ? "\n" + string.Join(", ", implemented.Select(name => name.Split('.').Last())) : ""), StringComparer.Ordinal);
        var dependencies = new List<Dependency>();
        foreach (Dependency link in links)
        {
            if (hidden.Contains(link.FromType!)) continue;
            if (!hidden.Contains(link.ToType!)) { dependencies.Add(item: link); continue; }
            if (link.DependencyType == DependencyType.Inheritance) continue;
            // A remaining contract call must still reach every available implementation.
            foreach (string owner in owners[link.ToType!]) dependencies.Add(item: new Dependency
            {
                FromType = link.FromType, ToType = owner, DependencyType = link.DependencyType
            });
        }
        return new ProjectModelPresentation(new ProjectModel
        {
            Name = model.Name, Path = model.Path, Types = visible, Dependencies = dependencies
                .DistinctBy(link => (link.FromType, link.ToType, link.DependencyType))
                .Select(link => new Dependency { FromType = link.FromType, ToType = link.ToType, DependencyType = link.DependencyType }).ToArray()
        }, labels);
    }
}
