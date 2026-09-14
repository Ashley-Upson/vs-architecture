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
                (x.Type.HasDeclaredBehaviour == true || x.Type.Methods is { Length: > 0 })).OrderBy(x => x.Type.Name,StringComparer.Ordinal).ToArray();
        var dependencies = model.ProjectModels.SelectMany(p => p.Dependencies ?? []).ToLookup(d => d.FromType!);
        var trees = new List<CompositionTree>();
        string Short(string name) => Regex.Replace(name, @"(?:[A-Za-z_]\w*\.)+", "");
        foreach (var entry in owned)
        {
            var type = entry.Type;
            string name = type.Name!;
            var nodes = new List<CompositionTreeNode>();
            string Add(string target, string label, int depth, string? parent, string? memberId = null, string? targetMemberId = null, string? targetType = null, string[]? details = null)
            {
                string id = "tree-" + trees.Count + "-" + nodes.Count;
                nodes.Add(new(id,target,label,depth,parent) { MemberId=memberId,TargetMemberId=targetMemberId,TargetTypeName=targetType,Details=details });
                return id;
            }
            string root = Add(name,Short(name),0,null);
            if (type.BaseTypeName is not null) Add(type.BaseTypeName,Short(type.BaseTypeName)+" (base type)",1,root,targetType:type.BaseTypeName);
            foreach (string contract in type.InterfaceNames ?? []) Add(contract,Short(contract),1,root,targetType:contract);
            var members = (type.CompositionMembers ?? []).ToDictionary(m => m.Id ?? m.Name);
            foreach (var method in type.Methods ?? [])
                if (!members.Values.Any(m => m.Name == method.Name)) members.TryAdd(method.Name!,new(method.Name!,[]));
            foreach (var group in dependencies[name].Where(d => d.FromMethod != null).GroupBy(d => d.FromMethod!))
                if (!members.Values.Any(m => m.Name == group.Key)) members.TryAdd(group.Key,new(group.Key,[]));
            MethodReference[] Calls(CompositionMember member) => member.Calls ?? dependencies[name]
                .Where(d => d.DependencyType != DependencyType.Inheritance && d.FromMethod == member.Name && d.ToMethod != null && d.ToType != null)
                .Select(d => new MethodReference(d.ToType!,d.ToMethod!)).Distinct().ToArray();
            string? Local(MethodReference call) => call.TypeName != name ? null : call.MethodId is not null && members.ContainsKey(call.MethodId) ? call.MethodId :
                members.Where(m => m.Value.Name == call.MethodName).Select(m => m.Key).Take(2).ToArray() is { Length: 1 } matches ? matches[0] : null;
            var rendered = new HashSet<string>(StringComparer.Ordinal);
            void RenderMember(string key, int depth, string parent)
            {
                if (!rendered.Add(key)) return;
                var member = members[key];
                string label = member.Name.Length == 0 ? "Lambda Expression" : member.Name == ".ctor" ? "Constructor" : member.Name;
                string[]? details = model.DiagramType == DiagramTypes.CallChain ? new[] { "Inputs" }
                    .Concat((member.Inputs ?? []).Select(p => "  " + Short(p.Type ?? "?") + " : " + p.Name))
                    .Concat(new[] { "Outputs", "  " + (member.Output is null ? "None" : Short(member.Output)) }).ToArray() : null;
                string id = Add(name,label+(member.IsDeclaration ? " (declaration)" : ""),depth,parent,memberId:key,details:details);
                foreach (string target in member.TypeNames.Distinct().OrderBy(n => n,StringComparer.Ordinal))
                    Add(target,Short(target),depth+1,id,targetType:target);
                foreach (var call in Calls(member).Distinct().OrderBy(c => c.TypeName,StringComparer.Ordinal).ThenBy(c => c.MethodName,StringComparer.Ordinal))
                {
                    string? local = Local(call);
                    Add(call.TypeName,Short(call.TypeName)+" → "+(call.MethodName==".ctor"?"Constructor":call.MethodName.Length==0?"Lambda Expression":call.MethodName),depth+1,id,
                        targetMemberId:local ?? call.MethodId ?? call.MethodName,targetType:call.TypeName);
                }
                foreach (string child in members.Where(m => m.Value.ParentId == key && m.Value.Name.Length == 0).Select(m => m.Key).OrderBy(k => k,StringComparer.Ordinal))
                    RenderMember(child,depth+1,id);
            }
            var top = members.Where(m => m.Value.Name.Length > 0 || m.Value.ParentId == null || !members.ContainsKey(m.Value.ParentId))
                .OrderBy(m => m.Value.Name == ".ctor" ? 0 : (m.Value.IsPublicContract ?? (type.Methods ?? []).Any(method=>method.Name==m.Value.Name)) ? 1 : 2)
                .ThenBy(m => m.Value.Name,StringComparer.Ordinal).ThenBy(m => m.Key,StringComparer.Ordinal).Select(m=>m.Key).ToArray();
            foreach (string key in top) RenderMember(key,1,root);
            string plainName=name.Split('<')[0];int dot=plainName.LastIndexOf('.');
            trees.Add(new(entry.Project.Name+" / "+Short(name),nodes.ToArray()) {
                ProjectName=entry.Project.Name ?? "Project",NamespaceName=type.NamespaceName ?? (dot<0?"":plainName[..dot]) });
        }
        return trees.ToArray();
    }
}
