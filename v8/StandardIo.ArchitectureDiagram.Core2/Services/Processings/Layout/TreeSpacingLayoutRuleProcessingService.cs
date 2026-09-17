// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core2.Models;
namespace StandardIo.ArchitectureDiagram.Core2.Services.Processings.Layout;
internal sealed class TreeSpacingLayoutRuleProcessingService : ArchitectureLayoutRuleProcessingService
{
    protected override void ApplyArchitectureRule(RenderModel renderModel)
    {
        if (renderModel.LayoutInitialized) return;
        foreach (var project in renderModel.Projects)
        {
            var drawing = Layout(new ProjectModelPresentation(LayoutGraph.ToProjectModel(project), project.Nodes.ToDictionary(node => node.TypeName, node => node.Label)), 0, 0, project.Nodes.ToDictionary(node => node.TypeName, node => (int)Math.Round((node.Y - 60) / renderModel.Configuration.Architecture.RowDepth)), renderModel.Configuration.Architecture);
            var byName = drawing.Nodes.ToDictionary(node => node.Type.Name!);
            for (int index = 0; index < project.Nodes.Length; index++)
            {
                var node = project.Nodes[index];
                var position = byName[node.TypeName];
                project.Nodes[index] = node with { X = position.X, Y = position.Y };
            }
        }
        renderModel.LayoutInitialized = true;
    }
    private static ProjectModelDrawing Layout(ProjectModelPresentation presentation, int index, double left, Dictionary<string, int> assignedDepths, ArchitectureRenderConfiguration configuration)
    {
        ProjectModel model = presentation.Model;
        DefinedType[] types = model.Types!;
        TypeRelationship[] links = model.Dependencies!;
        var outgoing = links.ToLookup(keySelector: link => link.FromType!);

        var incoming = links.Where(predicate: link => link.FromType != link.ToType)
            .Select(selector: link => link.ToType!)
            .ToHashSet();

        var depths = new Dictionary<string, int>(comparer: StringComparer.Ordinal);
        var children = types.ToDictionary(keySelector: type => type.Name!, elementSelector: _ => new List<string>(), comparer: StringComparer.Ordinal);
        var order = new List<string>();
        var roots = new List<string>();

        foreach (DefinedType seed in types.Where(predicate: type => !incoming.Contains(item: type.Name!))
            .Concat(second: types))
        {
            if (!depths.TryAdd(key: seed.Name!, value: 0))
            {
                continue;
            }

            roots.Add(item: seed.Name!);
            var pending = new Queue<string>();
            pending.Enqueue(item: seed.Name!);

            while (pending.Count > 0)
            {
                string current = pending.Dequeue();
                order.Add(item: current);

                foreach (TypeRelationship link in outgoing[current])
                {
                    if (!depths.TryAdd(key: link.ToType!, value: depths[current] + 1))
                    {
                        continue;
                    }

                    children[current].Add(item: link.ToType!);
                    pending.Enqueue(item: link.ToType!);
                }
            }
        }


        depths = assignedDepths;

        var sharedNames = links.Where(link => link.FromType != link.ToType)
            .GroupBy(link => link.ToType!).Where(group => group.Select(link => link.FromType).Distinct().Count() > 1)
            .Select(group => group.Key).ToHashSet();
        foreach (var ownedChildren in children.Values)
        {
            ownedChildren.RemoveAll(child => sharedNames.Contains(child));
        }

        double nodeWidth = configuration.NodeWidth;
        double gap = configuration.NodeSpacing;
        var widths = new Dictionary<string, double>(comparer: StringComparer.Ordinal);

        foreach (string name in order.AsEnumerable()
            .Reverse())
        {
            int count = children[name].Count;
            widths[name] = count == 0 ? nodeWidth : Math.Max(nodeWidth, children[name].Sum(child => widths[child]) + gap * (count - 1));
        }

        double contentWidth = roots.Sum(selector: name => widths[name]) + Math.Max(val1: 0, val2: roots.Count - 1) * gap;
        double width = Math.Max(val1: 300, val2: contentWidth + 80);
        var positions = new Dictionary<string, double>(comparer: StringComparer.Ordinal);
        double next = (width - contentWidth) / 2;

        foreach (string root in roots)
        {
            positions[root] = next;
            next += widths[root] + gap;
        }

        foreach (var row in sharedNames.GroupBy(name => depths[name]))
        {
            double sharedLeft = 40;
            foreach (string name in row.OrderBy(name => name, StringComparer.Ordinal))
            {
                positions[name] = sharedLeft;
                sharedLeft += widths[name] + gap;
            }
        }

        foreach (string name in order)
        {
            positions.TryAdd(name, 40);
            if (children[name].Count == 0)
            {
                continue;
            }

            double childLeft = positions[name];

            for (int childIndex = 0; childIndex < children[name].Count; childIndex++)
            {
                string child = children[name][childIndex];
                positions[child] = childLeft;
                childLeft += widths[child] + gap;
            }
        }

        foreach (string name in order)
        {
            positions.TryAdd(name, 40);
        }

        string id = "tree-" + index;

        DrawingNode[] nodes = types.Select(selector: (type, nodeIndex) => new DrawingNode(id + "-node-" + nodeIndex, type, presentation.Labels[type.Name!], positions[type.Name!] + (widths[type.Name!] - nodeWidth) / 2, 60 + depths[type.Name!] * configuration.RowDepth, nodeWidth, 60))
            .ToArray();

        double height = types.Length == 0 ? 120 : (depths.Values.Max() + 1) * configuration.RowDepth + 40;
        return new ProjectModelDrawing(id, model, left, width, height, nodes);
    }

}
