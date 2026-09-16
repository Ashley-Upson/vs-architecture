// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using StandardIo.ArchitectureDiagram.Core2.Models;
namespace StandardIo.ArchitectureDiagram.Core2.Services.Processings.Layout;
internal static class LayoutGraph
{
    private static readonly ConditionalWeakTable<RenderConnection[], LayoutTopology> Topologies = new();
    private static readonly ConditionalWeakTable<RenderNode[], Dictionary<string, int>> NodeIndices = new();
    private static LayoutTopology Topology(RenderProject project) => Topologies.GetValue(project.Connections, connections => new LayoutTopology(connections));
    private static int Index(RenderProject project, string id) => NodeIndices.GetValue(project.Nodes,
        nodes => nodes.Select((node, index) => (node.Id, index)).ToDictionary(pair => pair.Id, pair => pair.index))[id];
    internal static RenderProject ProjectGraph(RenderModel model)
    {
        var owners = model.Projects.SelectMany(project => project.Nodes.Select(node => (node.Id, Owner: project.Id)))
            .ToDictionary(pair => pair.Id, pair => pair.Owner);
        var nodes = model.Projects.Select(project => new RenderNode(project.Id, project.Id, project.Name, "#374151",
            project.X, project.Y, project.Width, project.Height, Array.Empty<RenderText>())).ToArray();
        var links = model.CrossProjectConnections.Select(edge => (Source: owners[edge.SourceId], Target: owners[edge.TargetId]))
            .Distinct().Select((pair, index) => new RenderConnection("project-edge-" + index, pair.Source, pair.Target,
                pair.Source, pair.Target, false, Array.Empty<DrawingPoint>())).ToArray();
        return new RenderProject("projects", "Projects", 0, 0,
            nodes.Select(node => node.X + node.Width + 40).DefaultIfEmpty(300).Max(),
            nodes.Select(node => node.Y + node.Height + 40).DefaultIfEmpty(200).Max(), nodes, links);
    }

    internal static RenderNode OwnedChainRoot(RenderProject project, RenderNode node)
    {
        var root = node;
        var visited = new HashSet<string>();
        while (visited.Add(root.Id))
        {
            var owners = Parents(project, root.Id);
            if (owners.Length != 1 || owners[0].Y >= root.Y || OwnedChildren(project, owners[0].Id).Length > 1) break;
            root = owners[0];
        }
        return root;
    }

    internal static RenderNode[] OwnedBranch(RenderProject project, string rootId)
    {
        // A shared descendant belongs to this branch when all its parents belong to it.
        var ids = new HashSet<string> { rootId };
        var pending = new Queue<string>();
        pending.Enqueue(rootId);
        while (pending.Count > 0)
        {
            string id = pending.Dequeue();
            foreach (var child in Children(project, id))
            {
                if (!ids.Contains(child.Id) && Parents(project, child.Id).All(parent => ids.Contains(parent.Id)))
                {
                    ids.Add(child.Id);
                    pending.Enqueue(child.Id);
                }
            }
        }
        return project.Nodes.Where(node => ids.Contains(node.Id)).ToArray();
    }

    internal static IEnumerable<RenderNode[]> BranchGroups(RenderProject project)
    {
        var branchCache = new Dictionary<string, RenderNode[]>();
        RenderNode[] Branch(string id)
        {
            if (!branchCache.TryGetValue(id, out var branch)) branchCache[id] = branch = OwnedBranch(project, id);
            return branch;
        }
        foreach (var parent in project.Nodes.OrderByDescending(node => node.Y))
        {
            var children = OwnedChildren(project, parent.Id);
            if (children.Length > 1) yield return children;
        }
        // Unrelated trees reserve their full bounds. Shared descendants compete at their own level, not against an ancestor's full bounds.
        var roots = project.Nodes.Where(node => Parents(project, node.Id).Length != 1)
            .Select(root => (Root: root, Branch: Branch(root.Id)))
            .OrderBy(pair => pair.Branch.Min(node => node.Y)).ToArray();
        var outgoing = project.Connections.ToLookup(edge => edge.SourceId, edge => edge.TargetId);
        var descendants = roots.ToDictionary(pair => pair.Root.Id, pair =>
        {
            var result = new HashSet<string>();
            var pending = new Queue<string>(outgoing[pair.Root.Id]);
            while (pending.Count > 0)
            {
                string id = pending.Dequeue();
                if (!result.Add(id)) continue;
                foreach (string child in outgoing[id]) pending.Enqueue(child);
            }
            return result;
        });
        var seen = new HashSet<string>();
        foreach (double top in roots.Select(pair => pair.Branch.Min(node => node.Y)).Distinct())
        {
            var group = roots.Where(pair => pair.Branch.Min(node => node.Y) <= top && pair.Branch.Max(node => node.Y + node.Height) > top)
                .Select(pair => pair.Root).ToArray();
            string key = string.Join("|", group.Select(root => root.Id).OrderBy(id => id, StringComparer.Ordinal));
            if (group.Length <= 1 || !seen.Add(key)) continue;
            bool Related(RenderNode a, RenderNode b) => descendants[a.Id].Contains(b.Id) || descendants[b.Id].Contains(a.Id);
            if (!group.Any(a => group.Any(b => a.Id != b.Id && Related(a, b))))
            {
                yield return group;
                continue;
            }
            for (int first = 0; first < group.Length; first++)
            for (int second = first + 1; second < group.Length; second++)
            {
                var a = group[first]; var b = group[second];
                if (!Related(a, b)) { yield return new[] { a, b }; continue; }
                var ancestor = descendants[a.Id].Contains(b.Id) ? a : b;
                var shared = ancestor.Id == a.Id ? b : a;
                var sharedIds = Branch(shared.Id).Select(node => node.Id).ToHashSet();
                var lower = Branch(ancestor.Id).Where(node => node.Y >= shared.Y && !sharedIds.Contains(node.Id)).ToArray();
                var lowerIds = lower.Select(node => node.Id).ToHashSet();
                foreach (var frontier in lower.Where(node => !Parents(project, node.Id).Any(parent => lowerIds.Contains(parent.Id))))
                    yield return new[] { shared, frontier };
            }
        }
    }

    internal static double BranchClearance(RenderProject project, string leftId, string rightId, bool alignSharedRoots = false, bool respectBranchOrder = true)
    {
        var left = OwnedBranch(project,leftId); var right = OwnedBranch(project,rightId);
        var leftRoot = project.Nodes.Single(node => node.Id == leftId);
        var rightRoot = project.Nodes.Single(node => node.Id == rightId);
        var leftParents = Parents(project,leftId); var rightParents = Parents(project,rightId);
        bool sameParent = leftParents.Length == 1 && rightParents.Length == 1 && leftParents[0].Id == rightParents[0].Id;
        if (!sameParent && (left.Max(node=>node.Y+node.Height) <= right.Min(node=>node.Y) ||
            right.Max(node=>node.Y+node.Height) <= left.Min(node=>node.Y)))
            return double.PositiveInfinity;
        bool siblings = leftRoot.Y == rightRoot.Y && sameParent;
        // Shared graphs have no exclusive rectangular subtree ownership. Independent
        // branches may use empty rows on either side of one another; siblings still
        // preserve their ordering. Measure actual occupied intervals in that case.
        if (alignSharedRoots && !siblings && !respectBranchOrder)
            return left.SelectMany(a => right.Where(b => a.Y < b.Y + b.Height && a.Y + a.Height > b.Y)
                .Select(b => Math.Max(b.X-a.X-a.Width, a.X-b.X-b.Width)))
                .DefaultIfEmpty(double.PositiveInfinity).Min();
        if (!siblings && !alignSharedRoots) return right.Min(node => node.X) - left.Max(node => node.X + node.Width);
        // Compare occupied rows, allowing shallow siblings above wide descendants.
        return left.SelectMany(a => right.Where(b => a.Y < b.Y + b.Height && a.Y + a.Height > b.Y)
            .Select(b => b.X - a.X - a.Width)).DefaultIfEmpty(double.PositiveInfinity).Min();
    }

    internal const double Tolerance = 0.01;
    internal static double Centre(RenderNode node) => node.X + node.Width / 2;
    internal static ProjectModel ToProjectModel(RenderProject project) => new()
    {
        Name = project.Name,
        Types = project.Nodes.Select(node => new DefinedType { Name = node.TypeName }).ToArray(),
        Dependencies = project.Connections.Select(edge => new TypeRelationship { FromType = edge.FromType, ToType = edge.ToType, IsComposition = edge.IsComposition, DependencyType = edge.Inheritance ? DependencyType.Inheritance : DependencyType.Consumed }).ToArray()
    };
    internal static RenderNode[] Parents(RenderProject project, string id) => Topology(project).Parents.TryGetValue(id, out var parents)
        ? parents.Select(parent => project.Nodes[Index(project, parent)]).ToArray() : Array.Empty<RenderNode>();
    internal static RenderNode[] Children(RenderProject project, string id) => Topology(project).Children.TryGetValue(id, out var children)
        ? children.Select(child => project.Nodes[Index(project, child)]).Where(node => node.Y > project.Nodes[Index(project, id)].Y).ToArray() : Array.Empty<RenderNode>();
    internal static RenderNode[] OwnedChildren(RenderProject project, string id) => Children(project, id).Where(node => Parents(project, node.Id).Length == 1).ToArray();
    internal static double Midpoint(IEnumerable<RenderNode> nodes) => (nodes.Min(node => node.X) + nodes.Max(node => node.X + node.Width)) / 2;
    internal static void Move(RenderProject project, string id, double delta)
    {
        int index = Index(project, id);
        project.Nodes[index] = project.Nodes[index] with { X = project.Nodes[index].X + delta };
    }
    internal static void MoveSubtree(RenderProject project, string id, double delta)
    {
        foreach (var node in OwnedBranch(project, id))
            Move(project, node.Id, delta);
    }
    internal static IEnumerable<RenderNode[]> SharedGroups(RenderProject project) => project.Nodes
        .Where(node => Parents(project, node.Id).Length > 1)
        .GroupBy(node => string.Join("|", Parents(project, node.Id).Select(parent => parent.Id).OrderBy(id => id, StringComparer.Ordinal)))
        .Select(group => group.ToArray());
}
