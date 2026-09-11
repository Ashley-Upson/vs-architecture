// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
namespace StandardIo.ArchitectureDiagram.Core2.Models;
public sealed class ArchitectureRenderConfiguration
{
    public bool InlineExternals { get; set; }
    public double RowDepth { get; set; } = 160;
    public double NodeWidth { get; set; } = 210;
    public double NodeSpacing { get; set; } = 60;
    public double ProjectSpacing { get; set; } = 150;
    // Orchestration, processing, foundation, broker, exposure, other internal, external.
    public string[] LayerColours { get; set; } = ["#046079", "#6420b0", "#176538", "#415f19", "#2057a0", "#7e4a16", "#963918"];
}
