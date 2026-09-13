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
            double rowHeight = group.Max(t => Math.Max(60, t.Lines.Length * 18 + 24));
            int index = 0;
            foreach (var type in group)
            {
                double x = 40 + (index % 4) * (width + nodeSpacing), y = 60 + (index / 4) * (rowHeight + rowSpacing);
                double height = Math.Max(60, type.Lines.Length * 18 + 24);
                var lines = type.Lines.Select((text, i) => new RenderText(text, x + width / 2, y + 20 + i * 18, i == 0, i == 0 ? 12 : 11)).ToArray();
                nodes.Add(new RenderNode("context-node-" + id++, type.Name, string.Join("\n", type.Lines), model.DiagramType == DiagramTypes.Composition ? "#075985" : "#166534", x, y, width, height, lines));
                index++;
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
            double gutter = ay + 25;
            DrawingPoint[] points = by > ay ? [new(ax, ay), new(ax, gutter), new(bx, gutter), new(bx, by)]
                : [new(ax, ay), new(ax, gutter), new(bx + b.Node.Width / 2 + 25, gutter), new(bx + b.Node.Width / 2 + 25, by - 20), new(bx, by - 20), new(bx, by)];
            var edge = new RenderConnection("context-edge-" + id++, a.Node.Id, b.Node.Id, link.From, link.To, false, points) { Label = link.Label, IsComposition = model.DiagramType == DiagramTypes.Composition };
            if (local) { int i = projects.FindIndex(p => p.Id == a.Project.Id); projects[i] = projects[i] with { Connections = [..projects[i].Connections, edge] }; }
            else cross.Add(edge);
        }
        model.Projects = projects.ToArray(); model.CrossProjectConnections = cross.ToArray();
        model.Width = projects.Select(p => p.X + p.Width + 40).DefaultIfEmpty(400).Max(); model.Height = Math.Max(200, top);
        return model;
    }
}
