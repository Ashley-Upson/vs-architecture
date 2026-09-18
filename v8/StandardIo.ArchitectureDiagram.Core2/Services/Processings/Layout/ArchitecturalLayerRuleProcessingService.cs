using System;
using System.Collections.Generic;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core2.Models;
namespace StandardIo.ArchitectureDiagram.Core2.Services.Processings.Layout;
internal sealed class ArchitecturalLayerRuleProcessingService : ArchitectureLayoutRuleProcessingService
{
    protected override void ApplyArchitectureRule(RenderModel model)
    {
        if (model.IsProjectGraph || model.DiagramType != DiagramTypes.Architecture) return;
        foreach (var project in model.Projects)
        {
            var roles = project.Nodes.ToDictionary(n => n.Id, n => n.Category ?? RenderNodeCategory.Other);
            var categories = roles.Values.Where(c => c != RenderNodeCategory.Other && c != RenderNodeCategory.Exposure).Distinct().OrderBy(c => c).ToArray();
            var transitions = project.Connections.Where(e => !e.IsComposition && !e.Inheritance)
                .Select(e => (From: roles[e.SourceId], To: roles[e.TargetId]))
                .Where(e => e.From != e.To && categories.Contains(e.From) && categories.Contains(e.To))
                .GroupBy(e => e).Select(g => (g.Key.From, g.Key.To, Weight: g.Count())).ToArray();
            // At most four classified service categories: exhaustively compare their relative orders.
            var ordered = Permutations(categories).OrderBy(order => transitions.Where(e => Array.IndexOf(order, e.From) > Array.IndexOf(order, e.To)).Sum(e => e.Weight)).First();
            project.ArchitecturalLayers = roles.Values.Contains(RenderNodeCategory.Exposure) ? new[] { RenderNodeCategory.Exposure }.Concat(ordered).ToArray() : ordered;
        }
    }
    private static IEnumerable<RenderNodeCategory[]> Permutations(RenderNodeCategory[] values)
    {
        if (values.Length == 0) { yield return []; yield break; }
        foreach (var head in values)
            foreach (var tail in Permutations(values.Where(v => v != head).ToArray()))
                yield return new[] { head }.Concat(tail).ToArray();
    }
}
