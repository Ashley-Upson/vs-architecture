using System;
using System.Linq;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using StandardIo.ArchitectureDiagram.Core2.Models;
namespace StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Rendering;
internal interface IContextualModelService { ContextualDiagram Prepare(RenderModel model); }
internal sealed class ContextualModelService(ICompositionTreeService compositionTrees) : IContextualModelService
{
    public ContextualDiagram Prepare(RenderModel model)
    {
        var types = model.ProjectModels.SelectMany(p => (p.Types ?? []).Select(t => (Project: p, Type: t)))
            .GroupBy(x => x.Type.Name!).Select(g => g.OrderByDescending(x => x.Type.IsInternal).First()).Where(x => x.Type.IsInternal).ToArray();
        bool Data(DefinedType t) => t.IsDataType || t.HasDeclaredBehaviour == false || t.HasDeclaredBehaviour is null && t.Methods is { Length: 0 };
        string Short(string name) => Regex.Replace(name, @"(?:[A-Za-z_]\w*\.)+", "");
        var links = new List<ContextualLink>();
        if (model.DiagramType is DiagramTypes.Composition or DiagramTypes.CallChain)
        {
            var names = types.Where(x => !Data(x.Type)).Select(x => x.Type.Name!).ToHashSet();
            links.AddRange(model.ProjectModels.SelectMany(p => p.Dependencies ?? []).Where(d => d.IsComposition && names.Contains(d.FromType!) && names.Contains(d.ToType!))
                .Select(d => new ContextualLink(d.FromType!, d.ToType!, "references")));
            var used = links.SelectMany(l => new[] { l.From, l.To }).ToHashSet();
            return new(types.Where(x => used.Contains(x.Type.Name!)).Select(x => new ContextualType(x.Type.Name!, x.Type.IsInternal ? x.Project.Name ?? "Project" : x.Type.AssemblyName ?? "External", [Short(x.Type.Name!)])).ToArray(), links.Distinct().ToArray()) { Trees = compositionTrees.Build(model) };
        }
        var data = types.Where(x => Data(x.Type)).ToArray();
        foreach (var item in data)
        foreach (var member in (item.Type.Properties ?? []).Select(p => (p.Name, p.Type)).Concat((item.Type.Fields ?? []).Select(f => (f.Name, f.Type))))
        foreach (var target in data)
        {
            string name = target.Type.Name!;
            if (member.Type is null || !Regex.IsMatch(member.Type, @"(?<![\w.])" + Regex.Escape(name) + @"(?![\w.])")) continue;
            bool many = member.Type.EndsWith("[]", StringComparison.Ordinal) || (member.Type.StartsWith("System.Collections.", StringComparison.Ordinal) || member.Type.StartsWith("System.Linq.IQueryable<", StringComparison.Ordinal));
            links.Add(new(item.Type.Name!, name, member.Name + (many ? " [many]" : " [single]")));
        }
        return new(data.Select(x => new ContextualType(x.Type.Name!, x.Type.IsInternal ? x.Project.Name ?? "Project" : x.Type.AssemblyName ?? "External",
            new[] { Short(x.Type.Name!) }.Concat((x.Type.Properties ?? []).Select(p => Short(p.Type ?? "?") + " : " + p.Name))
            .Concat((x.Type.Fields ?? []).Select(f => Short(f.Type ?? "?") + " : " + f.Name)).ToArray())).ToArray(), links.Distinct().ToArray());
    }
}
