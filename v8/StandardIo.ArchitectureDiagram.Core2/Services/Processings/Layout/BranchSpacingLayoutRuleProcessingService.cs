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
            bool sharedNodes = LayoutGraph.HasSharedNodes(project);
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
            var sharedGroups = LayoutGraph.SharedGroups(project).ToArray();
            var independentSharedLeaves = sharedGroups.Where(group => group.Length == 1)
                .Select(group => group[0].Id).Where(id => branches[id].Count == 1).ToHashSet();
            double? RootAnchor(string id)
            {
                if (renderModel.PassageOffsetParents.Contains(id) || !sharedNodes || renderModel.IsProjectGraph || branches[id].Count != 1 || LayoutGraph.Parents(project,id).Length != 0) return null;
                var targets = LayoutGraph.Children(project,id);
                if (targets.Select(node => node.Y).Distinct().Count() < 2) return null;
                return LayoutGraph.Midpoint(targets.Where(node => node.Y == targets.Min(child => child.Y))) - Current(id).Width / 2;
            }
            void Arrange(string owner)
            {
                bool SharedLeaf(string id) => reclaimSpace && independentSharedLeaves.Contains(id);
                var siblings = children[owner].ToArray();
                double Position(string id)
                {
                    if (RootAnchor(id) is double anchor) return anchor;
                    if ((reclaimSpace || renderModel.LayoutInitialized) && sharedNodes && !renderModel.IsProjectGraph && branches[id].Count == 1)
                    {
                        var directConsumers = LayoutGraph.Parents(project,id);
                        if (directConsumers.Length > 1)
                        {
                            var group = sharedGroups.Single(group => group.Any(node => node.Id == id))
                                .Select(node => Current(node.Id)).Where(node => node.Y == Current(id).Y)
                                .OrderBy(node => node.X).ThenBy(node => node.Id, StringComparer.Ordinal).ToArray();
                            double width = group.Sum(node => node.Width) + spacing * (group.Length - 1);
                            double offset = group.TakeWhile(node => node.Id != id).Sum(node => node.Width + spacing);
                            return directConsumers.Average(node => LayoutGraph.Centre(node)) - width / 2 + offset;
                        }
                    }
                    if (!reclaimSpace || branches[id].Count == 1 || LayoutGraph.Parents(project,id).Length < 2) return Current(id).X;
                    // A shared branch sits among the sibling branches containing its
                    // consumers, rather than retaining an unrelated initial position.
                    var parents = LayoutGraph.Parents(project,id).Select(node => node.Id).ToHashSet();
                    var consumers = siblings.Where(sibling => sibling != id && branches[sibling].Overlaps(parents)).ToArray();
                    return consumers.Length > 1 ? consumers.Average(sibling => Current(sibling).X) : Current(id).X;
                }
                var roots = siblings.OrderBy(id => RootAnchor(id).HasValue).ThenBy(id => SharedLeaf(id)).ThenBy(Position).ThenBy(id => id, StringComparer.Ordinal).ToArray();
                if (reclaimSpace && roots.Length > 2)
                {
                    var owned = roots.Where(id => LayoutGraph.Parents(project,id) is var parents && parents.Length == 1 && parents[0].Id == owner).ToHashSet();
                    var affinities = project.Connections.GroupBy(edge => edge.TargetId)
                        .Select(group => roots.Where(id => owned.Contains(id) && group.Any(edge => branches[id].Contains(edge.SourceId))).ToArray())
                        .Where(group => group.Length > 1).ToArray();
                    int Cost() => affinities.Sum(group => group.Max(id => Array.IndexOf(roots,id)) - group.Min(id => Array.IndexOf(roots,id)));
                    int cost = Cost();
                    bool changed;
                    do
                    {
                        changed = false;
                        for (int index = 1; index < roots.Length; index++)
                        {
                            if (!owned.Contains(roots[index-1]) || !owned.Contains(roots[index])) continue;
                            (roots[index-1],roots[index]) = (roots[index],roots[index-1]);
                            int candidate = Cost();
                            if (candidate < cost) { cost = candidate; changed = true; }
                            else (roots[index-1],roots[index]) = (roots[index],roots[index-1]);
                        }
                    } while (changed);
                }
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
                        .Select(branch => left + spacing - LayoutGraph.BranchClearance(project, branch.Root, root, (reclaimSpace || renderModel.LayoutInitialized) && sharedNodes && !renderModel.IsProjectGraph, respectBranchOrder: reclaimSpace)).Where(double.IsFinite).DefaultIfEmpty(left).Max();
                    if (RootAnchor(root) is double anchor)
                    {
                        var peers = placed.SelectMany(branch => branches[branch.Root].Select(Current))
                            .Where(node => node.Y < bottom && node.Y + node.Height > top).ToArray();
                        double width = right - left;
                        next = peers.SelectMany(node => new[] { node.X - spacing - width, node.X + node.Width + spacing })
                            .Append(anchor).Where(x => peers.All(node => x + width + spacing <= node.X + LayoutGraph.Tolerance || x >= node.X + node.Width + spacing - LayoutGraph.Tolerance))
                            .OrderBy(x => Math.Abs(x - anchor)).ThenBy(x => x).First();
                    }
                    if (SharedLeaf(root))
                    {
                        // Shared leaves occupy their own row, after owned branches have
                        // been packed. They must not reserve a column above that row.
                        var peers = placed.Where(branch => branch.Top < bottom && branch.Bottom > top || MustSeparate(root, branch.Root)).ToArray();
                        double width = right - left;
                        double preferred = LayoutGraph.Midpoint(LayoutGraph.Parents(project,root)) - width / 2;
                        next = peers.SelectMany(branch => new[] { branch.Left - spacing - width, branch.Right + spacing })
                            .Append(preferred).Where(x => peers.All(branch => x + width + spacing <= branch.Left + LayoutGraph.Tolerance || x >= branch.Right + spacing - LayoutGraph.Tolerance))
                            .OrderBy(x => Math.Abs(x - preferred)).ThenBy(x => x).First();
                    }
                    double delta = reclaimSpace ? next - left : Math.Max(0, next - left);
                    foreach (string id in branches[root]) LayoutGraph.Move(project, id, delta);
                    placed.Add((root, left + delta, right + delta, top, bottom));
                }
                if (owner.Length == 0 || renderModel.PassageOffsetParents.Contains(owner)) return;
                var ownedChildren = LayoutGraph.OwnedChildren(project, owner);
                if (ownedChildren.Length > 0)
                    LayoutGraph.Move(project, owner, LayoutGraph.Midpoint(ownedChildren) - LayoutGraph.Centre(Current(owner)));
            }
            Arrange(string.Empty);
        }
    }
}
