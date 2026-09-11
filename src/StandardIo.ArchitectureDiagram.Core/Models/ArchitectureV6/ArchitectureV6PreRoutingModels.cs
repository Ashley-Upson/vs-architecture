namespace StandardIo.ArchitectureDiagram.Core.Models.ArchitectureV6;

public sealed record ArchitectureV6NodeSpanRequirement(
    string PhysicalNodeId,
    int RequiredSpan,
    int RequiredPhysicalWidth,
    int LabelWidth,
    int TopTerminalWidth,
    int BottomTerminalWidth,
    int ConfiguredMinimumWidth,
    string Provenance);
