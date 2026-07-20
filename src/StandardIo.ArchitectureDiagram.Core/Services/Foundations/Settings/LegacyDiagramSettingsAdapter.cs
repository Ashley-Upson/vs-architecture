using System.Linq;
using StandardIo.ArchitectureDiagram.Core.Models;
using StandardIo.ArchitectureDiagram.Core.Models.Drawios;
using StandardIo.ArchitectureDiagram.Core.Models.Generation;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Settings;

public static class LegacyDiagramSettingsAdapter
{
    public static ArchitectureAnalysisSettings ToArchitectureAnalysis(DiagramSettings settings) => new()
    {
        ExcludedNamespaces = settings.ExcludedNamespaces.ToList(),
        ExcludedNames = settings.ExcludedNames.ToList(),
        RootDiscoveryPatternsText = settings.RootDiscoveryPatternsText,
        ExternalDependencyTag = settings.ExternalDependencyTag
    };

    public static ArchitectureRenderSettings ToArchitectureRendering(DiagramSettings settings) => new()
    {
        Canvas = settings.Canvas,
        Layout = settings.Layout,
        StyleRules = settings.StyleRules.ToList(),
        Overrides = settings.Overrides.ToList(),
        ShowProjectContainers = settings.ShowProjectContainers,
        ProjectContainerStyle = settings.ProjectContainerStyle,
        ExternalDependencyStyle = settings.ExternalDependencyStyle,
        Connector = settings.Connector,
        NodeDuplication = settings.NodeDuplication
    };

    public static DataModelAnalysisSettings ToDataModelAnalysis(DiagramSettings settings) => new()
    {
        ExcludedNamespaces = settings.ExcludedNamespaces.ToList(),
        ExcludedNames = settings.ExcludedNames.ToList()
    };

    public static DataModelRenderSettings ToDataModelRendering(DiagramSettings settings) => new()
    {
        Canvas = settings.Canvas,
        Connector = settings.Connector,
        TableWidth = settings.Layout.DataModelTableWidth,
        ColumnWidth = settings.Layout.DataModelColumnWidth,
        RowSpacing = settings.Layout.DataModelRowSpacing,
        CanvasMargin = settings.Layout.DataModelCanvasMargin,
        HeaderHeight = settings.Layout.DataModelHeaderHeight,
        PropertyRowHeight = settings.Layout.DataModelPropertyRowHeight,
        RelationshipLaneSpacing = settings.Layout.DataModelRelationshipLaneSpacing,
        RelationshipSideOffset = settings.Layout.DataModelRelationshipSideOffset,
        RelationshipStubLength = settings.Layout.DataModelRelationshipStubLength,
        RelationshipPortSpacing = settings.Layout.DataModelRelationshipPortSpacing,
        MinimumTableGap = settings.Layout.DataModelMinimumTableGap,
        RadialMinimumRadius = settings.Layout.DataModelRadialMinimumRadius,
        RadialRingSpacing = settings.Layout.DataModelRadialRingSpacing,
        ComponentSpacing = settings.Layout.DataModelComponentSpacing,
        ComponentRowWidth = settings.Layout.DataModelComponentRowWidth
    };

    public static DiagramGenerationRequest CombinedRequest(DiagramSettings settings) => new(
        [
            new ArchitectureGenerationJob(ToArchitectureAnalysis(settings), ToArchitectureRendering(settings)),
            new DataModelGenerationJob(ToDataModelAnalysis(settings), ToDataModelRendering(settings))
        ],
        new DrawioDocumentSettings());

    public static DiagramGenerationRequest ArchitectureRequest(DiagramSettings settings) => new(
        [new ArchitectureGenerationJob(ToArchitectureAnalysis(settings), ToArchitectureRendering(settings), "Architecture")],
        new DrawioDocumentSettings());

    public static DiagramGenerationRequest DataModelRequest(DiagramSettings settings) => new(
        [new DataModelGenerationJob(ToDataModelAnalysis(settings), ToDataModelRendering(settings), PageNameHint: "Data Model")],
        new DrawioDocumentSettings());
}
