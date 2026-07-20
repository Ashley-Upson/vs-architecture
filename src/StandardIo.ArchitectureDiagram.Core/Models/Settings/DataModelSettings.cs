using System.Collections.Generic;

namespace StandardIo.ArchitectureDiagram.Core.Models;

public sealed class DataModelAnalysisSettings
{
    public List<string> ExcludedNamespaces { get; set; } = new();
    public List<string> ExcludedNames { get; set; } = new();
    public bool RequirePublicInstanceProperty { get; set; } = true;
    public bool RequireZeroPublicInstanceMethods { get; set; } = true;
}

public sealed class DataModelRenderSettings
{
    public CanvasSettings Canvas { get; set; } = new();
    public ConnectorStyle Connector { get; set; } = new();
    public int TableWidth { get; set; } = 320;
    public int ColumnWidth { get; set; } = 380;
    public int RowSpacing { get; set; } = 80;
    public int CanvasMargin { get; set; } = 80;
    public int HeaderHeight { get; set; } = 32;
    public int PropertyRowHeight { get; set; } = 24;
    public int RelationshipLaneSpacing { get; set; } = 18;
    public int RelationshipSideOffset { get; set; } = 80;
    public int RelationshipStubLength { get; set; } = 50;
    public int RelationshipPortSpacing { get; set; } = 20;
    public int MinimumTableGap { get; set; } = 90;
    public int RadialMinimumRadius { get; set; } = 420;
    public int RadialRingSpacing { get; set; } = 520;
    public int ComponentSpacing { get; set; } = 260;
    public int ComponentRowWidth { get; set; } = 4200;
}
