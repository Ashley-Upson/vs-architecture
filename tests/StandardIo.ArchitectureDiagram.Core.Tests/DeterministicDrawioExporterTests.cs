using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
using StandardIo.ArchitectureDiagram.Core.Models;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.Renderers;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core.Tests;

public sealed class DeterministicDrawioExporterTests
{
    [Fact]
    public void ValidateStrict_is_explicit_and_rejects_enforced_findings()
    {
        var finding = new ValidationFinding(
            "NodeCollision",
            "edge",
            null,
            "node",
            1,
            "edge crosses node",
            Array.Empty<ValidationPoint>(),
            Array.Empty<ValidationSegment>(),
            null,
            null,
            null,
            IsStrictlyEnforced: true);
        var result = new DrawioGenerationResult(
            "<mxfile />",
            Array.Empty<ValidationFinding>(),
            new[] { finding },
            Array.Empty<RouteRepairAttempt>(),
            Array.Empty<GeneratedRoute>(),
            serializationSucceeded: true,
            strictValidationPassed: false,
            new DrawioDiagnosticExportResult("", "{}", new Dictionary<string, string>(), 1, 1));

        Assert.Throws<InvalidOperationException>(() => new DeterministicDrawioExporter().ValidateStrict(result));
        Assert.Equal(0, result.DiagnosticMaterializationCount);
    }

    [Fact]
    public void GenerateResult_exposes_document_routes_and_validation_without_console_parsing()
    {
        var diagram = new DiagramModel(
            new[]
            {
                new ProjectContainer("project", "Project", new[]
                {
                    Node("source", "project", "Source"),
                    Node("target", "project", "Target")
                })
            },
            Array.Empty<ExternalDependencyNode>(),
            new[] { new DependencyEdge("edge", "source", "target", "internal") });

        var exporter = new DeterministicDrawioExporter();
        var result = exporter.GenerateResult(diagram, DiagramSettings.CreateDefault());

        Assert.True(result.SerializationSucceeded);
        Assert.True(result.StrictValidationPassed);
        Assert.Empty(result.RepairAttempts);
        var route = Assert.Single(result.Routes);
        Assert.Equal("edge", route.LogicalRouteId);
        Assert.True(route.Points.Count >= 2);
        Assert.Equal("mxfile", XDocument.Parse(result.Document).Root!.Name.LocalName);
        Assert.Equal(1, result.PreparationCount);
        Assert.Equal(0, result.DiagnosticMaterializationCount);
        var diagnostics = exporter.ExportDiagnostic(result);
        Assert.Equal(1, result.DiagnosticMaterializationCount);
        Assert.Same(diagnostics, exporter.ExportDiagnostic(result));
        Assert.Equal(1, result.DiagnosticMaterializationCount);
        Assert.False(string.IsNullOrWhiteSpace(result.Diagnostics.ReportJson));
        Assert.Contains(result.StageTimings, timing => timing.Stage == "render graph construction");
        Assert.Contains(result.StageTimings, timing => timing.Stage == "normalization");
        Assert.Contains(result.StageTimings, timing => timing.Stage == "ownership");
        Assert.Contains(result.StageTimings, timing => timing.Stage == "serialization");
        using var report = JsonDocument.Parse(result.Diagnostics.ReportJson);
        var summary = report.RootElement.GetProperty("summary");
        Assert.True(summary.GetProperty("diagnosticReuse").GetBoolean());
        Assert.True(summary.TryGetProperty("routeRevisionsCreated", out _));
        Assert.True(summary.TryGetProperty("routePairsRevalidated", out _));
        Assert.True(report.RootElement.GetProperty("stageTimings").GetArrayLength() > 0);
    }

    [Fact]
    public void Render_emits_regional_optimisation_diagnostics_for_exposure_tree_edges()
    {
        var settings = DiagramSettings.CreateDefault();
        settings.Layout.ExposureTreeLayoutThreshold = 2;
        var document = Render(new DiagramModel(
            new[]
            {
                new ProjectContainer("project_api", "Api", new[]
                {
                    Node("type_controller", "project_api", "ApiController"),
                    Node("type_service", "project_api", "Service"),
                    Node("type_repository", "project_api", "Repository")
                })
            },
            Array.Empty<ExternalDependencyNode>(),
            new[]
            {
                new DependencyEdge("edge_controller_service", "type_controller", "type_service", "internal"),
                new DependencyEdge("edge_controller_repository", "type_controller", "type_repository", "internal"),
                new DependencyEdge("edge_service_repository", "type_service", "type_repository", "internal")
            }),
            settings);

        var edges = document.Descendants("mxCell").Where(cell => (string?)cell.Attribute("edge") == "1").ToArray();

        Assert.NotEmpty(edges);
        Assert.All(edges, edge =>
        {
            Assert.Equal("regional", (string?)edge.Attribute("optimisationMode"));
            Assert.NotNull(edge.Attribute("regionDecision"));
            Assert.NotNull(edge.Attribute("regionFallbackReason"));
            Assert.NotNull(edge.Attribute("pathInitialSignature"));
            Assert.NotNull(edge.Attribute("pathFinalSignature"));
        });
        Assert.Contains(edges, edge => edge.Attribute("fanoutGroups") is not null);
    }

    [Fact]
    public void Render_emits_global_path_selection_diagnostics_on_physical_segments()
    {
        var document = Render(new DiagramModel(
            new[]
            {
                new ProjectContainer("project_api", "Api", new[]
                {
                    Node("type_source", "project_api", "Source"),
                    Node("type_left", "project_api", "Left"),
                    Node("type_right", "project_api", "Right")
                })
            },
            Array.Empty<ExternalDependencyNode>(),
            new[]
            {
                new DependencyEdge("edge_source_left", "type_source", "type_left", "internal"),
                new DependencyEdge("edge_source_right", "type_source", "type_right", "internal")
            }));

        var edges = document.Descendants("mxCell")
            .Where(cell => (string?)cell.Attribute("edge") == "1")
            .ToArray();

        Assert.NotEmpty(edges);
        Assert.All(edges, edge =>
        {
            Assert.False(string.IsNullOrWhiteSpace((string?)edge.Attribute("pathInitialSignature")));
            Assert.False(string.IsNullOrWhiteSpace((string?)edge.Attribute("pathFinalSignature")));
            Assert.False(string.IsNullOrWhiteSpace((string?)edge.Attribute("pathDecision")));
            var localCost = (string?)edge.Attribute("pathLocalCost");
            Assert.Contains("length=", localCost);
            Assert.Contains(";bends=", localCost);
            Assert.Contains(";envelopeExpansion=", localCost);
            Assert.False(string.IsNullOrWhiteSpace((string?)edge.Attribute("pathRejectedAlternatives")));
        });
    }

    [Fact]
    public void Render_aligns_nodes_by_dependency_depth()
    {
        var document = Render(new DiagramModel(
            new[]
            {
                new ProjectContainer("project_api", "Api", new[]
                {
                    Node("type_controller", "project_api", "Controller"),
                    Node("type_job", "project_api", "Job"),
                    Node("type_service", "project_api", "Service")
                })
            },
            Array.Empty<ExternalDependencyNode>(),
            new[]
            {
                new DependencyEdge("edge_controller_service", "type_controller", "type_service", "internal"),
                new DependencyEdge("edge_job_service", "type_job", "type_service", "internal")
            }));

        Assert.Equal(AbsoluteY(document, "type_controller"), AbsoluteY(document, "type_job"));
        Assert.True(AbsoluteY(document, "type_service") > AbsoluteY(document, "type_controller"));
    }

    [Fact]
    public void Render_layers_nodes_by_distance_from_exit_layer()
    {
        var document = Render(new DiagramModel(
            new[]
            {
                new ProjectContainer("project_api", "Api", new[]
                {
                    Node("type_controller", "project_api", "Controller"),
                    Node("type_processing", "project_api", "Processing"),
                    Node("type_service", "project_api", "Service"),
                    Node("type_job", "project_api", "Job")
                })
            },
            Array.Empty<ExternalDependencyNode>(),
            new[]
            {
                new DependencyEdge("edge_controller_processing", "type_controller", "type_processing", "internal"),
                new DependencyEdge("edge_processing_service", "type_processing", "type_service", "internal"),
                new DependencyEdge("edge_job_service", "type_job", "type_service", "internal")
            }));

        Assert.True(AbsoluteY(document, "type_controller") < AbsoluteY(document, "type_processing"));
        Assert.True(AbsoluteY(document, "type_service") > AbsoluteY(document, "type_processing"));
    }

    [Fact]
    public void Render_only_forces_topmost_depth_to_same_horizontal_layer()
    {
        var document = Render(new DiagramModel(
            new[]
            {
                new ProjectContainer("project_api", "Api", new[]
                {
                    Node("type_controller", "project_api", "Controller"),
                    Node("type_job", "project_api", "Job"),
                    Node("type_busy", "project_api", "BusyProcessing"),
                    Node("type_quiet", "project_api", "QuietProcessing"),
                    Node("type_leaf_a", "project_api", "LeafA"),
                    Node("type_leaf_b", "project_api", "LeafB"),
                    Node("type_leaf_c", "project_api", "LeafC"),
                    Node("type_leaf_d", "project_api", "LeafD")
                })
            },
            Array.Empty<ExternalDependencyNode>(),
            new[]
            {
                new DependencyEdge("edge_controller_busy", "type_controller", "type_busy", "internal"),
                new DependencyEdge("edge_job_quiet", "type_job", "type_quiet", "internal"),
                new DependencyEdge("edge_busy_leaf_a", "type_busy", "type_leaf_a", "internal"),
                new DependencyEdge("edge_busy_leaf_b", "type_busy", "type_leaf_b", "internal"),
                new DependencyEdge("edge_busy_leaf_c", "type_busy", "type_leaf_c", "internal"),
                new DependencyEdge("edge_quiet_leaf_d", "type_quiet", "type_leaf_d", "internal")
            }));

        Assert.Equal(AbsoluteY(document, "type_controller"), AbsoluteY(document, "type_job"));
        Assert.NotEqual(AbsoluteY(document, "type_busy"), AbsoluteY(document, "type_quiet"));
    }

    [Fact]
    public void Render_aligns_default_higher_order_service_baseline()
    {
        var document = Render(new DiagramModel(
            new[]
            {
                new ProjectContainer("project_api", "Api", new[]
                {
                    Node("type_aggregation", "project_api", "ContentAggregationService"),
                    Node("type_coordination", "project_api", "ContentCoordinationService"),
                    Node("type_template_orchestration", "project_api", "TemplateRenderOrchestrationService"),
                    Node("type_component_orchestration", "project_api", "ComponentRenderOrchestrationService"),
                    Node("type_processing", "project_api", "TemplateRenderProcessingService"),
                    Node("type_foundation", "project_api", "TemplateRenderFoundationService")
                })
            },
            Array.Empty<ExternalDependencyNode>(),
            new[]
            {
                new DependencyEdge("edge_aggregation_template", "type_aggregation", "type_template_orchestration", "internal"),
                new DependencyEdge("edge_coordination_component", "type_coordination", "type_component_orchestration", "internal"),
                new DependencyEdge("edge_template_processing", "type_template_orchestration", "type_processing", "internal"),
                new DependencyEdge("edge_processing_foundation", "type_processing", "type_foundation", "internal"),
                new DependencyEdge("edge_component_foundation", "type_component_orchestration", "type_foundation", "internal")
            }));
        var baselineY = AbsoluteY(document, "type_template_orchestration");

        Assert.Equal(baselineY, AbsoluteY(document, "type_component_orchestration"));
        Assert.Equal(baselineY, AbsoluteY(document, "type_aggregation"));
        Assert.Equal(baselineY, AbsoluteY(document, "type_coordination"));
    }

    [Fact]
    public void Render_does_not_vertically_shift_matched_baseline_nodes_under_link_pressure()
    {
        var nodes = Enumerable.Range(0, 4)
            .SelectMany(index => new[]
            {
                Node($"type_processing_{index}", "project_api", $"Processing{index}Service"),
                Node($"type_leaf_{index}", "project_api", $"Leaf{index}")
            })
            .ToArray();
        var edges = Enumerable.Range(0, 4)
            .SelectMany(index => new[]
            {
                new DependencyEdge($"edge_orchestration_processing_{index}", "type_orchestration", $"type_processing_{index}", "internal"),
                new DependencyEdge($"edge_processing_leaf_{index}", $"type_processing_{index}", $"type_leaf_{index}", "internal")
            })
            .Concat(new[] { new DependencyEdge("edge_coordination_leaf", "type_coordination", "type_leaf_0", "internal") })
            .ToArray();
        var document = Render(new DiagramModel(
            new[]
            {
                new ProjectContainer("project_api", "Api", new[]
                {
                    Node("type_orchestration", "project_api", "TemplateRenderOrchestrationService"),
                    Node("type_coordination", "project_api", "TemplateRenderCoordinationService")
                }.Concat(nodes).ToArray())
            },
            Array.Empty<ExternalDependencyNode>(),
            edges));

        Assert.Equal(AbsoluteY(document, "type_orchestration"), AbsoluteY(document, "type_coordination"));
    }

    [Fact]
    public void Render_uses_configured_regex_for_baseline_alignment()
    {
        var settings = DiagramSettings.CreateDefault();
        settings.Layout.BaselineAlignmentPattern = ".*ProcessingService$";
        var document = Render(new DiagramModel(
            new[]
            {
                new ProjectContainer("project_api", "Api", new[]
                {
                    Node("type_template_orchestration", "project_api", "TemplateRenderOrchestrationService"),
                    Node("type_template_processing", "project_api", "TemplateRenderProcessingService"),
                    Node("type_component_processing", "project_api", "ComponentRenderProcessingService"),
                    Node("type_foundation", "project_api", "TemplateRenderFoundationService")
                })
            },
            Array.Empty<ExternalDependencyNode>(),
            new[]
            {
                new DependencyEdge("edge_template_processing", "type_template_orchestration", "type_template_processing", "internal"),
                new DependencyEdge("edge_template_foundation", "type_template_processing", "type_foundation", "internal"),
                new DependencyEdge("edge_component_foundation", "type_component_processing", "type_foundation", "internal")
            }),
            settings);

        Assert.Equal(
            AbsoluteY(document, "type_template_processing"),
            AbsoluteY(document, "type_component_processing"));
    }

    [Fact]
    public void Render_reuses_shared_dependency_node()
    {
        var document = Render(new DiagramModel(
            new[]
            {
                new ProjectContainer("project_api", "Api", new[]
                {
                    Node("type_controller", "project_api", "Controller"),
                    Node("type_job", "project_api", "Job"),
                    Node("type_service", "project_api", "Service")
                })
            },
            Array.Empty<ExternalDependencyNode>(),
            new[]
            {
                new DependencyEdge("edge_controller_service", "type_controller", "type_service", "internal"),
                new DependencyEdge("edge_job_service", "type_job", "type_service", "internal")
            }));

        Assert.Single(document.Descendants("mxCell"), cell => (string?)cell.Attribute("id") == "type_service");
        Assert.Equal(2, document.Descendants("mxCell").Count(cell => (string?)cell.Attribute("target") == "type_service"));
    }

    [Fact]
    public void Render_keeps_cycles_finite()
    {
        var document = Render(new DiagramModel(
            new[]
            {
                new ProjectContainer("project_api", "Api", new[]
                {
                    Node("type_a", "project_api", "A"),
                    Node("type_b", "project_api", "B")
                })
            },
            Array.Empty<ExternalDependencyNode>(),
            new[]
            {
                new DependencyEdge("edge_a_b", "type_a", "type_b", "internal"),
                new DependencyEdge("edge_b_a", "type_b", "type_a", "internal")
            }));

        Assert.Single(document.Descendants("mxCell"), cell => (string?)cell.Attribute("id") == "type_a");
        Assert.Single(document.Descendants("mxCell"), cell => (string?)cell.Attribute("id") == "type_b");
        Assert.Contains(document.Descendants("mxCell"), cell => (string?)cell.Attribute("id") == "edge_a_b");
        Assert.Contains(document.Descendants("mxCell"), cell => (string?)cell.Attribute("id") == "edge_b_a");
    }

    [Fact]
    public void Render_routes_links_from_bottom_to_top_with_configurable_ports()
    {
        var settings = DiagramSettings.CreateDefault();
        settings.Layout.EdgePortSpacing = 18;
        var document = Render(new DiagramModel(
            new[]
            {
                new ProjectContainer("project_api", "Api", new[]
                {
                    Node("type_controller", "project_api", "Controller"),
                    Node("type_cache", "project_api", "Cache"),
                    Node("type_repository", "project_api", "Repository")
                })
            },
            Array.Empty<ExternalDependencyNode>(),
            new[]
            {
                new DependencyEdge("edge_cache", "type_controller", "type_cache", "internal"),
                new DependencyEdge("edge_repository", "type_controller", "type_repository", "internal")
            }),
            settings);
        var cache = Cell(document, "edge_cache");
        var repository = Cell(document, "edge_repository");

        Assert.Equal("1", StyleValue(cache, "exitY"));
        Assert.Equal("0", StyleValue(cache, "entryY"));
        Assert.NotEqual(StyleValue(cache, "exitX"), StyleValue(repository, "exitX"));
        AssertParallelSegmentsSeparated(
            EdgePoints(document, "edge_cache"),
            EdgePoints(document, "edge_repository"),
            settings.Layout.ParallelLaneSpacing);
    }

    [Fact]
    public void Render_emits_exporter_owned_connector_geometry()
    {
        var document = Render(new DiagramModel(
            new[]
            {
                new ProjectContainer("project_api", "Api", new[]
                {
                    Node("type_source", "project_api", "Source"),
                    Node("type_target", "project_api", "Target")
                })
            },
            Array.Empty<ExternalDependencyNode>(),
            new[] { new DependencyEdge("edge", "type_source", "type_target", "internal") }));
        var edge = Cell(document, "edge");
        var style = (string?)edge.Attribute("style") ?? string.Empty;

        Assert.Contains("edgeStyle=none", style);
        Assert.Contains("noEdgeStyle=1", style);
        Assert.Contains("orthogonal=0", style);
        Assert.Contains("exitPerimeter=0", style);
        Assert.Contains("entryPerimeter=0", style);
        Assert.DoesNotContain("orthogonalEdgeStyle", style);
        Assert.DoesNotContain("jettySize", style);
        Assert.NotEmpty(edge.Descendants("mxPoint"));
    }

    [Fact]
    public void Render_uses_local_lane_offsets_instead_of_global_edge_order()
    {
        var unrelatedNodes = Enumerable.Range(0, 8)
            .SelectMany(index => new[]
            {
                Node($"type_unrelated_source_{index}", "project_api", $"UnrelatedSource{index}"),
                Node($"type_unrelated_target_{index}", "project_api", $"UnrelatedTarget{index}")
            })
            .ToArray();
        var unrelatedEdges = Enumerable.Range(0, 8)
            .Select(index => new DependencyEdge(
                $"edge_unrelated_{index}",
                $"type_unrelated_source_{index}",
                $"type_unrelated_target_{index}",
                "internal"))
            .ToArray();
        var document = Render(new DiagramModel(
            new[]
            {
                new ProjectContainer("project_api", "Api", new[]
                {
                    Node("type_top", "project_api", "Top"),
                    Node("type_middle", "project_api", "Middle"),
                    Node("type_bottom", "project_api", "Bottom")
                }.Concat(unrelatedNodes).ToArray())
            },
            Array.Empty<ExternalDependencyNode>(),
            unrelatedEdges.Concat(new[]
            {
                new DependencyEdge("edge_top_middle", "type_top", "type_middle", "internal"),
                new DependencyEdge("edge_middle_bottom", "type_middle", "type_bottom", "internal"),
                new DependencyEdge("edge_top_bottom", "type_top", "type_bottom", "internal")
            }).ToArray()));

        var laneY = EdgePoints(document, "edge_top_bottom")[1].Y;

        Assert.True(laneY < AbsoluteY(document, "type_bottom") - 10);
    }

    [Fact]
    public void Render_keeps_nearby_dependency_routes_local()
    {
        var document = Render(new DiagramModel(
            new[]
            {
                new ProjectContainer("project_api", "Api", new[]
                {
                    Node("type_orchestration", "project_api", "TemplateRenderOrchestrationService"),
                    Node("type_processing", "project_api", "TemplateRenderProcessingService"),
                    Node("type_far_source", "project_api", "FarSource"),
                    Node("type_far_target", "project_api", "FarTarget")
                })
            },
            Array.Empty<ExternalDependencyNode>(),
            new[]
            {
                new DependencyEdge("edge_template", "type_orchestration", "type_processing", "internal"),
                new DependencyEdge("edge_far", "type_far_source", "type_far_target", "internal")
            }));

        var localRight = Math.Max(AbsoluteRight(document, "type_orchestration"), AbsoluteRight(document, "type_processing"));
        var farLeft = AbsoluteX(document, "type_far_source");
        var routeRight = EdgePoints(document, "edge_template").Max(point => point.X);

        Assert.True(routeRight < farLeft);
        Assert.True(routeRight <= localRight + DiagramSettings.CreateDefault().Layout.HorizontalSpacing);
    }

    [Fact]
    public void Render_does_not_let_unrelated_link_density_expand_simple_vertical_gaps()
    {
        var unrelatedNodes = Enumerable.Range(0, 10)
            .SelectMany(index => new[]
            {
                Node($"type_unrelated_source_{index}", "project_api", $"UnrelatedSource{index}"),
                Node($"type_unrelated_target_{index}", "project_api", $"UnrelatedTarget{index}")
            })
            .ToArray();
        var unrelatedEdges = Enumerable.Range(0, 10)
            .Select(index => new DependencyEdge(
                $"edge_unrelated_{index}",
                $"type_unrelated_source_{index}",
                $"type_unrelated_target_{index}",
                "internal"))
            .ToArray();
        var settings = DiagramSettings.CreateDefault();
        var document = Render(new DiagramModel(
            new[]
            {
                new ProjectContainer("project_api", "Api", new[]
                {
                    Node("type_orchestration", "project_api", "TemplateRenderOrchestrationService"),
                    Node("type_processing", "project_api", "TemplateRenderProcessingService")
                }.Concat(unrelatedNodes).ToArray())
            },
            Array.Empty<ExternalDependencyNode>(),
            new[]
            {
                new DependencyEdge("edge_template", "type_orchestration", "type_processing", "internal")
            }.Concat(unrelatedEdges).ToArray()),
            settings);

        var verticalGap = AbsoluteY(document, "type_processing") - AbsoluteBottom(document, "type_orchestration");

        Assert.True(verticalGap <= settings.Layout.VerticalSpacing + settings.Layout.ParallelLaneSpacing);
    }

    [Fact]
    public void Render_separates_overlapping_corners()
    {
        var settings = DiagramSettings.CreateDefault();
        settings.Layout.ParallelLaneSpacing = 20;
        var document = Render(new DiagramModel(
            new[]
            {
                new ProjectContainer("project_api", "Api", new[]
                {
                    Node("type_a", "project_api", "A"),
                    Node("type_b", "project_api", "B"),
                    Node("type_c", "project_api", "C")
                })
            },
            Array.Empty<ExternalDependencyNode>(),
            new[]
            {
                new DependencyEdge("edge_a_c", "type_a", "type_c", "internal"),
                new DependencyEdge("edge_b_c", "type_b", "type_c", "internal")
            }),
            settings);
        var corners = document.Descendants("mxPoint")
            .Select(point => $"{(string?)point.Attribute("x")}:{(string?)point.Attribute("y")}")
            .ToArray();

        Assert.Equal(corners.Length, corners.Distinct().Count());
    }

    [Fact]
    public void Render_places_standalone_nodes_away_from_main_tree()
    {
        var settings = DiagramSettings.CreateDefault();
        settings.Layout.StandaloneGroupSpacing = 300;
        var document = Render(new DiagramModel(
            new[]
            {
                new ProjectContainer("project_api", "Api", new[]
                {
                    Node("type_controller", "project_api", "Controller"),
                    Node("type_service", "project_api", "Service"),
                    Node("type_standalone", "project_api", "Standalone")
                })
            },
            Array.Empty<ExternalDependencyNode>(),
            new[] { new DependencyEdge("edge_controller_service", "type_controller", "type_service", "internal") }),
            settings);

        Assert.True(AbsoluteX(document, "type_standalone") - AbsoluteX(document, "type_controller") >= settings.Layout.StandaloneGroupSpacing);
    }

    [Fact]
    public void Render_places_standalone_nodes_in_square_root_grid()
    {
        var standaloneNodes = Enumerable.Range(0, 10)
            .Select(index => Node($"type_standalone_{index}", "project_api", $"Standalone{index}"))
            .ToArray();
        var document = Render(new DiagramModel(
            new[]
            {
                new ProjectContainer("project_api", "Api", new[]
                {
                    Node("type_controller", "project_api", "Controller"),
                    Node("type_service", "project_api", "Service")
                }.Concat(standaloneNodes).ToArray())
            },
            Array.Empty<ExternalDependencyNode>(),
            new[] { new DependencyEdge("edge_controller_service", "type_controller", "type_service", "internal") }));

        Assert.Equal(AbsoluteY(document, "type_standalone_0"), AbsoluteY(document, "type_standalone_3"));
        Assert.Equal(AbsoluteX(document, "type_standalone_0"), AbsoluteX(document, "type_standalone_4"));
        Assert.True(AbsoluteY(document, "type_standalone_4") > AbsoluteY(document, "type_standalone_0"));
        Assert.True(AbsoluteX(document, "type_standalone_9") > AbsoluteX(document, "type_standalone_8"));
        Assert.Equal(AbsoluteY(document, "type_standalone_8"), AbsoluteY(document, "type_standalone_9"));
    }

    [Fact]
    public void Render_labels_external_boundary_nodes_with_configured_tag()
    {
        var settings = DiagramSettings.CreateDefault();
        settings.ExternalDependencyTag = "[Outside]";
        var document = Render(new DiagramModel(
            new[]
            {
                new ProjectContainer("project_api", "Api", new[]
                {
                    Node("type_controller", "project_api", "Controller")
                })
            },
            new[] { new ExternalDependencyNode("external_domain", "DomainService", "Domain", "external-guid", "Domain.DomainService", "[Outside]") },
            new[] { new DependencyEdge("edge_external", "type_controller", "external_domain", "external") }),
            settings);
        var value = (string?)Cell(document, "external_domain").Attribute("value");

        Assert.Contains("[Outside]", value);
        Assert.Contains("Domain.DomainService", value);
    }

    [Fact]
    public void Render_places_external_dependency_directly_below_constructing_node()
    {
        var settings = DiagramSettings.CreateDefault();
        var document = Render(new DiagramModel(
            new[]
            {
                new ProjectContainer("project_api", "Api", new[]
                {
                    Node("type_controller", "project_api", "Controller"),
                    Node("type_service", "project_api", "Service")
                })
            },
            new[] { new ExternalDependencyNode("external_domain", "DomainService", "Domain", "external-guid", "Domain.DomainService", "[External]") },
            new[]
            {
                new DependencyEdge("edge_controller_service", "type_controller", "type_service", "internal"),
                new DependencyEdge("edge_controller_external", "type_controller", "external_domain", "external")
            }),
            settings);

        Assert.Equal(
            AbsoluteBottom(document, "type_controller") + settings.Layout.VerticalSpacing,
            AbsoluteY(document, "external_domain"));
    }

    [Fact]
    public void Render_duplicates_shared_external_dependency_per_constructing_node()
    {
        var document = Render(new DiagramModel(
            new[]
            {
                new ProjectContainer("project_api", "Api", new[]
                {
                    Node("type_controller", "project_api", "Controller"),
                    Node("type_job", "project_api", "Job")
                })
            },
            new[] { new ExternalDependencyNode("external_domain", "DomainService", "Domain", "external-guid", "Domain.DomainService", "[External]") },
            new[]
            {
                new DependencyEdge("edge_controller_external", "type_controller", "external_domain", "external"),
                new DependencyEdge("edge_job_external", "type_job", "external_domain", "external")
            }));
        var controllerExternalId = "external_domain__type_controller__edge_controller_external";
        var jobExternalId = "external_domain__type_job__edge_job_external";

        Assert.Equal(controllerExternalId, (string?)Cell(document, "edge_controller_external").Attribute("target"));
        Assert.Equal(jobExternalId, (string?)Cell(document, "edge_job_external").Attribute("target"));
        Assert.Contains("[External]", (string?)Cell(document, controllerExternalId).Attribute("value"));
        Assert.Contains("[External]", (string?)Cell(document, jobExternalId).Attribute("value"));
        Assert.True(Math.Abs(AbsoluteX(document, controllerExternalId) - AbsoluteX(document, "type_controller")) <
            DiagramSettings.CreateDefault().Layout.HorizontalSpacing * 2);
        Assert.True(Math.Abs(AbsoluteX(document, jobExternalId) - AbsoluteX(document, "type_job")) <
            DiagramSettings.CreateDefault().Layout.HorizontalSpacing * 2);
    }

    [Fact]
    public void Render_routes_waypoints_around_non_endpoint_nodes_including_external_nodes()
    {
        var document = Render(new DiagramModel(
            new[]
            {
                new ProjectContainer("project_api", "Api", new[]
                {
                    Node("type_controller", "project_api", "Controller"),
                    Node("type_job", "project_api", "Job"),
                    Node("type_orchestration", "project_api", "TemplateRenderOrchestrationService"),
                    Node("type_processing", "project_api", "TemplateRenderProcessingService"),
                    Node("type_foundation", "project_api", "TemplateRenderFoundationService")
                })
            },
            new[]
            {
                new ExternalDependencyNode("external_logger", "ILogger", "Microsoft.Extensions.Logging", "external-logger", "Microsoft.Extensions.Logging.ILogger", "[External]"),
                new ExternalDependencyNode("external_clock", "IClock", "System", "external-clock", "System.IClock", "[External]")
            },
            new[]
            {
                new DependencyEdge("edge_controller_orchestration", "type_controller", "type_orchestration", "internal"),
                new DependencyEdge("edge_job_orchestration", "type_job", "type_orchestration", "internal"),
                new DependencyEdge("edge_orchestration_processing", "type_orchestration", "type_processing", "internal"),
                new DependencyEdge("edge_processing_foundation", "type_processing", "type_foundation", "internal"),
                new DependencyEdge("edge_controller_logger", "type_controller", "external_logger", "external"),
                new DependencyEdge("edge_job_logger", "type_job", "external_logger", "external"),
                new DependencyEdge("edge_orchestration_clock", "type_orchestration", "external_clock", "external")
            }));

        AssertNoWaypointSegmentsCrossNonEndpointNodes(document);
    }

    [Fact]
    public void Render_keeps_project_container_as_border_parent()
    {
        var document = Render(new DiagramModel(
            new[]
            {
                new ProjectContainer("project_api", "Api", new[]
                {
                    Node("type_controller", "project_api", "Controller")
                })
            },
            Array.Empty<ExternalDependencyNode>(),
            Array.Empty<DependencyEdge>()));

        Assert.Equal("1", (string?)Cell(document, "project_api").Attribute("vertex"));
        Assert.Equal("project_api", (string?)Cell(document, "type_controller").Attribute("parent"));
    }

    [Fact]
    public void Render_places_data_model_relationships_around_highest_connected_model()
    {
        var document = Render(new DiagramModel(
            new[]
            {
                new ProjectContainer("project_api", "Api", new[]
                {
                    Model("type_session", "Session", ("App", "App", "type_app"), ("Request", "Request", "type_request"), ("Page", "Page", "type_page"), ("User", "User", "type_user")),
                    Model("type_app", "App"),
                    Model("type_request", "Request"),
                    Model("type_page", "Page"),
                    Model("type_user", "User")
                })
            },
            Array.Empty<ExternalDependencyNode>(),
            Array.Empty<DependencyEdge>()));
        var hub = NodeRect(document, "data_model_type_session");
        var related = new[] { "data_model_type_app", "data_model_type_request", "data_model_type_page", "data_model_type_user" }
            .Select(id => NodeRect(document, id))
            .ToArray();

        Assert.Contains(related, rect => rect.X > hub.X + hub.Width);
        Assert.Contains(related, rect => rect.X + rect.Width < hub.X);
        Assert.Contains(related, rect => rect.Y + rect.Height < hub.Y);
        Assert.Contains(related, rect => rect.Y > hub.Y + hub.Height);
    }

    [Fact]
    public void Render_prevents_data_model_table_overlaps_after_radial_layout()
    {
        var settings = DiagramSettings.CreateDefault();
        var document = Render(new DiagramModel(
            new[]
            {
                new ProjectContainer("project_api", "Api", new[]
                {
                    Model("type_hub", "Hub", ("First", "First", "type_first"), ("Second", "Second", "type_second"), ("Third", "Third", "type_third"), ("Fourth", "Fourth", "type_fourth")),
                    TallModel("type_first", "First"),
                    TallModel("type_second", "Second"),
                    TallModel("type_third", "Third"),
                    TallModel("type_fourth", "Fourth")
                })
            },
            Array.Empty<ExternalDependencyNode>(),
            Array.Empty<DependencyEdge>()),
            settings);

        AssertNoDataModelTableOverlaps(document);
        AssertDataModelTableGap(document, settings.Layout.DataModelMinimumTableGap);
    }

    [Fact]
    public void Render_keeps_nested_data_model_children_near_parent_side()
    {
        var document = Render(new DiagramModel(
            new[]
            {
                new ProjectContainer("project_api", "Api", new[]
                {
                    Model("type_hub", "Hub", ("Alpha", "Alpha", "type_alpha"), ("Beta", "Beta", "type_beta"), ("Page", "Page", "type_page"), ("Zeta", "Zeta", "type_zeta")),
                    Model("type_alpha", "Alpha"),
                    Model("type_beta", "Beta"),
                    Model("type_page", "Page"),
                    Model("type_zeta", "Zeta"),
                    Model("type_page_info", "PageInfo", ("Page", "Page", "type_page"))
                })
            },
            Array.Empty<ExternalDependencyNode>(),
            Array.Empty<DependencyEdge>()));
        var hub = NodeRect(document, "data_model_type_hub");
        var page = NodeRect(document, "data_model_type_page");
        var pageInfo = NodeRect(document, "data_model_type_page_info");

        Assert.Equal(Math.Sign(CenterY(page) - CenterY(hub)), Math.Sign(CenterY(pageInfo) - CenterY(hub)));
    }

    [Fact]
    public void Render_routes_data_model_relationships_orthogonally()
    {
        var document = Render(new DiagramModel(
            new[]
            {
                new ProjectContainer("project_api", "Api", new[]
                {
                    Model("type_app", "App", ("Layout", "Layout", "type_layout"), ("Page", "Page", "type_page"), ("Email", "Email", "type_email")),
                    Model("type_layout", "Layout"),
                    Model("type_page", "Page"),
                    Model("type_email", "Email", ("User", "User", "type_user")),
                    Model("type_user", "User")
                })
            },
            Array.Empty<ExternalDependencyNode>(),
            Array.Empty<DependencyEdge>()));

        Assert.All(DataModelRouteSegments(document), segment =>
            Assert.True(segment.Start.X == segment.End.X || segment.Start.Y == segment.End.Y));
    }

    [Fact]
    public void Render_keeps_busy_exposure_tree_dependencies_traceable_end_to_end()
    {
        var childIds = new[] { "json", "template", "component", "resource", "app", "script", "cache" };
        var nodes = new List<TypeNode> { Node("processing", "project", "TemplateRenderProcessingService") };
        nodes.AddRange(childIds.Select(id => Node(id, "project", id + "Service")));
        nodes.AddRange(new[]
        {
            Node("template_broker", "project", "TemplateBroker"),
            Node("component_broker", "project", "ComponentBroker"),
            Node("resource_broker", "project", "ResourceBroker"),
            Node("app_broker", "project", "AppBroker")
        });
        var edges = childIds.Select((id, index) =>
                new DependencyEdge("edge_" + id, "processing", id, "internal"))
            .Concat(new[]
            {
                new DependencyEdge("edge_template_broker", "template", "template_broker", "internal"),
                new DependencyEdge("edge_component_broker", "component", "component_broker", "internal"),
                new DependencyEdge("edge_resource_broker", "resource", "resource_broker", "internal"),
                new DependencyEdge("edge_app_broker", "app", "app_broker", "internal")
            })
            .ToArray();
        var settings = DiagramSettings.CreateDefault();
        settings.Layout.ExposureTreeLayoutThreshold = 1;
        var document = Render(new DiagramModel(
            new[] { new ProjectContainer("project", "Project", nodes) },
            Array.Empty<ExternalDependencyNode>(),
            edges), settings);
        var routes = childIds.ToDictionary(
            id => id,
            id => CompleteEdgePointsBetween(document, "TemplateRenderProcessingService", id + "Service"));

        foreach (var id in childIds)
        {
            var route = routes[id];
            var source = NodeRect(document, CellIdByValue(document, "TemplateRenderProcessingService"));
            var target = NodeRect(document, CellIdByValue(document, id + "Service"));
            Assert.True(route.Count >= 2);
            Assert.Equal(source.Y + source.Height, route[0].Y);
            Assert.Equal(route[0].X, route[1].X);
            Assert.True(route[1].Y > route[0].Y);
            Assert.Equal(target.Y, route[^1].Y);
            Assert.Equal(route[^1].X, route[^2].X);
            Assert.True(route[^2].Y < route[^1].Y);
            Assert.All(TestSegments(route), segment =>
                Assert.True(segment.Start.X == segment.End.X || segment.Start.Y == segment.End.Y));
        }

        var routePairs = childIds.SelectMany((left, index) => childIds.Skip(index + 1).Select(right => (left, right)));
        foreach (var (left, right) in routePairs)
        {
            Assert.Equal(0, SharedLength(routes[left], routes[right]));
            AssertParallelSegmentsSeparated(routes[left], routes[right], settings.Layout.ParallelLaneSpacing);
        }
    }

    [Theory]
    [InlineData(5)]
    [InlineData(7)]
    public void Render_orders_same_side_fan_out_monotonically(int targetCount)
    {
        var targetIds = Enumerable.Range(0, targetCount).Select(index => $"target_{index}").ToArray();
        var nodes = new[] { Node("source", "project", "ProcessingSource") }
            .Concat(targetIds.Select(id => Node(id, "project", id)))
            .ToArray();
        var edges = targetIds.Select(id => new DependencyEdge($"edge_{id}", "source", id, "internal")).ToArray();
        var settings = DiagramSettings.CreateDefault();
        settings.Layout.ExposureTreeLayoutThreshold = 1;

        var forward = Render(new DiagramModel(
            new[] { new ProjectContainer("project", "Project", nodes) },
            Array.Empty<ExternalDependencyNode>(),
            edges), settings);
        var reversed = Render(new DiagramModel(
            new[] { new ProjectContainer("project", "Project", nodes) },
            Array.Empty<ExternalDependencyNode>(),
            edges.AsEnumerable().Reverse().ToArray()), settings);

        AssertMonotonicFanOut(forward, "ProcessingSource", targetIds, settings.Layout.ParallelLaneSpacing);
        AssertMonotonicFanOut(reversed, "ProcessingSource", targetIds, settings.Layout.ParallelLaneSpacing);
        foreach (var targetId in targetIds)
        {
            Assert.Equal(
                CompleteEdgePointsBetween(forward, "ProcessingSource", targetId),
                CompleteEdgePointsBetween(reversed, "ProcessingSource", targetId));
        }
    }

    private static XDocument Render(DiagramModel diagram, DiagramSettings? settings = null)
    {
        return XDocument.Parse(new DrawioDiagramRenderer().Render(diagram, settings ?? DiagramSettings.CreateDefault()));
    }

    private static TypeNode Node(string id, string projectId, string name)
    {
        return new TypeNode(id, projectId, name, $"Api.{name}", "Class");
    }

    private static TypeNode Model(string id, string name, params (string Name, string TypeName, string TypeId)[] properties)
    {
        var modelProperties = properties.Length == 0
            ? new[] { new TypeProperty("Id", "int") }
            : properties.Select(property => new TypeProperty(property.Name, property.TypeName, TypeId: property.TypeId)).ToArray();
        return new TypeNode(id, "project_api", name, $"Api.{name}", "Class", Properties: modelProperties);
    }

    private static TypeNode TallModel(string id, string name)
    {
        var properties = Enumerable.Range(0, 18)
            .Select(index => new TypeProperty($"Property{index}", "string"))
            .ToArray();
        return new TypeNode(id, "project_api", name, $"Api.{name}", "Class", Properties: properties);
    }

    private static XElement Cell(XDocument document, string id)
    {
        var exact = document.Descendants("mxCell").SingleOrDefault(cell => (string?)cell.Attribute("id") == id);
        if (exact is not null)
        {
            return exact;
        }

        return LogicalEdgeCells(document, id).OrderByDescending(SegmentIndex).First();
    }

    private static int AbsoluteX(XDocument document, string id)
    {
        var cell = Cell(document, id);
        var x = Geometry(document, id, "x");
        var parentId = (string?)cell.Attribute("parent");
        return string.IsNullOrWhiteSpace(parentId) || parentId == "1"
            ? x
            : x + Geometry(document, parentId, "x");
    }

    private static int AbsoluteY(XDocument document, string id)
    {
        var cell = Cell(document, id);
        var y = Geometry(document, id, "y");
        var parentId = (string?)cell.Attribute("parent");
        return string.IsNullOrWhiteSpace(parentId) || parentId == "1"
            ? y
            : y + Geometry(document, parentId, "y");
    }

    private static int AbsoluteRight(XDocument document, string id)
    {
        return AbsoluteX(document, id) + Geometry(document, id, "width");
    }

    private static int AbsoluteBottom(XDocument document, string id)
    {
        return AbsoluteY(document, id) + Geometry(document, id, "height");
    }

    private static int Geometry(XDocument document, string id, string attributeName)
    {
        return int.Parse((string)Cell(document, id).Element("mxGeometry")!.Attribute(attributeName)!);
    }

    private static IReadOnlyList<(int X, int Y)> EdgePoints(XDocument document, string id)
    {
        var segments = LogicalEdgeCells(document, id).OrderBy(SegmentIndex).ToArray();
        if (segments.Length == 0)
        {
            segments = new[] { Cell(document, id) };
        }

        var points = new List<(int X, int Y)>();
        for (var index = 0; index < segments.Length; index++)
        {
            var segment = segments[index];
            var parentId = (string?)segment.Attribute("parent");
            var offsetX = string.IsNullOrWhiteSpace(parentId) || parentId == "1" ? 0 : AbsoluteX(document, parentId);
            var offsetY = string.IsNullOrWhiteSpace(parentId) || parentId == "1" ? 0 : AbsoluteY(document, parentId);
            if (index > 0)
            {
                var source = Cell(document, (string)segment.Attribute("source")!);
                points.Add((AbsoluteX(document, (string)source.Attribute("id")!), AbsoluteY(document, (string)source.Attribute("id")!)));
            }

            points.AddRange(segment.Descendants("mxPoint").Select(point => (
                int.Parse((string)point.Attribute("x")!) + offsetX,
                int.Parse((string)point.Attribute("y")!) + offsetY)));
            if (index < segments.Length - 1)
            {
                var target = Cell(document, (string)segment.Attribute("target")!);
                points.Add((AbsoluteX(document, (string)target.Attribute("id")!), AbsoluteY(document, (string)target.Attribute("id")!)));
            }
        }

        return NormalizeRoutePoints(points);
    }

    private static XElement[] LogicalEdgeCells(XDocument document, string id) =>
        document.Descendants("mxCell")
            .Where(cell => (string?)cell.Attribute("edge") == "1" &&
                (string?)cell.Attribute("logicalEdgeId") == id)
            .ToArray();

    private static int SegmentIndex(XElement edge) =>
        int.TryParse((string?)edge.Attribute("segmentIndex"), out var index) ? index : 0;

    private static IReadOnlyList<(int X, int Y)> NormalizeRoutePoints(IEnumerable<(int X, int Y)> source)
    {
        var result = new List<(int X, int Y)>();
        foreach (var point in source)
        {
            if (result.Count == 0 || result[^1] != point)
            {
                result.Add(point);
            }

            while (result.Count >= 3 &&
                (result[^3].X == result[^2].X && result[^2].X == result[^1].X ||
                 result[^3].Y == result[^2].Y && result[^2].Y == result[^1].Y))
            {
                result.RemoveAt(result.Count - 2);
            }
        }

        return result;
    }

    private static void AssertNoWaypointSegmentsCrossNonEndpointNodes(XDocument document)
    {
        var nodeRects = document.Descendants("mxCell")
            .Where(cell => (string?)cell.Attribute("vertex") == "1" &&
                !(((string?)cell.Attribute("id")) ?? string.Empty).StartsWith("project_", StringComparison.Ordinal))
            .ToDictionary(
                cell => (string)cell.Attribute("id")!,
                cell => NodeRect(document, (string)cell.Attribute("id")!),
                StringComparer.Ordinal);

        foreach (var edge in document.Descendants("mxCell").Where(cell => (string?)cell.Attribute("edge") == "1"))
        {
            var edgeId = (string)edge.Attribute("id")!;
            var sourceId = (string?)edge.Attribute("source");
            var targetId = (string?)edge.Attribute("target");
            foreach (var segment in TestSegments(EdgePoints(document, edgeId)))
            {
                foreach (var node in nodeRects.Where(node =>
                    !string.Equals(node.Key, sourceId, StringComparison.Ordinal) &&
                    !string.Equals(node.Key, targetId, StringComparison.Ordinal)))
                {
                    Assert.False(
                        SegmentIntersects(segment.Start, segment.End, node.Value),
                        $"{edgeId} crosses {node.Key} between {segment.Start} and {segment.End}.");
                }
            }
        }
    }

    private static void AssertNoDataModelTableOverlaps(XDocument document)
    {
        var rects = document.Descendants("mxCell")
            .Where(cell => (string?)cell.Attribute("vertex") == "1" &&
                (string?)cell.Attribute("parent") == "1" &&
                (((string?)cell.Attribute("id")) ?? string.Empty).StartsWith("data_model_", StringComparison.Ordinal))
            .Select(cell => NodeRect(document, (string)cell.Attribute("id")!))
            .ToArray();

        for (var left = 0; left < rects.Length; left++)
        {
            for (var right = left + 1; right < rects.Length; right++)
            {
                Assert.False(Overlaps(rects[left], rects[right]));
            }
        }
    }

    private static void AssertDataModelTableGap(XDocument document, int minimumGap)
    {
        var rects = document.Descendants("mxCell")
            .Where(cell => (string?)cell.Attribute("vertex") == "1" &&
                (string?)cell.Attribute("parent") == "1" &&
                (((string?)cell.Attribute("id")) ?? string.Empty).StartsWith("data_model_", StringComparison.Ordinal))
            .Select(cell => NodeRect(document, (string)cell.Attribute("id")!))
            .ToArray();

        for (var left = 0; left < rects.Length; left++)
        {
            for (var right = left + 1; right < rects.Length; right++)
            {
                if (ProjectedGap(rects[left], rects[right]) is { } gap)
                {
                    Assert.True(gap >= minimumGap);
                }
            }
        }
    }

    private static int? ProjectedGap(
        (int X, int Y, int Width, int Height) left,
        (int X, int Y, int Width, int Height) right)
    {
        var xOverlap = left.X < right.X + right.Width && left.X + left.Width > right.X;
        var yOverlap = left.Y < right.Y + right.Height && left.Y + left.Height > right.Y;
        if (xOverlap)
        {
            return Math.Max(left.Y, right.Y) - Math.Min(left.Y + left.Height, right.Y + right.Height);
        }

        if (yOverlap)
        {
            return Math.Max(left.X, right.X) - Math.Min(left.X + left.Width, right.X + right.Width);
        }

        return null;
    }

    private static (int X, int Y, int Width, int Height) NodeRect(XDocument document, string id)
    {
        return (AbsoluteX(document, id), AbsoluteY(document, id), Geometry(document, id, "width"), Geometry(document, id, "height"));
    }

    private static int CenterY((int X, int Y, int Width, int Height) rect)
    {
        return rect.Y + rect.Height / 2;
    }

    private static IEnumerable<((int X, int Y) Start, (int X, int Y) End)> DataModelRouteSegments(XDocument document)
    {
        foreach (var edge in document.Descendants("mxCell").Where(cell =>
            (string?)cell.Attribute("edge") == "1" &&
            (((string?)cell.Attribute("id")) ?? string.Empty).StartsWith("data_model_edge_", StringComparison.Ordinal)))
        {
            var points = DataModelRoutePoints(document, edge);
            foreach (var segment in TestSegments(points))
            {
                yield return segment;
            }
        }
    }

    private static IReadOnlyList<(int X, int Y)> DataModelRoutePoints(XDocument document, XElement edge)
    {
        var sourceId = (string)edge.Attribute("source")!;
        var targetId = (string)edge.Attribute("target")!;
        var source = NodeRect(document, sourceId);
        var target = NodeRect(document, targetId);
        var points = new List<(int X, int Y)>
        {
            RatioPoint(source, StyleValue(edge, "exitX"), StyleValue(edge, "exitY"))
        };
        points.AddRange(EdgePoints(document, (string)edge.Attribute("id")!));
        points.Add(RatioPoint(target, StyleValue(edge, "entryX"), StyleValue(edge, "entryY")));

        return points;
    }

    private static (int X, int Y) RatioPoint(
        (int X, int Y, int Width, int Height) rect,
        string xRatio,
        string yRatio)
    {
        var x = double.Parse(xRatio, CultureInfo.InvariantCulture);
        var y = double.Parse(yRatio, CultureInfo.InvariantCulture);
        return ((int)Math.Round(rect.X + rect.Width * x), (int)Math.Round(rect.Y + rect.Height * y));
    }

    private static bool SegmentIntersects((int X, int Y) start, (int X, int Y) end, (int X, int Y, int Width, int Height) rect)
    {
        var right = rect.X + rect.Width;
        var bottom = rect.Y + rect.Height;
        if (start.Y == end.Y)
        {
            return start.Y > rect.Y &&
                start.Y < bottom &&
                Math.Max(start.X, end.X) > rect.X &&
                Math.Min(start.X, end.X) < right;
        }

        if (start.X == end.X)
        {
            return start.X > rect.X &&
                start.X < right &&
                Math.Max(start.Y, end.Y) > rect.Y &&
                Math.Min(start.Y, end.Y) < bottom;
        }

        return false;
    }

    private static bool Overlaps(
        (int X, int Y, int Width, int Height) left,
        (int X, int Y, int Width, int Height) right)
    {
        return left.X < right.X + right.Width &&
            left.X + left.Width > right.X &&
            left.Y < right.Y + right.Height &&
            left.Y + left.Height > right.Y;
    }

    private static void AssertParallelSegmentsSeparated(
        IReadOnlyList<(int X, int Y)> left,
        IReadOnlyList<(int X, int Y)> right,
        int spacing)
    {
        foreach (var leftSegment in TestSegments(left))
        {
            foreach (var rightSegment in TestSegments(right))
            {
                if (leftSegment.Start.X == leftSegment.End.X &&
                    rightSegment.Start.X == rightSegment.End.X &&
                    RangesOverlap(leftSegment.Start.Y, leftSegment.End.Y, rightSegment.Start.Y, rightSegment.End.Y))
                {
                    Assert.True(Math.Abs(leftSegment.Start.X - rightSegment.Start.X) >= spacing);
                }

                if (leftSegment.Start.Y == leftSegment.End.Y &&
                    rightSegment.Start.Y == rightSegment.End.Y &&
                    RangesOverlap(leftSegment.Start.X, leftSegment.End.X, rightSegment.Start.X, rightSegment.End.X))
                {
                    Assert.True(Math.Abs(leftSegment.Start.Y - rightSegment.Start.Y) >= spacing);
                }
            }
        }
    }

    private static IEnumerable<((int X, int Y) Start, (int X, int Y) End)> TestSegments(IReadOnlyList<(int X, int Y)> points)
    {
        for (var index = 0; index < points.Count - 1; index++)
        {
            yield return (points[index], points[index + 1]);
        }
    }

    private static IReadOnlyList<(int X, int Y)> CompleteEdgePointsBetween(
        XDocument document,
        string sourceValue,
        string targetValue)
    {
        var sourceId = CellIdByValue(document, sourceValue);
        var targetId = CellIdByValue(document, targetValue);
        var logicalEdges = document.Descendants("mxCell").Where(cell =>
            (string?)cell.Attribute("edge") == "1" &&
            ((string?)cell.Attribute("semanticSourceId") == sourceId &&
             (string?)cell.Attribute("semanticTargetId") == targetId ||
             (string?)cell.Attribute("source") == sourceId &&
             (string?)cell.Attribute("target") == targetId)).ToArray();
        var firstEdge = logicalEdges.OrderBy(SegmentIndex).First();
        var lastEdge = logicalEdges.OrderByDescending(SegmentIndex).First();
        var logicalEdgeId = (string?)firstEdge.Attribute("logicalEdgeId") ?? (string)firstEdge.Attribute("id")!;
        var points = new List<(int X, int Y)>
        {
            RatioPoint(NodeRect(document, sourceId), StyleValue(firstEdge, "exitX"), StyleValue(firstEdge, "exitY"))
        };
        points.AddRange(EdgePoints(document, logicalEdgeId));
        points.Add(RatioPoint(NodeRect(document, targetId), StyleValue(lastEdge, "entryX"), StyleValue(lastEdge, "entryY")));
        return points;
    }

    private static void AssertMonotonicFanOut(
        XDocument document,
        string sourceValue,
        IReadOnlyList<string> targetValues,
        int laneSpacing)
    {
        var source = NodeRect(document, CellIdByValue(document, sourceValue));
        var routes = targetValues.Select(value => new
        {
            Value = value,
            Target = NodeRect(document, CellIdByValue(document, value)),
            Points = CompleteEdgePointsBetween(document, sourceValue, value)
        }).ToArray();

        foreach (var route in routes)
        {
            Assert.Equal(source.Y + source.Height, route.Points[0].Y);
            Assert.Equal(route.Target.Y, route.Points[^1].Y);
        }

        var sides = new[]
        {
            routes.Where(route => route.Target.X + route.Target.Width / 2 < source.X + source.Width / 2)
                .OrderByDescending(route => route.Target.X + route.Target.Width / 2).ToArray(),
            routes.Where(route => route.Target.X + route.Target.Width / 2 > source.X + source.Width / 2)
                .OrderBy(route => route.Target.X + route.Target.Width / 2).ToArray()
        };
        foreach (var side in sides)
        {
            Assert.True(side.Length >= 2);
            var sourcePorts = side.Select(route => route.Points[0].X).ToArray();
            var laneCoordinates = side.Select(route => TestSegments(route.Points)
                .First(segment => segment.Start.Y == segment.End.Y && segment.Start.X != segment.End.X)
                .Start.Y).ToArray();
            if (side[0].Target.X < source.X)
            {
                Assert.Equal(sourcePorts.OrderByDescending(value => value), sourcePorts);
            }
            else
            {
                Assert.Equal(sourcePorts.OrderBy(value => value), sourcePorts);
            }

            Assert.Equal(laneCoordinates.OrderByDescending(value => value), laneCoordinates);
            Assert.All(laneCoordinates.Zip(laneCoordinates.Skip(1), (left, right) => left - right),
                distance => Assert.True(distance >= laneSpacing));
            foreach (var pair in side.SelectMany((left, index) => side.Skip(index + 1).Select(right => (left, right))))
            {
                Assert.False(
                    RoutesCross(pair.left.Points, pair.right.Points),
                    $"Routes {pair.left.Value} and {pair.right.Value} cross: " +
                    $"{string.Join(" -> ", pair.left.Points)} / {string.Join(" -> ", pair.right.Points)}");
            }
        }
    }

    private static bool RoutesCross(IReadOnlyList<(int X, int Y)> left, IReadOnlyList<(int X, int Y)> right) =>
        TestSegments(left).Any(leftSegment => TestSegments(right).Any(rightSegment =>
            leftSegment.Start.X == leftSegment.End.X && rightSegment.Start.Y == rightSegment.End.Y &&
            BetweenStrict(leftSegment.Start.X, rightSegment.Start.X, rightSegment.End.X) &&
            BetweenStrict(rightSegment.Start.Y, leftSegment.Start.Y, leftSegment.End.Y) ||
            leftSegment.Start.Y == leftSegment.End.Y && rightSegment.Start.X == rightSegment.End.X &&
            BetweenStrict(rightSegment.Start.X, leftSegment.Start.X, leftSegment.End.X) &&
            BetweenStrict(leftSegment.Start.Y, rightSegment.Start.Y, rightSegment.End.Y)));

    private static bool BetweenStrict(int value, int first, int second) =>
        value > Math.Min(first, second) && value < Math.Max(first, second);

    private static string CellIdByValue(XDocument document, string value) =>
        (string)document.Descendants("mxCell").Single(cell =>
            ((string?)cell.Attribute("value"))?.StartsWith(value, StringComparison.Ordinal) == true).Attribute("id")!;

    private static int SharedLength(IReadOnlyList<(int X, int Y)> left, IReadOnlyList<(int X, int Y)> right)
    {
        return TestSegments(left).Sum(leftSegment => TestSegments(right).Sum(rightSegment =>
        {
            if (leftSegment.Start.Y == leftSegment.End.Y && rightSegment.Start.Y == rightSegment.End.Y &&
                leftSegment.Start.Y == rightSegment.Start.Y)
            {
                return RangeOverlapLength(leftSegment.Start.X, leftSegment.End.X, rightSegment.Start.X, rightSegment.End.X);
            }

            if (leftSegment.Start.X == leftSegment.End.X && rightSegment.Start.X == rightSegment.End.X &&
                leftSegment.Start.X == rightSegment.Start.X)
            {
                return RangeOverlapLength(leftSegment.Start.Y, leftSegment.End.Y, rightSegment.Start.Y, rightSegment.End.Y);
            }

            return 0;
        }));
    }

    private static int RangeOverlapLength(int leftStart, int leftEnd, int rightStart, int rightEnd) =>
        Math.Max(0, Math.Min(Math.Max(leftStart, leftEnd), Math.Max(rightStart, rightEnd)) -
            Math.Max(Math.Min(leftStart, leftEnd), Math.Min(rightStart, rightEnd)));

    private static bool RangesOverlap(int firstStart, int firstEnd, int secondStart, int secondEnd)
    {
        var firstMin = Math.Min(firstStart, firstEnd);
        var firstMax = Math.Max(firstStart, firstEnd);
        var secondMin = Math.Min(secondStart, secondEnd);
        var secondMax = Math.Max(secondStart, secondEnd);
        return firstMin <= secondMax && secondMin <= firstMax;
    }

    private static string StyleValue(XElement edge, string key)
    {
        var prefix = key + "=";
        return ((string)edge.Attribute("style")!)
            .Split(';')
            .Single(part => part.StartsWith(prefix, StringComparison.Ordinal))
            .Substring(prefix.Length);
    }
}
