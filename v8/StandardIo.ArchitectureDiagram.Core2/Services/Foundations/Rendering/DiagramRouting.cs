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
        IEnumerable<(string TargetId, DrawingPoint[] Points)>? reservedRoutes = null, DiagramTypes diagramType = DiagramTypes.Architecture)
    {
        double horizontalOffset = (configuration ?? new()).HorizontalOffset;
        if (!double.IsFinite(horizontalOffset) || horizontalOffset <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(horizontalOffset));
        }

        bool preserveExits = configuration is not null && diagramType == DiagramTypes.Architecture;
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
                    .OrderByDescending(group => preserveExits ? nodes[group.Key!].Y : 0)
                    .ThenBy(group => Math.Abs(nodes[group.Key!].X + nodes[group.Key!].Width / 2 - center))
                    .ThenBy(group => group.Key, StringComparer.Ordinal).ToArray();
                double step = Math.Min(horizontalOffset, (from.Width / 2 - 5) / destinations.Length);
                if (preserveExits && direction.Key != 0)
                    step = Math.Min(step, destinations.Select((group,index) =>
                        Math.Abs(nodes[group.Key!].X + nodes[group.Key!].Width / 2 - center) / (index + 1)).Min());
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
            if (preserveExits && drawing.Nodes.Any(node => node.Y > from.Y + from.Height && node.Y < to.Y))
            {
                DrawingPoint[] passage = [new(sourceX, from.Y + from.Height), new(sourceX, busY),
                    new(targetX, busY), new(targetX, to.Y)];
                if (Clear(passage, drawing.Nodes.Where(node => node.Id != from.Id && node.Id != to.Id).ToArray(), horizontalOffset))
                    return new DrawingRoute(link, passage);
            }
            bool preferFewestBends = diagramType == DiagramTypes.Architecture && configuration is not null;
            if (preferFewestBends && to.Y > from.Y + from.Height)
            {
                double nextRow = drawing.Nodes.Where(node => node.Y > from.Y + from.Height)
                    .Select(node => node.Y).DefaultIfEmpty(to.Y).Min();
                double departure = (from.Y + from.Height + nextRow) / 2;
                DrawingPoint[] direct = [new(sourceX, from.Y + from.Height), new(sourceX, departure),
                    new(targetX, departure), new(targetX, to.Y)];
                if (Clear(direct, drawing.Nodes.Where(node => node.Id != from.Id && node.Id != to.Id).ToArray(), horizontalOffset))
                    return new DrawingRoute(link, direct);
            }
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
                return new DrawingRoute(link, PreferContinuousCorridor(points.ToArray(), obstacles, clearance, preferFewestBends));
            }
            return new DrawingRoute(link, new[]
            {
                new DrawingPoint(sourceX, from.Y + from.Height),
                new DrawingPoint(sourceX, busY),
                new DrawingPoint(targetX, busY),
                new DrawingPoint(targetX, to.Y)
            });
        }).ToArray();
        SeparateHorizontalSegments(routes, nodes, horizontalOffset, reservedRoutes, preserveExits);
        if (!preserveExits) OrderSourceExits(routes, nodes, horizontalOffset);
        return routes;
    }

    // Horizontal separation can change bend order. Assign exits from the final
    // routes so outer stems turn first and inner stems cannot cross those runs.
    internal static void OrderSourceExits(DrawingRoute[] routes, Dictionary<string, DrawingNode> nodes, double offset)
    {
        foreach (var group in routes.GroupBy(route => route.Relationship.FromType))
        {
            if (group.Count() < 2) continue;
            bool crosses = group.Any(horizontal => horizontal.Points.Length >= 3 && group.Any(vertical =>
                vertical != horizontal && vertical.Points.Length >= 2
                && vertical.Points[0].X > Math.Min(horizontal.Points[1].X, horizontal.Points[2].X)
                && vertical.Points[0].X < Math.Max(horizontal.Points[1].X, horizontal.Points[2].X)
                && horizontal.Points[1].Y > Math.Min(vertical.Points[0].Y, vertical.Points[1].Y)
                && horizontal.Points[1].Y < Math.Max(vertical.Points[0].Y, vertical.Points[1].Y)));
            if (!crosses) continue;
            var source = nodes[group.Key!];
            double centre = source.X + source.Width / 2;
            foreach (var side in group.Where(route => route.Points.Length >= 4)
                .GroupBy(route => Math.Sign(route.Points[2].X - centre)).Where(side => side.Key != 0))
            {
                var ordered = side.OrderBy(route => route.Points[1].Y)
                    .ThenByDescending(route => Math.Abs(route.Points[2].X - centre))
                    .ThenBy(route => route.Relationship.ToType, StringComparer.Ordinal).ToArray();
                double step = Math.Min(offset, (source.Width / 2 - 5) / ordered.Length);
                for (int index = 0; index < ordered.Length; index++)
                {
                    var points = ordered[index].Points;
                    double x = centre + side.Key * step * (ordered.Length - index);
                    var candidate = new[] { points[0] with { X = x }, points[1] with { X = x }, points[2] };
                    if (!Clear(candidate, nodes.Values.Where(node => node.Id != source.Id).ToArray(), 0)) continue;
                    points[0] = candidate[0];
                    points[1] = candidate[1];
                }
            }
        }
    }

    private static DrawingPoint[] PreferContinuousCorridor(DrawingPoint[] original, DrawingNode[] obstacles, double clearance, bool preferFewestBends)
    {
        if (original.Length <= 6) return original;
        double Length(DrawingPoint[] points) => points.Zip(points.Skip(1)).Sum(pair => Math.Abs(pair.First.X - pair.Second.X) + Math.Abs(pair.First.Y - pair.Second.Y));
        return obstacles.SelectMany(node => new[] { node.X - clearance, node.X + node.Width + clearance })
            .Append(original[0].X).Append(original[^1].X).Distinct()
            .Select(column => new[] { original[0], original[1], new DrawingPoint(column, original[1].Y), new DrawingPoint(column, original[^2].Y), original[^2], original[^1] })
            .Where(points => (preferFewestBends || Length(points) <= Length(original) + 0.001) && Clear(points, obstacles, clearance))
            .OrderBy(Length).FirstOrDefault() ?? original;
    }

    private static bool Clear(DrawingPoint[] points, DrawingNode[] obstacles, double clearance) => points.Zip(points.Skip(1)).All(pair => obstacles.All(node =>
            pair.First.X == pair.Second.X
                ? pair.First.X <= node.X - clearance || pair.First.X >= node.X + node.Width + clearance || Math.Max(pair.First.Y, pair.Second.Y) <= node.Y || Math.Min(pair.First.Y, pair.Second.Y) >= node.Y + node.Height
                : pair.First.Y <= node.Y || pair.First.Y >= node.Y + node.Height || Math.Max(pair.First.X, pair.Second.X) <= node.X || Math.Min(pair.First.X, pair.Second.X) >= node.X + node.Width));

    private static void SeparateHorizontalSegments(DrawingRoute[] routes, Dictionary<string, DrawingNode> nodes,
        double horizontalOffset, IEnumerable<(string TargetId, DrawingPoint[] Points)>? reservedRoutes, bool preserveExits = false)
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
            .GroupBy(segment => (segment.TargetId, segment.Y, Source: preserveExits && segment.Index == 2 ? segment.Route.Relationship.FromType : null))
            .OrderBy(group => preserveExits ? group.Key.Source : null, StringComparer.Ordinal)
            .ThenByDescending(group => preserveExits && group.Key.Source is not null ? Math.Abs(group.First().Route.Points[0].X - nodes[group.Key.Source].X - nodes[group.Key.Source].Width / 2) : 0)
            .ThenByDescending(group => group.Any(segment => segment.Index == segment.Route.Points.Length - 2))
            .ThenBy(group => group.Key.Y)
            .ThenByDescending(group => group.Max(segment => segment.Right) - group.Min(segment => segment.Left))
            .ThenBy(group => group.Key.TargetId, StringComparer.Ordinal).ToArray();

        var departures = new List<(string Source, int Side, double Top, double Bottom, double Y)>();
        foreach (var group in segments)
        {
            double left = group.Min(segment => segment.Left), right = group.Max(segment => segment.Right);
            double preferred = group.Key.Y;
            double top = nodes.Values.Select(node => node.Y + node.Height)
                .Where(y => y < preferred).DefaultIfEmpty(preferred - horizontalOffset * 2).Max();
            double bottom = nodes.Values.Select(node => node.Y)
                .Where(y => y > preferred).DefaultIfEmpty(preferred + horizontalOffset * 2).Min();
            double gutterTop = top;
            int side = group.Key.Source is null ? 0 : Math.Sign(group.First().Route.Points[2].X - group.First().Route.Points[0].X);
            if (preserveExits && group.Key.Source is not null)
            {
                top = departures.Where(run => run.Source == group.Key.Source && run.Side == side && run.Top == gutterTop && run.Bottom == bottom)
                    .Select(run => run.Y).DefaultIfEmpty(top).Max();
            }
            var boundaries = occupied.Where(run => run.TargetId != group.Key.TargetId
                    && run.Right >= left && right >= run.Left && run.Y > top && run.Y < bottom)
                .Select(run => run.Y).Append(top).Append(bottom).Distinct().OrderBy(y => y).ToArray();
            var gaps = boundaries.Zip(boundaries.Skip(1)).ToArray();
            double spacing = Math.Min(horizontalOffset, gaps.Max(gap => gap.Second - gap.First) / 2);
            double height = gaps.Where(gap => gap.Second - gap.First >= spacing * 2 - 0.000001)
                .Select(gap => Math.Clamp(preferred, gap.First + spacing, Math.Max(gap.First + spacing, gap.Second - spacing)))
                .OrderBy(y => Math.Abs(y - preferred)).ThenByDescending(y => y).First();
            foreach (var segment in group)
            {
                segment.Route.Points[segment.Index - 1] = segment.Route.Points[segment.Index - 1] with { Y = height };
                segment.Route.Points[segment.Index] = segment.Route.Points[segment.Index] with { Y = height };
            }
            occupied.Add((group.Key.TargetId, left, right, height));
            if (preserveExits && group.Key.Source is not null) departures.Add((group.Key.Source, side, gutterTop, bottom, height));
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
