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
        var links = model.ProjectModels.SelectMany(p => p.Dependencies ?? []).Where(d => d.IsComposition && d.DependencyType != DependencyType.Inheritance).ToLookup(d => d.FromType!);
        var calls = model.ProjectModels.SelectMany(p => p.Dependencies ?? [])
            .Where(d => d.DependencyType != DependencyType.Inheritance &&
                d.FromMethod != null && d.ToMethod != null && d.ToType != null)
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
                var members = (type.CompositionMembers ?? []).ToDictionary(m => m.Id ?? m.Name);
                foreach (var method in type.Methods ?? [])
                    if (!members.Values.Any(m => m.Name == method.Name)) members.TryAdd(method.Name!,new(method.Name!,[]));
                // Older saved models have only the architectural relationship list. New
                // models carry direct calls, including local helpers and lambda ownership.
                foreach (var group in calls[name].GroupBy(d => d.FromMethod!))
                    if (!members.Values.Any(m => m.Name == group.Key)) members.TryAdd(group.Key,new(group.Key,[]));
                foreach (var group in links[name].Where(d => d.FromMethod != null).GroupBy(d => d.FromMethod!))
                {
                    var member = members.FirstOrDefault(m => m.Value.Name == group.Key);
                    if (member.Value is { Calls: null }) members[member.Key] = member.Value with { TypeNames = member.Value.TypeNames.Concat(group.Select(d => d.ToType!)).Distinct().ToArray() };
                }
                var attributed = members.Values.SelectMany(m => m.TypeNames).ToHashSet();
                var other = links[name].Where(d => d.FromMethod == null && !attributed.Contains(d.ToType!)).Select(d => d.ToType!).Distinct().ToArray();
                if (other.Length > 0) members["Type references"] = new("Type references",other);
                void RenderMember(string key, int memberDepth, string parentNode)
                {
                    var member = members[key];
                    string label = member.Name.Length == 0 ? "Lambda Expression" : member.Name == ".ctor" ? "Constructor" : member.Name;
                    string memberId = Add(name,label + (member.IsDeclaration ? " (declaration)" : ""),memberDepth,parentNode);
                    foreach (string target in member.TypeNames.Distinct().OrderBy(n => n,StringComparer.Ordinal))
                        Expand(target,memberDepth+1,memberId,next);
                    var directCalls = member.Calls ?? calls[name].Where(d => d.FromMethod == member.Name).Select(d => new MethodReference(d.ToType!,d.ToMethod!)).ToArray();
                    foreach (var call in directCalls.Distinct().OrderBy(d => d.TypeName,StringComparer.Ordinal).ThenBy(d => d.MethodName,StringComparer.Ordinal))
                        Add(call.TypeName,Short(call.TypeName) + " → " + (call.MethodName == ".ctor" ? "Constructor" : call.MethodName),memberDepth+1,memberId);
                    foreach (string child in members.Where(m => m.Value.ParentId == key).Select(m => m.Key).OrderBy(k => k,StringComparer.Ordinal))
                        RenderMember(child,memberDepth+1,memberId);
                }
                foreach (var member in members.Where(m => m.Value.ParentId == null || !members.ContainsKey(m.Value.ParentId))
                    .OrderBy(m => m.Value.Name == ".ctor" ? "" : m.Value.Name,StringComparer.Ordinal).ThenBy(m => m.Key,StringComparer.Ordinal))
                    RenderMember(member.Key,depth+1,id);
            }
            Expand(root,0,null,new(StringComparer.Ordinal));
            trees.Add(new(owned[root].Project.Name + " / " + Short(root),nodes.ToArray()));
        }
        return trees.ToArray();
    }
}
