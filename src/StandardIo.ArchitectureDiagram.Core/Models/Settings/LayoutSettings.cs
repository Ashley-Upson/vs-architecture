using System.Collections.Generic;

namespace StandardIo.ArchitectureDiagram.Core.Models;

public sealed class LayoutSettings
{
    public const string DefaultBaselineAlignmentPattern =
        ".*(Aggregation|Coordination|Orchestration)Service$";

    public int NodeWidth { get; set; } = 200;
    public int NodeHeight { get; set; } = 80;
    // V7 sizing-policy inputs. These are explicit repository defaults until a reviewed
    // horizontal/vertical policy selects different preserved-user values.
    public int BaseCellWidth { get; set; } = 100;
    public int RoutingRowMinimum { get; set; } = 20;
    public int BoundaryRowMinimum { get; set; } = 20;
    public int VerticalNodeClearance { get; set; } = 10;
    public int LabelCharacterWidth { get; set; } = 8;
    public int HorizontalSpacing { get; set; } = 80;
    public int VerticalSpacing { get; set; } = 80;
    public int ContainerPadding { get; set; } = 40;
    public int EdgePortSpacing { get; set; } = 5;
    public int ParallelLaneSpacing { get; set; } = 12;
    // V7 has one midpoint-relative connection/lane spacing authority. The
    // older fields remain readable for settings migration but are not used by
    // the V7 production pipeline.
    public int ConnectionLaneSpacing { get; set; } = 12;
    public int StandaloneGroupSpacing { get; set; } = 160;
    public int ProjectHeaderHeight { get; set; } = 34;
    public int LinkPadding { get; set; } = 10;
    public int LinkNodeWidthPadding { get; set; } = 20;
    public int ExposureTreeLayoutThreshold { get; set; } = 75;
    public int ExposureTreeMinVerticalSpacing { get; set; } = 100;
    public int ExposureTreeMinHorizontalSpacing { get; set; } = 70;
    public int ExposureTreeHorizontalSpacingBonus { get; set; } = 20;
    public int ExposureTreeDepthSpacingReduction { get; set; } = 10;
    public int ExposureTreeConnectorMinSegment { get; set; } = 20;
    public int ExposureTreeConnectorClearanceMultiplier { get; set; } = 3;
    public int ExposureTreeConnectorDetourAttempts { get; set; } = 4;
    public int DataModelTableWidth { get; set; } = 320;
    public int DataModelColumnWidth { get; set; } = 380;
    public int DataModelRowSpacing { get; set; } = 80;
    public int DataModelCanvasMargin { get; set; } = 80;
    public int DataModelHeaderHeight { get; set; } = 32;
    public int DataModelPropertyRowHeight { get; set; } = 24;
    public int DataModelRelationshipLaneSpacing { get; set; } = 18;
    public int DataModelRelationshipSideOffset { get; set; } = 80;
    public int DataModelRelationshipStubLength { get; set; } = 50;
    public int DataModelRelationshipPortSpacing { get; set; } = 20;
    public int DataModelMinimumTableGap { get; set; } = 90;
    public int DataModelRadialMinimumRadius { get; set; } = 420;
    public int DataModelRadialRingSpacing { get; set; } = 520;
    public int DataModelComponentSpacing { get; set; } = 260;
    public int DataModelComponentRowWidth { get; set; } = 4200;
    public string BaselineAlignmentPattern { get; set; } = DefaultBaselineAlignmentPattern;
    public List<string> ReservedLayerTypePatterns { get; set; } = CreateDefaultReservedLayerTypePatterns();
    public List<string> DuplicateHighNoiseNodePatterns { get; set; } = new()
    {
        "*DbContext",
        "*Context",
        "*EventHub",
        "*Hub",
        "*Logger",
        "ILogger*"
    };
    public List<NodeLayerGroupRule> NodeLayerGroups { get; set; } = CreateDefaultNodeLayerGroups();

    public static List<string> CreateDefaultReservedLayerTypePatterns() => new()
    {
        "*AggregationService",
        "*CoordinationService",
        "*OrchestrationService",
        "*ProcessingService",
        "*Service",
        "*Broker"
    };

    public static List<NodeLayerGroupRule> CreateDefaultNodeLayerGroups() => new()
    {
        new() { Name = "Controller", Pattern = "Controller$" },
        new() { Name = "Manager", Pattern = "Manager$" },
        new() { Name = "AggregationService", Pattern = "AggregationService$" },
        new() { Name = "ManagementService", Pattern = "ManagementService$" },
        new() { Name = "CoordinationService", Pattern = "CoordinationService$" },
        new() { Name = "OrchestrationService", Pattern = "OrchestrationService$" },
        new() { Name = "ProcessingService", Pattern = "ProcessingService$" },
        new() { Name = "Service", Pattern = "Service$" },
        new() { Name = "Broker", Pattern = "Broker$" }
    };
}
