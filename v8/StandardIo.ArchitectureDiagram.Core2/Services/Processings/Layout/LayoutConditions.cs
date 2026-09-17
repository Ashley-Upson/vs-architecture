using System;
using System.Collections.Generic;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core2.Models;
namespace StandardIo.ArchitectureDiagram.Core2.Services.Processings.Layout;
internal static class LayoutConditions
{
    private static IEnumerable<RenderProject> Projects(RenderModel model) => model.CrossProjectConnections.Length == 0 ? model.Projects : model.Projects.Append(LayoutGraph.ProjectGraph(model));
    internal static IEnumerable<string> FiniteCoordinates(RenderModel model) => model.Projects.SelectMany(p=>p.Nodes)
        .Where(n=>!double.IsFinite(n.X)||!double.IsFinite(n.Y)).Select(n=>"Invalid position: "+n.TypeName);
    internal static IEnumerable<string> Bounds(RenderModel model) => Projects(model).SelectMany(p=>p.Nodes
        .Where(n=>n.X < -LayoutGraph.Tolerance || n.Y < -LayoutGraph.Tolerance || n.X+n.Width>p.Width+LayoutGraph.Tolerance || n.Y+n.Height>p.Height+LayoutGraph.Tolerance)
        .Select(n=>"Container bounds: "+n.TypeName));
    internal static IEnumerable<string> ParentCentring(RenderModel model)
    {
        foreach(var project in Projects(model)) foreach(var parent in project.Nodes)
        {
            var children=LayoutGraph.OwnedChildren(project,parent.Id);
            if(children.Length>0 && !model.PassageOffsetParents.Contains(parent.Id) && Math.Abs(LayoutGraph.Centre(parent)-LayoutGraph.Midpoint(children))>LayoutGraph.Tolerance)
                yield return "Parent centring: "+parent.TypeName;
        }
    }
    internal static IEnumerable<string> BranchSpacing(RenderModel model)
    {
        foreach(var project in Projects(model)) foreach(var group in LayoutGraph.BranchGroups(project))
        {
            double spacing=model.Projects.Contains(project)?model.Configuration.Architecture.NodeSpacing:model.Configuration.Architecture.ProjectSpacing;
            var roots=group.OrderBy(n=>n.X).ThenBy(n=>n.Id,StringComparer.Ordinal).ToArray();
            foreach(var pair in roots.Zip(roots.Skip(1)))
                if(LayoutGraph.BranchClearance(project,pair.First.Id,pair.Second.Id,model.Projects.Contains(project),respectBranchOrder:false)<spacing-LayoutGraph.Tolerance)
                    yield return "Branch spacing: "+pair.Second.TypeName;
        }
    }
    internal static IEnumerable<string> NodeSpacing(RenderModel model)
    {
        foreach(var project in Projects(model)) foreach(var row in project.Nodes.GroupBy(n=>n.Y))
        {
            double spacing=model.Projects.Contains(project)?model.Configuration.Architecture.NodeSpacing:model.Configuration.Architecture.ProjectSpacing;
            var nodes=row.OrderBy(n=>n.X).ToArray();
            foreach(var pair in nodes.Zip(nodes.Skip(1)))
                if(pair.Second.X-pair.First.X-pair.First.Width<spacing-LayoutGraph.Tolerance) yield return "Node spacing: "+pair.Second.TypeName;
        }
    }
    internal static IEnumerable<string> Depth(RenderModel model)
    {
        foreach(var project in Projects(model)) foreach(var edge in project.Connections)
        {
            var source=project.Nodes.Single(n=>n.Id==edge.SourceId);var target=project.Nodes.Single(n=>n.Id==edge.TargetId);
            if(target.Y<=source.Y && !HasPath(project,target.Id,source.Id)) yield return "Child depth: "+edge.ToType;
        }
    }
    private static bool HasPath(RenderProject project, string source, string target)
    {
        var pending = new Queue<string>();
        var visited = new HashSet<string>();
        pending.Enqueue(source);
        while (pending.Count > 0)
        {
            string current = pending.Dequeue();
            if (current == target) return true;
            if (!visited.Add(current)) continue;
            foreach (var edge in project.Connections.Where(edge => edge.SourceId == current)) pending.Enqueue(edge.TargetId);
        }
        return false;
    }
}
