// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core2.Models;
namespace StandardIo.ArchitectureDiagram.Core2.Services.Processings.Layout;
internal sealed class DepthLayoutRuleProcessingService : ILayoutRuleProcessingService
{
    public void ApplyRule(RenderModel renderModel)
    {
        foreach (var project in renderModel.Projects)
        {
            var model = LayoutGraph.ToProjectModel(project);
            var depths = CalculateDepths(model.Types!.Select(type => type.Name!).ToArray(), model.Dependencies!.ToLookup(link => link.FromType!));
            for (int index = 0; index < project.Nodes.Length; index++)
            {
                var node = project.Nodes[index];
                project.Nodes[index] = node with { Y = 60 + depths[node.TypeName] * renderModel.Configuration.Architecture.RowDepth };
            }
        }
    }
    private static Dictionary<string, int> CalculateDepths(
        IReadOnlyList<string> names,
        ILookup<string, TypeRelationship> outgoing)
    {
        var visiting = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var ordered = new List<string>();
        var forward = names.ToDictionary(name => name, _ => new List<string>(), StringComparer.Ordinal);

        void Visit(string name)
        {
            if (!visited.Add(name))
            {
                return;
            }

            visiting.Add(name);
            foreach (var link in outgoing[name])
            {
                string child = link.ToType!;
                if (visiting.Contains(child))
                {
                    continue;
                }

                forward[name].Add(child);
                Visit(child);
            }

            visiting.Remove(name);
            ordered.Add(name);
        }

        foreach (string name in names)
        {
            Visit(name);
        }

        var depths = names.ToDictionary(name => name, _ => 0, StringComparer.Ordinal);
        foreach (string name in ordered.AsEnumerable().Reverse())
        {
            foreach (string child in forward[name])
            {
                depths[child] = Math.Max(depths[child], depths[name] + 1);
            }
        }

        return depths;
    }

}
