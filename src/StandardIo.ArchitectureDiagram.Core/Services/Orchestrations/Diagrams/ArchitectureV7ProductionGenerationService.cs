using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using StandardIo.ArchitectureDiagram.Core.Models;
using StandardIo.ArchitectureDiagram.Core.Models.Architectures;
using StandardIo.ArchitectureDiagram.Core.Models.ArchitectureV7;
using StandardIo.ArchitectureDiagram.Core.Models.Drawios;
using StandardIo.ArchitectureDiagram.Core.Models.Generation;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.Analyses;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.ArchitectureV7;
using ArchitectureDiagramModel = StandardIo.ArchitectureDiagram.Core.Models.Architectures.ArchitectureDiagram;

namespace StandardIo.ArchitectureDiagram.Core.Services.Orchestrations.Diagrams;

public sealed class ArchitectureV7ProductionGenerationService : IArchitectureGenerationService
{
    private readonly IArchitectureAnalyser analyser;
    private readonly IDrawioDocumentComposer composer;

    public ArchitectureV7ProductionGenerationService(IArchitectureAnalyser analyser, IDrawioDocumentComposer composer)
    { this.analyser = analyser ?? throw new ArgumentNullException(nameof(analyser)); this.composer = composer ?? throw new ArgumentNullException(nameof(composer)); }

    public async Task<TypedArchitectureGenerationResult> GenerateAsync(IEnumerable<Project> selectedProjects, ArchitectureGenerationJob job,
        ArchitectureRenderingMode mode = ArchitectureRenderingMode.Production, int serializationRepeatCount = 0, CancellationToken cancellationToken = default)
    {
        var diagram = await analyser.AnalyseAsync(selectedProjects, job.Analysis, cancellationToken).ConfigureAwait(false);
        return await GenerateAsync(diagram, job, mode, serializationRepeatCount, cancellationToken).ConfigureAwait(false);
    }

    public Task<TypedArchitectureGenerationResult> GenerateAsync(ArchitectureDiagramModel diagram, ArchitectureGenerationJob job,
        ArchitectureRenderingMode mode = ArchitectureRenderingMode.Production, int serializationRepeatCount = 0, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var pre = Configuration(job.Rendering.Layout);
        var projection = new ArchitectureV7PhysicalProjectionStage().Project(diagram, new ArchitectureV7ProjectionPolicy(
            job.Rendering.NodeDuplication.AllowDuplicateNodes ? ArchitectureV7ProjectionMode.ConfiguredDuplicateBranches : ArchitectureV7ProjectionMode.Canonical,
            (job.Rendering.NodeDuplication.DuplicationExceptionPatterns ?? new List<string>()).Concat(job.Rendering.Layout.DuplicateHighNoiseNodePatterns ?? new List<string>()).Distinct(StringComparer.Ordinal).ToArray()));
        var ownership = new ArchitectureV7PositionalOwnershipStage().Resolve(projection);
        var sizing = new ArchitectureV7PreRoutingNodeSpanSizer().Size(ownership, pre);
        var inspection = new ArchitectureV7ReservedRoleConstraintInspector().Inspect(ownership, pre);
        var reservation = new ArchitectureV7ReservationReconciliationStage().Reconcile(inspection);
        var trees = new ArchitectureV7RecursiveTreeGridStage().Build(sizing, reservation.Table);
        var placement = new ArchitectureV7ProjectCompositionStage().Compose(trees);
        var routes = new ArchitectureV7LogicalRelationshipRoutingStage().Route(placement, projection);
        var allocation = new ArchitectureV7CollectivePostRoutingAllocationStage().Allocate(placement, routes,
            new ArchitectureV7AllocationConfiguration(job.Rendering.Layout.ParallelLaneSpacing, job.Rendering.Layout.EdgePortSpacing, job.Rendering.Layout.LinkNodeWidthPadding));
        var sceneConfiguration = new ArchitectureV7PhysicalSceneConfiguration(job.Rendering.Layout.NodeWidth / 3d, job.Rendering.Layout.NodeHeight + job.Rendering.Layout.VerticalSpacing,
            job.Rendering.Layout.NodeWidth, job.Rendering.Layout.NodeHeight, 8, job.Rendering.Layout.LinkNodeWidthPadding, job.Rendering.Layout.LinkPadding,
            Math.Max(1, job.Rendering.Layout.HorizontalSpacing / 2d), job.Rendering.Layout.ParallelLaneSpacing, job.Rendering.Layout.EdgePortSpacing, job.Rendering.Layout.LinkNodeWidthPadding);
        var scene = new ArchitectureV7PhysicalSceneCompilationStage().Compile(placement, routes, allocation, sceneConfiguration);
        var acceptance = new ArchitectureV7FinalAcceptanceValidationStage().Validate(projection, ownership, sizing, reservation, placement, routes, allocation, scene, sceneConfiguration);
        var strict = mode == ArchitectureRenderingMode.StrictValidation;
        var findings = acceptance.Findings.Select(finding => new ValidationFinding(finding.Code, finding.SubjectId ?? finding.Stage, finding.SubjectId, null, 1, finding.Message, true)).ToArray();
        DrawioPage page;
        if (strict && !acceptance.IsStrictEligible)
            page = RejectedPage();
        else
            page = new ArchitectureV7MechanicalDrawioRenderer().Render(diagram, projection, placement, scene, job.Rendering);
        var rendererFindings = ValidateRendererFidelity(page, projection, scene);
        page = page with { Diagnostics = page.Diagnostics.Concat(rendererFindings).ToArray() };
        if (!string.IsNullOrWhiteSpace(job.PageNameHint)) page = page with { SuggestedName = job.PageNameHint!.Trim() };
        var rendererFidelity = new
        {
            Findings = rendererFindings,
            IsValid = rendererFindings.Length == 0,
            NodeCount = scene.Nodes.Count,
            EmittedNodeCount = page.GraphModel.Descendants("mxCell").Count(x => (string?)x.Attribute("vertex") == "1"),
            RouteCount = scene.Routes.Count,
            EmittedRouteCount = page.GraphModel.Descendants("mxCell").Count(x => (string?)x.Attribute("edge") == "1")
        };
        var acceptanceSummary = new
        {
            acceptance.Counts,
            acceptance.HardFailureCount,
            acceptance.IsStrictEligible,
            acceptance.IsNormalEligible,
            acceptance.ProjectionFingerprint,
            acceptance.OwnershipFingerprint,
            acceptance.SizingFingerprint,
            acceptance.ReservationFingerprint,
            acceptance.PlacementFingerprint,
            acceptance.RouteFingerprint,
            acceptance.AllocationFingerprint,
            acceptance.SceneFingerprint,
            FindingCodes = acceptance.Findings.Select(x => x.Code).ToArray()
        };
        var reportJson = JsonSerializer.Serialize(new
        {
            pipeline = "V7",
            acceptance = acceptanceSummary,
            rendererFidelity,
            stages = new
            {
                projection = new { PhysicalNodeCount = projection.PhysicalNodes.Count, PhysicalLinkCount = projection.PhysicalLinks.Count, Fingerprint = projection.FreezeFingerprint },
                ownership = new { DecisionCount = ownership.Decisions.Count, Fingerprint = ownership.FreezeFingerprint },
                sizing = new { RequirementCount = sizing.Requirements.Count, Fingerprint = sizing.FreezeFingerprint },
                reservation = new { ReservationCount = reservation.Table.Reservations.Count, Fingerprint = reservation.Table.Fingerprint },
                placement = new { ProjectCount = placement.Projects.Count, NodeCount = placement.Nodes.Count, Fingerprint = placement.PlacementFingerprint },
                routing = new { RouteCount = routes.Routes.Count, CompleteRouteCount = routes.Routes.Count(x => x.IsComplete), Fingerprint = routes.RouteFingerprint },
                allocation = new { AssignmentCount = allocation.RunAssignments.Count, Fingerprint = allocation.AllocationFingerprint },
                scene = new { NodeCount = scene.Nodes.Count, RouteCount = scene.Routes.Count, DiagnosticCount = scene.Diagnostics.Count, scene.PhysicalSceneFingerprint }
            }
        }, new JsonSerializerOptions { WriteIndented = true });
        var manifest = new ArchitectureGenerationManifest(diagram.Projects.Count, projection.PhysicalNodes.Count, diagram.Links.Count, routes.Routes.Count,
            acceptance.Findings.Count, acceptance.Findings.Count, page.StablePageKey)
        {
            ProjectedRenderNodeCount = projection.PhysicalNodes.Count, ProjectedRenderLinkCount = projection.PhysicalLinks.Count,
            DuplicatedInstanceCount = projection.PhysicalNodes.Count(x => x.ProjectionMode == ArchitectureV7ProjectionMode.ConfiguredDuplicateBranches),
            CanonicalSharedNodeCount = projection.PhysicalNodes.Count(x => x.ProjectionMode == ArchitectureV7ProjectionMode.Canonical),
            ExceptionAuthorisedDuplicateCount = projection.PhysicalNodes.Count(x => x.DuplicationProvenance is not null)
        };
        var eligibility = new ArchitectureEligibilityResult(!strict || acceptance.IsStrictEligible, acceptance.Findings.Select(x => x.Code + ": " + x.Message).ToArray());
        return Task.FromResult(new TypedArchitectureGenerationResult(diagram, page, findings, manifest, eligibility,
            () => new DrawioDiagnosticExportResult(page.GraphModel.ToString(), reportJson,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["final-v7-acceptance-report.json"] = JsonSerializer.Serialize(acceptanceSummary, new JsonSerializerOptions { WriteIndented = true }),
                    ["renderer-fidelity-report.json"] = JsonSerializer.Serialize(rendererFidelity, new JsonSerializerOptions { WriteIndented = true })
                }, acceptance.HardFailureCount + rendererFindings.Length, routes.Routes.Count(x => !x.IsComplete)),
            serializationRepeatCount > 0 ? new SerializationRepeatResult(serializationRepeatCount, true, Array.Empty<string>()) : null));
    }

    private static DiagramDiagnostic[] ValidateRendererFidelity(DrawioPage page, ArchitectureV7PhysicalProjectionResult projection, ArchitectureV7PhysicalSceneFreeze scene)
    {
        var cells = page.GraphModel.Descendants("mxCell").ToArray();
        var emittedNodes = cells.Where(x => (string?)x.Attribute("vertex") == "1").ToArray();
        var emittedEdges = cells.Where(x => (string?)x.Attribute("edge") == "1").ToArray();
        var findings = new List<DiagramDiagnostic>();
        if (emittedNodes.Length != scene.Nodes.Count) findings.Add(new DiagramDiagnostic("V7RendererNodeCount", $"Renderer emitted {emittedNodes.Length} nodes; scene contains {scene.Nodes.Count}."));
        if (emittedEdges.Length != scene.Routes.Count) findings.Add(new DiagramDiagnostic("V7RendererRouteCount", $"Renderer emitted {emittedEdges.Length} edges; scene contains {scene.Routes.Count}."));
        foreach (var node in scene.Nodes)
        {
            var cell = cells.FirstOrDefault(x => (string?)x.Attribute("id") == ArchitectureV7MechanicalDrawioRenderer.IdFor("node", node.PhysicalNodeId));
            var geometry = cell?.Element("mxGeometry");
            if (geometry is null || !Equal(node.Bounds.Left, geometry.Attribute("x")) || !Equal(node.Bounds.Top, geometry.Attribute("y")) || !Equal(node.Bounds.Right - node.Bounds.Left, geometry.Attribute("width")) || !Equal(node.Bounds.Bottom - node.Bounds.Top, geometry.Attribute("height")))
                findings.Add(new DiagramDiagnostic("V7RendererNodeBounds", $"Renderer node geometry does not match frozen bounds for {node.PhysicalNodeId}.", node.PhysicalNodeId));
        }
        foreach (var route in scene.Routes)
        {
            var cell = cells.FirstOrDefault(x => (string?)x.Attribute("physicalLinkId") == route.PhysicalLinkId);
            var points = cell?.Element("mxGeometry")?.Element("Array")?.Elements("mxPoint").ToArray() ?? Array.Empty<XElement>();
            var expected = route.Points.Skip(1).Take(Math.Max(0, route.Points.Count - 2)).ToArray();
            if (cell is null || points.Length != expected.Length || points.Zip(expected, (actual, wanted) => Equal(wanted.X, actual.Attribute("x")) && Equal(wanted.Y, actual.Attribute("y"))).Any(x => !x))
                findings.Add(new DiagramDiagnostic("V7RendererRouteGeometry", $"Renderer route geometry does not match frozen route {route.PhysicalLinkId}.", route.PhysicalLinkId));
        }
        return findings.ToArray();
    }

    private static bool Equal(double expected, XAttribute? actual) => actual is not null && double.TryParse(actual.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && Math.Abs(expected - value) < 0.0001;

    private static ArchitectureV7PrePlacementConfiguration Configuration(LayoutSettings layout) => new(3, layout.NodeWidth, 8, layout.LinkNodeWidthPadding,
        Math.Max(1, layout.EdgePortSpacing), Math.Max(0, layout.LinkNodeWidthPadding), (layout.ReservedLayerTypePatterns ?? new List<string>()).Select((pattern, index) => new ArchitectureV7ReservedRoleRule(pattern, pattern, index)).ToArray());
    private static DrawioPage RejectedPage() => new("Architecture (rejected)", "architecture-rejected", new XElement("mxGraphModel", new XElement("root", new XElement("mxCell", new XAttribute("id", "0")), new XElement("mxCell", new XAttribute("id", "1"), new XAttribute("parent", "0")))), new[] { new DiagramDiagnostic("V7StrictRejected", "V7 acceptance failed; Draw.io renderer was not invoked.") });
}

internal sealed class ArchitectureV7MechanicalDrawioRenderer
{
    public DrawioPage Render(ArchitectureDiagramModel diagram, ArchitectureV7PhysicalProjectionResult projection, ArchitectureV7PlacementFreeze placement,
        ArchitectureV7PhysicalSceneFreeze scene, ArchitectureRenderSettings settings)
    {
        var root = new XElement("root", new XElement("mxCell", new XAttribute("id", "0")), new XElement("mxCell", new XAttribute("id", "1"), new XAttribute("parent", "0")));
        var nodes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var project in placement.Projects.OrderBy(x => x.ProjectId, StringComparer.Ordinal))
        {
            var transform = project.Transform; var id = Id("project", project.ProjectId);
            root.Add(Vertex(id, project.ProjectId, "shape=swimlane;html=1;whiteSpace=wrap;fillColor=#323a40;strokeColor=#263238;fontColor=#ffffff;", "1", transform.InteriorOriginColumn, transform.InteriorOriginRow, transform.Width, transform.Height));
        }
        foreach (var node in scene.Nodes.OrderBy(x => x.PhysicalNodeId, StringComparer.Ordinal))
        {
            var source = projection.PhysicalNodes.FirstOrDefault(x => x.PhysicalNodeId == node.PhysicalNodeId); if (source is null) continue;
            var id = Id("node", node.PhysicalNodeId); nodes[node.PhysicalNodeId] = id;
            var parent = source.ProjectId is not null && placement.Projects.Any(x => x.ProjectId == source.ProjectId) ? Id("project", source.ProjectId) : "1";
            root.Add(Vertex(id, source.Name, Style(source, settings), parent, node.Bounds.Left, node.Bounds.Top, node.Bounds.Right - node.Bounds.Left, node.Bounds.Bottom - node.Bounds.Top));
        }
        foreach (var link in projection.PhysicalLinks.OrderBy(x => x.PhysicalLinkId, StringComparer.Ordinal))
        {
            var route = scene.Routes.FirstOrDefault(x => x.PhysicalLinkId == link.PhysicalLinkId); if (route is null || !nodes.ContainsKey(link.SourcePhysicalNodeId) || !nodes.ContainsKey(link.DestinationPhysicalNodeId)) continue;
            var points = route.Points.Skip(1).Take(Math.Max(0, route.Points.Count - 2)).Select(point => new XElement("mxPoint", new XAttribute("x", point.X.ToString(CultureInfo.InvariantCulture)), new XAttribute("y", point.Y.ToString(CultureInfo.InvariantCulture))));
            root.Add(new XElement("mxCell", new XAttribute("id", Id("edge", link.PhysicalLinkId)), new XAttribute("parent", "1"), new XAttribute("edge", "1"), new XAttribute("source", nodes[link.SourcePhysicalNodeId]), new XAttribute("target", nodes[link.DestinationPhysicalNodeId]), new XAttribute("physicalLinkId", link.PhysicalLinkId), new XAttribute("semanticLinkId", link.SemanticLinkId), new XAttribute("style", "edgeStyle=none;orthogonal=0;curved=0;rounded=0;labelPosition=none;"), new XElement("mxGeometry", new XAttribute("relative", "1"), new XAttribute("as", "geometry"), new XElement("Array", new XAttribute("as", "points"), points))));
        }
        var graph = new XElement("mxGraphModel", new XAttribute("grid", "0"), new XAttribute("page", "0"), new XAttribute("background", settings.Canvas.BackgroundColor), root);
        return new DrawioPage("Architecture", "architecture", graph, Array.Empty<DiagramDiagnostic>());
    }
    private static XElement Vertex(string id, string value, string style, string parent, double x, double y, double width, double height) => new("mxCell", new XAttribute("id", id), new XAttribute("value", value), new XAttribute("style", style), new XAttribute("vertex", "1"), new XAttribute("parent", parent), new XElement("mxGeometry", new XAttribute("x", x.ToString(CultureInfo.InvariantCulture)), new XAttribute("y", y.ToString(CultureInfo.InvariantCulture)), new XAttribute("width", width.ToString(CultureInfo.InvariantCulture)), new XAttribute("height", height.ToString(CultureInfo.InvariantCulture)), new XAttribute("as", "geometry")));
    private static string Style(ArchitectureV7PhysicalNode node, ArchitectureRenderSettings settings) => node.IsExternal ? "shape=rhombus;html=1;fillColor=#f36c21;strokeColor=#a43b08;fontColor=#111111;" : "rounded=1;html=1;fillColor=#dae8fc;strokeColor=#6c8ebf;fontColor=#111111;";
    internal static string IdFor(string kind, string value) { using var sha = SHA256.Create(); return "v7_" + kind + "_" + string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(value)).Take(8).Select(x => x.ToString("x2", CultureInfo.InvariantCulture))); }
    private static string Id(string kind, string value) => IdFor(kind, value);
}
