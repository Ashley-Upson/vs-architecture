using System;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core2.Models;
namespace StandardIo.ArchitectureDiagram.Core2.Services.Processings.Layout;
internal sealed class CategoryRowLayoutRuleProcessingService : ILayoutRuleProcessingService
{
    public void ApplyRule(RenderModel model)
    {
        if (model.IsProjectGraph || model.DiagramType != DiagramTypes.Architecture) return;
        model.Rows.Clear();
        foreach (var project in model.Projects)
        {
            var nodes = project.Nodes.ToDictionary(n => n.Id);
            // Depth has already identified cycle-closing links. Cross-project links position boxes, not local rows.
            var links = project.Connections.Where(e => nodes[e.TargetId].Y > nodes[e.SourceId].Y).ToArray();
            var incoming = links.ToLookup(e => e.TargetId, e => e.SourceId);
            var outgoing = links.ToLookup(e => e.SourceId, e => e.TargetId);
            var categories = nodes.Values.ToDictionary(n => n.Id, n => n.Category ?? StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Rendering.DiagramStyles.GetRoleCategory(n.TypeName));
            var pending = nodes.Keys.ToHashSet(StringComparer.Ordinal);
            var counts = nodes.Keys.ToDictionary(id => id, id => incoming[id].Count());
            var rows = new System.Collections.Generic.Dictionary<string, int>();
            int row = 0;
            while (pending.Count > 0)
            {
                var groups = pending.Where(id => counts[id] == 0).GroupBy(id => categories[id]);
                // Ready categories with older satisfied parents take precedence over newly unlocked layers.
                var group = groups.OrderBy(g => g.Min(id => incoming[id].Select(parent => rows[parent] + 1).DefaultIfEmpty(row + 1).Max()))
                    .ThenBy(g => g.Key == RenderNodeCategory.Exposure ? -1 : (int)g.Key).First();
                var occupants = group.OrderBy(id => id, StringComparer.Ordinal).ToArray();
                model.Rows[project.Id + ":" + group.Key + ":" + row] = new RenderRowGroup(row, occupants);
                foreach (var id in occupants) { rows[id] = row; pending.Remove(id); }
                foreach (var id in occupants) foreach (var child in outgoing[id]) counts[child]--;
                row++;
            }
            for (int i = 0; i < project.Nodes.Length; i++)
            {
                var node = project.Nodes[i];
                project.Nodes[i] = node with { Y = 60 + rows[node.Id] * model.Configuration.Architecture.RowDepth };
            }
        }
    }
}
