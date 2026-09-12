// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core2.Models;
namespace StandardIo.ArchitectureDiagram.Core2.Services.Processings.Layout;
internal sealed class BranchSpacingLayoutRuleProcessingService : ILayoutRuleProcessingService
{
    public void ApplyRule(RenderModel renderModel)
    {
        ArrangeBranches(renderModel, reclaimSpace: false);
    }

    internal static void ArrangeBranches(RenderModel renderModel, bool reclaimSpace)
    {
        foreach (var project in renderModel.Projects)
        {
            // Finish descendants before reserving their entire branch bounds. Shared nodes
            // belong to the smallest branch containing all their consumers, when one exists.
            var branches = project.Nodes.ToDictionary(node => node.Id,
                node => LayoutGraph.OwnedBranch(project, node.Id).Select(member => member.Id).ToHashSet());
            var owners = project.Nodes.ToDictionary(node => node.Id, node => branches
                .Where(branch => branch.Value.Count > branches[node.Id].Count && branch.Value.Contains(node.Id))
                .OrderBy(branch => branch.Value.Count).Select(branch => branch.Key).FirstOrDefault());
            var children = project.Nodes.ToLookup(node => owners[node.Id] ?? string.Empty, node => node.Id);
            var separation = new Dictionary<string, HashSet<string>>();
            foreach (var group in LayoutGraph.BranchGroups(project))
            foreach (var first in group)
            foreach (var second in group)
            {
                if (first.Id == second.Id) continue;
                if (!separation.TryGetValue(first.Id, out var peers)) separation[first.Id] = peers = [];
                peers.Add(second.Id);
            }
            bool MustSeparate(string first, string second) => branches[first].Any(id =>
                separation.TryGetValue(id, out var peers) && peers.Overlaps(branches[second]));
            double spacing = renderModel.IsProjectGraph ? renderModel.Configuration.Architecture.ProjectSpacing : renderModel.Configuration.Architecture.NodeSpacing;
            RenderNode Current(string id) => project.Nodes.Single(node => node.Id == id);
            void Arrange(string owner)
            {
                var roots = children[owner].OrderBy(id => Current(id).X).ThenBy(id => id, StringComparer.Ordinal).ToArray();
                foreach (string root in roots) Arrange(root);
                // Translate completed branches as units; later placement cannot undo
                // their internal spacing or parent centring.
                var placed = new List<(string Root, double Left, double Right, double Top, double Bottom)>();
                foreach (string root in roots)
                {
                    var nodes = branches[root].Select(Current).ToArray();
                    double left = nodes.Min(node => node.X), right = nodes.Max(node => node.X + node.Width);
                    double top = nodes.Min(node => node.Y), bottom = nodes.Max(node => node.Y + node.Height);
                    double next = placed.Where(branch => branch.Top < bottom && branch.Bottom > top || MustSeparate(root, branch.Root))
                        .Select(branch => branch.Right + spacing).DefaultIfEmpty(left).Max();
                    double delta = reclaimSpace ? next - left : Math.Max(0, next - left);
                    foreach (string id in branches[root]) LayoutGraph.Move(project, id, delta);
                    placed.Add((root, left + delta, right + delta, top, bottom));
                }
                if (owner.Length == 0) return;
                var ownedChildren = LayoutGraph.OwnedChildren(project, owner);
                if (ownedChildren.Length > 0)
                    LayoutGraph.Move(project, owner, LayoutGraph.Midpoint(ownedChildren) - LayoutGraph.Centre(Current(owner)));
            }
            Arrange(string.Empty);
        }
    }
}
