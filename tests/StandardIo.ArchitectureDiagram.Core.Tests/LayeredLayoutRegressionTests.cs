using StandardIo.ArchitectureDiagram.Core.Models;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.Renderers;
using System.Globalization;
using System.Xml.Linq;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core.Tests;

public sealed class LayeredLayoutRegressionTests
{
    [Fact]
    public void Small_exposure_tree_centres_root_over_descendant_group()
    {
        var document = Export(MessyExposureGraph());

        var childSpanCentre = (AbsoluteX(document, "tree_path") + Right(document, "tree_generation_b")) / 2;

        Assert.Equal(childSpanCentre, CenterX(document, "tree_root"));
    }

    [Fact]
    public void Small_exposure_tree_aligns_single_parent_chains()
    {
        var document = Export(MessyExposureGraph());

        Assert.Equal(CenterX(document, "tree_registry_a"), CenterX(document, "tree_drawio_a"));
        Assert.Equal(CenterX(document, "tree_drawio_a"), CenterX(document, "tree_exporter_a"));
        Assert.Equal(CenterX(document, "tree_analysis_b"), CenterX(document, "tree_roslyn_b"));
        Assert.Equal(CenterX(document, "tree_roslyn_b"), CenterX(document, "tree_analyser_b"));
    }

    [Fact]
    public void Small_exposure_tree_balances_two_children_around_parent()
    {
        var document = Export(MessyExposureGraph());
        var parent = CenterX(document, "tree_generation_a");
        var left = CenterX(document, "tree_analysis_a");
        var right = CenterX(document, "tree_rendering_a");

        Assert.True(left < parent);
        Assert.True(right > parent);
        Assert.InRange(Math.Abs((parent - left) - (right - parent)), 0, 4);
    }

    [Fact]
    public void Small_exposure_tree_routes_share_consistent_initial_fanout_depth()
    {
        var document = Export(MessyExposureGraph());
        var edges = EdgesFrom(document, "tree_generation_a").ToArray();

        Assert.Equal(2, edges.Length);
        Assert.Single(edges.Select(edge => Points(edge).Skip(1).First().Y).Distinct());
    }

    [Fact]
    public void Exported_routes_do_not_contain_same_axis_immediate_reversal()
    {
        var document = Export(MessyExposureGraph());

        foreach (var edge in document.Descendants("mxCell").Where(cell => (string?)cell.Attribute("edge") == "1"))
        {
            Assert.False(ContainsImmediateReversal(PointsIncludingTerminals(document, edge)),
                $"{(string?)edge.Attribute("source")} -> {(string?)edge.Attribute("target")}");
        }
    }

    [Fact]
    public void Direct_vertical_dependency_emits_no_interior_waypoints()
    {
        var graph = new ArchitectureGraph(
            new[] { new ProjectContainer("project_a", "App", new[] { Node("tree_parent", "Parent"), Node("tree_child", "Child") }) },
            Array.Empty<ExternalDependencyNode>(),
            new[] { Edge("edge", "tree_parent", "tree_child") });
        var edge = Export(graph).Descendants("mxCell").Single(cell => (string?)cell.Attribute("id") == "edge");

        Assert.Empty(edge.Descendants("mxPoint"));
    }

    [Fact]
    public void Reversed_enumeration_produces_identical_small_exposure_layout()
    {
        var graph = MessyExposureGraph();
        var reversed = new ArchitectureGraph(
            graph.Projects,
            graph.ExternalDependencies,
            graph.Edges.Reverse().ToArray());

        Assert.Equal(GeometrySignature(Export(graph)), GeometrySignature(Export(reversed)));
    }

    private static ArchitectureGraph MessyExposureGraph()
    {
        var nodes = new[]
        {
            Node("tree_root", "DiagramGenerationExposure"),
            Node("tree_path", "DiagramPathGenerationCoordinationService"),
            Node("tree_generation_a", "DiagramGenerationOrchestrationService"),
            Node("tree_generation_b", "DiagramGenerationOrchestrationService"),
            Node("tree_workspace", "WorkspacePathBroker"),
            Node("tree_registry_a", "DiagramRendererRegistry"),
            Node("tree_analysis_a", "DiagramAnalysisProcessingService"),
            Node("tree_rendering_a", "DiagramRenderingProcessingService"),
            Node("tree_analysis_b", "DiagramAnalysisProcessingService"),
            Node("tree_rendering_b", "DiagramRenderingProcessingService"),
            Node("tree_drawio_a", "DrawioDiagramRenderer"),
            Node("tree_roslyn_a", "RoslynBroker"),
            Node("tree_registry_b", "DiagramRendererRegistry"),
            Node("tree_roslyn_b", "RoslynBroker"),
            Node("tree_drawio_b", "DrawioDiagramRenderer"),
            Node("tree_exporter_a", "DeterministicDrawioExporter"),
            Node("tree_analyser_a", "RoslynDependencyAnalyzer"),
            Node("tree_exporter_b", "DeterministicDrawioExporter"),
            Node("tree_analyser_b", "RoslynDependencyAnalyzer")
        };
        var edges = new[]
        {
            Edge("root_path", "tree_root", "tree_path"),
            Edge("root_generation_b", "tree_root", "tree_generation_b"),
            Edge("path_workspace", "tree_path", "tree_workspace"),
            Edge("path_registry", "tree_path", "tree_registry_a"),
            Edge("path_generation_a", "tree_path", "tree_generation_a"),
            Edge("generation_a_analysis", "tree_generation_a", "tree_analysis_a"),
            Edge("generation_a_rendering", "tree_generation_a", "tree_rendering_a"),
            Edge("generation_b_analysis", "tree_generation_b", "tree_analysis_b"),
            Edge("generation_b_rendering", "tree_generation_b", "tree_rendering_b"),
            Edge("registry_a_drawio", "tree_registry_a", "tree_drawio_a"),
            Edge("analysis_a_roslyn", "tree_analysis_a", "tree_roslyn_a"),
            Edge("rendering_a_registry", "tree_rendering_a", "tree_registry_b"),
            Edge("analysis_b_roslyn", "tree_analysis_b", "tree_roslyn_b"),
            Edge("rendering_b_drawio", "tree_rendering_b", "tree_drawio_b"),
            Edge("drawio_a_exporter", "tree_drawio_a", "tree_exporter_a"),
            Edge("roslyn_a_analyser", "tree_roslyn_a", "tree_analyser_a"),
            Edge("drawio_b_exporter", "tree_drawio_b", "tree_exporter_b"),
            Edge("roslyn_b_analyser", "tree_roslyn_b", "tree_analyser_b")
        };

        return new ArchitectureGraph(
            new[] { new ProjectContainer("project_a", "App", nodes) },
            Array.Empty<ExternalDependencyNode>(),
            edges);
    }

    private static TypeNode Node(string id, string name) => new(id, "project_a", name, $"App.{name}", "Class");

    private static DependencyEdge Edge(string id, string source, string target) => new(id, source, target, "internal");

    private static XDocument Export(ArchitectureGraph graph) => XDocument.Parse(ExportText(graph));

    private static string ExportText(ArchitectureGraph graph)
    {
        var settings = DiagramSettings.CreateDefault();
        settings.Layout.HorizontalSpacing = 20;
        settings.Layout.VerticalSpacing = 40;
        settings.Layout.ContainerPadding = 25;
        return new DrawioDiagramRenderer().Render(graph, settings);
    }

    private static IEnumerable<XElement> EdgesFrom(XDocument document, string sourceId) =>
        document.Descendants("mxCell").Where(cell =>
            (string?)cell.Attribute("edge") == "1" &&
            (string?)cell.Attribute("source") == sourceId);

    private static double CenterX(XDocument document, string id)
    {
        var geometry = Cell(document, id).Element("mxGeometry")!;
        return AbsoluteX(document, id) + Number(geometry, "width") / 2;
    }

    private static double Right(XDocument document, string id)
    {
        var geometry = Cell(document, id).Element("mxGeometry")!;
        return AbsoluteX(document, id) + Number(geometry, "width");
    }

    private static string GeometrySignature(XDocument document) => string.Join("\n",
        document.Descendants("mxCell")
            .Where(cell => (string?)cell.Attribute("id") is string id &&
                (id.StartsWith("tree_", StringComparison.Ordinal) || (string?)cell.Attribute("logicalEdgeId") is not null))
            .OrderBy(cell => (string?)cell.Attribute("id"), StringComparer.Ordinal)
            .Select(cell => $"{(string?)cell.Attribute("id")}|{cell.Element("mxGeometry")}"));

    private static double AbsoluteX(XDocument document, string id)
    {
        var cell = Cell(document, id);
        var x = Number(cell.Element("mxGeometry")!, "x");
        var parent = (string?)cell.Attribute("parent");
        return string.IsNullOrWhiteSpace(parent) || parent == "1" ? x : x + AbsoluteX(document, parent);
    }

    private static double AbsoluteY(XDocument document, string id)
    {
        var cell = Cell(document, id);
        var y = Number(cell.Element("mxGeometry")!, "y");
        var parent = (string?)cell.Attribute("parent");
        return string.IsNullOrWhiteSpace(parent) || parent == "1" ? y : y + AbsoluteY(document, parent);
    }

    private static XElement Cell(XDocument document, string id) =>
        document.Descendants("mxCell").Single(cell => (string?)cell.Attribute("id") == id);

    private static IReadOnlyList<(double X, double Y)> Points(XElement edge) =>
        edge.Descendants("mxPoint")
            .Select(point => (Number(point, "x"), Number(point, "y")))
            .ToArray();

    private static IReadOnlyList<(double X, double Y)> PointsIncludingTerminals(XDocument document, XElement edge)
    {
        var source = Cell(document, (string)edge.Attribute("source")!);
        var target = Cell(document, (string)edge.Attribute("target")!);
        var sourceGeometry = source.Element("mxGeometry")!;
        var targetGeometry = target.Element("mxGeometry")!;
        var style = ((string?)edge.Attribute("style") ?? string.Empty)
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('='))
            .Where(parts => parts.Length == 2)
            .ToDictionary(parts => parts[0], parts => parts[1], StringComparer.Ordinal);
        double Ratio(string name, double fallback) => style.TryGetValue(name, out var value)
            ? double.Parse(value, CultureInfo.InvariantCulture)
            : fallback;
        var sourcePoint = (
            AbsoluteX(document, (string)source.Attribute("id")!) + Number(sourceGeometry, "width") * Ratio("exitX", 0.5),
            AbsoluteY(document, (string)source.Attribute("id")!) + Number(sourceGeometry, "height") * Ratio("exitY", 1));
        var targetPoint = (
            AbsoluteX(document, (string)target.Attribute("id")!) + Number(targetGeometry, "width") * Ratio("entryX", 0.5),
            AbsoluteY(document, (string)target.Attribute("id")!) + Number(targetGeometry, "height") * Ratio("entryY", 0));
        return new[] { sourcePoint }.Concat(Points(edge)).Append(targetPoint).ToArray();
    }

    private static bool ContainsImmediateReversal(IReadOnlyList<(double X, double Y)> points)
    {
        for (var index = 0; index + 2 < points.Count; index++)
        {
            var a = points[index];
            var b = points[index + 1];
            var c = points[index + 2];
            if (a.X == b.X && b.X == c.X && (b.Y < Math.Min(a.Y, c.Y) || b.Y > Math.Max(a.Y, c.Y))) return true;
            if (a.Y == b.Y && b.Y == c.Y && (b.X < Math.Min(a.X, c.X) || b.X > Math.Max(a.X, c.X))) return true;
        }
        return false;
    }

    private static double Number(XElement element, string attribute) =>
        double.Parse((string)element.Attribute(attribute)!, CultureInfo.InvariantCulture);
}
