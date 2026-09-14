using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using StandardIo.ArchitectureDiagram.Core2.Models;
namespace StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Rendering;
internal interface ICallChainModelService { ContextualDiagram Prepare(RenderModel model); }
internal sealed class CallChainModelService : ICallChainModelService
{
    public ContextualDiagram Prepare(RenderModel model)
    {
        var owned = model.ProjectModels.SelectMany(p => (p.Types ?? []).Where(t=>t.IsInternal).Select(t=>(Project:p.Name,Type:t)))
            .DistinctBy(x=>x.Type.Name).ToDictionary(x=>x.Type.Name!);
        var members = owned.ToDictionary(x=>x.Key,x=>(x.Value.Type.CompositionMembers ?? (x.Value.Type.Methods ?? []).Select(m=>new CompositionMember(m.Name!,[])).ToArray())
            .ToDictionary(m=>m.Id??m.Name));
        bool Public(string type,CompositionMember member) => member.IsPublicContract ??
            (member.Name.Length>0 && member.Name[0]!='.' && (owned[type].Type.Methods ?? []).Any(m=>m.Name==member.Name));
        var rawLinks = model.ProjectModels.SelectMany(p=>p.Dependencies ?? []).ToLookup(d=>d.FromType!);
        MethodReference[] Calls(string type,CompositionMember member) => member.Calls ?? rawLinks[type]
            .Where(d=>d.FromMethod==member.Name && d.ToMethod!=null && d.ToType!=null && !d.IsComposition)
            .Select(d=>new MethodReference(d.ToType!,d.ToMethod!)).Distinct().ToArray();
        var nodes = new Dictionary<(string Type,string Method),CompositionTreeNode>();
        var sourceMembers=new Dictionary<string,(string Type,string Name)>();
        var trees = new Dictionary<string,List<CompositionTreeNode>>();
        string Short(string name) => Regex.Replace(name,@"(?:[A-Za-z_]\w*\.)+","");
        string Ensure(string type,string key,string label,CompositionMember? member)
        {
            if(nodes.TryGetValue((type,key),out var existing)) return existing.Id;
            if(!trees.TryGetValue(type,out var tree))
            {
                tree = [new("call-type-"+trees.Count,type,Short(type),0,null)]; trees.Add(type,tree);
            }
            string id="call-method-"+nodes.Count;
            string[]? details=member is null ? null : (member.Inputs is { Length: > 0 } ? new[]{"Inputs"}.Concat(member.Inputs.Select(p=>"  "+Short(p.Type??"?")+" : "+p.Name)) : Array.Empty<string>())
                .Concat(new[]{"Outputs","  "+Short(member.Output??"System.Void")}).ToArray();
            var node=new CompositionTreeNode(id,type,label,1,tree[0].Id) { MemberId=key,Details=details };
            nodes[(type,key)]=node;sourceMembers[id]=(type,label);tree.Add(node);return id;
        }
        foreach(var type in owned.Keys.OrderBy(x=>x,StringComparer.Ordinal))
        foreach(var member in members[type].Where(m=>Public(type,m.Value)&&!m.Value.IsDeclaration)) Ensure(type,member.Key,member.Value.Name,member.Value);
        var links=new HashSet<ContextualLink>();
        IEnumerable<MethodReference> Resolve(string source,MethodReference call)
        {
            if(!owned.TryGetValue(call.TypeName,out var contract)||contract.Type.FrameworkType!=FrameworkType.Interface) return [call];
            var origin=sourceMembers[source];
            var implementations=rawLinks[origin.Type].Where(d=>!d.IsComposition&&d.FromMethod==origin.Name&&d.ToMethod==call.MethodName&&d.ToType!=call.TypeName)
                .Select(d=>d.ToType!).Distinct().Where(t=>owned.TryGetValue(t,out var candidate)&&candidate.Type.FrameworkType==FrameworkType.Class&&
                    ((candidate.Type.InterfaceNames??[]).Contains(call.TypeName)||rawLinks[t].Any(d=>d.DependencyType==DependencyType.Inheritance&&d.ToType==call.TypeName)));
            var resolved=new List<MethodReference>();
            foreach(string type in implementations)
            {
                var candidates=members[type].Where(m=>Public(type,m.Value)&&(m.Value.Name==call.MethodName||m.Value.Name.EndsWith("."+call.MethodName,StringComparison.Ordinal))).ToArray();
                if(candidates.Length>1&&call.MethodId is not null)
                {
                    string signature=call.MethodId[call.MethodId.IndexOf('(')..];
                    candidates=candidates.Where(m=>m.Key.EndsWith(signature,StringComparison.Ordinal)).ToArray();
                }
                if(candidates.Length==1)resolved.Add(new(type,candidates[0].Value.Name) { MethodId=candidates[0].Key,IsPublicContract=true });
            }
            return resolved.Count>0?resolved:[call];
        }
        void Trace(string source,string type,string key,HashSet<(string,string)> visited)
        {
            if(!visited.Add((type,key))) return;
            var member=members[type][key];
            foreach(var call in Calls(type,member).SelectMany(c=>Resolve(source,c)))
            {
                if(call.MethodName.StartsWith(".",StringComparison.Ordinal)) continue;
                if(members.TryGetValue(call.TypeName,out var targetMembers))
                {
                    string? targetKey=call.MethodId is not null && targetMembers.ContainsKey(call.MethodId)?call.MethodId:null;
                    if(targetKey is null)
                    {
                        var matches=targetMembers.Where(m=>m.Value.Name==call.MethodName).Select(m=>m.Key).Take(2).ToArray();
                        if(matches.Length==1) targetKey=matches[0];
                    }
                    if(targetKey is null) continue;
                    var target=targetMembers[targetKey];
                    if(Public(call.TypeName,target))
                    {
                        string id=Ensure(call.TypeName,targetKey,target.Name,target);
                        links.Add(new(source,id,"calls"));
                    }
                    else Trace(source,call.TypeName,targetKey,visited);
                }
                else if(call.IsPublicContract!=false)
                {
                    string id=Ensure(call.TypeName,call.MethodId??call.MethodName,call.MethodName,null);
                    links.Add(new(source,id,"calls"));
                }
            }
            // Lambda bodies and local helpers remain attributed to their public entry;
            // they do not become additional visible execution boundaries.
            foreach(var child in members[type].Where(m=>m.Value.ParentId==key)) Trace(source,type,child.Key,visited);
        }
        foreach(var type in owned.Keys)
        foreach(var member in members[type].Where(m=>Public(type,m.Value)&&!m.Value.IsDeclaration)) Trace(nodes[(type,member.Key)].Id,type,member.Key,new());
        return new([],links.ToArray()) { Trees=trees.Select(t=>new CompositionTree(Short(t.Key),t.Value.ToArray()) { ProjectName=owned.TryGetValue(t.Key,out var owner)?owner.Project: model.ProjectModels.SelectMany(p=>p.Types??[]).FirstOrDefault(type=>type.Name==t.Key)?.AssemblyName??"External" }).ToArray() };
    }
}
