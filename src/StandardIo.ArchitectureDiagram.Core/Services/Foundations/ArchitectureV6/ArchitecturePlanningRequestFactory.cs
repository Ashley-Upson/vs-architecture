using System;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core.Models;
using StandardIo.ArchitectureDiagram.Core.Models.ArchitectureV6;
using StandardIo.ArchitectureDiagram.Core.Models.Architectures;
using StandardIo.ArchitectureDiagram.Core.Models.Generation;
using ArchitectureSemanticModel = StandardIo.ArchitectureDiagram.Core.Models.Architectures.ArchitectureDiagram;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.ArchitectureV6;

public static class ArchitecturePlanningRequestFactory
{
    public static ArchitecturePlanningRequest Create(
        ArchitectureSemanticModel diagram,
        ArchitectureGenerationJob job,
        ArchitectureRenderingMode mode)
    {
        if (diagram is null) throw new ArgumentNullException(nameof(diagram));
        if (job is null) throw new ArgumentNullException(nameof(job));
        var rendering = job.Rendering ?? new ArchitectureRenderSettings();
        var analysis = job.Analysis ?? new ArchitectureAnalysisSettings();
        var layout = rendering.Layout ?? new LayoutSettings();
        var duplication = rendering.NodeDuplication ?? new NodeDuplicationSettings();
        var selection = diagram.Selection;
        var scope = new ArchitectureSelectionScope(
            selection?.ScopePolicy ?? "FullInput",
            diagram.Projects.Select(project => project.Id).ToArray(),
            selection?.Roots.Select(root => root.SemanticNodeId).ToArray() ?? Array.Empty<string>());
        var projection = duplication.AllowDuplicateNodes
            ? NodeProjectionMode.DuplicateBranches
            : NodeProjectionMode.Canonical;
        return new ArchitecturePlanningRequest(
            diagram,
            scope,
            new ArchitectureGenerationSettingsSnapshot(
                rendering.OutputRenderer, analysis.ExternalDependencyTag, analysis.ExcludedNamespaces.ToArray(), analysis.ExcludedNames.ToArray()),
            new NodeProjectionPolicy(projection, layout.DuplicateHighNoiseNodePatterns.ToArray()),
            new ProjectPlacementPolicy(rendering.ShowProjectContainers, rendering.ProjectContainerStyle.Shape),
            new NodePlacementPolicy(layout.BaselineAlignmentPattern, layout.NodeWidth, layout.NodeHeight,
                layout.HorizontalSpacing, layout.VerticalSpacing,
                layout.NodeLayerGroups.Select((rule, index) => new ArchitectureV6RoleRule(rule.Name, rule.Pattern, index)).ToArray(),
                ArchitectureV6SpacingPolicy.From(layout.HorizontalSpacing) with
                {
                    LogicalLayer = layout.VerticalSpacing,
                    External = Math.Max(layout.HorizontalSpacing, layout.StandaloneGroupSpacing / 2),
                    ProjectBoundary = Math.Max(layout.HorizontalSpacing * 2, layout.ContainerPadding * 2)
                }),
            new RoutePlanningPolicy(layout.ParallelLaneSpacing, layout.EdgePortSpacing, analysis.ExternalDependencyTag),
            new GridSizingPolicy(layout.NodeWidth, layout.NodeHeight, layout.ContainerPadding, layout.ProjectHeaderHeight)
            {
                NodeToRouteClearance = layout.LinkPadding + layout.EdgePortSpacing,
                RoutingRowMinimum = 20,
                ProjectTransitionRowMinimum = 20
            },
            new ValidationPolicy(mode switch
            {
                ArchitectureRenderingMode.StrictValidation => ArchitectureValidationMode.Strict,
                ArchitectureRenderingMode.Production => ArchitectureValidationMode.Normal,
                _ => ArchitectureValidationMode.Diagnostic
            }),
            rendering.StyleRules.Select(rule => rule.Match).ToArray(),
            rendering.Overrides.Select(item => item.FullName).ToArray(),
            rendering.StyleRules.Select(rule => new ArchitectureV6StyleRule(rule.Match, rule.Style.FillColor, rule.Style.StrokeColor, rule.Style.FontColor, rule.Style.Shape, rule.Style.Shadow, rule.Style.ExtraStyle)).ToArray(),
            rendering.Overrides.Select(item => new ArchitectureV6StyleOverride(item.FullName, new ArchitectureV6StyleRule(item.FullName, item.Style.FillColor, item.Style.StrokeColor, item.Style.FontColor, item.Style.Shape, item.Style.Shadow, item.Style.ExtraStyle))).ToArray(),
            new ArchitectureV6StyleRule("<project>", rendering.ProjectContainerStyle.FillColor, rendering.ProjectContainerStyle.StrokeColor, rendering.ProjectContainerStyle.FontColor, rendering.ProjectContainerStyle.Shape, rendering.ProjectContainerStyle.Shadow, rendering.ProjectContainerStyle.ExtraStyle),
            new ArchitectureV6StyleRule("<external>", rendering.ExternalDependencyStyle.FillColor, rendering.ExternalDependencyStyle.StrokeColor, rendering.ExternalDependencyStyle.FontColor, rendering.ExternalDependencyStyle.Shape, rendering.ExternalDependencyStyle.Shadow, rendering.ExternalDependencyStyle.ExtraStyle),
            new ArchitectureV6ConnectorStyle(rendering.Connector.StrokeColor, rendering.Connector.StrokeWidth, rendering.Connector.Rounded,
                rendering.Connector.Dashed, rendering.Connector.DashPattern, rendering.Connector.StartArrow,
                rendering.Connector.EndArrow, rendering.Connector.ArrowSize, rendering.Connector.Opacity,
                rendering.Connector.FontColor, rendering.Connector.ShowLabels, rendering.Connector.StartFill,
                rendering.Connector.EndFill, rendering.Connector.ExtraStyle));
    }
}
