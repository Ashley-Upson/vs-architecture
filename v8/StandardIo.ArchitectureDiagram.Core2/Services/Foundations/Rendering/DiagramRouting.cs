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
    internal static DrawingRoute[] CreateRoutes(ProjectModelDrawing drawing, RenderConfiguration? configuration = null,
        IEnumerable<(string TargetId, DrawingPoint[] Points)>? reservedRoutes = null)
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

        var routes = links.Select(selector: link =>
        {
            DrawingNode from = nodes[link.FromType!], to = nodes[link.ToType!];
            double sourceX = sourceExits[link];
            double targetX = to.X + to.Width / 2;
            double busY = busHeights[link.ToType!];
            var obstacles = drawing.Nodes.Where(node => node.Id != from.Id && node.Id != to.Id
                && node.Y < busY && node.Y + node.Height > from.Y + from.Height).ToArray();
            if (obstacles.Any(node => sourceX > node.X && sourceX < node.X + node.Width))
            {
                double clearance = horizontalOffset + Math.Abs(sourceX - from.X - from.Width / 2);
                var points = new List<DrawingPoint> { new(sourceX, from.Y + from.Height) };
                double column = sourceX, previousBottom = from.Y + from.Height;
                var bands = new List<List<DrawingNode>>();
                foreach (var obstacle in obstacles.OrderBy(node => node.Y))
                {
                    if (bands.Count == 0 || obstacle.Y >= bands[^1].Max(node => node.Y + node.Height))
                        bands.Add([]);
                    bands[^1].Add(obstacle);
                }
                foreach (var band in bands)
                {
                    if (band.Any(node => column > node.X - clearance && column < node.X + node.Width + clearance))
                    {
                        double nextColumn = band.SelectMany(node => new[] { node.X - clearance, node.X + node.Width + clearance })
                            .Where(x => band.All(node => x <= node.X - clearance || x >= node.X + node.Width + clearance))
                            .OrderBy(x => Math.Abs(x - column)).ThenBy(x => Math.Abs(x - targetX)).First();
                        double departure = (previousBottom + band.Min(node => node.Y)) / 2;
                        points.Add(new(column, departure));
                        points.Add(new(nextColumn, departure));
                        column = nextColumn;
                    }
                    previousBottom = band.Max(node => node.Y + node.Height);
                }
                points.Add(new(column, busY));
                points.Add(new(targetX, busY));
                points.Add(new(targetX, to.Y));
                return new DrawingRoute(link, points.ToArray());
            }
            return new DrawingRoute(link, new[]
            {
                new DrawingPoint(sourceX, from.Y + from.Height),
                new DrawingPoint(sourceX, busY),
                new DrawingPoint(targetX, busY),
                new DrawingPoint(targetX, to.Y)
            });
        }).ToArray();
        SeparateHorizontalSegments(routes, nodes, horizontalOffset, reservedRoutes);
        return routes;
    }

    private static void SeparateHorizontalSegments(DrawingRoute[] routes, Dictionary<string, DrawingNode> nodes,
        double horizontalOffset, IEnumerable<(string TargetId, DrawingPoint[] Points)>? reservedRoutes)
    {
        var occupied = new List<(string TargetId, double Left, double Right, double Y)>();
        foreach (var route in reservedRoutes ?? [])
        {
            for (int index = 1; index < route.Points.Length; index++)
            {
                var a = route.Points[index - 1];
                var b = route.Points[index];
                if (a.Y == b.Y && Math.Abs(a.X - b.X) > 0.001)
                    occupied.Add((route.TargetId, Math.Min(a.X, b.X), Math.Max(a.X, b.X), a.Y));
            }
        }

        // Reserve completed bus spans first, then the detours that were added around obstacles.
        // A destination shares its bus; unrelated destinations must not share an overlapping segment.
        var segments = routes.SelectMany(route => Enumerable.Range(1, route.Points.Length - 1)
            .Where(index => route.Points[index - 1].Y == route.Points[index].Y
                && Math.Abs(route.Points[index - 1].X - route.Points[index].X) > 0.001)
            .Select(index => (Route: route, Index: index, TargetId: nodes[route.Relationship.ToType!].Id,
                Y: route.Points[index].Y,
                Left: Math.Min(route.Points[index - 1].X, route.Points[index].X),
                Right: Math.Max(route.Points[index - 1].X, route.Points[index].X))))
            .GroupBy(segment => (segment.TargetId, segment.Y))
            .OrderByDescending(group => group.Any(segment => segment.Index == segment.Route.Points.Length - 2))
            .ThenBy(group => group.Key.Y)
            .ThenByDescending(group => group.Max(segment => segment.Right) - group.Min(segment => segment.Left))
            .ThenBy(group => group.Key.TargetId, StringComparer.Ordinal).ToArray();

        foreach (var group in segments)
        {
            double left = group.Min(segment => segment.Left), right = group.Max(segment => segment.Right);
            double preferred = group.Key.Y;
            double top = nodes.Values.Select(node => node.Y + node.Height)
                .Where(y => y < preferred).DefaultIfEmpty(preferred - horizontalOffset * 2).Max();
            double bottom = nodes.Values.Select(node => node.Y)
                .Where(y => y > preferred).DefaultIfEmpty(preferred + horizontalOffset * 2).Min();
            var boundaries = occupied.Where(run => run.TargetId != group.Key.TargetId
                    && run.Right >= left && right >= run.Left && run.Y > top && run.Y < bottom)
                .Select(run => run.Y).Append(top).Append(bottom).Distinct().OrderBy(y => y).ToArray();
            var gaps = boundaries.Zip(boundaries.Skip(1)).ToArray();
            double spacing = Math.Min(horizontalOffset, gaps.Max(gap => gap.Second - gap.First) / 2);
            double height = gaps.Where(gap => gap.Second - gap.First >= spacing * 2 - 0.000001)
                .Select(gap => Math.Clamp(preferred, gap.First + spacing, Math.Max(gap.First + spacing, gap.Second - spacing)))
                .OrderBy(y => Math.Abs(y - preferred)).ThenBy(y => y).First();
            foreach (var segment in group)
            {
                segment.Route.Points[segment.Index - 1] = segment.Route.Points[segment.Index - 1] with { Y = height };
                segment.Route.Points[segment.Index] = segment.Route.Points[segment.Index] with { Y = height };
            }
            occupied.Add((group.Key.TargetId, left, right, height));
        }
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
