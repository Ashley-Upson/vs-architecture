// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core2.Models;
namespace StandardIo.ArchitectureDiagram.Core2.Services.Processings.Layout;
internal static class LayoutGraph
{
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
        var ids = new HashSet<string>();
        var pending = new Queue<string>();
        pending.Enqueue(rootId);
        while (pending.Count > 0)
        {
            string id = pending.Dequeue();
            if (!ids.Add(id)) continue;
            foreach (var child in OwnedChildren(project, id)) pending.Enqueue(child.Id);
        }
        return project.Nodes.Where(node => ids.Contains(node.Id)).ToArray();
    }

    internal static IEnumerable<RenderNode[]> BranchGroups(RenderProject project)
    {
        foreach (var parent in project.Nodes.OrderByDescending(node => node.Y))
        {
            var children = OwnedChildren(project, parent.Id);
            if (children.Length > 1) yield return children;
        }
        // Shared nodes start independent branches; only vertically overlapping branches compete for horizontal space.
        var roots = project.Nodes.Where(node => Parents(project, node.Id).Length != 1)
            .Select(root => (Root: root, Branch: OwnedBranch(project, root.Id)))
            .OrderBy(pair => pair.Branch.Min(node => node.Y)).ToArray();
        var seen = new HashSet<string>();
        foreach (double top in roots.Select(pair => pair.Branch.Min(node => node.Y)).Distinct())
        {
            var group = roots.Where(pair => pair.Branch.Min(node => node.Y) <= top && pair.Branch.Max(node => node.Y + node.Height) > top)
                .Select(pair => pair.Root).ToArray();
            string key = string.Join("|", group.Select(root => root.Id).OrderBy(id => id, StringComparer.Ordinal));
            if (group.Length > 1 && seen.Add(key)) yield return group;
        }
    }

    internal const double Tolerance = 0.01;
    internal static double Centre(RenderNode node) => node.X + node.Width / 2;
    internal static ProjectModel ToProjectModel(RenderProject project) => new()
    {
        Name = project.Name,
        Types = project.Nodes.Select(node => new DefinedType { Name = node.TypeName }).ToArray(),
        Dependencies = project.Connections.Select(edge => new TypeRelationship { FromType = edge.FromType, ToType = edge.ToType, DependencyType = edge.Inheritance ? DependencyType.Inheritance : DependencyType.Consumed }).ToArray()
    };
    internal static RenderNode[] Parents(RenderProject project, string id) => project.Connections
        .Where(edge => edge.TargetId == id && edge.SourceId != id)
        .Select(edge => project.Nodes.Single(node => node.Id == edge.SourceId)).DistinctBy(node => node.Id).ToArray();
    internal static RenderNode[] Children(RenderProject project, string id) => project.Connections
        .Where(edge => edge.SourceId == id && edge.TargetId != id)
        .Select(edge => project.Nodes.Single(node => node.Id == edge.TargetId)).Where(node => node.Y > project.Nodes.Single(parent => parent.Id == id).Y).DistinctBy(node => node.Id).ToArray();
    internal static RenderNode[] OwnedChildren(RenderProject project, string id) => Children(project, id).Where(node => Parents(project, node.Id).Length == 1).ToArray();
    internal static double Midpoint(IEnumerable<RenderNode> nodes) => (nodes.Min(node => node.X) + nodes.Max(node => node.X + node.Width)) / 2;
    internal static void Move(RenderProject project, string id, double delta)
    {
        int index = Array.FindIndex(project.Nodes, node => node.Id == id);
        project.Nodes[index] = project.Nodes[index] with { X = project.Nodes[index].X + delta };
    }
    internal static void MoveSubtree(RenderProject project, string id, double delta)
    {
        var pending = new Queue<string>();
        var visited = new HashSet<string>();
        pending.Enqueue(id);
        while (pending.Count > 0)
        {
            string current = pending.Dequeue();
            if (!visited.Add(current)) continue;
            foreach (var child in OwnedChildren(project, current)) pending.Enqueue(child.Id);
            Move(project, current, delta);
        }
    }
    internal static IEnumerable<RenderNode[]> SharedGroups(RenderProject project) => project.Nodes
        .Where(node => Parents(project, node.Id).Length > 1)
        .GroupBy(node => string.Join("|", Parents(project, node.Id).Select(parent => parent.Id).OrderBy(id => id, StringComparer.Ordinal)))
        .Select(group => group.ToArray());
}
