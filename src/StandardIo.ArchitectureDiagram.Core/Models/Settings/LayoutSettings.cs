using System.Collections.Generic;

namespace StandardIo.ArchitectureDiagram.Core.Models;

public sealed class LayoutSettings
{
    public const string DefaultBaselineAlignmentPattern =
        ".*(Aggregation|Coordination|Orchestration)Service$";

    public int NodeWidth { get; set; } = 200;
    public int NodeHeight { get; set; } = 80;
    public int HorizontalSpacing { get; set; } = 80;
    public int VerticalSpacing { get; set; } = 80;
    public int ContainerPadding { get; set; } = 40;
    public int EdgePortSpacing { get; set; } = 5;
    public int ParallelLaneSpacing { get; set; } = 12;
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
    public string BaselineAlignmentPattern { get; set; } = DefaultBaselineAlignmentPattern;
    public List<string> DuplicateHighNoiseNodePatterns { get; set; } = new()
    {
        "*DbContext",
        "*Context",
        "*EventHub",
        "*Hub",
        "*Logger",
        "ILogger*"
    };
}
