// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core2.Models;

namespace StandardIo.ArchitectureDiagram.Core2.Services.Processings.Layout;

internal sealed class LayoutCleanupRuleProcessingService : ILayoutRuleProcessingService
{
    public void ApplyRule(RenderModel renderModel)
    {
        // Pack completed branches using the same ownership and clearance rules as
        // initial placement, allowing excess space to be reclaimed in either direction.
        BranchSpacingLayoutRuleProcessingService.ArrangeBranches(renderModel, reclaimSpace: true);
        foreach (var project in renderModel.Projects)
        {
            double spacing = renderModel.IsProjectGraph ? renderModel.Configuration.Architecture.ProjectSpacing : renderModel.Configuration.Architecture.NodeSpacing;
            var groups = LayoutGraph.BranchGroups(project).Select(g => g.Select(n => n.Id).ToArray()).ToArray();
            foreach (string id in project.Nodes.OrderByDescending(n => n.Y).Select(n => n.Id).ToArray())
            {
                var children = LayoutGraph.Children(project, id);
                if (children.Length != 1 || LayoutGraph.Parents(project, children[0].Id).Length < 2) continue;
                var parent = project.Nodes.Single(n => n.Id == id);
                var root = LayoutGraph.OwnedChainRoot(project, parent);
                // A branch owned by a wider sibling set cannot move independently without
                // changing its parent's centring. Only move independently placed chains.
                if (LayoutGraph.Parents(project, root.Id).Length != 0) continue;
                var branch = LayoutGraph.OwnedBranch(project, root.Id);
                var ids = branch.Select(n => n.Id).ToHashSet();
                double minimum = double.NegativeInfinity, maximum = double.PositiveInfinity;
                void Reserve(double left, double right, double peerLeft, double peerRight)
                {
                    if (right <= peerLeft) maximum = Math.Min(maximum, peerLeft - spacing - right);
                    else if (left >= peerRight) minimum = Math.Max(minimum, peerRight + spacing - left);
                }
                foreach (var node in branch)
                foreach (var peer in project.Nodes.Where(n => !ids.Contains(n.Id) && n.Y < node.Y + node.Height && n.Y + n.Height > node.Y))
                    Reserve(node.X, node.X + node.Width, peer.X, peer.X + peer.Width);
                foreach (var group in groups.Where(g => g.Any(ids.Contains)))
                foreach (string ownId in group.Where(ids.Contains))
                foreach (string peerId in group.Where(n => !ids.Contains(n)))
                {
                    var own = LayoutGraph.OwnedBranch(project, ownId);
                    var peer = LayoutGraph.OwnedBranch(project, peerId);
                    Reserve(own.Min(n => n.X), own.Max(n => n.X + n.Width), peer.Min(n => n.X), peer.Max(n => n.X + n.Width));
                }
                if (minimum > maximum) continue;
                double delta = Math.Clamp(LayoutGraph.Centre(children[0]) - LayoutGraph.Centre(parent), minimum, maximum);
                foreach (var node in branch) LayoutGraph.Move(project, node.Id, delta);
            }
        }
    }
}
