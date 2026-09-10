// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core2.Models;

namespace StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Rendering;
internal static class DiagramRouting
{
    internal static DrawingRoute[] CreateRoutes(ProjectModelDrawing drawing, RenderConfiguration? configuration = null)
    {
        double horizontalOffset = (configuration ?? new()).HorizontalOffset;
        if (!double.IsFinite(horizontalOffset) || horizontalOffset <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(horizontalOffset));
        }

        var nodes = drawing.Nodes.ToDictionary(keySelector: node => node.Type.Name!);
        TypeRelationship[] links = drawing.Model.Dependencies ?? Array.Empty<TypeRelationship>();
        var sourceExits = links.ToDictionary(keySelector: link => link,
            elementSelector: link => nodes[link.FromType!].X + nodes[link.FromType!].Width / 2);
        foreach (var source in links.GroupBy(link => link.FromType))
        {
            DrawingNode from = nodes[source.Key!];
            double center = from.X + from.Width / 2;
            if (source.Select(link => link.ToType).Distinct().Count() == 1)
            {
                continue;
            }

            foreach (var direction in source.GroupBy(link => Math.Sign(nodes[link.ToType!].X + nodes[link.ToType!].Width / 2 - center)))
            {
                var destinations = direction.GroupBy(link => link.ToType)
                    .OrderBy(group => Math.Abs(nodes[group.Key!].X + nodes[group.Key!].Width / 2 - center))
                    .ThenBy(group => group.Key, StringComparer.Ordinal).ToArray();
                double step = Math.Min(horizontalOffset, (from.Width / 2 - 5) / destinations.Length);
                for (int index = 0; index < destinations.Length; index++)
                {
                    foreach (var link in destinations[index])
                    {
                        sourceExits[link] = center + direction.Key * step * (index + 1);
                    }
                }
            }
        }

        var busHeights = AssignBusHeights(drawing, nodes, links, sourceExits, horizontalOffset);

        return links.Select(selector: link =>
        {
            DrawingNode from = nodes[link.FromType!], to = nodes[link.ToType!];
            double sourceX = sourceExits[link];
            double targetX = to.X + to.Width / 2;
            double busY = busHeights[link.ToType!];
            return new DrawingRoute(link, new[]
            {
                new DrawingPoint(sourceX, from.Y + from.Height),
                new DrawingPoint(sourceX, busY),
                new DrawingPoint(targetX, busY),
                new DrawingPoint(targetX, to.Y)
            });
        }).ToArray();
    }

    private static Dictionary<string, double> AssignBusHeights(
        ProjectModelDrawing drawing,
        Dictionary<string, DrawingNode> nodes,
        TypeRelationship[] links,
        Dictionary<TypeRelationship, double> sourceExits, double horizontalOffset)
    {
        var destinations = links.Select(selector: link => link.ToType!).Distinct().Select(selector: name => nodes[name]);
        var busHeights = new Dictionary<string, double>(comparer: StringComparer.Ordinal);

        foreach (var row in destinations.GroupBy(keySelector: node => node.Y))
        {
            var targets = row.OrderBy(keySelector: node => node.X).ThenBy(keySelector: node => node.Type.Name, comparer: StringComparer.Ordinal).ToArray();
            double top = drawing.Nodes.Select(selector: node => node.Y + node.Height)
                .Where(predicate: bottom => bottom < row.Key)
                .DefaultIfEmpty(defaultValue: 0)
                .Max();
            var spans = targets.Select(target =>
            {
                double center = target.X + target.Width / 2;
                var sources = links.Where(link => link.ToType == target.Type.Name)
                    .Select(link => sourceExits[link])
                    .Append(center).ToArray();
                double distance = links.Where(link => link.ToType == target.Type.Name)
                    .Max(link => Math.Abs(center - (nodes[link.FromType!].X + nodes[link.FromType!].Width / 2)));
                return (Target: target, Left: sources.Min(), Right: sources.Max(), Distance: distance);
            }).OrderByDescending(span => span.Distance).ThenBy(span => span.Left)
                .ThenBy(span => span.Target.Type.Name, StringComparer.Ordinal).ToArray();

            var lanes = new List<List<(double Left, double Right)>>();
            foreach (var span in spans)
            {
                int lane = lanes.FindIndex(runs => runs.All(run => run.Right < span.Left || span.Right < run.Left));
                if (lane < 0)
                {
                    lane = lanes.Count;
                    lanes.Add(new List<(double Left, double Right)>());
                }
                lanes[lane].Add((span.Left, span.Right));

                busHeights.Add(span.Target.Type.Name!, lane);
            }
            double slice = Math.Min(horizontalOffset, (row.Key - top) / (2 * Math.Max(1, lanes.Count)));
            foreach (var span in spans)
            {
                string name = span.Target.Type.Name!;
                busHeights[name] = (top + row.Key) / 2 + slice * busHeights[name];
            }
        }

        return busHeights;
    }

}