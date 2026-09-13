using System;
using System.Linq;
using System.Collections.Generic;
using StandardIo.ArchitectureDiagram.Core2.Models;
namespace StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Rendering;
internal interface IContextualLayoutService { RenderModel Layout(RenderModel model, ContextualDiagram diagram); }
internal sealed class ContextualLayoutService : IContextualLayoutService
{
    public RenderModel Layout(RenderModel model, ContextualDiagram diagram)
    {
        bool composition = model.DiagramType == DiagramTypes.Composition;
        double nodeWidth = composition ? model.Configuration.Composition.NodeWidth : model.Configuration.DataModel.NodeWidth;
        double nodeSpacing = composition ? model.Configuration.Composition.NodeSpacing : model.Configuration.DataModel.NodeSpacing;
        double rowSpacing = composition ? model.Configuration.Composition.RowSpacing : model.Configuration.DataModel.RowSpacing;
        double projectSpacing = composition ? model.Configuration.Composition.ProjectSpacing : model.Configuration.DataModel.ProjectSpacing;
        var projects = new List<RenderProject>();
        double top = 40;
        int id = 0;
        foreach (var group in diagram.Types.GroupBy(t => t.Project))
        {
            var nodes = new List<RenderNode>();
            double width = nodeWidth;
            double rowTop = 60;
            foreach (var row in Rows(group.ToArray(), diagram.Links, composition))
            {
                int column = 0;
                foreach (var type in row)
                {
                    double x = 40 + column++ * (width + nodeSpacing), y = rowTop;
                    double height = Math.Max(60, type.Lines.Length * 18 + 24);
                    var lines = type.Lines.Select((text, i) => new RenderText(text, x + width / 2, y + 20 + i * 18, i == 0, i == 0 ? 12 : 11)).ToArray();
                    nodes.Add(new RenderNode("context-node-" + id++, type.Name, string.Join("\n", type.Lines), composition ? "#075985" : "#166534", x, y, width, height, lines));
                }
                rowTop += row.Max(type => Math.Max(60, type.Lines.Length * 18 + 24)) + rowSpacing;
            }
            double h = nodes.Max(n => n.Y + n.Height) + 50;
            projects.Add(new RenderProject("context-project-" + projects.Count, group.Key, 40, top, nodes.Max(n => n.X + n.Width) + 40, h, nodes.ToArray(), []));
            top += h + projectSpacing;
        }
        var owners = projects.SelectMany(p => p.Nodes.Select(n => (Project: p, Node: n))).ToDictionary(x => x.Node.TypeName);
        var cross = new List<RenderConnection>();
        foreach (var link in diagram.Links)
        {
            var a = owners[link.From]; var b = owners[link.To];
            bool local = a.Project.Id == b.Project.Id;
            double ax = a.Node.X + a.Node.Width / 2 + (local ? 0 : a.Project.X), ay = a.Node.Y + a.Node.Height + (local ? 0 : a.Project.Y);
            double bx = b.Node.X + b.Node.Width / 2 + (local ? 0 : b.Project.X), by = b.Node.Y + (local ? 0 : b.Project.Y);
            double gutter = a.Project.Nodes.Where(n => n.Y == a.Node.Y).Max(n => n.Y + n.Height) + (local ? 0 : a.Project.Y) + 25;
            DrawingPoint[] points = by > ay ? [new(ax, ay), new(ax, gutter), new(bx, gutter), new(bx, by)]
                : [new(ax, ay), new(ax, gutter), new(bx + b.Node.Width / 2 + 25, gutter), new(bx + b.Node.Width / 2 + 25, by - 20), new(bx, by - 20), new(bx, by)];
            var edge = new RenderConnection("context-edge-" + id++, a.Node.Id, b.Node.Id, link.From, link.To, false, points) { Label = link.Label, IsComposition = model.DiagramType == DiagramTypes.Composition };
            if (local) { int i = projects.FindIndex(p => p.Id == a.Project.Id); projects[i] = projects[i] with { Connections = [..projects[i].Connections, edge] }; }
            else cross.Add(edge);
        }
        model.Projects = projects.ToArray(); model.CrossProjectConnections = cross.ToArray();
        model.Width = projects.Select(p => p.X + p.Width + 40).DefaultIfEmpty(400).Max(); model.Height = Math.Max(200, top);
        RouteForwardLinks(model);
        return model;
    }
    private static IEnumerable<ContextualType[]> Rows(ContextualType[] types, ContextualLink[] links, bool composition)
    {
        if (!composition) return types.Chunk(4);
        var names = types.Select(t => t.Name).ToHashSet(StringComparer.Ordinal);
        var local = links.Where(l => names.Contains(l.From) && names.Contains(l.To) && l.From != l.To).DistinctBy(l => (l.From, l.To)).ToArray();
        var depth = names.ToDictionary(n => n, _ => 0, StringComparer.Ordinal);
        var parents = names.ToDictionary(n => n, n => local.Count(l => l.To == n), StringComparer.Ordinal);
        var pending = new Queue<string>(names.Where(n => parents[n] == 0).OrderBy(n => n, StringComparer.Ordinal));
        var placed = new HashSet<string>(StringComparer.Ordinal);
        while (placed.Count < names.Count)
        {
            // Reference cycles are retained, but cannot impose an endless sequence of rows.
            if (pending.Count == 0) pending.Enqueue(names.Where(n => !placed.Contains(n)).OrderBy(n => n, StringComparer.Ordinal).First());
            string current = pending.Dequeue();
            if (!placed.Add(current)) continue;
            foreach (var link in local.Where(l => l.From == current && !placed.Contains(l.To)))
            {
                depth[link.To] = Math.Max(depth[link.To], depth[current] + 1);
                if (--parents[link.To] == 0) pending.Enqueue(link.To);
            }
        }
        return types.GroupBy(t => depth[t.Name]).OrderBy(g => g.Key)
            .SelectMany(g => g.OrderBy(t => t.Name, StringComparer.Ordinal).Chunk(4));
    }

    private static void RouteForwardLinks(RenderModel model)
    {
        var nodes = model.Projects.SelectMany(p => p.Nodes.Select(n => new DrawingNode(n.Id, new DefinedType { Name = n.Id }, n.Label, p.X + n.X, p.Y + n.Y, n.Width, n.Height))).ToArray();
        var byId = nodes.ToDictionary(n => n.Id);
        var edges = model.Projects.SelectMany(p => p.Connections).Concat(model.CrossProjectConnections)
            .Where(e => byId[e.TargetId].Y > byId[e.SourceId].Y + byId[e.SourceId].Height).ToArray();
        if (edges.Length == 0) return;
        var source = new ProjectModel { Dependencies = edges.Select(e => new TypeRelationship { FromType = e.SourceId, ToType = e.TargetId, DependencyType = DependencyType.Consumed }).ToArray() };
        var routes = DiagramRouting.CreateRoutes(new ProjectModelDrawing("context", source, 0, model.Width, model.Height, nodes), model.Configuration);
        var points = edges.Select((e, i) => (e.Id, routes[i].Points)).ToDictionary(x => x.Id, x => x.Points);
        foreach (var project in model.Projects)
        for (int i = 0; i < project.Connections.Length; i++)
        {
            var edge = project.Connections[i];
            if (points.TryGetValue(edge.Id, out var route)) project.Connections[i] = edge with { Points = route.Select(p => new DrawingPoint(p.X - project.X, p.Y - project.Y)).ToArray() };
        }
        for (int i = 0; i < model.CrossProjectConnections.Length; i++)
            if (points.TryGetValue(model.CrossProjectConnections[i].Id, out var route)) model.CrossProjectConnections[i] = model.CrossProjectConnections[i] with { Points = route };
    }
}
