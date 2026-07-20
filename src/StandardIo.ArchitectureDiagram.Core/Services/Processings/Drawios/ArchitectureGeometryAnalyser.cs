using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using StandardIo.ArchitectureDiagram.Core.Models;
using StandardIo.ArchitectureDiagram.Core.Models.Generation;

namespace StandardIo.ArchitectureDiagram.Core.Services.Processings.Drawios;

public sealed class ArchitectureGeometryAnalyser : IArchitectureGeometryAnalyser
{
    public ArchitectureGeometryAnalysis Analyse(TypedArchitectureGenerationResult generation)
    {
        if (generation is null) throw new ArgumentNullException(nameof(generation));
        var root = generation.Page.GraphModel.Element("root")
            ?? throw new InvalidOperationException("Draw.io page has no root element.");
        var cells = root.Elements("mxCell").ToDictionary(Id, StringComparer.Ordinal);
        var projects = cells.Values.Where(IsProject).Select(cell => new ArchitectureProjectGeometry(
            Id(cell), Value(cell), AbsoluteBounds(cell, cells))).OrderBy(item => item.Id, StringComparer.Ordinal).ToArray();
        var projectIds = new HashSet<string>(projects.Select(item => item.Id), StringComparer.Ordinal);
        var nodes = cells.Values.Where(cell => IsNode(cell, projectIds)).Select(cell => new ArchitectureNodeGeometry(
            Id(cell), Owner(cell, projectIds), Value(cell), Style(cell).Contains("shape=rhombus", StringComparison.Ordinal),
            AbsoluteBounds(cell, cells))).OrderBy(item => item.Id, StringComparer.Ordinal).ToArray();
        var findings = new List<ArchitectureGeometryFinding>();
        ValidateNodes(nodes, projects, findings);
        var terminals = LogicalTerminals(cells.Values);
        var routes = generation.Routes.OrderBy(item => item.LogicalRouteId, StringComparer.Ordinal)
            .Select(route => AnalyseRoute(route, nodes, terminals, findings)).ToArray();
        ValidateRoutePairs(generation.Routes, findings);
        IncludeTypedFindings(generation, findings);
        var orderedFindings = findings.OrderBy(item => item.Severity).ThenBy(item => item.Code, StringComparer.Ordinal)
            .ThenBy(item => item.LogicalRouteId, StringComparer.Ordinal).ThenBy(item => item.NodeId, StringComparer.Ordinal).ToArray();
        var pageBounds = Bounds(nodes.Select(item => item.Bounds).Concat(projects.Select(item => item.Bounds)));
        var pageSha = Hash(generation.Page.GraphModel.ToString(SaveOptions.DisableFormatting));
        var analysisSha = Hash(JsonSerializer.Serialize(new { nodes, projects, routes, findings = orderedFindings }));
        var summary = new ArchitectureGeometrySummary(
            generation.Diagram.Projects.Sum(project => project.Nodes.Count) + generation.Diagram.ExternalNodes.Count,
            generation.Diagram.Links.Count, nodes.Length, generation.Routes.Count,
            cells.Values.Count(cell => Attr(cell, "edge") == "1"), projects.Length,
            orderedFindings.Count(item => item.Severity == ArchitectureAnalysisSeverity.HardInvalid),
            orderedFindings.Count(item => item.Severity == ArchitectureAnalysisSeverity.LikelyDefect),
            orderedFindings.Count(item => item.Severity == ArchitectureAnalysisSeverity.VisualQualityWarning),
            Count(orderedFindings, "NodeOverlap"), Count(orderedFindings, "LinkNodeIntersection"),
            Count(orderedFindings, "SharedSegment"), Count(orderedFindings, "InvalidPerpendicularContact"),
            Count(orderedFindings, "ParallelClearanceDeficit"), Count(orderedFindings, "DiagonalSegment"),
            Count(orderedFindings, "ZeroLengthSegment"), routes.Sum(item => item.Length),
            routes.Select(item => item.Length).DefaultIfEmpty().Max(), routes.Select(item => item.DetourRatio).DefaultIfEmpty().Max(),
            routes.Sum(item => item.BendCount), routes.Select(item => item.BendCount).DefaultIfEmpty().Max(),
            routes.Sum(item => item.PointCount), pageBounds, pageSha, analysisSha);
        return new ArchitectureGeometryAnalysis(summary, nodes, projects, routes, orderedFindings);
    }

    public string ToJson(ArchitectureGeometryAnalysis analysis) => JsonSerializer.Serialize(
        analysis ?? throw new ArgumentNullException(nameof(analysis)), new JsonSerializerOptions { WriteIndented = true });

    public string ToMarkdown(ArchitectureGeometryAnalysis analysis)
    {
        if (analysis is null) throw new ArgumentNullException(nameof(analysis));
        var s = analysis.Summary;
        var result = new StringBuilder().AppendLine("# Architecture geometry analysis").AppendLine()
            .AppendLine($"- Semantic nodes/links: {s.SemanticNodeCount} / {s.SemanticLinkCount}")
            .AppendLine($"- Rendered nodes/routes: {s.RenderedNodeCount} / {s.RenderedLogicalRouteCount}")
            .AppendLine($"- Hard findings: {s.HardFindingCount}")
            .AppendLine($"- Likely defects: {s.LikelyDefectCount}")
            .AppendLine($"- Visual warnings: {s.VisualWarningCount}")
            .AppendLine($"- Page bounds: {s.PageBounds.Width} x {s.PageBounds.Height}")
            .AppendLine($"- Total/max route length: {s.TotalRouteLength} / {s.MaximumRouteLength}")
            .AppendLine($"- Maximum detour ratio: {s.MaximumDetourRatio:0.00}")
            .AppendLine($"- Total/max bends: {s.TotalBends} / {s.MaximumBendsPerRoute}")
            .AppendLine($"- Page SHA-256: `{s.PageSha256}`")
            .AppendLine($"- Analysis SHA-256: `{s.AnalysisSha256}`").AppendLine();
        if (analysis.Findings.Count == 0) return result.AppendLine("No findings.").ToString();
        result.AppendLine("## Findings").AppendLine();
        foreach (var finding in analysis.Findings)
            result.AppendLine($"- **{finding.Severity} / {finding.Code}**: {finding.Description}");
        return result.ToString();
    }

    private static void ValidateNodes(IReadOnlyList<ArchitectureNodeGeometry> nodes,
        IReadOnlyList<ArchitectureProjectGeometry> projects, ICollection<ArchitectureGeometryFinding> findings)
    {
        foreach (var node in nodes)
        {
            if (node.Bounds.Width <= 0 || node.Bounds.Height <= 0)
                findings.Add(Hard("InvalidNodeDimensions", $"Node {node.Id} has non-positive dimensions.", node: node.Id));
            if (node.OwnerProjectId is { } owner)
            {
                var project = projects.SingleOrDefault(item => item.Id == owner);
                if (project is null || !Contains(project.Bounds, node.Bounds))
                    findings.Add(Hard("NodeOutsideOwner", $"Node {node.Id} is outside owning project {owner}.", node: node.Id, other: owner));
            }
        }
        for (var left = 0; left < nodes.Count; left++)
        for (var right = left + 1; right < nodes.Count; right++)
            if (InteriorIntersects(nodes[left].Bounds, nodes[right].Bounds))
                findings.Add(Hard("NodeOverlap", $"Nodes {nodes[left].Id} and {nodes[right].Id} overlap.", nodes[left].Id, other: nodes[right].Id));
    }

    private static ArchitectureRouteAnalysis AnalyseRoute(GeneratedRoute route,
        IReadOnlyList<ArchitectureNodeGeometry> nodes,
        IReadOnlyDictionary<string, (string? Source, string? Target)> terminals,
        ICollection<ArchitectureGeometryFinding> findings)
    {
        var points = route.Points;
        var length = 0;
        var bends = 0;
        for (var index = 1; index < points.Count; index++)
        {
            var start = points[index - 1]; var end = points[index];
            if (start == end) findings.Add(Hard("ZeroLengthSegment", $"Route {route.LogicalRouteId} contains a zero-length segment.",
                route: route.LogicalRouteId, locations: new[] { start }));
            if (start.X != end.X && start.Y != end.Y) findings.Add(Hard("DiagonalSegment", $"Route {route.LogicalRouteId} contains a diagonal segment.",
                route: route.LogicalRouteId, locations: new[] { start, end }));
            length += Distance(start, end);
            if (index > 1 && Direction(points[index - 2], start) != Direction(start, end)) bends++;
        }
        terminals.TryGetValue(route.LogicalRouteId, out var terminal);
        foreach (var node in nodes.Where(node => node.Id != terminal.Source && node.Id != terminal.Target))
        for (var index = 1; index < points.Count; index++)
            if (SegmentIntersectsInterior(points[index - 1], points[index], node.Bounds))
            {
                findings.Add(Hard("LinkNodeIntersection", $"Route {route.LogicalRouteId} intersects node {node.Id}.", node.Id,
                    route.LogicalRouteId, locations: new[] { points[index - 1], points[index] }));
                break;
            }
        var direct = points.Count < 2 ? 0 : Distance(points[0], points[points.Count - 1]);
        var ratio = direct == 0 ? (length == 0 ? 1d : double.PositiveInfinity) : (double)length / direct;
        var stub = points.Count < 2 ? 0 : Math.Max(Distance(points[0], points[1]),
            Distance(points[points.Count - 2], points[points.Count - 1]));
        return new ArchitectureRouteAnalysis(route.LogicalRouteId, length, direct, ratio, bends, points.Count, stub);
    }

    private static void ValidateRoutePairs(IReadOnlyList<GeneratedRoute> routes, ICollection<ArchitectureGeometryFinding> findings)
    {
        for (var left = 0; left < routes.Count; left++)
        for (var right = left + 1; right < routes.Count; right++)
        {
            var maximum = Segments(routes[left].Points).SelectMany(a => Segments(routes[right].Points).Select(b => CollinearOverlap(a, b))).DefaultIfEmpty().Max();
            if (maximum > 0) findings.Add(Hard("SharedSegment", $"Routes {routes[left].LogicalRouteId} and {routes[right].LogicalRouteId} share {maximum}px.",
                route: routes[left].LogicalRouteId, other: routes[right].LogicalRouteId));
        }
    }

    private static void IncludeTypedFindings(TypedArchitectureGenerationResult generation, ICollection<ArchitectureGeometryFinding> findings)
    {
        foreach (var finding in generation.LogicalFindings.Concat(generation.PhysicalFindings).Where(item => item.IsStrictlyEnforced))
        {
            var code = Normalize(finding.Category);
            if (findings.Any(item => item.Code == code && item.LogicalRouteId == finding.LogicalRouteId && item.OtherId == finding.OtherRouteId)) continue;
            findings.Add(new ArchitectureGeometryFinding(ArchitectureAnalysisSeverity.HardInvalid, code, finding.Description,
                finding.OtherNodeId, finding.LogicalRouteId, finding.OtherRouteId, finding.Locations));
        }
    }

    private static string Normalize(string category) => category switch
    { "ParallelSpacing" or "SpacingDeficit" => "ParallelClearanceDeficit", "PerpendicularCrossing" => "InvalidPerpendicularContact", _ => category };
    private static IReadOnlyDictionary<string, (string? Source, string? Target)> LogicalTerminals(IEnumerable<XElement> cells) =>
        cells.Where(cell => Attr(cell, "edge") == "1" && cell.Attribute("logicalEdgeId") is not null)
            .GroupBy(cell => Attr(cell, "logicalEdgeId")!, StringComparer.Ordinal).ToDictionary(group => group.Key, group =>
            { var ordered = group.OrderBy(cell => Int(cell.Attribute("segmentIndex"))).ToArray(); return (Attr(ordered[0], "source"), Attr(ordered[ordered.Length - 1], "target")); }, StringComparer.Ordinal);
    private static bool IsProject(XElement cell) => Attr(cell, "vertex") == "1" && Style(cell).Contains("shape=swimlane", StringComparison.Ordinal);
    private static bool IsNode(XElement cell, ISet<string> projects)
    {
        if (Attr(cell, "vertex") != "1" || IsProject(cell) || cell.Element("mxGeometry") is not { } geometry) return false;
        if (Int(geometry.Attribute("width")) == 0 && Int(geometry.Attribute("height")) == 0) return false;
        var parent = Attr(cell, "parent"); return parent == "1" || (parent is not null && projects.Contains(parent));
    }
    private static string? Owner(XElement cell, ISet<string> projects) => Attr(cell, "parent") is { } parent && projects.Contains(parent) ? parent : null;
    private static ValidationRectangle AbsoluteBounds(XElement cell, IReadOnlyDictionary<string, XElement> cells)
    {
        var geometry = cell.Element("mxGeometry") ?? throw new InvalidOperationException($"Cell {Id(cell)} has no geometry.");
        var x = Int(geometry.Attribute("x")); var y = Int(geometry.Attribute("y")); var parent = Attr(cell, "parent");
        var visited = new HashSet<string>(StringComparer.Ordinal);
        while (parent is not null && parent != "0" && parent != "1" && cells.TryGetValue(parent, out var parentCell) && visited.Add(parent))
        { var g = parentCell.Element("mxGeometry"); if (g is not null) { x += Int(g.Attribute("x")); y += Int(g.Attribute("y")); } parent = Attr(parentCell, "parent"); }
        return new ValidationRectangle(x, y, Int(geometry.Attribute("width")), Int(geometry.Attribute("height")));
    }
    private static IEnumerable<(ValidationPoint Start, ValidationPoint End)> Segments(IReadOnlyList<ValidationPoint> points)
    { for (var index = 1; index < points.Count; index++) yield return (points[index - 1], points[index]); }
    private static int CollinearOverlap((ValidationPoint Start, ValidationPoint End) a, (ValidationPoint Start, ValidationPoint End) b)
    {
        if (a.Start.X == a.End.X && b.Start.X == b.End.X && a.Start.X == b.Start.X) return Overlap(a.Start.Y, a.End.Y, b.Start.Y, b.End.Y);
        if (a.Start.Y == a.End.Y && b.Start.Y == b.End.Y && a.Start.Y == b.Start.Y) return Overlap(a.Start.X, a.End.X, b.Start.X, b.End.X);
        return 0;
    }
    private static int Overlap(int a1, int a2, int b1, int b2) => Math.Max(0, Math.Min(Math.Max(a1, a2), Math.Max(b1, b2)) - Math.Max(Math.Min(a1, a2), Math.Min(b1, b2)));
    private static bool SegmentIntersectsInterior(ValidationPoint a, ValidationPoint b, ValidationRectangle r) =>
        a.X == b.X ? a.X > r.X && a.X < r.Right && Math.Max(Math.Min(a.Y, b.Y), r.Y) < Math.Min(Math.Max(a.Y, b.Y), r.Bottom) :
        a.Y == b.Y && a.Y > r.Y && a.Y < r.Bottom && Math.Max(Math.Min(a.X, b.X), r.X) < Math.Min(Math.Max(a.X, b.X), r.Right);
    private static bool InteriorIntersects(ValidationRectangle a, ValidationRectangle b) => Math.Max(a.X, b.X) < Math.Min(a.Right, b.Right) && Math.Max(a.Y, b.Y) < Math.Min(a.Bottom, b.Bottom);
    private static bool Contains(ValidationRectangle a, ValidationRectangle b) => b.X >= a.X && b.Y >= a.Y && b.Right <= a.Right && b.Bottom <= a.Bottom;
    private static ValidationRectangle Bounds(IEnumerable<ValidationRectangle> source)
    { var a = source.ToArray(); if (a.Length == 0) return new(0, 0, 0, 0); var x = a.Min(i => i.X); var y = a.Min(i => i.Y); return new(x, y, a.Max(i => i.Right) - x, a.Max(i => i.Bottom) - y); }
    private static int Distance(ValidationPoint a, ValidationPoint b) => Math.Abs(b.X - a.X) + Math.Abs(b.Y - a.Y);
    private static int Direction(ValidationPoint a, ValidationPoint b) => a.X == b.X ? Math.Sign(b.Y - a.Y) * 2 : Math.Sign(b.X - a.X);
    private static int Count(IEnumerable<ArchitectureGeometryFinding> findings, string code) => findings.Count(item => item.Code == code);
    private static string Id(XElement cell) => Attr(cell, "id") ?? throw new InvalidOperationException("Draw.io cell has no id.");
    private static string Value(XElement cell) => Attr(cell, "value") ?? string.Empty;
    private static string Style(XElement cell) => Attr(cell, "style") ?? string.Empty;
    private static string? Attr(XElement cell, string name) => (string?)cell.Attribute(name);
    private static int Int(XAttribute? attribute) => attribute is null ? 0 : (int)Math.Round(double.Parse(attribute.Value, CultureInfo.InvariantCulture));
    private static ArchitectureGeometryFinding Hard(string code, string description, string? node = null, string? route = null,
        string? other = null, IReadOnlyList<ValidationPoint>? locations = null) => new(ArchitectureAnalysisSeverity.HardInvalid, code, description, node, route, other, locations);
    private static string Hash(string value) { using var sha = SHA256.Create(); return string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(value)).Select(item => item.ToString("x2", CultureInfo.InvariantCulture))); }
}
