using System;
using System.Collections.Generic;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core2.Models;
namespace StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Rendering;
internal sealed class ProjectModelLayoutService : IProjectModelLayoutService
{
    public ProjectModelDrawing Layout(ProjectModelPresentation presentation, int index, double left)
    {
        ProjectModel model = presentation.Model;
        DefinedType[] types = model.Types!;
        Dependency[] links = model.Dependencies!;
        var outgoing = links.ToLookup(link => link.FromType!);
        var incoming = links.Where(link => link.FromType != link.ToType).Select(link => link.ToType!).ToHashSet();
        var depths = new Dictionary<string, int>(comparer: StringComparer.Ordinal);
        var children = types.ToDictionary(type => type.Name!, _ => new List<string>(), StringComparer.Ordinal);
        var order = new List<string>();
        var roots = new List<string>();
        // First discovery owns placement. Additional parents and cycles remain as links.
        foreach (DefinedType seed in types.Where(type => !incoming.Contains(type.Name!)).Concat(types))
        {
            if (!depths.TryAdd(key: seed.Name!, value: 0)) continue;
            roots.Add(item: seed.Name!);
            var pending = new Queue<string>();
            pending.Enqueue(item: seed.Name!);
            while (pending.Count > 0)
            {
                string current = pending.Dequeue();
                order.Add(item: current);
                foreach (Dependency link in outgoing[current])
                {
                    if (!depths.TryAdd(key: link.ToType!, value: depths[current] + 1)) continue;
                    children[current].Add(item: link.ToType!);
                    pending.Enqueue(item: link.ToType!);
                }
            }
        }
        const double nodeWidth = 180;
        const double gap = 60;
        var widths = new Dictionary<string, double>(comparer: StringComparer.Ordinal);
        foreach (string name in order.AsEnumerable().Reverse())
        {
            int count = children[name].Count;
            widths[name] = count == 0 ? nodeWidth : children[name].Max(child => widths[child]) * count + gap * (count - 1);
        }
        double contentWidth = roots.Sum(name => widths[name]) + Math.Max(0, roots.Count - 1) * gap;
        double width = Math.Max(300, contentWidth + 80);
        var positions = new Dictionary<string, double>(comparer: StringComparer.Ordinal);
        double next = (width - contentWidth) / 2;
        foreach (string root in roots) { positions[root] = next; next += widths[root] + gap; }
        foreach (string name in order)
        {
            if (children[name].Count == 0) continue;
            double slotWidth = children[name].Max(child => widths[child]);
            for (int childIndex = 0; childIndex < children[name].Count; childIndex++)
            {
                string child = children[name][childIndex];
                positions[child] = positions[name] + childIndex * (slotWidth + gap) + (slotWidth - widths[child]) / 2;
            }
        }
        string id = "tree-" + index;
        // Keep input order for stable XML identity, independently of placement order.
        DrawingNode[] nodes = types.Select((type, nodeIndex) => new DrawingNode(id + "-node-" + nodeIndex, type,
            presentation.Labels[type.Name!], positions[type.Name!] + (widths[type.Name!] - nodeWidth) / 2,
            60 + depths[type.Name!] * 160, nodeWidth, 60)).ToArray();
        double height = types.Length == 0 ? 120 : (depths.Values.Max() + 1) * 160 + 40;
        return new ProjectModelDrawing(id, model, left, width, height, nodes);
    }
}
