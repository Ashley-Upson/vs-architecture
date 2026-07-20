using System.Xml.Linq;
using StandardIo.ArchitectureDiagram.Core.Models;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.Renderers;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core.Tests;

public sealed class NodeDuplicationExporterTests
{
    [Fact]
    public void Default_and_explicitly_enabled_output_are_byte_identical()
    {
        var defaults = Settings();
        var enabled = Settings();
        enabled.NodeDuplication.AllowDuplicateNodes = true;

        Assert.Equal(Render(Model(), defaults), Render(Model(), enabled));
    }

    [Fact]
    public void Disabled_output_contains_one_shared_vertex_and_two_logical_dependencies()
    {
        var settings = Settings(allowDuplicates: false);
        var document = XDocument.Parse(Render(Model(), settings));

        Assert.Single(Vertices(document), cell => Value(cell).StartsWith("SharedService", StringComparison.Ordinal));
        Assert.Equal(2, LogicalEdges(document).Count(edge => (string?)edge.Attribute("semanticTargetId") == "shared"));
    }

    [Fact]
    public void Disabled_output_is_byte_identical_when_edge_and_project_enumeration_are_reversed()
    {
        var settings = Settings(allowDuplicates: false);
        var model = Model(twoProjects: true);
        var reversed = model with
        {
            Projects = model.Projects.Reverse().ToArray(),
            Edges = model.Edges.Reverse().ToArray()
        };

        var first = XDocument.Parse(Render(model, settings));
        var second = XDocument.Parse(Render(reversed, settings));

        Assert.Equal(GeometrySignature(first), GeometrySignature(second));
    }

    [Fact]
    public void Adding_a_later_parent_does_not_move_the_first_canonical_placement()
    {
        var settings = Settings(allowDuplicates: false);
        var model = Model();
        var firstOnly = model with { Edges = model.Edges.Take(1).ToArray() };

        var firstRect = AbsoluteVertexRect(XDocument.Parse(Render(firstOnly, settings)), "SharedService");
        var sharedRect = AbsoluteVertexRect(XDocument.Parse(Render(model, settings)), "SharedService");

        Assert.Equal(firstRect, sharedRect);
    }

    [Fact]
    public void Any_matching_exception_retains_branch_local_vertices()
    {
        var settings = Settings(allowDuplicates: false);
        settings.NodeDuplication.DuplicationExceptionPatterns.Add("does-not-match");
        settings.NodeDuplication.DuplicationExceptionPatterns.Add("^App\\.SharedService$");
        var document = XDocument.Parse(Render(Model(), settings));

        Assert.Equal(2, Vertices(document).Count(cell => Value(cell).StartsWith("SharedService", StringComparison.Ordinal)));
    }

    [Fact]
    public void Duplicated_exposure_graph_skips_repair_when_validation_has_only_non_blocking_advisories()
    {
        var settings = Settings();
        var graph = RenderGraph.From(Model(), settings);

        var layout = RenderLayout.Build(graph, settings);

        Assert.Equal("SkippedDuplicatedModeNonBlockingAdvisories", layout.RepairRunReason);
        Assert.Empty(layout.RepairAttempts);
        Assert.Equal(0, layout.RepairWorkUsed);
    }

    private static DiagramSettings Settings(bool allowDuplicates = true)
    {
        var settings = DiagramSettings.CreateDefault();
        settings.Layout.ExposureTreeLayoutThreshold = 1;
        settings.NodeDuplication.AllowDuplicateNodes = allowDuplicates;
        return settings;
    }

    private static DiagramModel Model(bool twoProjects = false)
    {
        var projectA = new ProjectContainer("project_a", "Project A", new[]
        {
            Node("parent_a", "project_a", "ParentAController"),
            Node("shared", "project_a", "SharedService")
        });
        var projects = twoProjects
            ? new[]
            {
                projectA,
                new ProjectContainer("project_b", "Project B", new[]
                {
                    Node("parent_b", "project_b", "ParentBController")
                })
            }
            : new[]
            {
                new ProjectContainer("project_a", "Project A", projectA.Types.Concat(new[]
                {
                    Node("parent_b", "project_a", "ParentBController")
                }).ToArray())
            };
        return new DiagramModel(
            projects,
            Array.Empty<ExternalDependencyNode>(),
            new[]
            {
                new DependencyEdge("edge_a", "parent_a", "shared", "internal"),
                new DependencyEdge("edge_b", "parent_b", "shared", "internal")
            },
            RootMetadata("parent_a", "parent_b"));
    }

    private static DiagramMetadata RootMetadata(params string[] ids) => new(
        SemanticSelection: new SemanticSelectionReport(
            "ConfiguredRootOutgoingReachability",
            new[] { new RootDiscoveryPatternDefinition(0, 1, "fixture") },
            ids.Select(id => new SemanticRootMatch(id, id, 0, 1, "fixture")).ToArray(),
            Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<int>()));

    private static TypeNode Node(string id, string projectId, string name) =>
        new(id, projectId, name, $"App.{name}", "Class");

    private static string Render(DiagramModel model, DiagramSettings settings) =>
        new DrawioDiagramRenderer().Render(model, settings);

    private static IEnumerable<XElement> Cells(XDocument document) =>
        document.Descendants("mxGraphModel").First().Descendants("mxCell");

    private static XElement[] Vertices(XDocument document) => Cells(document)
        .Where(cell => (string?)cell.Attribute("vertex") == "1" && (string?)cell.Attribute("boundaryAnchor") != "1")
        .ToArray();

    private static XElement[] LogicalEdges(XDocument document) => Cells(document)
        .Where(cell => (string?)cell.Attribute("edge") == "1" && (int?)cell.Attribute("segmentIndex") == 0)
        .ToArray();

    private static string Value(XElement cell) => (string?)cell.Attribute("value") ?? string.Empty;

    private static string[] GeometrySignature(XDocument document) => Cells(document)
        .Where(cell => cell.Element("mxGeometry") is not null)
        .OrderBy(cell => (string?)cell.Attribute("id"), StringComparer.Ordinal)
        .Select(cell => string.Join("|",
            (string?)cell.Attribute("id") ?? string.Empty,
            (string?)cell.Attribute("parent") ?? string.Empty,
            (string?)cell.Attribute("source") ?? string.Empty,
            (string?)cell.Attribute("target") ?? string.Empty,
            cell.Element("mxGeometry")!.ToString(SaveOptions.DisableFormatting)))
        .ToArray();

    private static (int X, int Y, int Width, int Height) AbsoluteVertexRect(XDocument document, string valuePrefix)
    {
        var cells = Cells(document).ToDictionary(cell => (string)cell.Attribute("id")!, StringComparer.Ordinal);
        var cell = Vertices(document).Single(vertex => Value(vertex).StartsWith(valuePrefix, StringComparison.Ordinal));
        var geometry = cell.Element("mxGeometry")!;
        var x = (int?)geometry.Attribute("x") ?? 0;
        var y = (int?)geometry.Attribute("y") ?? 0;
        var parentId = (string?)cell.Attribute("parent");
        if (parentId is not null && cells.TryGetValue(parentId, out var parent))
        {
            var parentGeometry = parent.Element("mxGeometry");
            x += (int?)parentGeometry?.Attribute("x") ?? 0;
            y += (int?)parentGeometry?.Attribute("y") ?? 0;
        }

        return (x, y, (int?)geometry.Attribute("width") ?? 0, (int?)geometry.Attribute("height") ?? 0);
    }
}
