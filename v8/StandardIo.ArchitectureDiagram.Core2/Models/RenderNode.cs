// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
namespace StandardIo.ArchitectureDiagram.Core2.Models;
public sealed record RenderNode(string Id, string TypeName, string Label, string Fill, double X, double Y, double Width, double Height, RenderText[] TextLines)
{
    public bool HasHeader { get; init; }
    public RenderNodeCategory? Category { get; init; }
}
