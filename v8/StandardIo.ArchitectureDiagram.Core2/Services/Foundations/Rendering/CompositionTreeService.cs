using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using StandardIo.ArchitectureDiagram.Core2.Models;
namespace StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Rendering;
internal interface ICompositionTreeService { CompositionTree[] Build(RenderModel model); }
internal sealed class CompositionTreeService : ICompositionTreeService
{
    public CompositionTree[] Build(RenderModel model)
    {
        var owned = model.ProjectModels.SelectMany(p => (p.Types ?? []).Where(t => t.IsInternal).Select(t => (Project: p, Type: t)))
            .DistinctBy(x => x.Type.Name).Where(x => !x.Type.IsDataType && x.Type.HasDeclaredBehaviour != false &&
                (x.Type.HasDeclaredBehaviour == true || x.Type.Methods is { Length: > 0 })).ToDictionary(x => x.Type.Name!);
        var links = model.ProjectModels.SelectMany(p => p.Dependencies ?? []).Where(d => d.IsComposition && d.DependencyType != DependencyType.Inheritance && !d.IsResultExtension).ToLookup(d => d.FromType!);
        var calls = model.ProjectModels.SelectMany(p => p.Dependencies ?? [])
            .Where(d => !d.IsComposition && !d.IsResultExtension && d.DependencyType != DependencyType.Inheritance &&
                d.FromMethod != null && d.ToMethod != null && d.ToType != null && owned.ContainsKey(d.ToType))
            .ToLookup(d => d.FromType!);
        IEnumerable<string> Targets(string name) => links[name].Select(d => d.ToType!).Concat((owned[name].Type.CompositionMembers ?? []).SelectMany(m => m.TypeNames)).Where(owned.ContainsKey).Where(n => n != name).Distinct();
        var incoming = owned.Keys.SelectMany(Targets).ToHashSet(StringComparer.Ordinal);
        var roots = owned.Keys.Where(n => !incoming.Contains(n)).OrderBy(n => n, StringComparer.Ordinal).ToList();
        var covered = new HashSet<string>(StringComparer.Ordinal);
        var trees = new List<CompositionTree>();
        var renderedRoots = new HashSet<string>(StringComparer.Ordinal);
        string Short(string name) => Regex.Replace(name, @"(?:[A-Za-z_]\w*\.)+", "");
        // Remaining components (including cycles) also receive an entry tree.
        foreach (string root in roots.Concat(owned.Keys.OrderBy(n => n, StringComparer.Ordinal)))
        {
            if (!roots.Contains(root) && covered.Contains(root) || !renderedRoots.Add(root)) continue;
            var nodes = new List<CompositionTreeNode>();
            string Add(string type, string label, int depth, string? parent)
            {
                string id = "tree-" + trees.Count + "-" + nodes.Count;
                nodes.Add(new(id,type,label,depth,parent)); return id;
            }
            void Expand(string name, int depth, string? parent, HashSet<string> path)
            {
                bool cycle = path.Contains(name);
                string id = Add(name, Short(name) + (cycle ? " (circular reference)" : ""), depth, parent);
                if (cycle || !owned.TryGetValue(name, out var entry)) return;
                covered.Add(name);
                var next = new HashSet<string>(path, StringComparer.Ordinal) { name };
                var type = entry.Type;
                if (type.BaseTypeName is not null) Add(type.BaseTypeName, Short(type.BaseTypeName) + " (base type)",depth+1,id);
                foreach (string contract in type.InterfaceNames ?? []) Add(contract,Short(contract),depth+1,id);
                var members = (type.CompositionMembers ?? []).ToDictionary(m => m.Name,m => m.TypeNames.AsEnumerable());
                foreach (var method in type.Methods ?? []) members.TryAdd(method.Name!, []);
                foreach (var group in links[name].Where(d => d.FromMethod != null).GroupBy(d => d.FromMethod!))
                    members[group.Key] = members.GetValueOrDefault(group.Key, []).Concat(group.Select(d => d.ToType!));
                foreach (var call in calls[name]) members.TryAdd(call.FromMethod!, []);
                var attributed = members.Values.SelectMany(n => n).ToHashSet();
                var other = links[name].Where(d => d.FromMethod == null && !attributed.Contains(d.ToType!)).Select(d => d.ToType!).Distinct().Where(owned.ContainsKey).ToArray();
                if (other.Length > 0) members["Type references"] = other;
                foreach (var member in members.OrderBy(m => m.Key == ".ctor" ? "" : m.Key, StringComparer.Ordinal))
                {
                    string memberId = Add(name,member.Key.Length == 0 ? "Lambda Expression" : member.Key == ".ctor" ? "Constructor" : member.Key,depth+1,id);
                    foreach (string target in member.Value.Distinct().Where(owned.ContainsKey).OrderBy(n => n,StringComparer.Ordinal))
                        Expand(target,depth+2,memberId,next);
                    foreach (var call in calls[name].Where(d => d.FromMethod == member.Key).DistinctBy(d => (d.ToType,d.ToMethod))
                        .OrderBy(d => d.ToType,StringComparer.Ordinal).ThenBy(d => d.ToMethod,StringComparer.Ordinal))
                        Add(call.ToType!,Short(call.ToType!) + " → " + call.ToMethod,depth+2,memberId);
                }
            }
            Expand(root,0,null,new(StringComparer.Ordinal));
            trees.Add(new(owned[root].Project.Name + " / " + Short(root),nodes.ToArray()));
        }
        return trees.ToArray();
    }
}
