// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
namespace StandardIo.ArchitectureDiagram.Core2.Models;
public sealed class RenderConfiguration
{
    public bool NoDuplicates { get; set; }
    public double HorizontalOffset { get; set; } = 10;
    public bool ColourLines { get; set; }
    public int MaxLayoutIterations { get; set; } = 1000;
    public ArchitectureRenderConfiguration Architecture { get; set; } = new();
    public CallChainRenderConfiguration CallChain { get; set; } = new();
    public DataModelRenderConfiguration DataModel { get; set; } = new();
}
