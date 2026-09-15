// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core2.Brokers.Rendering;
using StandardIo.ArchitectureDiagram.Core2.Models;
using StandardIo.ArchitectureDiagram.Core2.Services.Processings.Layout;
namespace StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Rendering;
internal sealed class ProjectModelLayoutService(IProjectModelLayoutBroker projectModelLayoutBroker) : IProjectModelLayoutService
{
    public RenderModel Layout(RenderModel renderModel)
    {
        ArgumentNullException.ThrowIfNull(renderModel);
        int maxIterations = renderModel.Configuration.MaxLayoutIterations;
        if (maxIterations <= 0) throw new ArgumentOutOfRangeException(nameof(maxIterations));
        var rules = projectModelLayoutBroker.GetLayoutRuleServices().ToArray();
        string[] errors = Array.Empty<string>();
        for (int iteration = 0; iteration < maxIterations; iteration++)
        {
            renderModel.LayoutIterations = iteration + 1;
            foreach (var rule in rules) rule.ApplyRule(renderModel);
            errors = Validate(renderModel).ToArray();
            if (errors.Length == 0) return renderModel;
        }
        throw new InvalidOperationException($"Layout did not converge after {maxIterations} iterations: {string.Join("; ", errors)}");
    }

    private static IEnumerable<string> Validate(RenderModel model)
    {
        var projects = model.CrossProjectConnections.Length == 0 ? model.Projects : model.Projects.Append(LayoutGraph.ProjectGraph(model));
        foreach (var project in projects)
        {
            double spacing = model.Projects.Contains(project) ? model.Configuration.Architecture.NodeSpacing : model.Configuration.Architecture.ProjectSpacing;
            foreach (var parent in project.Nodes)
            {
                if (!double.IsFinite(parent.X) || !double.IsFinite(parent.Y)) yield return $"Invalid position: {parent.TypeName}";
                if (parent.X < -LayoutGraph.Tolerance || parent.Y < -LayoutGraph.Tolerance || parent.X + parent.Width > project.Width + LayoutGraph.Tolerance || parent.Y + parent.Height > project.Height + LayoutGraph.Tolerance)
                    yield return $"Container bounds: {parent.TypeName}";
                var children = LayoutGraph.OwnedChildren(project, parent.Id);
                if (children.Length > 0 && Math.Abs(LayoutGraph.Centre(parent) - LayoutGraph.Midpoint(children)) > LayoutGraph.Tolerance)
                    yield return $"Parent centring: {parent.TypeName}";
            }
            foreach (var group in LayoutGraph.BranchGroups(project))
            {
                var roots = group.OrderBy(root => root.X).ThenBy(root => root.Id, StringComparer.Ordinal).ToArray();
                for (int index = 1; index < roots.Length; index++)
                    if (LayoutGraph.BranchClearance(project, roots[index - 1].Id, roots[index].Id, model.Configuration.NoDuplicates && model.Projects.Contains(project)) < spacing - LayoutGraph.Tolerance)
                        yield return $"Branch spacing: {roots[index].TypeName}";
            }
            foreach (var row in project.Nodes.GroupBy(node => node.Y))
            {
                var nodes = row.OrderBy(node => node.X).ToArray();
                for (int index = 1; index < nodes.Length; index++)
                    if (nodes[index].X - nodes[index - 1].X - nodes[index - 1].Width < spacing - LayoutGraph.Tolerance)
                        yield return $"Node spacing: {nodes[index].TypeName}";
            }
            foreach (var edge in project.Connections)
            {
                var source = project.Nodes.Single(node => node.Id == edge.SourceId);
                var target = project.Nodes.Single(node => node.Id == edge.TargetId);
                // A return path identifies a genuine cycle, whose closing edge cannot point down.
                if (target.Y <= source.Y && !HasPath(project, target.Id, source.Id))
                    yield return $"Child depth: {edge.ToType}";
            }
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
